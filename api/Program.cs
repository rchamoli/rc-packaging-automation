using System.Text.Json;
using Company.Function.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// Register application services
builder.Services.AddSingleton<MetadataReader>();
builder.Services.AddSingleton<StorageService>();
builder.Services.AddSingleton<AppDataSeeder>();
builder.Services.AddSingleton<IntuneGraphService>();
builder.Services.AddSingleton<PackagingService>();
builder.Services.AddSingleton<ActivityService>();
builder.Services.AddSingleton<NotificationService>();

// Configure JSON serialization to use camelCase for Azure Functions Worker
// JsonSerializerDefaults.Web enables camelCase naming and case-insensitive deserialization
builder.Services.Configure<WorkerOptions>(options =>
{
    options.Serializer = new Azure.Core.Serialization.JsonObjectSerializer(
        new JsonSerializerOptions(JsonSerializerDefaults.Web));
});

// Only enable Application Insights when a connection string is configured;
// avoids noisy blocked DNS lookups to *.in.applicationinsights.azure.com in local dev.
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
{
    builder.Services
        .AddApplicationInsightsTelemetryWorkerService()
        .ConfigureFunctionsApplicationInsights();
}

builder.Build().Run();
