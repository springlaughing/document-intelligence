using AnalysisService.Worker.Messaging;
using AnalysisService.Worker.Infrastructure;


var builder = Host.CreateApplicationBuilder(args);



// 2. BackgroundService, der Azure Service Bus konsumiert
// (oder Emulator - gleicher Code, andere ConnectionString)
builder.Services.AddHostedService<AzureServiceBusConsumer>();

builder.Services.AddSingleton<IBlobWriter, BlobWriter>(); // or FakeBlobWriter
//Publish AnalysisCompletedEvent (with blobRef) back to the bus.
builder.Services.AddSingleton<IAnalysisResultEventPublisher, ServiceBusAnalysisResultEventPublisher>();


var host = builder.Build();
await host.RunAsync();

