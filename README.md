# Aspire + Azure Functions POC

Proof of Concept que demuestra cómo ejecutar **Azure Functions** orquestadas por **.NET Aspire** con despliegue a **Azure**.

La solución implementa un sistema de **gestión de órdenes** que combina una Minimal API y Azure Functions para ilustrar patrones de comunicación síncrona (HTTP) y asíncrona (colas), con observabilidad completa a través del dashboard de Aspire.

## Arquitectura

```
┌─────────────────────────────────────────────────────────────────────┐
│                        Aspire AppHost                               │
│                   (Orquestador / Dashboard)                         │
├────────────────────┬────────────────────────────────────────────────┤
│                    │                                                │
│   ┌────────────────▼───────────────┐  ┌────────────────────────┐    │
│   │         API (Minimal API)      │  │    Azure Functions     │    │
│   │         :5000                  │  │    :7071               │    │
│   │                                │  │                        │    │
│   │  POST /api/orders ─────────┐   │  │  POST /api/orders      │    │
│   │  GET  /api/orders/{id}     │   │  │  GET  /api/orders/{id} │    │
│   │  GET  /api/functions-health│   │  │  GET  /api/health      │    │
│   │  GET  / (dashboard)        │   │  │  QueueTrigger: orders  │    │
│   └──┬────┬────────────────────┘   │  └───┬──────┬───────┬─────┘    │
│      │    │   service discovery    │      │      │       │          │
│      │    │   (https+http://)──────┼─────►│      │       │          │
│      │    │                        │      │      │       │          │
│  ┌───▼──┐ │  ┌──────────┐      ┌───▼───┐  │  ┌───▼────┐  │          │
│  │Redis │ └──▶Queue Stg │      │Queue  │  │  │Blob    │  │          │
│  │Cache │    │(enqueue) │      │Trigger│  │  │Storage │  │          │
│  └──────┘    └──────────┘      └───────┘  │  └────────┘  │          │
│      ▲                                    │              │          │
│      └────────────────────────────────────┘              │          │
│              (actualiza estado en Redis)                 │          │
│              (guarda orden procesada) ◄──────────────────┘          │
└─────────────────────────────────────────────────────────────────────┘
```

## Flujo de procesamiento de órdenes

El sistema implementa un patrón **command + async processor** donde la creación y el procesamiento de órdenes están desacoplados mediante una cola:

### 1. Creación de la orden (síncrono)

Una orden puede crearse desde **dos puntos de entrada** independientes:

- **Vía API** (`POST http://localhost:5000/api/orders`): La Minimal API recibe el request, genera un `orderId` (8 caracteres hex), serializa el payload como JSON, lo codifica en **base64** y lo envía a la cola `orders` de Azure Queue Storage.

- **Vía Functions** (`POST http://localhost:7071/api/orders`): La función `CreateOrder` hace lo mismo pero además almacena el estado inicial (`Queued`) en **Redis** con TTL de 30 minutos, permitiendo consultas inmediatas del estado.

### 2. Procesamiento asíncrono (QueueTrigger)

La función `ProcessOrder` se activa automáticamente cuando llega un mensaje a la cola `orders`:

1. Deserializa el mensaje JSON y extrae `orderId` y datos de la orden
2. Simula trabajo de procesamiento (500ms de delay)
3. Almacena la orden procesada como blob JSON en el contenedor `processed-orders` de **Azure Blob Storage** (`{orderId}.json`)
4. Actualiza el estado en **Redis** a `Processed` con TTL de 30 minutos

### 3. Consulta de estado (síncrono)

El estado de una orden se consulta desde Redis (ambos endpoints retornan lo mismo):

- `GET http://localhost:5000/api/orders/{orderId}` — vía API
- `GET http://localhost:7071/api/orders/{orderId}` — vía Functions

## Componentes del proyecto

### AppHost (`src/AspireAzureFunctions.AppHost`)

Orquestador central de Aspire. Define la topología completa de la aplicación:

- **Azure Storage** con emulador Azurite: proporciona colas (`queues`) y blobs (`blobs`)
- **Redis** como caché distribuido (`cache`)
- **Functions** project: conectado a queues, blobs y cache
- **Api** project: conectado a Functions (service discovery), cache y queues

Cada recurso se registra con un nombre lógico (ej. `"queues"`, `"cache"`) que los proyectos consumidores usan para resolver conexiones automáticamente.

### ServiceDefaults (`src/AspireAzureFunctions.ServiceDefaults`)

Biblioteca compartida referenciada por Functions y Api. Configura:

- **OpenTelemetry**: tracing distribuido (ASP.NET Core + HTTP client), métricas de runtime, y exportación OTLP al dashboard de Aspire
- **Health checks**: endpoints `/health` (readiness) y `/alive` (liveness), los requests de health se excluyen de los traces
- **Service discovery**: resolución automática de URLs entre servicios
- **Resiliencia HTTP**: políticas de retry/circuit-breaker vía `AddStandardResilienceHandler()`

### Functions (`src/AspireAzureFunctions.Functions`)

Azure Functions v4 en modo **isolated worker** con integración ASP.NET Core. Contiene tres funciones:

