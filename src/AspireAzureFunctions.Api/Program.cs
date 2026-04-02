using Azure.Storage.Queues;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire service defaults (OpenTelemetry, health checks, resilience, service discovery)
builder.AddServiceDefaults();

// Add Aspire-managed Redis
builder.AddRedisClient("cache");

// Add Aspire-managed Azure Storage Queues
builder.AddAzureQueueServiceClient("queues");

// Register an HttpClient for the Functions project using Aspire service discovery
builder.Services.AddHttpClient("functions", client =>
{
    client.BaseAddress = new Uri("https+http://functions");
});

var app = builder.Build();

app.MapDefaultEndpoints();

// Proxy endpoint: calls the Azure Functions health endpoint via service discovery
app.MapGet("/api/functions-health", async (IHttpClientFactory httpClientFactory) =>
{
    var client = httpClientFactory.CreateClient("functions");
    var response = await client.GetAsync("/api/health");
    var content = await response.Content.ReadAsStringAsync();
    return Results.Content(content, "application/json");
});

// Submit an order through the API — enqueues directly to the shared queue
app.MapPost("/api/orders", async (OrderRequest order, QueueServiceClient queueClient) =>
{
    var orderId = Guid.NewGuid().ToString("N")[..8];
    var queue = queueClient.GetQueueClient("orders");
    await queue.CreateIfNotExistsAsync();

    var payload = System.Text.Json.JsonSerializer.Serialize(new { order, orderId });
    await queue.SendMessageAsync(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload)));

    return Results.Created($"/api/orders/{orderId}", new { orderId, status = "Queued" });
});

// Get order status from Redis cache
app.MapGet("/api/orders/{orderId}", async (string orderId, IConnectionMultiplexer redis) =>
{
    var db = redis.GetDatabase();
    var cached = await db.StringGetAsync($"order:{orderId}");

    if (cached.IsNullOrEmpty)
        return Results.NotFound(new { message = $"Order {orderId} not found" });

    return Results.Content(cached!, "application/json");
});

// Dashboard endpoint showing all components status
app.MapGet("/", async (IConnectionMultiplexer redis, IHttpClientFactory httpClientFactory) =>
{
    string functionsStatus;
    try
    {
        var client = httpClientFactory.CreateClient("functions");
        var response = await client.GetAsync("/api/health");
        functionsStatus = response.IsSuccessStatusCode ? "Connected" : "Unavailable";
    }
    catch
    {
        functionsStatus = "Unavailable";
    }

    string redisStatus;
    try
    {
        var db = redis.GetDatabase();
        await db.PingAsync();
        redisStatus = "Connected";
    }
    catch
    {
        redisStatus = "Unavailable";
    }

    return Results.Ok(new
    {
        Service = "AspireAzureFunctions.Api",
        Timestamp = DateTime.UtcNow,
        Components = new
        {
            AzureFunctions = functionsStatus,
            Redis = redisStatus
        },
        Endpoints = new
        {
            CreateOrder = "POST /api/orders",
            GetOrder = "GET /api/orders/{orderId}",
            FunctionsHealth = "GET /api/functions-health"
        }
    });
});

app.Run();

public record OrderRequest(string ProductName, int Quantity, string CustomerEmail);
