using System.Diagnostics;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using AnalysisService.Worker.Messaging;
using AnalysisService.Worker.Infrastructure;
using Azure.Messaging.ServiceBus; 
using DocumentIntelligence.Messaging;
using DocumentIntelligence.Contracts;
using System.Text.Json;
using System.Text.Json.Serialization;



var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.TimestampFormat = "HH:mm:ss ";
    o.IncludeScopes = true;   // without this the trace id below is collected but never shown
});

// Stamps every log line with the current trace. Because the Service Bus SDK restores the
// trace from the incoming message, a line logged here carries the same trace id as the
// API request that started the whole thing.
builder.Logging.Configure(o =>
    o.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);

// Tracing always registers; only the exporter depends on the environment. See the same
// block in DocumentService.Api - duplicated deliberately, because a composition root is
// exactly the place where two services are allowed to configure themselves differently.
var aiConnectionString =
    builder.Configuration["ApplicationInsights:ConnectionString"]
    ?? Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");

var telemetry = builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(builder.Environment.ApplicationName));

telemetry.WithTracing(tracing =>
{
    // Spans for every Service Bus receive and send, with the W3C traceparent carried
    // inside the message - which is what joins this service to the API's trace.
    tracing.AddSource("Azure.*");

    if (string.IsNullOrWhiteSpace(aiConnectionString) && builder.Environment.IsDevelopment())
        tracing.AddConsoleExporter();
});

if (!string.IsNullOrWhiteSpace(aiConnectionString))
    telemetry.UseAzureMonitor(o => o.ConnectionString = aiConnectionString);
// Enums go on the wire as names, not ordinals: adding an enum value must not
// silently change the meaning of in-flight or dead-lettered messages.
builder.Services.AddSingleton(sp => new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    Converters = { new JsonStringEnumConverter() }
});

builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();

    var cs  = cfg.GetConnectionString("AzureServiceBus")
             ?? cfg["AzureServiceBus:ConnectionString"]; 

    var fqn = cfg["AzureServiceBus:FullyQualifiedNamespace"]; 

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
        return new Azure.Messaging.ServiceBus.ServiceBusClient(cs, options); // dev/conn-string

    if (string.IsNullOrWhiteSpace(fqn))
        throw new InvalidOperationException("AzureServiceBus connection not configured.");

    return new Azure.Messaging.ServiceBus.ServiceBusClient(
        fqn,
        new Azure.Identity.DefaultAzureCredential(),
        options
    ); // cloud/MSI
});




builder.Services.AddSingleton<IMessagePublisher, AzureServiceBusPublisher>();
builder.Services.AddScoped<IAnalysisResultEventPublisher, AnalysisResultEventPublisher>();

builder.Services.AddHostedService<AnalyzeDocumentCommandListener>();
builder.Services.AddScoped<IMessageHandler<AnalyzeDocumentCommand>, AnalyzeDocumentCommandHandler>();

// BlobWriter is stateless -> singleton.
builder.Services.AddSingleton<IBlobWriter, BlobWriter>();



var host = builder.Build();

var env     = host.Services.GetRequiredService<IHostEnvironment>();
var logger  = host.Services.GetRequiredService<ILogger<Program>>();

logger.LogInformation("Environment: {Env} (IsDevelopment={IsDev})",
    env.EnvironmentName, env.IsDevelopment());

await host.RunAsync();

