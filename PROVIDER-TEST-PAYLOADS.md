# Provider Test Payloads

Complete reference for testing webhook integrations with example payloads and expected outcomes.

## Table of Contents

- [Afriex Test Payloads](#afriex-test-payloads)
- [Flutterwave Test Payloads](#flutterwave-test-payloads)
- [Risk Scoring Examples](#risk-scoring-examples)
- [Signature Verification](#signature-verification)

---

## Afriex Test Payloads

### 1. Standard Transaction - Low Risk

**Scenario:** Regular customer, small amount, common corridor

```bash
curl -X POST http://localhost:5054/api/webhooks/afriex \
  -H "Content-Type: application/json" \
  -d '{
    "event": "TRANSACTION.CREATED",
    "data": {
      "transactionId": "AFX-LOW-001",
      "customerId": "cust-regular-001",
      "sourceAmount": "100",
      "sourceCurrency": "USD",
      "destinationAmount": "150000",
      "destinationCurrency": "NGN",
      "sourceCountry": "US",
      "destinationCountry": "NG",
      "status": "PENDING",
      "createdAt": "2025-01-15T10:00:00Z"
    }
  }'
```

**Expected Result:**
- Risk Score: 5-15 (Low)
- Action: Auto-approve
- Status: COMPLETED within seconds

---

### 2. High-Value Transaction - Medium Risk

**Scenario:** Large amount, established customer

```bash
curl -X POST http://localhost:5054/api/webhooks/afriex \
  -H "Content-Type: application/json" \
  -d '{
    "event": "TRANSACTION.CREATED",
    "data": {
      "transactionId": "AFX-MED-001",
      "customerId": "cust-established-002",
      "sourceAmount": "5500",
      "sourceCurrency": "USD",
      "destinationAmount": "8250000",
      "destinationCurrency": "NGN",
      "sourceCountry": "US",
      "destinationCountry": "NG",
      "status": "PENDING",
      "createdAt": "2025-01-15T11:30:00Z"
    }
  }'
```

**Expected Result:**
- Risk Score: 35-55 (Medium)
- Action: Manual review
- Flags: High-value transaction (>$5000)

---

### 3. New Customer - High Risk

**Scenario:** First transaction, new customer profile

```bash
curl -X POST http://localhost:5054/api/webhooks/afriex \
  -H "Content-Type: application/json" \
  -d '{
    "event": "TRANSACTION.CREATED",
    "data": {
      "transactionId": "AFX-HIGH-001",
      "customerId": "cust-new-001",
      "sourceAmount": "300",
      "sourceCurrency": "USD",
      "destinationAmount": "450000",
      "destinationCurrency": "NGN",
      "sourceCountry": "US",
      "destinationCountry": "NG",
      "status": "PENDING",
      "createdAt": "2025-01-15T12:00:00Z"
    }
  }'
```

**Expected Result:**
- Risk Score: 60-75 (High)
- Action: Manual review
- Flags: New customer (no transaction history)

---

### 4. Rapid Transactions - Critical Risk

**Scenario:** Multiple transactions within 5 minutes

```bash
# Transaction 1
curl -X POST http://localhost:5054/api/webhooks/afriex \
  -H "Content-Type: application/json" \
  -d '{
    "event": "TRANSACTION.CREATED",
    "data": {
      "transactionId": "AFX-CRIT-001",
      "customerId": "cust-velocity-001",
      "sourceAmount": "1000",
      "sourceCurrency": "USD",
      "destinationAmount": "1500000",
      "destinationCurrency": "NGN",
      "sourceCountry": "US",
      "destinationCountry": "NG",
      "status": "PENDING",
      "createdAt": "2025-01-15T13:00:00Z"
    }
  }'

# Transaction 2 (2 minutes later)
curl -X POST http://localhost:5054/api/webhooks/afriex \
  -H "Content-Type: application/json" \
  -d '{
    "event": "TRANSACTION.CREATED",
    "data": {
      "transactionId": "AFX-CRIT-002",
      "customerId": "cust-velocity-001",
      "sourceAmount": "1200",
      "sourceCurrency": "USD",
      "destinationAmount": "1800000",
      "destinationCurrency": "NGN",
      "sourceCountry": "US",
      "destinationCountry": "NG",
      "status": "PENDING",
      "createdAt": "2025-01-15T13:02:00Z"
    }
  }'
```

**Expected Result:**
- Risk Score: 85-100 (Critical)
- Action: Block transaction
- Flags: Velocity check failed (>1 transaction in 5 minutes)

---

### 5. Transaction Status Update

**Scenario:** Transaction completed successfully

```bash
curl -X POST http://localhost:5054/api/webhooks/afriex \
  -H "Content-Type: application/json" \
  -d '{
    "event": "TRANSACTION.UPDATED",
    "data": {
      "transactionId": "AFX-LOW-001",
      "status": "COMPLETED",
      "updatedAt": "2025-01-15T10:05:00Z"
    }
  }'
```

**Expected Result:**
- Transaction status updated to COMPLETED
- Customer profile updated (successful transaction count)
- Risk analysis archived

---

## Flutterwave Test Payloads

### 1. Card Payment - Low Risk

**Scenario:** Successful card payment

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
      "amount": 250,
      "currency": "USD",
      "charged_amount": 252.50,
      "status": "successful",
      "payment_type": "card",
      "customer": {
        "id": 456789,
        "email": "regular@example.com",
        "phone_number": "+1234567890",
        "name": "John Doe"
      },
      "card": {
        "first_6digits": "539983",
        "last_4digits": "8381",
        "issuer": "MASTERCARD",
        "country": "US",
        "type": "VISA"
      },
      "created_at": "2025-01-15T14:00:00Z"
    }
  }'
```

**Expected Result:**
- Risk Score: 10-20 (Low)
- Action: Auto-approve
- Source Country: US (from card)
- Destination Country: NG (inferred from payment_type)

---

### 2. Mobile Money - Medium Risk

**Scenario:** Mobile money transfer to Uganda

```bash
curl -X POST http://localhost:5054/api/webhooks/flutterwave \
  -H "Content-Type: application/json" \
  -H "verif-hash: your-webhook-secret-hash" \
  -d '{
    "event": "charge.completed",
    "data": {
      "id": 789012,
      "tx_ref": "FLW-MM-001",
      "flw_ref": "FLW789012345",
      "amount": 50000,
      "currency": "UGX",
      "charged_amount": 50000,
      "status": "successful",
      "payment_type": "mobile_money_uganda",
      "customer": {
        "id": 789123,
        "email": "uganda@example.com",
        "phone_number": "+256700000000",
        "name": "Jane Mukasa"
      },
      "created_at": "2025-01-15T15:00:00Z"
    }
  }'
```

**Expected Result:**
- Risk Score: 30-45 (Medium)
- Action: Auto-approve (if customer has history)
- Source Country: UG (inferred)
- Destination Country: UG (inferred)

---

### 3. Failed Payment

**Scenario:** Card payment declined

```bash
curl -X POST http://localhost:5054/api/webhooks/flutterwave \
  -H "Content-Type: application/json" \
  -H "verif-hash: your-webhook-secret-hash" \
  -d '{
    "event": "charge.failed",
    "data": {
      "id": 345678,
      "tx_ref": "FLW-FAIL-001",
      "flw_ref": "FLW345678901",
      "amount": 1000,
      "currency": "USD",
      "charged_amount": 1005,
      "status": "failed",
      "payment_type": "card",
      "customer": {
        "id": 901234,
        "email": "failed@example.com",
        "phone_number": "+1234567890",
        "name": "Test User"
      },
      "meta": {
        "reason": "Insufficient funds"
      },
      "created_at": "2025-01-15T16:00:00Z"
    }
  }'
