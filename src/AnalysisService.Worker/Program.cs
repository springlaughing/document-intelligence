using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry.Resources;
using AnalysisService.Worker.Messaging;
using AnalysisService.Worker.Infrastructure;
using Azure.Messaging.ServiceBus; 
using DocumentIntelligence.Contracts.Messaging;


var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => o.TimestampFormat = "HH:mm:ss ");

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
             ?? cfg["AzureServiceBus:ConnectionString"]; // dev/user-secrets

    var fqn = cfg["AzureServiceBus:FullyQualifiedNamespace"]; // "<ns>.servicebus.windows.net"

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
builder.Services.AddScoped<IBlobWriter, BlobWriter>();
builder.Services.AddScoped<IAnalysisResultEventPublisher, AnalysisResultEventPublisher>();


builder.Services.AddSingleton<IBlobWriter, BlobWriter>(); 



var host = builder.Build();

var env     = host.Services.GetRequiredService<IHostEnvironment>();
var logger  = host.Services.GetRequiredService<ILogger<Program>>();

logger.LogInformation("Environment: {Env} (IsDevelopment={IsDev})",
    env.EnvironmentName, env.IsDevelopment());

await host.RunAsync();

