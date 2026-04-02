# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

A proof-of-concept demonstrating Azure Functions orchestrated by .NET Aspire with Azure deployment. Written in Spanish (README, comments). Targets .NET 10 with Aspire 13.2.1.

## Build & Run

```bash
# Run everything (Aspire Dashboard, Azurite, Redis, Functions, API)
dotnet run --project src/AspireAzureFunctions.AppHost

# Build only
dotnet build src/AspireAzureFunctions.AppHost
```

Requires Docker Desktop running (for Azurite and Redis containers) and Azure Functions Core Tools v4.

Local endpoints after startup:
- Aspire Dashboard: `https://localhost:17180`
- API: `http://localhost:5000`
- Functions: `http://localhost:7071`

## Architecture

The AppHost (`src/AspireAzureFunctions.AppHost/AppHost.cs`) is the orchestrator. It wires all resources:

```
AppHost
├── Azure Storage (Azurite emulator locally)
│   ├── queues ("queues") ──► Functions, Api
│   └── blobs ("blobs")   ──► Functions
├── Redis ("cache")        ──► Functions, Api
├── Functions project      ──  HTTP + Queue triggers
└── Api project            ──  Minimal API, discovers Functions via service discovery
```

**Data flow for orders:**
1. HTTP POST arrives at Api or Functions `/api/orders`
2. Order is serialized, base64-encoded, and sent to Azure Queue Storage `orders` queue
3. `ProcessOrder` queue trigger picks it up, stores result in Blob Storage (`processed-orders` container), updates Redis cache
4. Order status is queryable from Redis via GET `/api/orders/{orderId}`

## Key Patterns

- **Service discovery**: Api references Functions as `https+http://functions` (configured in AppHost via `.WithReference(functions)`)
- **Aspire client registration**: Services use `builder.AddAzureQueueServiceClient("queues")`, `builder.AddAzureBlobServiceClient("blobs")`, `builder.AddRedisClient("cache")` — the connection names match resource names in AppHost
- **Primary constructors with DI**: Function classes use C# primary constructor injection (e.g., `OrderFunction(QueueServiceClient, IConnectionMultiplexer, ILogger<>)`)
- **Queue messages are base64-encoded**: Both Api and Functions encode queue messages as base64 before sending

## Projects

| Project | Role |
|---------|------|
| `AppHost` | Aspire orchestrator — defines resources and wiring |
| `ServiceDefaults` | Shared library: OpenTelemetry, health checks, service discovery, resilience |
| `Functions` | Azure Functions v4 isolated worker (HTTP + Queue triggers) |
| `Api` | Minimal API — proxies to Functions, also enqueues orders directly |

## Deployment

Uses Azure Developer CLI (`azd up`) which detects the Aspire AppHost and provisions Azure Container Apps, Functions App, Storage Account, Redis, and Container Registry.