```

**Expected Result:**
- Status: FAILED
- Risk Score: Not calculated (failed transaction)
- Customer profile: Failed transaction recorded

---

### 4. Bank Transfer - High Value

**Scenario:** Large bank transfer

```bash
curl -X POST http://localhost:5054/api/webhooks/flutterwave \
  -H "Content-Type: application/json" \
  -H "verif-hash: your-webhook-secret-hash" \
  -d '{
    "event": "transfer.completed",
    "data": {
      "id": 567890,
      "tx_ref": "FLW-BT-001",
      "flw_ref": "FLW567890123",
      "amount": 10000,
      "currency": "USD",
      "status": "successful",
      "payment_type": "bank_transfer",
      "customer": {
        "id": 234567,
        "email": "corporate@example.com",
        "phone_number": "+2348012345678",
        "name": "Corporate Account"
      },
      "bank_details": {
        "account_number": "0123456789",
        "bank_code": "044",
        "bank_name": "Access Bank"
      },
      "created_at": "2025-01-15T17:00:00Z"
    }
  }'
```

**Expected Result:**
- Risk Score: 50-65 (Medium-High)
- Action: Manual review
- Flags: High-value transaction (>$5000)
- Source Country: NG (inferred from bank_transfer)

---

## Risk Scoring Examples

### Low Risk (0-30)

**Characteristics:**
- Amount < $1000
- Established customer (>5 transactions)
- Common corridor (US → NG, UK → KE)
- No recent velocity violations

**Expected Outcome:** Auto-approve

---

### Medium Risk (31-60)

**Characteristics:**
- Amount $1000-$5000
- Customer with 2-5 transactions
- Less common corridors
- Minor velocity concerns

**Expected Outcome:** Manual review (optional)

---

### High Risk (61-80)

**Characteristics:**
- Amount $5000-$10000
- New customer (0-1 transactions)
- Rare corridors
- Velocity check warnings

**Expected Outcome:** Mandatory manual review

---

### Critical Risk (81-100)

**Characteristics:**
- Amount > $10000
- New customer with no history
- Multiple rapid transactions
- Suspicious patterns

**Expected Outcome:** Block + alert

---

## Signature Verification

### Afriex (Ed25519)

```bash
# Webhook public key from Afriex dashboard
PUBLIC_KEY="your-webhook-public-key"

