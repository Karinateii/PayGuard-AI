# Docker & Heroku Deployment Guide

Complete guide for deploying PayGuard AI to production using Docker and Heroku.

## Prerequisites

- Docker Desktop installed
- Heroku CLI installed (`brew install heroku/brew/heroku` on macOS)
- Heroku account created
- Git repository connected to GitHub

## Local Docker Testing

### 1. Build the Docker Image

```bash
cd /Users/ebenezer/Desktop/Afriex/PayGuardAI
docker build -t payguard-ai .
```

### 2. Run with Docker Compose

```bash
docker-compose up -d
```

The application will be available at: http://localhost:5054

### 3. View Logs

```bash
docker-compose logs -f payguard-web
```

### 4. Stop Containers

```bash
docker-compose down
```

### 5. Test the Application

Open browser to http://localhost:5054 and verify:
- ✅ Dashboard loads
- ✅ Transactions page works
- ✅ Health check responds: http://localhost:5054/health

## Heroku Deployment

### Step 1: Login to Heroku

```bash
heroku login
```

### Step 2: Create Heroku Apps

```bash
# Create staging app
heroku create payguard-ai-staging --stack container

# Create production app
heroku create payguard-ai-production --stack container
```

### Step 3: Set Environment Variables

#### Staging Environment

```bash
heroku config:set ASPNETCORE_ENVIRONMENT=Staging \
  FeatureFlags__PostgresEnabled=false \
  FeatureFlags__OAuthEnabled=false \
  FeatureFlags__FlutterwaveEnabled=false \
  Auth__DefaultUser=compliance_officer@payguard.ai \
  Auth__DefaultRoles=Reviewer,Manager \
  --app payguard-ai-staging
```

#### Production Environment

```bash
heroku config:set ASPNETCORE_ENVIRONMENT=Production \
  FeatureFlags__PostgresEnabled=false \
  FeatureFlags__OAuthEnabled=false \
  FeatureFlags__FlutterwaveEnabled=false \
  Auth__DefaultUser=compliance_officer@payguard.ai \
  Auth__DefaultRoles=Reviewer,Manager \
  --app payguard-ai-production
```

### Step 4: Deploy to Staging

```bash
# Add Heroku staging remote
git remote add heroku-staging https://git.heroku.com/payguard-ai-staging.git

# Deploy
git push heroku-staging main

# View logs
heroku logs --tail --app payguard-ai-staging
```

### Step 5: Test Staging

```bash
# Open staging app
heroku open --app payguard-ai-staging

# Check health endpoint
curl https://payguard-ai-staging.herokuapp.com/health
```

### Step 6: Deploy to Production

```bash
# Add Heroku production remote
git remote add heroku-production https://git.heroku.com/payguard-ai-production.git

# Deploy
git push heroku-production main

# View logs
heroku logs --tail --app payguard-ai-production
```

## Automated Deployments (Optional)

### Connect to GitHub for Auto-Deploy

1. Go to Heroku Dashboard → Your App → Deploy
2. Select "GitHub" as deployment method
3. Connect your GitHub repository
4. Enable "Automatic deploys" for staging (from `main` branch)
5. Keep production as "Manual deploy" (require button click)

### Result:
- Every push to `main` → auto-deploys to staging
- Production → requires manual "Deploy Branch" button click

## Environment Configuration

### Required Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `ASPNETCORE_ENVIRONMENT` | Environment name | Production |
| `FeatureFlags__PostgresEnabled` | Enable PostgreSQL | false |
| `FeatureFlags__OAuthEnabled` | Enable OAuth login | false |
| `FeatureFlags__FlutterwaveEnabled` | Enable Flutterwave | false |
| `Auth__DefaultUser` | Default user email | compliance_officer@payguard.ai |
| `Auth__DefaultRoles` | Default user roles | Reviewer,Manager |

### Optional Environment Variables

```bash
# For Afriex API integration
Afriex__ApiKey=your_api_key
Afriex__WebhookPublicKey=your_webhook_key

# For Flutterwave integration (when enabled)
Flutterwave__SecretKey=your_secret_key
Flutterwave__WebhookSecretHash=your_webhook_hash

# For OAuth (when enabled)
OAuth__ClientId=your_client_id
OAuth__ClientSecret=your_client_secret
OAuth__Authority=https://login.microsoftonline.com/your_tenant_id
```

