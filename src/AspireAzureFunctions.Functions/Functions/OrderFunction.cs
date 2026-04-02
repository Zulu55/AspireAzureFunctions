using System.Text.Json;
using AspireAzureFunctions.Functions.Models;
using Azure.Storage.Queues;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace AspireAzureFunctions.Functions.Functions;

/// <summary>
/// HTTP-triggered function that receives orders and enqueues them for async processing.
/// Demonstrates: HTTP trigger + Azure Queue Storage + Redis cache via Aspire.
/// </summary>
public class OrderFunction(
    QueueServiceClient queueServiceClient,
    IConnectionMultiplexer redis,
    ILogger<OrderFunction> logger)
{
    private const string QueueName = "orders";

    [Function("CreateOrder")]
    public async Task<IActionResult> CreateOrder(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "orders")] HttpRequest req)
    {
        var order = await req.ReadFromJsonAsync<OrderRequest>();
        if (order is null)
            return new BadRequestObjectResult("Invalid order payload");

        var orderId = Guid.NewGuid().ToString("N")[..8];
        var result = new OrderResult(orderId, "Queued", DateTime.UtcNow);

        // Enqueue the order for async processing
        var queueClient = queueServiceClient.GetQueueClient(QueueName);
        await queueClient.CreateIfNotExistsAsync();

        var message = JsonSerializer.Serialize(new { order, orderId });
        await queueClient.SendMessageAsync(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(message)));

        // Cache the order status in Redis
        var db = redis.GetDatabase();
        await db.StringSetAsync($"order:{orderId}", JsonSerializer.Serialize(result), TimeSpan.FromMinutes(30));

        logger.LogInformation("Order {OrderId} queued for processing", orderId);

        return new CreatedResult($"/api/orders/{orderId}", result);
    }

    [Function("GetOrder")]
    public async Task<IActionResult> GetOrder(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "orders/{orderId}")] HttpRequest req,
        string orderId)
    {
        var db = redis.GetDatabase();
        var cached = await db.StringGetAsync($"order:{orderId}");

        if (cached.IsNullOrEmpty)
            return new NotFoundObjectResult(new { Message = $"Order {orderId} not found" });

        var result = JsonSerializer.Deserialize<OrderResult>((string)cached!);
        return new OkObjectResult(result);
    }
}