# Signature in x-webhook-signature header
SIGNATURE="base64-encoded-signature"

# Payload body (raw JSON)
PAYLOAD='{"event":"TRANSACTION.CREATED","data":{...}}'
```

Verification handled automatically by `AfriexProvider.VerifyWebhookSignature()`.

---

### Flutterwave (HMAC SHA256)

```bash
# Calculate signature
echo -n '{"event":"charge.completed","data":{...}}' | \
  openssl dgst -sha256 -hmac "your-webhook-secret-hash"

# Include in header
curl -H "verif-hash: calculated-hash" ...
```

Verification handled automatically by `FlutterwaveProvider.VerifyWebhookSignature()`.

---

## Testing Workflow

### 1. Start Application

```bash
cd src/PayGuardAI.Web
dotnet run
```

### 2. Monitor Logs

```bash
# Watch for provider registration
# Expected: [Afriex] Provider registered
# Expected: [Flutterwave] Provider registered (if enabled)
```

### 3. Send Test Webhook

Use curl commands above or Postman collection.

### 4. Verify Response

**Success (200 OK):**
```json
{
  "success": true,
  "transactionId": "AFX-LOW-001",
  "riskScore": 12,
  "action": "APPROVED"
}
```

**Error (400 Bad Request):**
```json
{
  "error": "Invalid signature",
  "provider": "Flutterwave"
}
```

### 5. Check Database

```sql
-- Verify transaction recorded
SELECT * FROM "Transactions" WHERE "TransactionId" = 'AFX-LOW-001';

-- Check risk analysis
SELECT * FROM "RiskAnalyses" WHERE "TransactionId" = 'AFX-LOW-001';

-- View audit log
SELECT * FROM "AuditLogs" WHERE "TransactionId" = 'AFX-LOW-001' ORDER BY "Timestamp" DESC;
```

---

## Automated Testing

Create a shell script for batch testing:

```bash
#!/bin/bash
# test-webhooks.sh

BASE_URL="http://localhost:5054"

echo "Testing Afriex low-risk transaction..."
curl -X POST $BASE_URL/api/webhooks/afriex \
  -H "Content-Type: application/json" \
  -d @afriex-low-risk.json

sleep 2

echo "Testing Flutterwave card payment..."
curl -X POST $BASE_URL/api/webhooks/flutterwave \
  -H "Content-Type: application/json" \
  -H "verif-hash: test-hash" \
  -d @flutterwave-card.json

# Add more tests...
```

---

## Next Steps

1. **Review Logs:** Check `AuditLogs` for webhook processing
2. **Test Feature Flags:** Toggle `FlutterwaveEnabled` and retry
3. **Verify Notifications:** Check if alerts are sent for high-risk transactions
4. **Monitor Performance:** Measure webhook processing time
5. **Stress Test:** Send rapid webhooks to test velocity checks

For production deployment, see [FLUTTERWAVE-SETUP.md](FLUTTERWAVE-SETUP.md).
