# Production Deployment Guide

This guide covers deploying PayGuard AI to production environments.

## Prerequisites

- ✅ .NET 10.0 SDK installed
- ✅ PostgreSQL database (recommended for production)
- ✅ SSL/TLS certificates configured
- ✅ OAuth provider configured (Azure AD, Google, or Okta)
- ✅ Payment provider API keys (Afriex, Flutterwave)

## Environment Variables

Required environment variables for production:

```bash
# Database
ConnectionStrings__DefaultConnection="Host=localhost;Database=payguardai;Username=postgres;Password=***"

# Feature Flags
FeatureFlags__PostgresEnabled=true
FeatureFlags__OAuthEnabled=true
FeatureFlags__FlutterwaveEnabled=false

# OAuth Configuration
OAuth__Authority="https://login.microsoftonline.com/{tenant-id}/v2.0"
OAuth__ClientId="your-client-id"
OAuth__ClientSecret="your-client-secret"

# MFA Configuration
MFA__Issuer="PayGuard AI"
MFA__RequireForRoles="Approver,Admin"

# Afriex Configuration
Afriex__BaseUrl="https://api.afriex.com/v1"
Afriex__ApiKey="your-production-api-key"
Afriex__WebhookPublicKey="your-webhook-public-key"

# Flutterwave Configuration (if enabled)
Flutterwave__BaseUrl="https://api.flutterwave.com/v3"
Flutterwave__SecretKey="your-secret-key"
Flutterwave__WebhookSecretHash="your-webhook-hash"

# Application Settings
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS="https://+:443;http://+:80"
```

## Deployment Steps

### 1. Build for Production

```bash
dotnet publish src/PayGuardAI.Web/PayGuardAI.Web.csproj \
  -c Release \
  -o ./publish \
  --self-contained false
```

### 2. Database Migration

```bash
# Run migrations
dotnet ef database update --project src/PayGuardAI.Data

# Verify connection
psql -h localhost -U postgres -d payguardai -c "\dt"
```

### 3. Configure HTTPS

**Option A: Reverse Proxy (Nginx/Apache)**

```nginx
server {
    listen 443 ssl http2;
    server_name payguard.yourdomain.com;

    ssl_certificate /path/to/cert.pem;
    ssl_certificate_key /path/to/key.pem;

    location / {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

**Option B: Kestrel Direct**

```json
{
  "Kestrel": {
    "Endpoints": {
      "Https": {
        "Url": "https://*:443",
        "Certificate": {
          "Path": "/path/to/cert.pfx",
          "Password": "cert-password"
        }
      }
    }
  }
}
```

### 4. Systemd Service (Linux)

Create `/etc/systemd/system/payguardai.service`:

```ini
[Unit]
Description=PayGuard AI Application
After=network.target

[Service]
Type=notify
WorkingDirectory=/var/www/payguardai
ExecStart=/usr/bin/dotnet /var/www/payguardai/PayGuardAI.Web.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=payguardai
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target
```

Enable and start:

```bash
sudo systemctl enable payguardai
sudo systemctl start payguardai
sudo systemctl status payguardai
```

### 5. Health Checks

Verify deployment:

```bash
# Application health
curl https://payguard.yourdomain.com/api/webhooks/health

# Expected response:
# {
#   "status": "healthy",
#   "service": "PayGuard AI",
#   "providers": [...]
# }
```

## Cloud Deployment

### Azure App Service

```bash
# Login to Azure
az login

# Create resource group
az group create --name payguardai-rg --location eastus

# Create App Service plan
az appservice plan create \
  --name payguardai-plan \
  --resource-group payguardai-rg \
  --sku P1V2 \
  --is-linux

# Create web app
az webapp create \
  --name payguardai \
  --resource-group payguardai-rg \
  --plan payguardai-plan \
  --runtime "DOTNETCORE:10.0"

# Deploy
az webapp deploy \
  --resource-group payguardai-rg \
  --name payguardai \
  --src-path ./publish.zip \
  --type zip
```

### AWS Elastic Beanstalk

```bash
# Install EB CLI
pip install awsebcli

# Initialize
eb init -p "64bit Amazon Linux 2023 v3.2.0 running .NET 10" payguardai

# Create environment
eb create payguardai-prod --database.engine postgres

# Deploy
eb deploy
```

### Heroku

```bash
# Login
heroku login

