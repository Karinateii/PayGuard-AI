# PayGuard AI

> AI-powered transaction risk monitoring, compliance automation, and fraud detection for cross-border payments

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![Blazor](https://img.shields.io/badge/Blazor-Server-512BD4)](https://blazor.net/)
[![Build Status](https://img.shields.io/github/actions/workflow/status/Karinateii/PayGuard-AI/build-and-test.yml?branch=main&label=build)](https://github.com/Karinateii/PayGuard-AI/actions)
[![Tests](https://img.shields.io/badge/tests-266%20passing-brightgreen)](https://github.com/Karinateii/PayGuard-AI/actions)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

🌐 **Live Demo:** [https://payguard-ai-production.up.railway.app](https://payguard-ai-production.up.railway.app)

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
- 📊 **Live Dashboard** — Real-time stats, charts, risk distribution, and transaction monitoring
- 🔍 **Risk Analysis Engine** — 6 configurable rules + ML scoring with per-rule analytics
- 👥 **HITL Review Queue** — Prioritized list of transactions needing human review
- ⚡ **Real-time Updates** — SignalR-powered instant notifications
- 📋 **Rules Management** — Create, edit, toggle, and import risk detection rules
- 📈 **Compliance Reports** — Visual analytics with risk distribution charts and CSV export
- 📝 **Audit Logging** — Complete history of all actions and decisions

### AI & Machine Learning
- 🤖 **ML Risk Scoring** — Binary classification model trained on HITL feedback (FastTree)
- 🔄 **Auto-Retraining** — Background service checks hourly for new labeled data
- 📊 **Model Management** — View training metrics (AUC, F1, precision/recall), activate/deactivate models
- 🧠 **Feature Engineering** — 12 features extracted from transaction context (amount, velocity, time, corridor risk)
- 💡 **Smart Rule Suggestions** — ML-driven analysis of review patterns to suggest new rules and threshold adjustments

### Rule Marketplace
- 🏪 **Template Catalog** — 24 pre-built templates across 4 industries
- 📦 **Industry Packs** — One-click import of all 6 rules optimized for your industry
- 📊 **Rule Analytics** — Per-rule effectiveness: hit rate, precision, false positive rate
- 🔄 **Import/Update** — Import new rules or update existing ones with recommended thresholds
- 📜 **Rule Versioning** — Full version history with diff comparison and rollback

### Fraud Detection & Intelligence
- 🕸️ **Fan-out/Fan-in Detection** — Network analysis to detect structuring rings where one sender splits to many receivers or many senders funnel to one receiver
- 🚫 **Watchlists & Blocklists** — Custom watchlists with automatic matching against transactions using name/email/country criteria
- 🔗 **Relationship Analysis** — Graph-based visualization of transaction networks between entities

### Compliance & Reporting
- 🛡️ **GDPR Compliance** — Data subject search, export (JSON/CSV), right-to-erasure with full audit trail
- 📄 **Invoice PDF Generation** — QuestPDF-powered professional invoices with automatic numbering and PDF download
- 📊 **Advanced Reports** — Scheduled report generation with background processing and viewer dialog
- 🧾 **System Logs** — Centralized, structured logging with retention policies and level-based filtering

### Enterprise Features
- 🏢 **Multi-Tenancy** — Tenant-scoped data isolation via middleware + EF Core query filters
- 🔐 **OAuth 2.0 & Magic Links** — Production-ready auth (Azure AD/Google/Okta) + passwordless login
- 👮 **RBAC** — 4-tier roles: Reviewer, Manager, Admin, SuperAdmin with custom permissions
- 🚀 **Tenant Onboarding** — Guided wizard for new organizations
- 💳 **Billing** — Paystack-powered subscription management with usage-based pricing tiers (Trial/Starter/Pro/Enterprise)
- 📧 **Email Notifications** — Resend-powered alerts for critical risk events with per-user preferences
- 🔑 **API Keys & Webhooks** — Self-service API key management and webhook configuration with signature verification
- 💱 **Multi-Provider Support** — Afriex, Flutterwave, Wise payment provider abstraction

### Operations & Monitoring
- 📡 **Monitoring Dashboard** — Real-time operational metrics: throughput, error rates, risk distribution, review queue depth, webhook activity, and 7-day trends
- 🚦 **Rate Limiting** — Fixed-window rate limiter scoped per tenant
- 💾 **Response Caching** — In-memory caching for dashboard stats and transactions
- 🚨 **Alerting Service** — Automatic alerts for critical-risk transactions
- 📡 **Health Checks** — `/health` endpoint for uptime monitoring
- 📊 **Prometheus Metrics** — `/metrics` endpoint with request timing and slow-request warnings
- 🚩 **Feature Flags** — Safe deployment with instant rollback
- 🐘 **PostgreSQL** — Production database on Railway (SQLite for local dev)
- 🔒 **Security Hardened** — No eval() injection, Swagger restricted to dev, secure cookie policies, sanitized error messages

### Mobile & PWA
- 📱 **Progressive Web App** — Installable on mobile with offline shell caching
- 🔽 **Mobile Bottom Navigation** — Touch-friendly nav bar with badge counts
- 👆 **Swipe-to-Review** — Swipe right to approve, left to reject on mobile review queue
- 🔄 **Pull-to-Refresh** — Touch-native refresh gesture on review and transaction lists
- 📐 **Responsive Layout** — Auto-closing drawer on mobile, compact cards, 48px touch targets
- 🔔 **Real-time Alerts** — SignalR-powered instant notifications and toast alerts for new transactions

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
| **PDF** | QuestPDF |
| **Providers** | Afriex, Flutterwave, Wise |
| **Caching** | IMemoryCache (tenant-scoped) |
| **Monitoring** | Prometheus, Health Checks, Serilog |
| **Deployment** | Railway (Docker) |
| **Architecture** | Clean Architecture (3-layer) |

## Project Structure

```
PayGuardAI/
├── src/
│   ├── PayGuardAI.Core/                  # Domain entities and interfaces
│   │   ├── Entities/                      # 23 entities
│   │   │   ├── Transaction.cs             # Transaction entity
│   │   │   ├── RiskAnalysis.cs            # Risk scoring results
│   │   │   ├── RiskRule.cs                # Configurable risk rules
│   │   │   ├── RuleTemplate.cs            # Marketplace templates
│   │   │   ├── RuleVersion.cs             # Rule version history
│   │   │   ├── RuleGroup.cs               # Compound rule groups
│   │   │   ├── MLModel.cs                 # ML model storage
│   │   │   ├── CustomerProfile.cs         # Customer risk profiles
│   │   │   ├── AuditLog.cs                # Audit trail
│   │   │   ├── SystemLog.cs               # Centralized system logs
│   │   │   ├── TeamMember.cs              # RBAC team members
│   │   │   ├── CustomRole.cs              # Custom permission roles
│   │   │   ├── Invoice.cs                 # Billing invoices
│   │   │   ├── Watchlist.cs               # Watchlists & blocklists
│   │   │   ├── WatchlistEntry.cs          # Watchlist entries
│   │   │   ├── WebhookEndpoint.cs         # Webhook configuration
│   │   │   ├── TenantSubscription.cs      # Billing subscriptions
│   │   │   ├── OrganizationSettings.cs    # Tenant settings
│   │   │   └── ...                        # ApiKey, MagicLinkToken, etc.
│   │   └── Services/                      # 23 service interfaces
│   │       ├── IRiskScoringService.cs
│   │       ├── IRuleMarketplaceService.cs
│   │       ├── IRuleSuggestionService.cs
│   │       ├── IMLScoringService.cs
│   │       ├── IWatchlistService.cs
│   │       ├── IRelationshipAnalysisService.cs
│   │       ├── IGdprService.cs
│   │       ├── ITenantContext.cs
│   │       └── ...
│   │
│   ├── PayGuardAI.Data/                  # Data access and service implementations
│   │   ├── ApplicationDbContext.cs        # EF Core context with multi-tenant query filters
│   │   └── Services/                      # 34 service implementations
│   │       ├── RiskScoringService.cs          # Rule evaluation + ML scoring
│   │       ├── RuleMarketplaceService.cs      # Template browsing, import, analytics
│   │       ├── RuleSuggestionService.cs       # ML-driven rule suggestions
│   │       ├── RuleVersioningService.cs       # Rule version tracking
│   │       ├── MLScoringService.cs            # ML prediction engine
│   │       ├── MLTrainingService.cs           # Model training pipeline
│   │       ├── TransactionService.cs          # Cached, tenant-scoped
│   │       ├── ReviewService.cs               # HITL review workflow
│   │       ├── WatchlistService.cs            # Watchlist matching
│   │       ├── RelationshipAnalysisService.cs # Fan-out/fan-in detection
│   │       ├── GdprService.cs                 # GDPR data operations
│   │       ├── InvoiceService.cs              # Invoice CRUD
│   │       ├── MonitoringService.cs           # Real-time operational metrics
│   │       ├── TenantOnboardingService.cs     # Guided tenant setup
│   │       ├── DatabaseMigrationService.cs    # Auto-migration for PostgreSQL/SQLite
│   │       ├── WebhookDeliveryService.cs      # Webhook dispatch + retry
│   │       └── ...
│   │
│   └── PayGuardAI.Web/                   # Blazor UI, API controllers, middleware
│       ├── Components/Pages/              # 47 pages/dialogs
│       │   ├── Home.razor                 # Dashboard with live stats
│       │   ├── Transactions.razor         # Transaction list with filters
│       │   ├── Reviews.razor              # HITL review queue
│       │   ├── Rules.razor                # Rule management + suggestions
│       │   ├── RuleMarketplace.razor      # Template browsing + analytics
│       │   ├── MLModels.razor             # ML model management
│       │   ├── Reports.razor              # Compliance analytics + CSV export
│       │   ├── Audit.razor                # Audit log viewer
│       │   ├── Send.razor                 # Transaction simulator
│       │   ├── NetworkAnalysis.razor      # Fan-out/fan-in graph visualization
│       │   ├── Watchlists.razor           # Watchlist management
│       │   ├── GdprCompliance.razor       # GDPR search, export, erasure
│       │   ├── Invoices.razor             # Invoice management + PDF download
│       │   ├── Monitoring.razor           # Operational monitoring dashboard
│       │   ├── SystemLogs.razor           # Centralized log viewer
│       │   └── ...                        # Billing, Profile, Settings, etc.
│       ├── Controllers/                   # 6 API controllers
│       │   ├── WebhooksController.cs      # Multi-provider webhooks
│       │   ├── AuthController.cs          # Auth endpoints (OAuth, magic link, demo)
│       │   ├── InvoiceController.cs       # PDF download endpoint
│       │   └── ...
│       ├── Services/                      # 20 middleware & background services
│       │   ├── TenantResolutionMiddleware.cs
│       │   ├── SecurityHeadersMiddleware.cs
│       │   ├── InputValidationMiddleware.cs
│       │   ├── MLRetrainingBackgroundService.cs
│       │   ├── ScheduledReportBackgroundService.cs
│       │   ├── LogRetentionBackgroundService.cs
│       │   ├── InvoicePdfService.cs
│       │   └── ...
│       ├── Hubs/
│       │   └── TransactionHub.cs          # SignalR real-time hub
│       └── wwwroot/
│           └── js/payguard.js             # Safe JS interop helpers
│
└── tests/
    └── PayGuardAI.Tests/                  # 266 tests
        ├── Services/                       # 10 unit test classes
        │   ├── RuleMarketplaceServiceTests.cs
        │   ├── TenantOnboardingTests.cs
        │   ├── RbacServiceTests.cs
        │   ├── MLFeatureExtractorTests.cs
        │   ├── SecurityMiddlewareTests.cs
        │   ├── AfriexProviderTests.cs
        │   ├── FlutterwaveProviderTests.cs
        │   ├── WiseProviderTests.cs
        │   ├── PaymentProviderFactoryTests.cs
        │   └── TenantIsolationTests.cs
        └── Integration/                    # API integration tests
            ├── WebhooksControllerIntegrationTests.cs
            └── SecurityIntegrationTests.cs
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

### Security Hardening

- **No `eval()` injection** — All JS interop uses safe, parameterised helper functions (`wwwroot/js/payguard.js`)
- **Swagger restricted to development** — API docs are not exposed in production
- **Secure cookies** — `HttpOnly`, `SameSite=Lax`, `SecurePolicy=Always`
- **Sanitized error messages** — Exception details never leak to the UI; generic errors shown to users with full stack traces logged server-side
- **Security headers** — CSP, X-Content-Type-Options, X-Frame-Options, Referrer-Policy via middleware
- **Input validation** — Request validation middleware rejects malformed payloads
- **Webhook signature verification** — HMAC-based verification for all inbound webhooks

### Role-Based Access Control

| Role | Access Level |
|------|-------------|
| **Reviewer** | View transactions, approve/reject flagged items, view reports |
| **Manager** | + Rules, Billing, Invoices, Audit, Rule Marketplace, Watchlists |
| **Admin** | + Team, API Keys, Webhooks, Analytics, ML Models, Organization Settings, Monitoring, System Logs, GDPR |
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
| `UNUSUAL_TIME` | Transactions at 2–5 AM UTC | Always flags |

### ML Risk Scoring

The ML model augments rule-based scoring with learned patterns:
- **Algorithm:** FastTree binary classification (ML.NET)
- **Features:** 12 dimensions including amount, velocity, time, corridor risk, customer history
- **Training:** Learns from HITL review decisions (Approved = legitimate, Rejected = fraud)
- **Auto-retraining:** Background service checks hourly, retrains when 50+ new labeled samples exist
- **Model management:** View metrics, compare versions, activate/deactivate from Admin panel

### Smart Rule Suggestions

The platform analyzes review patterns and transaction data to automatically suggest:
- **New rules** based on frequently-rejected transaction characteristics
- **Threshold adjustments** when existing rules under- or over-flag
- **One-click apply** to immediately enable suggested rules

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

## Fraud Intelligence

### Fan-out / Fan-in Detection

Network graph analysis identifies structuring rings:
- **Fan-out:** One sender splitting transactions across many receivers to stay below thresholds
- **Fan-in:** Many senders funnelling money to a single receiver
- Interactive graph visualization on the Network Analysis page
- Configurable thresholds and time windows

### Watchlists & Blocklists

- Create custom watchlists with name, email, and country criteria
- Automatic real-time matching against incoming transactions
- Manual override options for compliance officers
- Bulk import/export support

## GDPR Compliance

Full General Data Protection Regulation tooling:
- **Data Subject Search** — Find all data for a customer by email or name
- **Data Export** — One-click export of all customer data in JSON or CSV format
- **Right to Erasure** — Anonymize or delete customer data with confirmation dialog
- **Audit Trail** — Every GDPR action is logged for regulatory proof

## Monitoring & Observability

### Operational Dashboard (`/admin/monitoring`)
Real-time operational health with 30-second auto-refresh:
- **Health Banner** — Healthy / Warning / Degraded status based on error rate
- **Throughput Metrics** — 24h transaction count with hourly breakdown chart
- **Risk Distribution** — Donut chart of risk level distribution
- **7-Day Trend** — Daily transaction volume bar chart
- **Error Rate** — Percentage of error-level system logs
- **Review Queue** — Pending review count for capacity planning
- **Webhook Activity** — Delivery success/failure rates
- **Active Rules** — Count of enabled risk detection rules

### System Logs (`/admin/logs`)
- Centralized structured logging via Serilog
- Filter by level (Debug, Info, Warning, Error, Fatal), source, and date range
- Automatic log retention with configurable cleanup via background service

### Endpoints

| Endpoint | Auth | Description |
|----------|------|-------------|
| `/health` | Public | Application health check |
| `/metrics` | Admin+ | Prometheus metrics |

## Invoice & Billing

### Subscription Tiers

| Plan | Price | Transactions/mo | Team Members | API Keys | Key Features |
|------|-------|-----------------|:------------:|:--------:|-------------|
| **Trial** | Free (14 days) | 100 | 2 | 1 | Core fraud detection |
| **Starter** | $99/mo (₦150,000) | 1,000 | 5 | 2 | Built-in rules, HITL, email alerts |
| **Pro** | $499/mo (₦800,000) | 10,000 | 25 | 10 | + Custom rules, ML scoring, webhooks, Slack, analytics |
| **Enterprise** | $2,000/mo (₦3.2M) | Unlimited | ∞ | ∞ | + GDPR tools, SLA, dedicated support |

### Invoice PDF Generation

- Automatic invoice numbering (`INV-YYYY-NNNN`)
- Professional A4 PDF layout generated with QuestPDF
- Download via API endpoint (`GET /api/invoices/{id}/pdf`)
- Invoice history with summary cards (total billed, outstanding, overdue)

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
| PaymentProviderFactoryTests | 48 | Factory pattern, provider selection |
| AfriexProviderTests | 30 | Afriex API integration |
| FlutterwaveProviderTests | 28 | Flutterwave normalization |
| RuleMarketplaceServiceTests | 25 | Template browsing, import, analytics |
| RbacServiceTests | 24 | Roles, permissions, team management |
| MLFeatureExtractorTests | 20 | Feature extraction for ML |
| WiseProviderTests | 20 | Wise transfer mapping |
| TenantOnboardingTests | 16 | Tenant setup, rule seeding |
| SecurityMiddlewareTests | 15 | Auth, rate limiting, CORS |
| TenantIsolationTests | — | Multi-tenant data isolation |
| Integration Tests | 40 | End-to-end webhook processing |
| **Total** | **266** | |

### Continuous Integration

GitHub Actions workflow runs on every push:
- ✅ Multi-platform testing (Ubuntu, Windows, macOS)
- ✅ Code quality checks
- ✅ Security vulnerability scanning

## API Endpoints

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| POST | `/api/webhooks/afriex` | API Key | Receive Afriex transaction webhooks |
| POST | `/api/webhooks/flutterwave` | API Key | Receive Flutterwave webhooks |
| POST | `/api/webhooks/wise` | API Key | Receive Wise webhooks |
| POST | `/api/auth/demo-login` | Anonymous | Demo login (dev mode only) |
| POST | `/api/auth/magic-link` | Anonymous | Request magic link email |
| GET | `/api/auth/verify` | Anonymous | Verify magic link token |
| GET | `/api/invoices/{id}/pdf` | Manager+ | Download invoice PDF |
| GET | `/health` | Public | Application health check |
| GET | `/metrics` | Admin+ | Prometheus metrics |
| — | `/transactionHub` | Authenticated | SignalR real-time connection |

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
- [QuestPDF](https://www.questpdf.com/) for the PDF generation library
- The ASP.NET Core team for SignalR and the middleware pipeline

---

Built with ❤️ for the Cross-Border Fintech Hackathon 2026
