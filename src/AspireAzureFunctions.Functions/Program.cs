using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// Add Aspire service defaults (OpenTelemetry, health checks, service discovery)
builder.AddServiceDefaults();

// Add Aspire-managed Azure Storage Queues client
builder.AddAzureQueueServiceClient("queues");

// Add Aspire-managed Azure Blob Storage client
builder.AddAzureBlobServiceClient("blobs");

// Add Aspire-managed Redis distributed cache
builder.AddRedisClient("cache");

builder.Build().Run();