# Create app
heroku create payguardai

# Add PostgreSQL
heroku addons:create heroku-postgresql:standard-0

# Set buildpack
heroku buildpacks:set https://github.com/heroku/heroku-buildpack-dotnetcore

# Deploy
git push heroku main
```

## Monitoring

### Application Insights (Azure)

```json
{
  "ApplicationInsights": {
    "ConnectionString": "InstrumentationKey=your-key;IngestionEndpoint=https://..."
  },
  "Logging": {
    "ApplicationInsights": {
      "LogLevel": {
        "Default": "Information",
        "Microsoft": "Warning"
      }
    }
  }
}
```

### Prometheus Metrics

Add to `Program.cs`:

```csharp
app.UseHttpMetrics(); // Prometheus middleware
app.MapMetrics();     // /metrics endpoint
```

### Log Aggregation

**Seq:**

```bash
docker run --name seq -d \
  -e ACCEPT_EULA=Y \
  -p 5341:80 \
  datalust/seq:latest
```

**Elasticsearch + Kibana:**

```bash
# Configure in appsettings.json
"Serilog": {
  "WriteTo": [
    {
      "Name": "Elasticsearch",
      "Args": {
        "nodeUris": "http://localhost:9200"
      }
    }
  ]
}
```

## Security Checklist

- [ ] HTTPS enforced (redirect HTTP to HTTPS)
- [ ] OAuth client secrets stored in Key Vault/Secrets Manager
- [ ] API keys encrypted at rest
- [ ] CORS configured for production domains only
- [ ] Rate limiting enabled
- [ ] SQL injection prevention (parameterized queries)
- [ ] XSS protection (CSP headers)
- [ ] CSRF tokens enabled
- [ ] Security headers configured
- [ ] Database backups automated
- [ ] Audit logs retention configured

## Performance Optimization

### Database Indexing

```sql
-- Add indexes for frequent queries
CREATE INDEX idx_transactions_customerid ON "Transactions"("CustomerId");
CREATE INDEX idx_transactions_status ON "Transactions"("Status");
CREATE INDEX idx_transactions_createdat ON "Transactions"("CreatedAt" DESC);
CREATE INDEX idx_riskanalyses_risklevel ON "RiskAnalyses"("RiskLevel");
```

### Response Caching

```csharp
// Add to Program.cs
builder.Services.AddResponseCaching();
builder.Services.AddMemoryCache();

app.UseResponseCaching();
```

### Database Connection Pooling

```csharp
"ConnectionStrings": {
  "DefaultConnection": "Host=localhost;Database=payguardai;Username=postgres;Password=***;Pooling=true;MinPoolSize=5;MaxPoolSize=100"
}
```

## Rollback Plan

1. **Keep previous version available:**
   ```bash
   # Tag release
   git tag v1.0.0
   git push origin v1.0.0
   ```

2. **Database backup before migration:**
   ```bash
   pg_dump payguardai > backup-$(date +%Y%m%d-%H%M%S).sql
   ```

3. **Quick rollback:**
   ```bash
   # Systemd
   sudo systemctl stop payguardai
   cp -r /var/www/payguardai-backup/* /var/www/payguardai/
   sudo systemctl start payguardai

   # Docker
   docker rollback payguardai-service
   ```

## Support

For deployment issues:
- Check logs: `sudo journalctl -u payguardai -f`
- Database status: `systemctl status postgresql`
- Network connectivity: `curl -v https://api.afriex.com/health`

## Post-Deployment Verification

```bash
# 1. Health check
curl https://payguard.yourdomain.com/api/webhooks/health

# 2. Test webhook
curl -X POST https://payguard.yourdomain.com/api/webhooks/afriex \
  -H "Content-Type: application/json" \
  -d '{"event":"TRANSACTION.CREATED","data":{"transactionId":"PROD-TEST-001","..."}}'

# 3. Check SignalR connection
# Open browser console: Connection ID should appear

# 4. Verify OAuth login
# Navigate to /login and complete auth flow

# 5. Test MFA setup
# Navigate to /mfa-setup with elevated user
```

## Maintenance Windows

Schedule weekly maintenance windows for:
- Database vacuum and analyze
- Log rotation and cleanup
- SSL certificate renewal checks
- Security patch application
- Backup verification

Recommended: **Sunday 2-4 AM UTC** (low traffic period)
