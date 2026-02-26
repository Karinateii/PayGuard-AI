#!/usr/bin/env python3
"""Write the updated README.md file."""
import os

readme_content = r"""# PayGuard AI

> AI-powered transaction risk monitoring, compliance automation, and fraud detection for cross-border payments

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![Blazor](https://img.shields.io/badge/Blazor-Server-512BD4)](https://blazor.net/)
[![Build Status](https://img.shields.io/github/actions/workflow/status/Karinateii/PayGuard-AI/build-and-test.yml?branch=main&label=build)](https://github.com/Karinateii/PayGuard-AI/actions)
[![Tests](https://img.shields.io/badge/tests-266%20passing-brightgreen)](https://github.com/Karinateii/PayGuard-AI/actions)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

## Overview

PayGuard AI is a compliance and risk tooling SaaS platform built for the [Afriex Cross-Border Fintech Hackathon](https://afriex.com). It provides financial institutions with real-time transaction monitoring, ML-powered risk scoring, a Human-in-the-Loop (HITL) review workflow, multi-tenancy, and a Rule Marketplace — all deployed on Railway with PostgreSQL.

### The Problem

Cross-border payment platforms process thousands of transactions daily. Compliance teams struggle with:
- **Manual review bottlenecks** — Can't keep up with transaction volume
- **Delayed fraud detection** — Suspicious activity caught too late
- **Inconsistent decisions** — No standardized risk assessment framework
- **Audit trail gaps** — Difficulty proving compliance to regulators
- **One-size-fits-all rules** — No industry-specific tuning for risk thresholds

### The Solution

PayGuard AI automates the first line of defense while keeping humans in control of critical decisions:

1. **Real-time Risk Scoring** — Every transaction analyzed instantly via configurable rules + ML model
2. **ML-Powered Fraud Detection** — Learns from HITL feedback, auto-retrains hourly
3. **Smart Routing** — Low-risk transactions auto-approve; high-risk ones go to human reviewers
4. **Human-in-the-Loop** — Compliance officers review, approve, or reject flagged transactions
5. **Rule Marketplace** — Pre-built industry packs (Remittance, E-Commerce, Lending, Crypto) with one-click import
6. **Complete Audit Trail** — Every decision logged for regulatory compliance
7. **Multi-Tenancy** — Full data isolation per organization with RBAC

## Features

### Core Platform
- 📊 **Live Dashboard** — Real-time stats, charts, and transaction monitoring
- 🔍 **Risk Analysis Engine** — 6 configurable rules + ML scoring with per-rule analytics
- 👥 **HITL Review Queue** — Prioritized list of transactions needing human review
- ⚡ **Real-time Updates** — SignalR-powered instant notifications
- 📋 **Rules Management** — Create, edit, toggle, and import risk detection rules
- 📈 **Compliance Reports** — Visual analytics with risk distribution charts
- 📝 **Audit Logging** — Complete history of all actions and decisions

### AI & Machine Learning
- 🤖 **ML Risk Scoring** — Binary classification model trained on HITL feedback (FastTree)
- 🔄 **Auto-Retraining** — Background service checks hourly for new labeled data
- 📊 **Model Management** — View training metrics (AUC, F1, precision/recall), activate/deactivate models
- 🧠 **Feature Engineering** — 12 features extracted from transaction context (amount, velocity, time, corridor risk)

### Rule Marketplace
- 🏪 **Template Catalog** — 24 pre-built templates across 4 industries
- 📦 **Industry Packs** — One-click import of all 6 rules optimized for your industry
- 📊 **Rule Analytics** — Per-rule effectiveness: hit rate, precision, false positive rate
- 🔄 **Import/Update** — Import new rules or update existing ones with recommended thresholds

### Enterprise Features
- 🏢 **Multi-Tenancy** — Tenant-scoped data isolation via middleware + EF Core query filters
- 🔐 **OAuth 2.0 & Magic Links** — Production-ready auth (Azure AD/Google/Okta) + passwordless login
- 👮 **RBAC** — 4-tier roles: Reviewer, Manager, Admin, SuperAdmin with custom permissions
- 🚀 **Tenant Onboarding** — Guided wizard for new organizations
- 💳 **Billing** — Paystack-powered subscription management with usage-based pricing tiers
- 📧 **Email Notifications** — Resend-powered alerts for critical risk events
- 🔑 **API Keys & Webhooks** — Self-service API key management and webhook configuration
- 💱 **Multi-Provider Support** — Afriex, Flutterwave, Wise payment provider abstraction

### Infrastructure
- 🚦 **Rate Limiting** — Fixed-window rate limiter scoped per tenant
- 💾 **Response Caching** — In-memory caching for dashboard stats and transactions
- 🚨 **Alerting Service** — Automatic alerts for critical-risk transactions
- 📡 **Health Checks** — `/health` endpoint for uptime monitoring
- 📊 **Prometheus Metrics** — Request logging with slow-request warnings
- 🚩 **Feature Flags** — Safe deployment with instant rollback
- 🐘 **PostgreSQL** — Production database on Railway (SQLite for local dev)

## Tech Stack

| Layer | Technology |
|-------|------------|
| **Frontend** | Blazor Server, MudBlazor 8.x |
| **Backend** | ASP.NET Core 10 |
| **Database** | PostgreSQL (production) / SQLite (development) |
| **ML** | ML.NET (FastTree binary classification) |
| **Real-time** | SignalR WebSockets |
| **Auth** | OAuth 2.0 / Magic Links / Demo mode |
| **Email** | Resend HTTP API |
| **Billing** | Paystack |
| **Providers** | Afriex, Flutterwave, Wise |
| **Caching** | IMemoryCache (tenant-scoped) |
| **Monitoring** | Prometheus, Health Checks, Serilog |
| **Deployment** | Railway (Docker) |
| **Architecture** | Clean Architecture (3-layer) |

## Project Structure

```
PayGuardAI/
├── src/
│   ├── PayGuardAI.Core/              # Domain entities and interfaces
│   │   ├── Entities/
│   │   │   ├── Transaction.cs         # Transaction entity
│   │   │   ├── RiskAnalysis.cs        # Risk scoring results
│   │   │   ├── RiskRule.cs            # Configurable risk rules
│   │   │   ├── RuleTemplate.cs        # Marketplace templates
│   │   │   ├── MLModel.cs             # ML model storage
│   │   │   ├── CustomerProfile.cs     # Customer risk profiles
│   │   │   ├── AuditLog.cs            # Audit trail
│   │   │   ├── TeamMember.cs          # RBAC team members
│   │   │   ├── CustomRole.cs          # Custom permission roles
│   │   │   └── ...                    # 15+ entities total
│   │   └── Services/
│   │       ├── IRiskScoringService.cs
│   │       ├── IRuleMarketplaceService.cs
│   │       ├── IMLScoringService.cs
│   │       ├── ITenantContext.cs
│   │       └── ...                    # 15+ service interfaces
│   │
│   ├── PayGuardAI.Data/              # Data access and service implementations
│   │   ├── ApplicationDbContext.cs    # EF Core context with multi-tenant filters
│   │   └── Services/
│   │       ├── RiskScoringService.cs      # Rule evaluation + ML scoring
│   │       ├── RuleMarketplaceService.cs  # Template browsing, import, analytics
│   │       ├── MLScoringService.cs        # ML prediction engine
│   │       ├── MLTrainingService.cs       # Model training pipeline
│   │       ├── TransactionService.cs      # Cached, tenant-scoped
│   │       ├── ReviewService.cs           # HITL review workflow
│   │       ├── TenantOnboardingService.cs # Guided tenant setup
│   │       ├── DatabaseMigrationService.cs # Auto-migration for PostgreSQL/SQLite
│   │       └── ...                        # 15+ service implementations
│   │
│   └── PayGuardAI.Web/              # Blazor UI, API controllers, middleware
│       ├── Components/Pages/
│       │   ├── Home.razor             # Dashboard with live stats
│       │   ├── Transactions.razor     # Transaction list with filters
│       │   ├── Reviews.razor          # HITL review queue
│       │   ├── Rules.razor            # Rule management
│       │   ├── RuleMarketplace.razor  # Template browsing + analytics
│       │   ├── MLModels.razor         # ML model management
│       │   ├── Reports.razor          # Compliance analytics
│       │   ├── Audit.razor            # Audit log viewer
│       │   ├── Send.razor             # Transaction simulator
│       │   └── ...                    # 20+ pages total
│       ├── Controllers/
│       │   └── WebhooksController.cs  # Multi-provider webhooks
│       ├── Services/
│       │   ├── TenantResolutionMiddleware.cs
│       │   ├── MLRetrainingBackgroundService.cs
│       │   └── CurrentUserService.cs
│       └── Hubs/
│           └── TransactionHub.cs      # SignalR real-time hub
│
└── tests/
    └── PayGuardAI.Tests/             # 266 tests
        ├── Services/                  # Unit tests (10 test classes)
        └── Integration/              # API integration tests
```

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Git
- Docker Desktop (optional, for containerized deployment)

### Installation

#### Option 1: Local Development (Recommended)

```bash
# Clone the repository
git clone https://github.com/Karinateii/PayGuard-AI.git
cd PayGuard-AI

# Restore dependencies
dotnet restore

# Run the application (uses SQLite by default)
cd src/PayGuardAI.Web
dotnet run

# Open in browser
open http://localhost:5054
```

#### Option 2: Docker (Production)

```bash
# Clone and start with Docker Compose
git clone https://github.com/Karinateii/PayGuard-AI.git
cd PayGuard-AI
./start-docker.sh
# Or: docker-compose up -d

# Open in browser
open http://localhost:5054

# View logs / stop
docker-compose logs -f payguard-web
docker-compose down
```

The app comes with 25 demo transactions and 24 rule templates pre-seeded for testing.

### Deployment

For production deployment to Railway, Heroku, or other cloud platforms, see [DEPLOYMENT.md](DEPLOYMENT.md) and [DOCKER-HEROKU-GUIDE.md](DOCKER-HEROKU-GUIDE.md).

## Authentication & Security

PayGuard AI supports three authentication modes:

### Development Mode (Default)
Demo authentication is enabled by default for quick testing:
- Access the dashboard immediately at `http://localhost:5054`
- Demo user: `compliance_officer@payguard.ai` (SuperAdmin)
- No login required

### Magic Link (Passwordless)
Passwordless email authentication via magic links:
- Users receive a one-time login link via email (Resend API)
- No passwords to manage or forget

### Production Mode (OAuth 2.0)
Enable enterprise-grade authentication by setting `FeatureFlags:OAuthEnabled` to `true`:

```json
{
  "FeatureFlags": { "OAuthEnabled": true },
  "OAuth": {
    "Provider": "AzureAD",
    "TenantId": "your-tenant-id",
    "ClientId": "your-client-id",
    "ClientSecret": "your-client-secret"
  }
}
```

**Supported Providers:** Azure AD, Google Workspace, Okta, any OIDC provider.

### Role-Based Access Control

| Role | Access Level |
|------|-------------|
| **Reviewer** | View transactions, approve/reject flagged items |
| **Manager** | + Rules, Billing, Audit, Rule Marketplace |
| **Admin** | + Team, API Keys, Webhooks, Analytics, ML Models, Organization Settings |
| **SuperAdmin** | + Tenant Management (platform owner) |

## Multi-Provider Integration

PayGuard AI supports multiple payment providers through a unified abstraction layer:

| Provider | Endpoint | Status |
|----------|----------|--------|
| **Afriex** | `POST /api/webhooks/afriex` | ✅ Always enabled |
| **Flutterwave** | `POST /api/webhooks/flutterwave` | ⚙️ Feature flag |
| **Wise** | `POST /api/webhooks/wise` | ⚙️ Feature flag |

### Testing Webhooks

```bash
# Afriex
curl -X POST http://localhost:5054/api/webhooks/afriex \
  -H "Content-Type: application/json" \
  -H "X-Afriex-Signature: test-signature" \
  -d '{"event":"transaction.completed","data":{"id":"TEST-001","type":"send","status":"completed","amount":500,"currency":"USD","source_country":"US","destination_country":"NG","customer":{"id":"cust-001","email":"test@example.com","name":"Test User"},"created_at":"2026-02-26T10:00:00Z"}}'
```

## Risk Scoring

### Rule-Based Scoring (6 configurable rules)

| Rule Code | Description | Default Threshold |
|-----------|-------------|-------------------|
| `HIGH_AMOUNT` | Large transaction amount | $5,000 |
| `VELOCITY_24H` | Too many transactions in 24h | 5 txns |
| `NEW_CUSTOMER` | First-time or new customer | < 3 txns |
| `HIGH_RISK_CORRIDOR` | OFAC-sanctioned countries | IR, KP, SY, YE, VE, CU |
| `ROUND_AMOUNT` | Suspiciously round amounts | $1,000 |
| `UNUSUAL_TIME` | Transactions at 2-5 AM UTC | Always flags |

### ML Risk Scoring

The ML model augments rule-based scoring with learned patterns:
- **Algorithm:** FastTree binary classification (ML.NET)
- **Features:** 12 dimensions including amount, velocity, time, corridor risk, customer history
- **Training:** Learns from HITL review decisions (Approved = legitimate, Rejected = fraud)
- **Auto-retraining:** Background service checks hourly, retrains when 50+ new labeled samples exist
- **Model management:** View metrics, compare versions, activate/deactivate from Admin panel

### Risk Levels

| Level | Score Range | Action |
|-------|-------------|--------|
| Low | 0–25 | Auto-approved |
| Medium | 26–50 | Flagged for review |
| High | 51–75 | Requires manual review |
| Critical | 76–100 | Requires manual review |

## Rule Marketplace

Pre-built rule templates optimized for different industries:

| Industry | Templates | Example Threshold |
|----------|-----------|-------------------|
| **Remittance** | 6 rules | HIGH_AMOUNT: $10,000, VELOCITY: 3/day |
| **E-Commerce** | 6 rules | HIGH_AMOUNT: $2,000, VELOCITY: 15/day |
| **Lending** | 6 rules | HIGH_AMOUNT: $5,000, VELOCITY: 2/day |
| **Crypto** | 6 rules | HIGH_AMOUNT: $50,000, VELOCITY: 10/day |

**Features:**
- Browse and filter by industry, category, or keyword
- One-click import of individual templates or entire industry packs
- Rule analytics with precision, hit rate, and false positive tracking
- Import count (popularity) tracking across tenants

## Testing

```bash
# Run all 266 tests
dotnet test

# Run with detailed output
dotnet test --logger "console;verbosity=detailed"

# Run specific test class
dotnet test --filter "RuleMarketplaceServiceTests"
```

### Test Coverage

| Test Class | Tests | Coverage |
|------------|-------|----------|
| RuleMarketplaceServiceTests | 25 | Template browsing, import, analytics |
| TenantOnboardingTests | 16 | Tenant setup, rule seeding |
| RbacServiceTests | 24 | Roles, permissions, team management |
| MLFeatureExtractorTests | 20 | Feature extraction for ML |
| SecurityMiddlewareTests | 15 | Auth, rate limiting, CORS |
| AfriexProviderTests | 30 | Afriex API integration |
| FlutterwaveProviderTests | 28 | Flutterwave normalization |
| WiseProviderTests | 20 | Wise transfer mapping |
| PaymentProviderFactoryTests | 48 | Factory pattern, provider selection |
| Integration Tests | 40 | End-to-end webhook processing |

### Continuous Integration

GitHub Actions workflow runs on every push:
- ✅ Multi-platform testing (Ubuntu, Windows, macOS)
- ✅ Code quality checks
- ✅ Security vulnerability scanning

## Database

### Local Development (SQLite)
```bash
# Reset database
rm src/PayGuardAI.Web/payguardai.db
dotnet run  # Auto-recreates with seed data
```

### Production (PostgreSQL)
PostgreSQL is enabled via feature flag. The `DatabaseMigrationService` automatically:
- Creates all tables if they don't exist
- Adds missing columns to existing tables
- Seeds default data (rules, templates, demo transactions)
- Fixes indexes for multi-tenancy

```json
{
  "FeatureFlags": { "PostgresEnabled": true },
  "ConnectionStrings": {
    "PostgreSQL": "Host=...;Database=payguard;Username=...;Password=..."
  }
}
```

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/webhooks/afriex` | Receive Afriex transaction webhooks |
| POST | `/api/webhooks/flutterwave` | Receive Flutterwave webhooks |
| POST | `/api/webhooks/wise` | Receive Wise webhooks |
| GET | `/health` | Application health check |
| GET | `/transactionHub` | SignalR real-time connection |

## Multi-Tenancy

Each organization gets fully isolated data:

- **Middleware-based resolution:** `X-Tenant-Id` header or email→tenant lookup
- **EF Core query filters:** All queries automatically scoped to current tenant
- **Tenant onboarding:** Guided wizard seeds rules, settings, and team
- **Default tenant:** `afriex-demo` for development

```json
{
  "MultiTenancy": {
    "DefaultTenantId": "afriex-demo"
  }
}
```

## Configuration

Key settings in `appsettings.json`:

```json
{
  "FeatureFlags": {
    "OAuthEnabled": false,
    "PostgresEnabled": false,
    "FlutterwaveEnabled": false,
    "WiseEnabled": false
  },
  "Auth": {
    "DefaultUser": "compliance_officer@payguard.ai",
    "DefaultRoles": "Reviewer,Manager,Admin,SuperAdmin"
  },
  "RateLimiting": {
    "PermitLimit": 60,
    "WindowSeconds": 60
  },
  "Afriex": {
    "BaseUrl": "https://staging.afx-server.com",
    "ApiKey": "your-api-key"
  }
}
```

## Hackathon Track

**Compliance and Risk Tooling** — Building tools that help fintech companies maintain regulatory compliance while processing cross-border payments efficiently.

## Contributing

This project was built for the Afriex Cross-Border Fintech Hackathon. Contributions, issues, and feature requests are welcome!

## License

MIT License — see [LICENSE](LICENSE) file for details.

## Acknowledgments

- [Afriex](https://afriex.com) for the hackathon opportunity and API documentation
- [MudBlazor](https://mudblazor.com) for the Blazor component library
- [ML.NET](https://dotnet.microsoft.com/apps/machinelearning-ai/ml-dotnet) for the machine learning framework
- The ASP.NET Core team for SignalR and the middleware pipeline

---

Built with ❤️ for the Cross-Border Fintech Hackathon 2026
"""

readme_path = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "README.md")
with open(readme_path, "w", encoding="utf-8") as f:
    f.write(readme_content.lstrip("\n"))

print(f"README.md written successfully to {readme_path}")
print(f"Size: {os.path.getsize(readme_path)} bytes")
