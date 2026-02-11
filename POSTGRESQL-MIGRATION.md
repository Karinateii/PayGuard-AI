# PostgreSQL Migration Guide

This guide explains how to switch PayGuardAI from SQLite to PostgreSQL using feature flags.

## Current Status

✅ **Feature flag system implemented**  
✅ **PostgreSQL support added**  
✅ **Safe fallback to SQLite (default)**  
✅ **Zero breaking changes**

---

## How It Works

The application uses the `FeatureFlags:PostgresEnabled` setting in `appsettings.json` to determine which database to use:

- `false` (default) → Uses **SQLite** (`payguardai.db`)
- `true` → Uses **PostgreSQL** (`payguard_dev` database)

---

## Switching to PostgreSQL

### Step 1: Ensure PostgreSQL is Running

Make sure PostgreSQL is installed and running locally. You can verify with:

```bash
psql --version
# Should show: psql (PostgreSQL) 15.x or higher
```

If PostgreSQL isn't running, start it (installation-dependent):

```bash
# If installed via Homebrew:
brew services start postgresql@15

# Or if installed with installer:
# Start PostgreSQL from Applications folder
```

### Step 2: Create the Database

```bash
psql -U postgres -c "CREATE DATABASE payguard_dev;"
```

Or connect to PostgreSQL and create it manually:

```bash
psql -U postgres
CREATE DATABASE payguard_dev;
\q
```

### Step 3: Update appsettings.json

Change the feature flag from `false` to `true`:

```json
{
  "FeatureFlags": {
    "PostgresEnabled": true,  // ← Change this
    "OAuthEnabled": false,
    "FlutterwaveEnabled": false
  }
}
```

### Step 4: Verify Connection String

Make sure your PostgreSQL connection string is correct in `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=payguardai.db",
    "PostgresConnection": "Host=localhost;Port=5432;Database=payguard_dev;Username=postgres;Password=postgres"
  }
}
```

**Note:** Update the password if you used a different one during PostgreSQL installation.

### Step 5: Run the Application

```bash
cd src/PayGuardAI.Web
dotnet run
```

You should see in the logs:

```
info: PayGuardAI.Data.Services.DatabaseMigrationService[0]
      Ensuring database is ready. Active database: PostgreSQL
info: PayGuardAI.Data.Services.DatabaseMigrationService[0]
      Database ready: PostgreSQL
info: Program[0]
      Active database: PostgreSQL
```

✅ **Success!** Your app is now running on PostgreSQL.

---

## Rolling Back to SQLite

If PostgreSQL isn't working or you encounter issues:

### Quick Rollback

1. Open `appsettings.json`
2. Change `"PostgresEnabled": true` to `"PostgresEnabled": false`
3. Restart the app

```bash
# Stop the app (Ctrl+C)
# Change the flag
# Restart
dotnet run
```

You should see:

```
info: Program[0]
      Active database: SQLite
```

✅ **Rolled back safely to SQLite!**

---

## Connection String Examples

### Local Development

```json
"PostgresConnection": "Host=localhost;Port=5432;Database=payguard_dev;Username=postgres;Password=postgres"
```

### Docker Container

```json
"PostgresConnection": "Host=payguard-postgres;Port=5432;Database=payguard_dev;Username=postgres;Password=postgres"
```

### Heroku PostgreSQL

```json
"PostgresConnection": "Host=your-postgres.compute-1.amazonaws.com;Port=5432;Database=your_database;Username=your_user;Password=your_password;SSL Mode=Require"
```

---

## Troubleshooting

### Issue: "Connection refused" Error

**Cause:** PostgreSQL isn't running.

**Fix:**

```bash
# Check if PostgreSQL is running
ps aux | grep postgres

# If not running, start it
brew services start postgresql@15
# Or start from GUI
```

### Issue: "Password authentication failed"

**Cause:** Incorrect password in connection string.

**Fix:**

1. Connect to PostgreSQL and set the password:

```bash
psql -U postgres
ALTER USER postgres WITH PASSWORD 'postgres';
\q
```

2. Update `appsettings.json` with the correct password.

### Issue: "Database does not exist"

**Cause:** `payguard_dev` database not created.

**Fix:**

```bash
psql -U postgres -c "CREATE DATABASE payguard_dev;"
```

### Issue: App crashes on startup

**Cause:** PostgreSQL connection string is invalid.

**Fix:**

1. Roll back to SQLite by setting `PostgresEnabled: false`
2. Verify PostgreSQL is accessible
3. Test connection manually:

```bash
psql -h localhost -U postgres -d payguard_dev
# Should connect successfully
```

---

## Verification Checklist

After switching to PostgreSQL, verify:

- [ ] App starts without errors
- [ ] Log shows "Active database: PostgreSQL"
- [ ] Dashboard loads at `http://localhost:5054`
- [ ] Transactions appear in the UI
- [ ] Demo data is seeded (if in Development)
- [ ] Can approve/reject transactions

If all checks pass → **PostgreSQL migration successful!**

---

## What Changed in the Code?

### Files Modified

1. **`appsettings.json`** - Added feature flags and PostgreSQL connection string
2. **`Program.cs`** - Added conditional database provider selection
3. **`PayGuardAI.Data.csproj`** - Added `Npgsql.EntityFrameworkCore.PostgreSQL` package

### Files Created

1. **`DatabaseMigrationService.cs`** - Manages database initialization and reports active database

### Behavior

- **Before:** Always used SQLite, hardcoded
- **After:** Uses feature flag to choose database, logs which is active

---

## Next Steps (Phase 1 - Week 3)

Once PostgreSQL is stable for 48+ hours:

1. Test feature flag switching multiple times
2. Verify rollback works smoothly
3. Deploy to staging environment with PostgreSQL
4. Monitor logs for any database-related errors
5. After 1 week of stability, consider PostgreSQL as primary

---

## Support

If you encounter issues not covered here:

1. Check application logs: Look for database-related errors
2. Verify PostgreSQL logs: Check PostgreSQL server logs
3. Test connection manually: Use `psql` to verify connectivity
4. Roll back to SQLite as a safe fallback

**Remember:** SQLite is always available as a fallback. The feature flag ensures zero downtime.
