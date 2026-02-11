# Flutterwave Integration Guide

Complete setup guide for enabling Flutterwave as a payment provider in PayGuard AI.

## Prerequisites

- Active Flutterwave account ([Sign up](https://flutterwave.com))
- API credentials (Secret Key, Public Key, Encryption Key)
- Webhook secret hash for signature verification

## Configuration

### 1. Enable Flutterwave Provider

Update `appsettings.json`:

```json
{
  "FeatureFlags": {
    "FlutterwaveEnabled": true
  },
  "Flutterwave": {
    "BaseUrl": "https://api.flutterwave.com/v3",
    "SecretKey": "FLWSECK-YOUR-SECRET-KEY",
    "PublicKey": "FLWPUBK-YOUR-PUBLIC-KEY",
    "EncryptionKey": "FLWSECK-YOUR-ENCRYPTION-KEY",
    "WebhookSecretHash": "your-webhook-secret-hash"
  }
}
```

**Important:** Use environment variables or Azure Key Vault for production:

```bash
export Flutterwave__SecretKey="FLWSECK-YOUR-SECRET-KEY"
export Flutterwave__WebhookSecretHash="your-webhook-secret-hash"
```

### 2. Configure Webhook URL

In your [Flutterwave Dashboard](https://dashboard.flutterwave.com/dashboard/settings/webhooks):

1. Navigate to **Settings** → **Webhooks**
2. Add webhook URL: `https://your-domain.com/api/webhooks/flutterwave`
3. Copy the **Secret Hash** and add to configuration

### 3. Verify Configuration

Check provider health:

```bash
curl http://localhost:5054/api/webhooks/health
```

Expected response:

```json
{
  "status": "healthy",
  "providers": [
    {
      "name": "Afriex",
      "configured": true
    },
    {
      "name": "Flutterwave",
      "configured": true
    }
  ]
}
```

## Testing

### Local Testing with ngrok

1. **Start PayGuard AI:**
   ```bash
   cd src/PayGuardAI.Web
   dotnet run
   ```

2. **Create ngrok tunnel:**
   ```bash
   ngrok http 5054
   ```

3. **Update Flutterwave webhook URL:**
   Use the ngrok URL: `https://abc123.ngrok.io/api/webhooks/flutterwave`

### Test Webhook Locally

```bash
curl -X POST http://localhost:5054/api/webhooks/flutterwave \
  -H "Content-Type: application/json" \
  -H "verif-hash: your-webhook-secret-hash" \
  -d '{
    "event": "charge.completed",
    "data": {
      "id": 123456,
      "tx_ref": "FLW-TEST-001",
      "flw_ref": "FLW123456789",
      "amount": 500,
      "currency": "USD",
      "charged_amount": 502,
      "status": "successful",
      "payment_type": "card",
      "customer": {
        "id": 456789,
        "email": "test@example.com",
        "phone_number": "+2348012345678",
        "name": "Test Customer"
      },
      "created_at": "2025-01-15T10:30:00Z"
    }
  }'
```

### Signature Verification

Flutterwave uses HMAC SHA256 for webhook verification:

```csharp
// Automatic verification in FlutterwaveProvider
// Header: verif-hash
// Algorithm: HMAC-SHA256(webhook-body, secret-hash)
```

**Test signature generation:**

```bash
echo -n '{"event":"charge.completed","data":{...}}' | \
  openssl dgst -sha256 -hmac "your-webhook-secret-hash"
```

## Supported Events

PayGuard AI processes these Flutterwave events:

| Event | Description | Normalized Status |
|-------|-------------|-------------------|
| `charge.completed` | Payment successful | COMPLETED |
| `transfer.completed` | Transfer finished | COMPLETED |
| `charge.failed` | Payment failed | FAILED |
| `transfer.failed` | Transfer failed | FAILED |

## Transaction Normalization

Flutterwave webhooks are normalized to PayGuard AI's unified format:

```json
{
  "transactionId": "FLW123456789",
  "provider": "Flutterwave",
  "customerId": "test@example.com",
  "sourceAmount": 500,
  "sourceCurrency": "USD",
  "destinationAmount": 500,
  "destinationCurrency": "USD",
  "sourceCountry": "US",
  "destinationCountry": "NG",
  "status": "COMPLETED",
  "timestamp": "2025-01-15T10:30:00Z"
}
```

### Country Inference

Since Flutterwave doesn't always provide country codes, we infer them:

| Payment Type | Inferred Country |
|--------------|------------------|
| `card` | US (default) |
| `mobile_money_uganda` | UG |
| `mobile_money_ghana` | GH |
| `mobile_money_rwanda` | RW |
| `mobile_money_zambia` | ZM |
| `mpesa` | KE |
| `bank_transfer` | NG |

## Exchange Rate Caching

Exchange rates are cached for 30 minutes per currency pair:

```csharp
// Automatic caching in FlutterwaveProvider
// API: GET /v3/transfers/rates?amount={amount}&destination_currency={to}&source_currency={from}
// Cache key: flutterwave_rate_{from}_{to}
```

## Troubleshooting

### Provider Not Showing in Health Check

**Cause:** Feature flag disabled or configuration missing

**Solution:**
1. Verify `FeatureFlags:FlutterwaveEnabled = true`
2. Check all required configuration keys are set
3. Restart application

### Webhook Signature Verification Failed

**Cause:** Secret hash mismatch or header missing

**Solution:**
1. Verify `Flutterwave:WebhookSecretHash` matches dashboard
2. Ensure `verif-hash` header is present
3. Check webhook body is sent as raw JSON (not form-encoded)

### Exchange Rate API Returns 401

**Cause:** Invalid or expired Secret Key

**Solution:**
1. Verify `Flutterwave:SecretKey` starts with `FLWSECK-`
2. Check key permissions in dashboard
3. Regenerate key if compromised

## Production Checklist

- [ ] Secret keys stored in Azure Key Vault / environment variables
- [ ] Webhook URL uses HTTPS
- [ ] Webhook secret hash verified
- [ ] Feature flag tested in staging environment
- [ ] Exchange rate cache monitored (30min TTL)
- [ ] Error logs configured for webhook failures
- [ ] Provider health check added to monitoring
- [ ] Rollback plan documented (disable feature flag)

## API Reference

### Flutterwave API v3

- **Base URL:** `https://api.flutterwave.com/v3`
- **Authentication:** `Authorization: Bearer FLWSECK-xxx`
- **Documentation:** [https://developer.flutterwave.com](https://developer.flutterwave.com)

### Endpoints Used

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/transfers/rates` | GET | Fetch exchange rates |

## Support

For Flutterwave-specific issues:
- [Flutterwave Support](https://support.flutterwave.com)
- [Developer Community](https://developer.flutterwave.com/discuss)

For PayGuard AI integration issues:
- Check application logs for `[Flutterwave]` prefix
- Review webhook payload in `AuditLog` table
- Verify provider registration in startup logs
