namespace AspireAzureFunctions.Functions.Models;

public record OrderRequest(string ProductName, int Quantity, string CustomerEmail);

public record OrderResult(string OrderId, string Status, DateTime CreatedAt);
