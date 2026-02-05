# PayGuard AI

> Real-time transaction risk monitoring and compliance system for cross-border payments

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![Blazor](https://img.shields.io/badge/Blazor-Server-512BD4)](https://blazor.net/)
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
- 🔗 **Afriex API Integration** - Direct integration with Afriex Business API

## Tech Stack

| Layer | Technology |
|-------|------------|
| **Frontend** | Blazor Server, MudBlazor 8.x |
| **Backend** | ASP.NET Core 10 |
| **Database** | SQLite with Entity Framework Core |
| **Real-time** | SignalR WebSockets |
| **API** | Afriex Business API |
| **Architecture** | Clean Architecture (3-layer) |

## Project Structure

```
PayGuardAI/
├── src/
│   ├── PayGuardAI.Core/          # Domain entities and interfaces
│   │   └── Entities/
│   │       ├── Transaction.cs
│   │       ├── RiskAnalysis.cs
│   │       ├── RiskRule.cs
│   │       └── AuditLog.cs
│   │
│   ├── PayGuardAI.Data/          # Data access and services
│   │   ├── AppDbContext.cs
│   │   └── Services/
│   │       ├── TransactionService.cs
│   │       ├── RiskScoringService.cs
│   │       ├── ReviewService.cs
│   │       └── AfriexApiService.cs
│   │
│   └── PayGuardAI.Web/           # Blazor UI and API controllers
│       ├── Components/
│       │   ├── Pages/
│       │   │   ├── Home.razor         # Dashboard
│       │   │   ├── Transactions.razor # Transaction list
│       │   │   ├── Reviews.razor      # HITL review queue
│       │   │   ├── Rules.razor        # Rules management
│       │   │   ├── Reports.razor      # Analytics
│       │   │   └── Audit.razor        # Audit log
│       │   └── Layout/
│       ├── Controllers/
│       │   └── WebhooksController.cs  # Webhook endpoint
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
   git clone https://github.com/YOUR_USERNAME/PayGuardAI.git
   cd PayGuardAI
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

### Configure Afriex API (Optional)

To use real Afriex API integration:

```bash
cd src/PayGuardAI.Web
dotnet user-secrets set "Afriex:ApiKey" "your_api_key_here"
```

## Webhook Integration

PayGuard AI receives transaction data via webhooks. The endpoint accepts POST requests at:

```
POST /api/webhooks/transaction
```

### Supported Events

- `TRANSACTION.CREATED` - New transaction received
- `TRANSACTION.UPDATED` - Transaction status changed

### Example Payload

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

### Testing Webhooks

```bash
curl -X POST http://localhost:5054/api/webhooks/transaction \
  -H "Content-Type: application/json" \
  -d '{"event":"TRANSACTION.CREATED","data":{"transactionId":"TEST-001","sourceAmount":"100","sourceCurrency":"USD","destinationAmount":"150000","destinationCurrency":"NGN","status":"PENDING"}}'
```

## Risk Scoring

Transactions are scored based on configurable rules:

| Risk Level | Score Range | Action |
|------------|-------------|--------|
| Low | 0-30 | Auto-approved |
| Medium | 31-60 | Flagged for review |
| High | 61-100 | Requires manual review |

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

### Run Tests

```bash
dotnet test
```

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
