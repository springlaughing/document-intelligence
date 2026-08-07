using System.Diagnostics;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using DocumentIntelligence.Messaging;
using DocumentService.Api.Domain;
using DocumentIntelligence.Contracts;
using DocumentService.Api.Features.Documents.RecordAnalysisResult;
using DocumentService.Api.Features.Documents.RequestAnalysis;
using DocumentService.Api.Infrastructure.Repositories;
using DocumentService.Api.Infrastructure.Outbox;
using DocumentService.Api.Infrastructure.Ef;
using DocumentService.Api.Infrastructure.Ef.Entities;
using Microsoft.EntityFrameworkCore;
using Azure.Messaging.ServiceBus; 
using Microsoft.OpenApi.Models;
using System.Text.Json;
using System.Text.Json.Serialization;


// The Azure SDK only emits ActivitySource spans - and only injects W3C traceparent into
// outgoing messages - when this is on. Without it the Service Bus hop produces no spans
// at all, so a trace stops dead at the publish and starts fresh in the consumer. Must be
// set before any Azure client is constructed, hence the very first line.
AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.TimestampFormat = "HH:mm:ss ";
    o.IncludeScopes = true;   // without this the trace id below is collected but never shown
});

// Stamps every log line with the current trace, so lines belonging to one request - or
// to one message, on either side of the wire - can be pulled together. This is the
// correlation id; there is no need to invent a second one.
builder.Logging.Configure(o =>
    o.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);

var authMode = builder.Configuration["AUTH_MODE"] ?? "userjwts";

if (authMode.Equals("userjwts", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(o => builder.Configuration.Bind("Authentication:Schemes:Bearer", o)); 
}
else if (authMode.Equals("entra", StringComparison.OrdinalIgnoreCase))
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("EntraId"));
}
else
{
    throw new InvalidOperationException($"Unknown AUTH_MODE: {authMode}");
}



// Enums go on the wire as names, not ordinals: adding an enum value must not
// silently change the meaning of in-flight or dead-lettered messages.
builder.Services.AddSingleton(sp => new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    Converters = { new JsonStringEnumConverter() }
});
// Tracing always registers; only the exporter depends on the environment. Previously the
// whole pipeline was skipped unless this was a non-Development environment *and* an
// Application Insights connection string existed - so the one environment where you are
// actually trying to follow a message through the system was the one producing no traces
// at all.
var aiConnectionString =
    builder.Configuration["ApplicationInsights:ConnectionString"]
    ?? Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");

var telemetry = builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(builder.Environment.ApplicationName));

telemetry.WithTracing(tracing =>
{
    tracing.AddAspNetCoreInstrumentation();

    // The Azure SDK emits a span for every send and receive and propagates W3C
    // traceparent inside the message. That is what stitches the two services together
    // across the wire hop - the hop Go-to-Definition cannot follow.
    tracing.AddSource("Azure.*");

    // Spans this service raises itself, currently the outbox poller restoring the trace
    // of the request that queued a message.
    tracing.AddSource(OutboxTelemetry.ActivitySourceName);

    if (string.IsNullOrWhiteSpace(aiConnectionString) && builder.Environment.IsDevelopment())
        tracing.AddConsoleExporter();
});

// The reconciliation sweep reports counts rather than spans: what matters is the rate of
// documents it had to repair, which is a number over time, not one operation to follow.
telemetry.WithMetrics(metrics => metrics.AddMeter(ReconciliationTelemetry.MeterName));

if (!string.IsNullOrWhiteSpace(aiConnectionString))
    telemetry.UseAzureMonitor(o => o.ConnectionString = aiConnectionString);

builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();

    var cs  = cfg.GetConnectionString("AzureServiceBus")
             ?? cfg["AzureServiceBus:ConnectionString"]; // dev

    var fqn = cfg["AzureServiceBus:FullyQualifiedNamespace"]; // cloud

    var options = new ServiceBusClientOptions
    {
        RetryOptions = new ServiceBusRetryOptions
        {
            Mode       = ServiceBusRetryMode.Exponential,
            MaxRetries = 5,
            Delay      = TimeSpan.FromMilliseconds(200),
            MaxDelay   = TimeSpan.FromSeconds(8)
        }
    };

    if (!string.IsNullOrWhiteSpace(cs))
        return new ServiceBusClient(cs, options); 

    if (string.IsNullOrWhiteSpace(fqn))
        throw new InvalidOperationException("AzureServiceBus connection not configured.");

    return new ServiceBusClient(
        fqn,
        new Azure.Identity.DefaultAzureCredential(),
        options
    ); // cloud
});


builder.Services.AddControllers();

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("ReadAccess", policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.IsInRole("admin") ||
            ctx.User.IsInRole("user")  ||
            ctx.User.HasClaim("scp", "read")))
    .AddPolicy("WriteAccess", policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.IsInRole("admin") ||
            ctx.User.HasClaim("scp", "write")))
    .AddPolicy("AdminOnly", p => p.RequireRole("admin"));


