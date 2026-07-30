using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry.Resources;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using DocumentIntelligence.Contracts.Messaging;
using DocumentIntelligence.Contracts.DomainContracts;
using DocumentIntelligence.Contracts.Contracts;
using DocumentService.Api.Messaging;
using DocumentService.Api.Infrastructure.Repositories;
using DocumentService.Api.Infrastructure.Ef;
using DocumentService.Api.Infrastructure.Ef.Entities;
using Microsoft.EntityFrameworkCore;
using Azure.Messaging.ServiceBus; 
using Microsoft.OpenApi.Models;
using System.Text.Json;
using System.Text.Json.Serialization;


var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => o.TimestampFormat = "HH:mm:ss ");

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
if (!builder.Environment.IsDevelopment())
{
    var aiConnStr =
        builder.Configuration["ApplicationInsights:ConnectionString"]
        ?? Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");

    if (!string.IsNullOrWhiteSpace(aiConnStr))
    {
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(builder.Environment.ApplicationName))
            .UseAzureMonitor(o => o.ConnectionString = aiConnStr);
    }

}

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


builder.Services.AddDbContext<DocumentApiDbContext>(options =>
{
    
    // InMemory (für lokale Tests)
    options.UseInMemoryDatabase("DocumentApiDb");
    
    // Cloud später: 
    // o.UseSqlServer(connString, sql => sql.EnableRetryOnFailure(
    // maxRetryCount: 5,
    // maxRetryDelay: TimeSpan.FromSeconds(5),
    // errorNumbersToAdd: null)));
});

// Repo
builder.Services.AddScoped<IDocumentRepository, EfDocumentRepository>();

// Messaging
builder.Services.AddSingleton<IMessagePublisher, AzureServiceBusPublisher>();
builder.Services.AddScoped<IAnalyzeDocumentCommandPublisher, AnalyzeDocumentCommandPublisher>();
builder.Services.AddScoped<IMessageHandler<AnalysisCompletedEvent>, AnalysisCompletedEventHandler>();
builder.Services.AddHostedService<AnalysisCompletedEventListener>();
builder.Services.AddScoped<IMessageHandler<AnalysisFailedEvent>, AnalysisFailedEventHandler>();
builder.Services.AddHostedService<AnalysisFailedEventListener>();


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

// Seed initial data (dev only)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DocumentApiDbContext>();

    if (!db.Documents.Any())
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

app.MapControllers();


app.Run();