## Database Setup

### SQLite (Default - No Setup Needed)

The application uses SQLite by default. Database file is created automatically on first run.

### PostgreSQL (Optional - Production Scale)

When ready to scale:

```bash
# Add Heroku Postgres addon
heroku addons:create heroku-postgresql:mini --app payguard-ai-production

# Get database URL
heroku config:get DATABASE_URL --app payguard-ai-production

# Enable PostgreSQL feature flag
heroku config:set FeatureFlags__PostgresEnabled=true --app payguard-ai-production
```

## Monitoring & Maintenance

### View Application Logs

```bash
# Real-time logs
heroku logs --tail --app payguard-ai-production

# Last 100 lines
heroku logs --num 100 --app payguard-ai-production

# Filter for errors
heroku logs --tail --app payguard-ai-production | grep ERROR
```

### Check Application Status

```bash
# App info
heroku ps --app payguard-ai-production

# Health check
curl https://payguard-ai-production.herokuapp.com/health
```

### Restart Application

```bash
heroku restart --app payguard-ai-production
```

### Scale Application

```bash
# Scale up (hobby dyno)
heroku ps:scale web=1:hobby --app payguard-ai-production

# Scale up (standard dyno)
heroku ps:scale web=1:standard-1x --app payguard-ai-production

# Scale down
heroku ps:scale web=0 --app payguard-ai-production
```

## Rollback

### View Recent Releases

```bash
heroku releases --app payguard-ai-production
```

### Rollback to Previous Version

```bash
# Rollback one version
heroku rollback --app payguard-ai-production

# Rollback to specific version
heroku rollback v123 --app payguard-ai-production
```

## Performance Optimization

### Enable Redis Caching (Optional)

```bash
# Add Redis addon
heroku addons:create heroku-redis:mini --app payguard-ai-production

# Get Redis URL
heroku config:get REDIS_URL --app payguard-ai-production
```

### Custom Domain (Optional)

```bash
# Add custom domain
heroku domains:add www.payguard-ai.com --app payguard-ai-production

# View DNS instructions
heroku domains --app payguard-ai-production
```

## Troubleshooting

### Build Failures

```bash
# View build logs
heroku logs --tail --app payguard-ai-production

# Check build status
heroku builds --app payguard-ai-production
```

### Application Crashes

```bash
# Check dyno status
heroku ps --app payguard-ai-production

# Restart application
heroku restart --app payguard-ai-production

# Check configuration
heroku config --app payguard-ai-production
```

### Database Issues

```bash
# Check database connection
heroku pg:info --app payguard-ai-production

# Access database console
heroku pg:psql --app payguard-ai-production
```

## Security Checklist

- [ ] Set strong secrets for production
- [ ] Enable HTTPS (automatic on Heroku)
- [ ] Configure CORS properly
- [ ] Set up rate limiting (already configured)
- [ ] Enable authentication (OAuth feature flag)
- [ ] Review environment variables
- [ ] Set up monitoring alerts
- [ ] Configure backup strategy

## Cost Estimates

### Heroku Pricing (Monthly)

- **Hobby Dyno**: $7/month
- **Standard 1X**: $25/month
- **Standard 2X**: $50/month
- **Postgres Mini**: $5/month (optional)
- **Redis Mini**: $3/month (optional)

**Recommended for staging**: Hobby dyno ($7/month)  
**Recommended for production**: Standard 1X + Postgres Mini ($30/month)

## Next Steps

1. ✅ Deploy to staging
2. ✅ Test all features in staging
3. ✅ Monitor staging for 24-48 hours
4. ✅ Deploy to production
5. ✅ Set up monitoring alerts
6. ✅ Configure custom domain (optional)
7. ✅ Enable feature flags gradually

## Support

- Heroku Documentation: https://devcenter.heroku.com/
- Docker Documentation: https://docs.docker.com/
- PayGuard AI Issues: https://github.com/Karinateii/PayGuard-AI/issues

---

**Remember**: Always test in staging before deploying to production!
