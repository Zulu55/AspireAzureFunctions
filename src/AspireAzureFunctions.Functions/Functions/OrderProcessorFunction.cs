using System.Text.Json;
using AspireAzureFunctions.Functions.Models;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace AspireAzureFunctions.Functions.Functions;

/// <summary>
/// Queue-triggered function that processes orders asynchronously.
/// Demonstrates: Queue trigger + Blob Storage + Redis cache via Aspire.
/// </summary>
public class OrderProcessorFunction(
    BlobServiceClient blobServiceClient,
    IConnectionMultiplexer redis,
    ILogger<OrderProcessorFunction> logger)
{
    private const string ContainerName = "processed-orders";

    [Function("ProcessOrder")]
    public async Task Run(
        [QueueTrigger("orders", Connection = "queues")] string messageText)
    {
        var payload = JsonSerializer.Deserialize<JsonElement>(messageText);
        var orderId = payload.GetProperty("orderId").GetString()!;
        var orderJson = payload.GetProperty("order");

        logger.LogInformation("Processing order {OrderId}...", orderId);

        // Simulate some processing work
        await Task.Delay(500);

        // Store processed order in Blob Storage
        var containerClient = blobServiceClient.GetBlobContainerClient(ContainerName);
        await containerClient.CreateIfNotExistsAsync();

        var blobClient = containerClient.GetBlobClient($"{orderId}.json");
        var processedData = JsonSerializer.Serialize(new
        {
            OrderId = orderId,
            Order = orderJson,
            ProcessedAt = DateTime.UtcNow,
            Status = "Processed"
        });
        await blobClient.UploadAsync(new BinaryData(processedData), overwrite: true);

        // Update the order status in Redis cache
        var db = redis.GetDatabase();
        var result = new OrderResult(orderId, "Processed", DateTime.UtcNow);
        await db.StringSetAsync($"order:{orderId}", JsonSerializer.Serialize(result), TimeSpan.FromMinutes(30));

        logger.LogInformation("Order {OrderId} processed and stored in blob storage", orderId);
    }
}
