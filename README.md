# PayGuard AI

> Real-time transaction risk monitoring and compliance system for cross-border payments

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![Blazor](https://img.shields.io/badge/Blazor-Server-512BD4)](https://blazor.net/)
[![Build Status](https://img.shields.io/github/actions/workflow/status/Karinateii/PayGuard-AI/build-and-test.yml?branch=main&label=build)](https://github.com/Karinateii/PayGuard-AI/actions)
[![Tests](https://img.shields.io/badge/tests-70%2F70%20passing-brightgreen)](https://github.com/Karinateii/PayGuard-AI/actions)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

## Overview

PayGuard AI is a compliance and risk tooling solution built for the [Afriex Cross-Border Fintech Hackathon](https://afriex.com). It provides financial institutions with real-time transaction monitoring, automated risk scoring, and a Human-in-the-Loop (HITL) review workflow for flagged transactions.

### The Problem

Cross-border payment platforms process thousands of transactions daily. Compliance teams struggle with:
- **Manual review bottlenecks** - Can't keep up with transaction volume
- **Delayed fraud detection** - Suspicious activity caught too late
- **Inconsistent decisions** - No standardized risk assessment framework
- **Audit trail gaps** - Difficulty proving compliance to regulators

### The Solution

PayGuard AI automates the first line of defense while keeping humans in control of critical decisions:

1. **Real-time Risk Scoring** - Every transaction is analyzed instantly using configurable rules
2. **Smart Routing** - Low-risk transactions auto-approve; high-risk ones go to human reviewers
3. **Human-in-the-Loop** - Compliance officers review, approve, or reject flagged transactions
4. **Complete Audit Trail** - Every decision is logged for regulatory compliance

## Features

- 📊 **Live Dashboard** - Real-time stats, charts, and transaction monitoring
- 🔍 **Risk Analysis Engine** - Configurable rules-based scoring system
- 👥 **HITL Review Queue** - Prioritized list of transactions needing human review
- ⚡ **Real-time Updates** - SignalR-powered instant notifications
- 📋 **Rules Management** - Create, edit, and toggle risk detection rules
- 📈 **Compliance Reports** - Visual analytics with risk distribution charts
- 📝 **Audit Logging** - Complete history of all actions and decisions
- � **Multi-Provider Support** - Unified abstraction for Afriex, Flutterwave, Wise, and other payment platforms
- 🔐 **OAuth 2.0 & MFA** - Production-ready authentication with Azure AD/Google/Okta + TOTP two-factor authentication
- 🏢 **Multi-Tenancy** - Tenant-scoped data isolation via middleware
- 🚦 **Rate Limiting** - Fixed-window rate limiter scoped per tenant
- 💾 **Response Caching** - In-memory caching for dashboard stats, transactions, and exchange rates
- 🚨 **Alerting Service** - Automatic alerts for critical-risk transactions
- 📡 **Health Checks** - `/health` endpoint for uptime monitoring
- 📊 **Request Logging** - Structured request/response logging with slow-request warnings
- 🚩 **Feature Flags** - Safe deployment with instant rollback (OAuth, PostgreSQL, Flutterwave)

## Tech Stack

| Layer | Technology |
|-------|------------|
| **Frontend** | Blazor Server, MudBlazor 8.x |
| **Backend** | ASP.NET Core 10 |
| **Database** | SQLite with Entity Framework Core |
| **Real-time** | SignalR WebSockets |
| **Auth** | OAuth 2.0 / OpenID Connect + TOTP MFA |
| **Providers** | Afriex (primary), Flutterwave (feature flag) |
| **Caching** | IMemoryCache (tenant-scoped) |
| **Rate Limiting** | ASP.NET Core Rate Limiting (per-tenant) |
| **Monitoring** | Health checks, structured request logging |
| **API** | Afriex Business API |
| **Architecture** | Clean Architecture (3-layer) |

## Project Structure

```
PayGuardAI/
├── src/
│   ├── PayGuardAI.Core/          # Domain entities and interfaces
│   │   ├── Entities/
│   │   │   ├── Transaction.cs
│   │   │   ├── RiskAnalysis.cs
│   │   │   ├── RiskRule.cs
│   │   │   └── AuditLog.cs
│   │   └── Services/
│   │       ├── ITenantContext.cs        # Multi-tenancy interface
│   │       ├── IAlertingService.cs      # Alerting interface
│   │       ├── IRiskScoringService.cs
│   │       ├── IReviewService.cs
│   │       └── ITransactionService.cs
│   │
│   ├── PayGuardAI.Data/          # Data access and services
│   │   ├── ApplicationDbContext.cs
│   │   └── Services/
│   │       ├── TransactionService.cs    # Cached, tenant-scoped
│   │       ├── RiskScoringService.cs    # With alerting
│   │       ├── ReviewService.cs         # Cache-invalidating
│   │       ├── AfriexApiService.cs      # Cached API client
│   │       ├── TenantContext.cs         # Tenant resolution
│   │       ├── AlertingService.cs       # Alert dispatcher
│   │       └── WebhookSignatureService.cs
│   │
│   └── PayGuardAI.Web/           # Blazor UI and API controllers
│       ├── Components/
│       │   ├── Pages/
│       │   │   ├── Home.razor         # Dashboard
│       │   │   ├── Transactions.razor # Transaction list
│       │   │   ├── Reviews.razor      # HITL review queue
│       │   │   ├── Rules.razor        # Rules management
│       │   │   ├── Reports.razor      # Analytics
│       │   │   ├── Audit.razor        # Audit log
│       │   │   └── Send.razor         # Send money
│       │   └── Layout/
│       ├── Controllers/
│       │   └── WebhooksController.cs  # Webhook endpoint
│       ├── Services/
│       │   ├── DemoAuthenticationHandler.cs  # Auth handler
│       │   ├── TenantResolutionMiddleware.cs # Multi-tenancy
│       │   ├── RequestLoggingMiddleware.cs   # Observability
│       │   └── CurrentUserService.cs         # User identity
│       └── Hubs/
│           └── TransactionHub.cs      # SignalR hub
```

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Git

### Installation

1. **Clone the repository**
   ```bash
   git clone https://github.com/Karinateii/PayGuard-AI.git
   cd PayGuard-AI
   ```

2. **Restore dependencies**
   ```bash
   dotnet restore
   ```

3. **Run the application**
   ```bash
   cd src/PayGuardAI.Web
   dotnet run
   ```

4. **Open in browser**
   ```
   http://localhost:5054
   ```

The app comes with 25 demo transactions pre-seeded for testing.

## Authentication & Security

PayGuard AI supports two authentication modes via feature flags:

### Development Mode (Default)
Demo authentication is enabled by default for quick testing:
- Access the dashboard immediately at `http://localhost:5054`
- Demo user: `compliance_officer@payguard.ai` (Reviewer, Manager roles)
- No login required

### Production Mode (OAuth 2.0 + MFA)

Enable enterprise-grade authentication by setting `FeatureFlags:OAuthEnabled` to `true` in `appsettings.json`:

```json
{
  "FeatureFlags": {
    "OAuthEnabled": true
  },
  "OAuth": {
    "Provider": "AzureAD",
    "TenantId": "your-tenant-id",
    "ClientId": "your-client-id",
    "ClientSecret": "your-client-secret",
    "Authority": "https://login.microsoftonline.com/{tenant}/v2.0"
  },
  "Mfa": {
    "EnforceMfaForAll": false,
    "RequiredMfaRoles": ["Admin", "Manager"]
  }
}
```

**Supported OAuth Providers:**
- Azure Active Directory (Azure AD)
- Google Workspace
- Okta
- Any OpenID Connect compliant provider

**Multi-Factor Authentication (TOTP):**
- RFC 6238 compliant TOTP implementation
- Works with Google Authenticator, Microsoft Authenticator, Authy
- Backup codes for account recovery
- Role-based MFA enforcement
- QR code setup flow at `/mfa/setup`

**Quick Switch:**
Toggle between Demo and OAuth mode instantly — no code changes, just restart the app after updating the feature flag.

### Configure Afriex API (Optional)

To use real Afriex API integration:

```bash
cd src/PayGuardAI.Web
dotnet user-secrets set "Afriex:ApiKey" "your_api_key_here"
```

## Multi-Provider Integration

PayGuard AI supports multiple payment providers through a unified abstraction layer. Add new providers without changing core business logic.

### Supported Providers

**Afriex** (Primary)
- Endpoint: `POST /api/webhooks/afriex`
- Status: ✅ Always enabled
- Configuration: `Afriex:ApiKey`, `Afriex:WebhookPublicKey`

**Flutterwave** (Feature Flag)
- Endpoint: `POST /api/webhooks/flutterwave`
- Status: ⚙️ Enable with `FeatureFlags:FlutterwaveEnabled = true`
- Configuration: `Flutterwave:SecretKey`, `Flutterwave:WebhookSecretHash`

**Adding New Providers:**
1. Implement `IPaymentProvider` interface
2. Add configuration to appsettings.json
3. Register in `Program.cs`
4. Add webhook endpoint in `WebhooksController`

### Provider Configuration

```json
{
  "FeatureFlags": {
    "FlutterwaveEnabled": false
  },
  "Afriex": {
    "BaseUrl": "https://staging.afx-server.com",
    "ApiKey": "your-api-key",
    "WebhookPublicKey": "your-webhook-public-key"
  },
  "Flutterwave": {
    "BaseUrl": "https://api.flutterwave.com/v3",
    "SecretKey": "your-secret-key",
    "WebhookSecretHash": "your-webhook-hash"
  }
}
```

## Webhook Integration

PayGuard AI receives transaction data via webhooks with automatic normalization across providers.

### Webhook Endpoints

```
POST /api/webhooks/afriex       # Afriex transactions
POST /api/webhooks/flutterwave  # Flutterwave transactions
POST /api/webhooks/transaction  # Legacy (defaults to Afriex)
```

### Supported Events

**Afriex:**
- `TRANSACTION.CREATED` - New transaction received
- `TRANSACTION.UPDATED` - Transaction status changed

**Flutterwave:**
- `charge.completed` - Payment successful
- `transfer.completed` - Transfer completed

### Example Payloads

**Afriex:**
```json
{
  "event": "TRANSACTION.CREATED",
  "data": {
    "transactionId": "TXN-123",
    "sourceAmount": "500",
    "sourceCurrency": "USD",
    "destinationAmount": "750000",
    "destinationCurrency": "NGN",
    "status": "PENDING",
    "customerId": "cust-001"
  }
}
```

**Flutterwave:**
```json
{
  "event": "charge.completed",
  "data": {
    "id": 123456,
    "tx_ref": "FLW-TXN-123",
    "amount": 500,
    "currency": "USD",
    "status": "successful",
    "customer": {
      "email": "customer@example.com"
    }
  }
}
```

## Testing

PayGuard AI includes comprehensive unit and integration tests to ensure reliability.

### Running Tests

```bash
# Run all tests
dotnet test

# Run with detailed output
dotnet test --logger "console;verbosity=detailed"

# Run with code coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Test Coverage

- **Unit Tests**: 60+ tests covering payment providers, factory pattern, and business logic
- **Integration Tests**: API endpoint testing with WebApplicationFactory
- **CI/CD**: Automated testing on push via GitHub Actions

### Test Structure

```
tests/
├── Services/
│   ├── AfriexProviderTests.cs          # Afriex provider unit tests
│   ├── FlutterwaveProviderTests.cs     # Flutterwave provider unit tests
│   └── PaymentProviderFactoryTests.cs  # Factory pattern tests
└── Integration/
    ├── PayGuardApiWebApplicationFactory.cs
    └── WebhooksControllerIntegrationTests.cs
```

### Continuous Integration

GitHub Actions workflow runs on every push:
- ✅ Multi-platform testing (Ubuntu, Windows, macOS)
- ✅ Code quality checks
- ✅ Security vulnerability scanning
- ✅ Test result reporting

### Testing Webhooks

**Afriex:**
```bash
curl -X POST http://localhost:5054/api/webhooks/afriex \
  -H "Content-Type: application/json" \
  -d '{"event":"TRANSACTION.CREATED","data":{"transactionId":"TEST-001","sourceAmount":"100","sourceCurrency":"USD","destinationAmount":"150000","destinationCurrency":"NGN","status":"PENDING"}}'
```

**Flutterwave:**
```bash
curl -X POST http://localhost:5054/api/webhooks/flutterwave \
  -H "Content-Type: application/json" \
  -H "verif-hash: your-webhook-hash" \
  -d '{"event":"charge.completed","data":{"id":123,"amount":100,"currency":"USD","status":"successful"}}'
```

## Risk Scoring

Transactions are scored based on configurable rules:

| Risk Level | Score Range | Action |
|------------|-------------|--------|
| Low | 0-25 | Auto-approved |
| Medium | 26-50 | Flagged for review |
| High | 51-75 | Requires manual review |
| Critical | 76-100 | Requires manual review |

### Default Risk Factors

- Large transaction amounts (>$5,000)
- New customer accounts
- High-risk destination countries
- Unusual transaction patterns
- Velocity checks (frequency)

## Screenshots

### Dashboard
Real-time overview with key metrics and recent high-risk transactions.

### Review Queue
Human-in-the-Loop interface for compliance officers to approve/reject flagged transactions.

### Rules Management
Configure and toggle risk detection rules without code changes.

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/webhooks/transaction` | Receive transaction webhooks |
| GET | `/transactionHub` | SignalR connection for real-time updates |

## Development

### Build

```bash
dotnet build
```

### Security & Production Features

#### Authentication & RBAC
PayGuard AI uses a pluggable authentication scheme with role-based access control:

| Role | Permissions |
|------|-------------|
| **Reviewer** | Approve/reject transactions, view dashboards |
| **Manager** | All Reviewer permissions + escalation handling |
| **Admin** | Full system access including rule management |

Configure the default user in `appsettings.json`:
```json
{
  "Auth": {
    "DefaultUser": "compliance_officer@payguard.ai",
    "DefaultRoles": "Reviewer,Manager"
  }
}
```

#### Multi-Tenancy
Tenant isolation is handled via middleware. Each request is scoped to a tenant:
- Default tenant: `afriex-demo`
- Override per request: `X-Tenant-Id` header
- Cache keys, rate limits, and data are all tenant-scoped

#### Rate Limiting
Fixed-window rate limiting protects API endpoints:
```json
{
  "RateLimiting": {
    "PermitLimit": 60,
    "WindowSeconds": 60
  }
}
```

#### Health Checks
Monitor application health at `GET /health` — returns `Healthy` / `Unhealthy` with component status.

#### Caching Strategy
| Resource | TTL | Invalidation |
|----------|-----|--------------|
| Dashboard stats | 10s | On review action |
| Transaction list | 15s | On webhook received |
| Exchange rates | 30s | Time-based expiry |

### Database

The app uses SQLite. The database is created automatically on first run. To reset:

```bash
rm src/PayGuardAI.Web/payguardai.db
dotnet run
```

## Hackathon Track

**Compliance and Risk Tooling** - Building tools that help fintech companies maintain regulatory compliance while processing cross-border payments efficiently.

## Contributing

This project was built for the Afriex Cross-Border Fintech Hackathon. Contributions, issues, and feature requests are welcome!

## License

MIT License - see [LICENSE](LICENSE) file for details.

## Acknowledgments

- [Afriex](https://afriex.com) for the hackathon opportunity and API documentation
- [MudBlazor](https://mudblazor.com) for the excellent Blazor component library
- The ASP.NET Core team for SignalR real-time capabilities

---

Built with ❤️ for the Cross-Border Fintech Hackathon 2026
