using AnalysisService.Worker.Messaging;
using AnalysisService.Worker.Outbound;

var builder = Host.CreateApplicationBuilder(args);

// 1. HttpClient für Callback zur API
builder.Services.AddHttpClient<AnalysisResultPublisher>(client =>
{
    // In dev läuft die API lokal. Schau welchen Port deine API wirklich hat.
    // Wenn sie z.B. auf http://localhost:5000 läuft, dann so lassen.
    // Wenn nicht: anpassen.
    client.BaseAddress = new Uri("http://localhost:5000");

    // Dev security: shared secret
    client.DefaultRequestHeaders.Add("X-Internal-Token", "dev-internal");
});

// 2. BackgroundService, der Azure Service Bus konsumiert
// (oder Emulator - gleicher Code, andere ConnectionString)
builder.Services.AddHostedService<AzureServiceBusConsumer>();

var host = builder.Build();
await host.RunAsync();

