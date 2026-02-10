using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

/// <summary>
/// Risk scoring engine implementation.
/// Evaluates transactions against configurable rules and generates explainable risk assessments.
/// </summary>
public class RiskScoringService : IRiskScoringService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<RiskScoringService> _logger;
    private readonly IAlertingService _alertingService;
    private readonly ITenantContext _tenantContext;

    // Risk level thresholds
    private const int LowRiskThreshold = 25;
    private const int MediumRiskThreshold = 50;
    private const int HighRiskThreshold = 75;

    // Auto-approve threshold (low risk transactions bypass HITL)
    private const int AutoApproveThreshold = 25;

    public RiskScoringService(
        ApplicationDbContext context,
        ILogger<RiskScoringService> logger,
        IAlertingService alertingService,
        ITenantContext tenantContext)
    {
        _context = context;
        _logger = logger;
        _alertingService = alertingService;
        _tenantContext = tenantContext;
    }

    public async Task<RiskAnalysis> AnalyzeTransactionAsync(Transaction transaction, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting risk analysis for transaction {TransactionId}", transaction.Id);

        // Get active rules
        var rules = await _context.RiskRules
            .Where(r => r.IsEnabled)
            .ToListAsync(cancellationToken);

        // Get customer profile (or create one)
        var customerProfile = await GetOrCreateCustomerProfileAsync(transaction.SenderId, cancellationToken);

        // Evaluate each rule
        var riskFactors = new List<RiskFactor>();
        int totalScore = 0;

        foreach (var rule in rules)
        {
            var factor = await EvaluateRuleAsync(rule, transaction, customerProfile, cancellationToken);
            if (factor != null)
            {
                riskFactors.Add(factor);
                totalScore += factor.ScoreContribution;
            }
        }

        // Cap score at 100
        totalScore = Math.Min(totalScore, 100);

        // Determine risk level
        var riskLevel = totalScore switch
        {
            <= LowRiskThreshold => RiskLevel.Low,
            <= MediumRiskThreshold => RiskLevel.Medium,
            <= HighRiskThreshold => RiskLevel.High,
            _ => RiskLevel.Critical
        };

        // Determine review status (HITL logic)
        var reviewStatus = totalScore <= AutoApproveThreshold 
            ? ReviewStatus.AutoApproved 
            : ReviewStatus.Pending;

        // Generate explanation (XAI)
        var explanation = GenerateExplanation(riskFactors, totalScore, riskLevel);

        // Create risk analysis
        var analysis = new RiskAnalysis
        {
            TransactionId = transaction.Id,
            RiskScore = totalScore,
            RiskLevel = riskLevel,
            ReviewStatus = reviewStatus,
            Explanation = explanation,
            AnalyzedAt = DateTime.UtcNow,
            RiskFactors = riskFactors
        };

        _context.RiskAnalyses.Add(analysis);
        await _context.SaveChangesAsync(cancellationToken);

        // Update customer profile
        await UpdateCustomerProfileAsync(customerProfile, transaction, riskLevel != RiskLevel.Low, cancellationToken);

        _logger.LogInformation(
            "Risk analysis complete for transaction {TransactionId}: Score={Score}, Level={Level}, Status={Status}",
            transaction.Id, totalScore, riskLevel, reviewStatus);

        if (riskLevel == RiskLevel.Critical)
        {
            await _alertingService.AlertAsync(
                $"Tenant {_tenantContext.TenantId}: Critical transaction {transaction.ExternalId} scored {totalScore}",
                cancellationToken);
        }

        return analysis;
    }

    public async Task<RiskAnalysis> ReanalyzeTransactionAsync(Guid transactionId, CancellationToken cancellationToken = default)
    {
        var transaction = await _context.Transactions
            .FirstOrDefaultAsync(t => t.Id == transactionId, cancellationToken)
            ?? throw new ArgumentException($"Transaction {transactionId} not found");

        // Remove existing analysis
        var existingAnalysis = await _context.RiskAnalyses
            .Include(r => r.RiskFactors)
            .FirstOrDefaultAsync(r => r.TransactionId == transactionId, cancellationToken);

        if (existingAnalysis != null)
        {
            _context.RiskFactors.RemoveRange(existingAnalysis.RiskFactors);
            _context.RiskAnalyses.Remove(existingAnalysis);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return await AnalyzeTransactionAsync(transaction, cancellationToken);
    }

    private async Task<RiskFactor?> EvaluateRuleAsync(
        RiskRule rule, 
        Transaction transaction, 
        CustomerProfile profile,
        CancellationToken cancellationToken)
    {
        return rule.RuleCode switch
        {
            "HIGH_AMOUNT" => EvaluateHighAmount(rule, transaction),
            "VELOCITY_24H" => await EvaluateVelocity24hAsync(rule, transaction, cancellationToken),
            "NEW_CUSTOMER" => EvaluateNewCustomer(rule, profile),
            "HIGH_RISK_CORRIDOR" => EvaluateHighRiskCorridor(rule, transaction),
            "ROUND_AMOUNT" => EvaluateRoundAmount(rule, transaction),
            "UNUSUAL_TIME" => EvaluateUnusualTime(rule, transaction),
            _ => null
        };
    }

    private RiskFactor? EvaluateHighAmount(RiskRule rule, Transaction transaction)
    {
        if (transaction.Amount >= rule.Threshold)
        {
            return new RiskFactor
            {
                Category = rule.Category,
                RuleName = rule.Name,
                Description = $"Transaction amount ({transaction.Amount:C}) exceeds threshold ({rule.Threshold:C})",
                ScoreContribution = rule.ScoreWeight,
                Severity = transaction.Amount >= rule.Threshold * 2 ? FactorSeverity.Critical : FactorSeverity.Warning,
                ContextData = $"{{\"amount\": {transaction.Amount}, \"threshold\": {rule.Threshold}}}"
            };
        }
        return null;
    }

    private async Task<RiskFactor?> EvaluateVelocity24hAsync(RiskRule rule, Transaction transaction, CancellationToken cancellationToken)
    {
        var cutoff = transaction.CreatedAt.AddHours(-24);
        var recentCount = await _context.Transactions
            .CountAsync(t => t.SenderId == transaction.SenderId && t.CreatedAt >= cutoff, cancellationToken);

        if (recentCount >= (int)rule.Threshold)
        {
            return new RiskFactor
            {
                Category = rule.Category,
                RuleName = rule.Name,
                Description = $"Customer has {recentCount} transactions in the last 24 hours (limit: {rule.Threshold})",
                ScoreContribution = rule.ScoreWeight,
                Severity = recentCount >= rule.Threshold * 2 ? FactorSeverity.Alert : FactorSeverity.Warning,
                ContextData = $"{{\"count\": {recentCount}, \"threshold\": {rule.Threshold}}}"
            };
        }
        return null;
    }

    private RiskFactor? EvaluateNewCustomer(RiskRule rule, CustomerProfile profile)
    {
        if (profile.TotalTransactions < (int)rule.Threshold)
        {
            return new RiskFactor
            {
                Category = rule.Category,
                RuleName = rule.Name,
                Description = $"New customer with only {profile.TotalTransactions} previous transactions",
                ScoreContribution = rule.ScoreWeight,
                Severity = profile.TotalTransactions == 0 ? FactorSeverity.Warning : FactorSeverity.Info,
                ContextData = $"{{\"totalTransactions\": {profile.TotalTransactions}}}"
            };
        }
        return null;
    }

    private RiskFactor? EvaluateHighRiskCorridor(RiskRule rule, Transaction transaction)
    {
        // List of high-risk country codes (for demo purposes)
        var highRiskCountries = new HashSet<string> { "IR", "KP", "SY", "YE", "VE", "CU" };

        bool isHighRisk = highRiskCountries.Contains(transaction.SourceCountry) || 
                          highRiskCountries.Contains(transaction.DestinationCountry);

        if (isHighRisk)
        {
            return new RiskFactor
            {
                Category = rule.Category,
                RuleName = rule.Name,
                Description = $"Transaction involves high-risk corridor: {transaction.SourceCountry} → {transaction.DestinationCountry}",
                ScoreContribution = rule.ScoreWeight,
                Severity = FactorSeverity.Critical,
                ContextData = $"{{\"source\": \"{transaction.SourceCountry}\", \"destination\": \"{transaction.DestinationCountry}\"}}"
            };
        }
        return null;
    }

    private RiskFactor? EvaluateRoundAmount(RiskRule rule, Transaction transaction)
    {
        // Check if amount is a round number (potential structuring)
        bool isRound = transaction.Amount >= rule.Threshold && 
                       transaction.Amount % 1000 == 0;

        if (isRound)
        {
            return new RiskFactor
            {
                Category = rule.Category,
                RuleName = rule.Name,
                Description = $"Transaction is an exact round amount ({transaction.Amount:C}) which may indicate structuring",
                ScoreContribution = rule.ScoreWeight,
                Severity = FactorSeverity.Info,
                ContextData = $"{{\"amount\": {transaction.Amount}}}"
            };
        }
        return null;
    }

    private RiskFactor? EvaluateUnusualTime(RiskRule rule, Transaction transaction)
    {
        // Consider transactions between 2 AM and 5 AM as unusual
        var hour = transaction.CreatedAt.Hour;
        bool isUnusual = hour >= 2 && hour <= 5;

        if (isUnusual)
        {
            return new RiskFactor
            {
                Category = rule.Category,
                RuleName = rule.Name,
                Description = $"Transaction occurred at unusual time ({transaction.CreatedAt:HH:mm} UTC)",
                ScoreContribution = rule.ScoreWeight,
                Severity = FactorSeverity.Info,
                ContextData = $"{{\"hour\": {hour}}}"
            };
        }
        return null;
    }

    private string GenerateExplanation(List<RiskFactor> factors, int score, RiskLevel level)
    {
        if (factors.Count == 0)
        {
            return "No risk factors detected. Transaction appears normal.";
        }

        var criticalFactors = factors.Where(f => f.Severity == FactorSeverity.Critical).ToList();
        var warningFactors = factors.Where(f => f.Severity >= FactorSeverity.Warning).ToList();

        var explanation = $"Risk Score: {score}/100 ({level}). ";

        if (criticalFactors.Count != 0)
        {
            explanation += $"CRITICAL: {string.Join("; ", criticalFactors.Select(f => f.Description))}. ";
        }

        if (warningFactors.Count > criticalFactors.Count)
        {
            var nonCriticalWarnings = warningFactors.Except(criticalFactors);
            explanation += $"Warnings: {string.Join("; ", nonCriticalWarnings.Select(f => f.Description))}. ";
        }

        if (factors.Any(f => f.Severity == FactorSeverity.Info))
        {
            explanation += $"Additional observations: {factors.Count(f => f.Severity == FactorSeverity.Info)} minor indicators noted.";
        }

        return explanation;
    }

    private async Task<CustomerProfile> GetOrCreateCustomerProfileAsync(string customerId, CancellationToken cancellationToken)
    {
        var profile = await _context.CustomerProfiles
            .FirstOrDefaultAsync(p => p.ExternalId == customerId, cancellationToken);

        if (profile == null)
        {
            profile = new CustomerProfile
            {
                ExternalId = customerId,
                TotalTransactions = 0,
                TotalVolume = 0,
                RiskTier = CustomerRiskTier.Unknown
            };
            _context.CustomerProfiles.Add(profile);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return profile;
    }

    private async Task UpdateCustomerProfileAsync(
        CustomerProfile profile, 
        Transaction transaction, 
        bool wasFlagged,
        CancellationToken cancellationToken)
    {
        profile.TotalTransactions++;
        profile.TotalVolume += transaction.Amount;
        profile.AverageTransactionAmount = profile.TotalVolume / profile.TotalTransactions;
        
        if (transaction.Amount > profile.MaxTransactionAmount)
        {
            profile.MaxTransactionAmount = transaction.Amount;
        }

        profile.FirstTransactionAt ??= transaction.CreatedAt;
        profile.LastTransactionAt = transaction.CreatedAt;

        if (wasFlagged)
        {
            profile.FlaggedTransactionCount++;
        }

        // Update risk tier based on history
        profile.RiskTier = CalculateRiskTier(profile);
        profile.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
    }

    private static CustomerRiskTier CalculateRiskTier(CustomerProfile profile)
    {
        if (profile.TotalTransactions < 5) return CustomerRiskTier.Unknown;
        
        var flagRate = (double)profile.FlaggedTransactionCount / profile.TotalTransactions;
        var rejectionRate = (double)profile.RejectedTransactionCount / profile.TotalTransactions;

        return (flagRate, rejectionRate) switch
        {
            ( > 0.5, _) or (_, > 0.2) => CustomerRiskTier.HighRisk,
            ( > 0.3, _) or (_, > 0.1) => CustomerRiskTier.Elevated,
            ( > 0.1, _) => CustomerRiskTier.Standard,
            _ => CustomerRiskTier.Trusted
        };
    }
}
