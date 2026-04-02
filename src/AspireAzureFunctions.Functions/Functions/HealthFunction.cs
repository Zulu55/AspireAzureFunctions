using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using StackExchange.Redis;

namespace AspireAzureFunctions.Functions.Functions;

public class HealthFunction(IConnectionMultiplexer redis)
{
    [Function("Health")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest req)
    {
        var db = redis.GetDatabase();
        await db.PingAsync();

        return new OkObjectResult(new
        {
            Status = "Healthy",
            Timestamp = DateTime.UtcNow,
            Redis = "Connected"
        });
    }
}
