# Aspire + Azure Functions POC

Proof of Concept que demuestra cómo ejecutar **Azure Functions** orquestadas por **.NET Aspire** con despliegue a **Azure**.

## Arquitectura

```
┌─────────────────────────────────────────────────────┐
│                  Aspire AppHost                      │
│              (Orquestador / Dashboard)               │
├─────────────┬──────────────┬────────────────────────┤
│             │              │                         │
│   ┌─────────▼──────┐  ┌───▼────────────┐           │
│   │  API (WebAPI)  │  │ Azure Functions │           │
│   │  :5000         │  │ :7071           │           │
│   └───┬────┬───────┘  └──┬──────┬──────┘           │
│       │    │              │      │                   │
│  ┌────▼┐ ┌─▼────────┐ ┌──▼──┐ ┌─▼──────┐          │
│  │Redis│ │Queue Stg. │ │Blobs│ │Queue   │          │
│  │Cache│ │(enqueue)  │ │Stg. │ │Trigger │          │
│  └─────┘ └───────────┘ └─────┘ └────────┘          │
└─────────────────────────────────────────────────────┘
```

## Componentes

| Proyecto | Descripción |
|----------|-------------|
| **AppHost** | Orquestador Aspire — define recursos Azure (Storage, Redis) y proyectos |
| **ServiceDefaults** | OpenTelemetry, health checks, service discovery, resiliencia |
| **Functions** | Azure Functions (HTTP + Queue triggers) con Aspire integrado |
| **Api** | Minimal API que se comunica con Functions vía service discovery |

## Funciones Azure

| Función | Trigger | Descripción |
|---------|---------|-------------|
| `Health` | HTTP GET `/api/health` | Health check con ping a Redis |
| `CreateOrder` | HTTP POST `/api/orders` | Recibe orden, la encola en Queue Storage |
| `GetOrder` | HTTP GET `/api/orders/{id}` | Consulta estado de orden desde Redis |
| `ProcessOrder` | Queue `orders` | Procesa orden, guarda en Blob Storage, actualiza Redis |

## Prerrequisitos

- .NET 10 SDK
- Docker Desktop (para emuladores locales de Azure Storage y Redis)
- Azure Functions Core Tools v4 (`npm i -g azure-functions-core-tools@4`)
- Azure Developer CLI (`azd`) — para despliegue a Azure

## Ejecución local

```bash
# Desde la raíz del repositorio
dotnet run --project src/AspireAzureFunctions.AppHost
```

Esto inicia:
- **Aspire Dashboard** en `https://localhost:17180` — métricas, traces, logs
- **Azure Storage Emulator** (Azurite) en contenedor Docker
- **Redis** en contenedor Docker
- **Azure Functions** en `http://localhost:7071`
- **API** en `http://localhost:5000`

## Probar

```bash
# Crear una orden (vía API)
curl -X POST http://localhost:5000/api/orders \
  -H "Content-Type: application/json" \
  -d '{"productName":"Laptop","quantity":1,"customerEmail":"test@test.com"}'

# Crear una orden (vía Functions directamente)
curl -X POST http://localhost:7071/api/orders \
  -H "Content-Type: application/json" \
  -d '{"productName":"Mouse","quantity":3,"customerEmail":"user@test.com"}'

# Consultar orden
curl http://localhost:5000/api/orders/{orderId}

# Health check de Functions vía API (service discovery)
curl http://localhost:5000/api/functions-health

# Dashboard del API
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

1. **Aspire como orquestador**: AppHost define y conecta todos los recursos
2. **Azure Functions en Aspire**: `AddAzureFunctionsProject<T>()` integra Functions nativamente
3. **Service Discovery**: La API descubre Functions automáticamente (`https+http://functions`)
4. **Recursos Azure emulados**: Storage con Azurite, Redis en Docker — sin cuenta Azure para desarrollo
5. **OpenTelemetry integrado**: Traces distribuidos entre API y Functions vía Aspire Dashboard
6. **Despliegue con azd**: Un solo comando para ir de local a Azure
