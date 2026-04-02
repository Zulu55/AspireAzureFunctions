var builder = DistributedApplication.CreateBuilder(args);

// Azure Storage account — provides blobs and queues
var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator();

var queues = storage.AddQueues("queues");
var blobs = storage.AddBlobs("blobs");

// Redis for distributed caching
var cache = builder.AddRedis("cache");

// Azure Functions project — wired to storage queues + blobs
var functions = builder.AddAzureFunctionsProject<Projects.AspireAzureFunctions_Functions>("functions")
    .WithReference(queues)
    .WithReference(blobs)
    .WithReference(cache)
    .WithExternalHttpEndpoints();

// API project — references the functions for service discovery
var api = builder.AddProject<Projects.AspireAzureFunctions_Api>("api")
    .WithReference(functions)
    .WithReference(cache)
    .WithReference(queues)
    .WithExternalHttpEndpoints();

builder.Build().Run();