// SQL Server when a connection string is configured, InMemory otherwise - the same
// shape as the ServiceBusClient registration above.
//
// InMemory keeps `dotnet run` working with no containers, but it is not a relational
// store: it ignores the RowVersion concurrency token, max lengths and unique
// constraints that DocumentEntityConfiguration declares. Anything that depends on
// those - optimistic concurrency, idempotency by unique key, the outbox - only really
// works on the relational path.
var documentDbConnection = builder.Configuration.GetConnectionString("DocumentDb");
var useRelationalDb = !string.IsNullOrWhiteSpace(documentDbConnection);

builder.Services.AddDbContext<DocumentApiDbContext>(options =>
{
    if (useRelationalDb)
    {
        options.UseSqlServer(documentDbConnection, sql => sql.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorNumbersToAdd: null));
    }
    else
    {
        options.UseInMemoryDatabase("DocumentApiDb");
    }
});

// Repo
builder.Services.AddScoped<IDocumentRepository, EfDocumentRepository>();

// Messaging
builder.Services.AddSingleton<IMessagePublisher, AzureServiceBusPublisher>();
builder.Services.AddScoped<IAnalyzeDocumentCommandQueue, AnalyzeDocumentCommandQueue>();

// Nothing publishes the analyze command inline any more; it is written to the outbox
// inside the same transaction as the status change, and this drains it.
builder.Services.AddScoped<OutboxDrainer>();
builder.Services.AddHostedService<OutboxPoller>();

// Both the inbox and the outbox are append-only; without this they grow forever.
builder.Services.AddScoped<OldMessageCleaner>();
builder.Services.AddHostedService<CleanupScheduler>();

// The outbox guarantees the command is published and the inbox that a result is applied
// once. Neither can see a command that dead-lettered, because nothing arrives to be
// handled - so this reads state on a timer instead. ADR 0004.
builder.Services.AddScoped<StuckAnalysisReconciler>();
builder.Services.AddHostedService<StuckAnalysisScheduler>();
builder.Services.AddScoped<IMessageHandler<AnalysisCompletedEvent>, AnalysisCompletedEventHandler>();
builder.Services.AddHostedService<AnalysisCompletedEventListener>();
builder.Services.AddScoped<IMessageHandler<AnalysisFailedEvent>, AnalysisFailedEventHandler>();
builder.Services.AddHostedService<AnalysisFailedEventListener>();


// Two probes, because the platform does two different things with the answers.
//
// Liveness asks "is this process still working?" and a failure gets the container
// restarted. It therefore checks nothing external: if it tested the database, a brief
// database blip would restart every replica at once, turning a recoverable dependency
// failure into a self-inflicted outage.
//
// Readiness asks "should this replica receive traffic?" and a failure only removes it from
// the load balancer. That is where dependency checks belong, because a replica that cannot
// reach its database should stop being sent requests while it recovers.
//
// The broker is deliberately *not* checked. The outbox means this service can accept and
// durably record work while Service Bus is unreachable - verified in
// docs/verification-log.md, where the analyze endpoint kept answering 202 with the broker
// stopped. Failing readiness on a broker outage would take the API out of the load balancer
// for a fault it is designed to survive.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<DocumentApiDbContext>("document-db", tags: ["ready"]);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Document Service API", Version = "v1" });

    // JWT bearer auth allows Swagger UI to show an "Authorize" button (for dev)
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Paste your JWT token here (without quotes)",
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        BearerFormat = "JWT",
        Scheme = "bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();
var env = app.Environment;


app.Logger.LogInformation("Environment: {Env} (IsDevelopment={IsDev})",
    env.EnvironmentName, env.IsDevelopment());

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DocumentApiDbContext>();

    // Migrating at startup is a convenience for a local demo. A real deployment applies
    // migrations as its own step, so two replicas starting together cannot race here.
    if (useRelationalDb)
    {
        app.Logger.LogInformation("Applying migrations to the document database.");
        await db.Database.MigrateAsync();
    }

    // Demo data, development only - it used to be seeded in every environment.
    if (app.Environment.IsDevelopment() && !db.Documents.Any())
    {
        var doc1Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var doc2Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var doc3Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        db.Documents.AddRange(
            new DocumentEntity
            {
                Id = doc1Id,
                FileName = "invoice-123.pdf",
                Status = DocumentStatus.Uploaded,
                AnalysisSummary = null,
                AnalysisBlobRef = null
            },
            new DocumentEntity
            {
                Id = doc2Id,
                FileName = "contract-foo.pdf",
                Status = DocumentStatus.Uploaded,
                AnalysisSummary = "This contract covers cooperation terms between Foo and Bar.",
                AnalysisBlobRef = null

            },
            new DocumentEntity
            {
                Id = doc3Id,
                FileName = "order.pdf",
                Status = DocumentStatus.Uploaded,
                AnalysisSummary = "Order conformation.",
                AnalysisBlobRef = null
 
            }
        );

        db.SaveChanges();
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


app.UseAuthentication();
app.UseAuthorization();

// Anonymous, because the thing calling these is the orchestrator's probe, which has no
// token and cannot be given one.
//
// Liveness runs no checks at all - Predicate false selects none of them. Answering at all
// is the whole signal: the process is up and the request pipeline works.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
   .AllowAnonymous();

// Readiness runs only what is tagged "ready", currently the database.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous();

app.MapControllers();


app.Run();