| Función | Trigger | Ruta | Descripción |
|---------|---------|------|-------------|
| `CreateOrder` | HTTP POST | `/api/orders` | Valida payload, encola orden en Queue Storage, cachea estado en Redis |
| `GetOrder` | HTTP GET | `/api/orders/{orderId}` | Consulta estado desde Redis |
| `ProcessOrder` | Queue `orders` | — | Procesa orden, guarda en Blob Storage, actualiza Redis |
| `Health` | HTTP GET | `/api/health` | Verifica conectividad con Redis |

Las funciones usan **primary constructors** para inyección de dependencias (`QueueServiceClient`, `BlobServiceClient`, `IConnectionMultiplexer`, `ILogger<T>`).

Los clientes de Azure se registran en `Program.cs` usando los componentes Aspire:
```csharp
builder.AddAzureQueueServiceClient("queues");
builder.AddAzureBlobServiceClient("blobs");
builder.AddRedisClient("cache");
```

### Api (`src/AspireAzureFunctions.Api`)

Minimal API que actúa como **gateway** y punto de entrada alternativo:

| Endpoint | Descripción |
|----------|-------------|
| `GET /` | Dashboard JSON con estado de Functions y Redis |
| `POST /api/orders` | Encola orden directamente en Queue Storage |
| `GET /api/orders/{orderId}` | Consulta estado desde Redis |
| `GET /api/functions-health` | Proxy al health check de Functions vía service discovery |

La comunicación con Functions usa un `HttpClient` configurado con **service discovery de Aspire**:
```csharp
builder.Services.AddHttpClient("functions", client =>
{
    client.BaseAddress = new Uri("https+http://functions");
});
```

El esquema `https+http://` permite que Aspire resuelva la dirección real del servicio automáticamente, tanto en desarrollo local como en Azure.

## Modelo de datos

```csharp
// Request para crear una orden
record OrderRequest(string ProductName, int Quantity, string CustomerEmail);

// Resultado almacenado en Redis
record OrderResult(string OrderId, string Status, DateTime CreatedAt);
```

Los mensajes en la cola se envían como JSON codificado en **base64** con la estructura `{ order, orderId }`.

Las órdenes procesadas se almacenan en Blob Storage como JSON con la estructura:
```json
{
  "OrderId": "a1b2c3d4",
  "Order": { "ProductName": "...", "Quantity": 1, "CustomerEmail": "..." },
  "ProcessedAt": "2026-04-02T...",
  "Status": "Processed"
}
```

## Prerrequisitos

- **.NET 10 SDK**
- **Docker Desktop** — para emuladores locales (Azurite para Azure Storage, Redis)
- **Azure Functions Core Tools v4** — `npm i -g azure-functions-core-tools@4`
- **Azure Developer CLI (`azd`)** — solo para despliegue a Azure

## Ejecución local

```bash
# Desde la raíz del repositorio
dotnet run --project src/AspireAzureFunctions.AppHost
```

Esto inicia automáticamente:
- **Aspire Dashboard** en `https://localhost:17180` — métricas, traces distribuidos, logs
- **Azurite** (emulador de Azure Storage) en contenedor Docker
- **Redis** en contenedor Docker
- **Azure Functions** en `http://localhost:7071`
- **API** en `http://localhost:5000`

## Probar

```bash
# Crear una orden vía API
curl -X POST http://localhost:5000/api/orders \
  -H "Content-Type: application/json" \
  -d '{"productName":"Laptop","quantity":1,"customerEmail":"test@test.com"}'

# Crear una orden vía Functions directamente
curl -X POST http://localhost:7071/api/orders \
  -H "Content-Type: application/json" \
  -d '{"productName":"Mouse","quantity":3,"customerEmail":"user@test.com"}'

# Consultar estado de una orden (reemplazar {orderId})
curl http://localhost:5000/api/orders/{orderId}

# Health check de Functions vía service discovery
curl http://localhost:5000/api/functions-health

# Dashboard del API (estado de componentes)
curl http://localhost:5000/
```

## Despliegue a Azure

```bash
# Login
azd auth login

# Inicializar (primera vez)
azd init

# Provisionar infraestructura y desplegar
azd up
```

`azd` detecta automáticamente el AppHost de Aspire y provisiona:
- **Azure Container Apps Environment** (para la API)
- **Azure Functions App** (plan Consumption/Flex)
- **Azure Storage Account** (Queues + Blobs)
- **Azure Cache for Redis**
- **Azure Container Registry**

## Conceptos clave demostrados

| Concepto | Implementación |
|----------|---------------|
| **Aspire como orquestador** | AppHost define recursos y sus dependencias en código C# |
| **Azure Functions en Aspire** | `AddAzureFunctionsProject<T>()` integra Functions como recurso nativo |
| **Service discovery** | La API resuelve Functions automáticamente con `https+http://functions` |
| **Recursos emulados** | Storage con Azurite y Redis en Docker — sin cuenta Azure para desarrollo |
| **Procesamiento asíncrono** | Patrón queue-based con desacople entre creación y procesamiento |
| **OpenTelemetry integrado** | Traces distribuidos entre API y Functions visibles en Aspire Dashboard |
| **Despliegue con azd** | Un solo comando para ir de local a Azure |
