using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

/// <summary>
/// Risk scoring engine implementation.
/// Evaluates transactions against configurable rules, blends with ML model predictions,
/// and generates explainable risk assessments.
/// </summary>
public class RiskScoringService : IRiskScoringService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<RiskScoringService> _logger;
    private readonly IAlertingService _alertingService;
    private readonly IEmailNotificationService _emailNotificationService;
    private readonly ITenantContext _tenantContext;
    private readonly IMLScoringService _mlScoringService;

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
        IEmailNotificationService emailNotificationService,
        ITenantContext tenantContext,
        IMLScoringService mlScoringService)
    {
        _context = context;
        _logger = logger;
        _alertingService = alertingService;
        _emailNotificationService = emailNotificationService;
        _tenantContext = tenantContext;
        _mlScoringService = mlScoringService;
    }

    public async Task<RiskAnalysis> AnalyzeTransactionAsync(Transaction transaction, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting risk analysis for transaction {TransactionId}", transaction.Id);

        // Get active rules — deduplicate so tenant-scoped rules override globals
        var allRules = await _context.RiskRules
            .Where(r => r.IsEnabled)
            .ToListAsync(cancellationToken);

        // Group by RuleCode: if a tenant has their own version, skip the global (TenantId=="")
        var rules = allRules
            .GroupBy(r => r.RuleCode)
            .Select(g => g.FirstOrDefault(r => r.TenantId != "") ?? g.First())
            .ToList();

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
                factor.TenantId = _tenantContext.TenantId;
                riskFactors.Add(factor);
                totalScore += factor.ScoreContribution;
            }
        }

        // Cap score at 100
        totalScore = Math.Min(totalScore, 100);

        // ── Compound rules (AND/OR groups) ──────────────────────────
        // Evaluate compound rules that combine multiple conditions.
        // These are additive — they run alongside single rules.
        try
        {
            var compoundRuleGroups = await _context.RuleGroups
                .Include(g => g.Conditions)
                .Where(g => g.IsEnabled && g.Conditions.Count > 0)
                .ToListAsync(cancellationToken);

            foreach (var group in compoundRuleGroups)
            {
                var factor = EvaluateCompoundRule(group, transaction, customerProfile);
                if (factor != null)
                {
                    factor.TenantId = _tenantContext.TenantId;
                    riskFactors.Add(factor);
                    totalScore += factor.ScoreContribution;
                    totalScore = Math.Min(totalScore, 100);
                }
            }
        }
        catch (Exception ex)
        {
            // Compound rule failure is non-fatal — fall back to single rules + ML
            _logger.LogWarning(ex, "Compound rule evaluation failed for {TransactionId}", transaction.Id);
        }

        // ── ML scoring enhancement ──────────────────────────────────
        // Try ML scoring for this tenant. ScoreTransactionAsync handles
        // lazy-loading the model from DB and returns null if no model exists.
        {
            try
            {
                var mlResult = await _mlScoringService.ScoreTransactionAsync(
                    transaction, customerProfile, cancellationToken);

                if (mlResult != null)
                {
                    var mlFactor = new RiskFactor
                    {
                        TenantId = _tenantContext.TenantId,
                        Category = "ML",
                        RuleName = $"ML Risk Model ({mlResult.ModelVersion})",
                        Description = $"ML model predicts {mlResult.FraudProbability:P1} fraud probability. " +
                                      $"Top signals: {mlResult.TopFeatures}",
                        ScoreContribution = mlResult.ScoreContribution,
                        Severity = mlResult.ScoreContribution >= 30 ? FactorSeverity.Critical
                                 : mlResult.ScoreContribution >= 15 ? FactorSeverity.Warning
                                 : FactorSeverity.Info,
                        ContextData = mlResult.ToJson()
                    };
                    riskFactors.Add(mlFactor);
                    totalScore += mlFactor.ScoreContribution;
                    totalScore = Math.Min(totalScore, 100);

                    _logger.LogInformation(
                        "ML enhanced scoring for {TransactionId}: +{MLScore} pts (P(fraud)={Prob:F3})",
                        transaction.Id, mlResult.ScoreContribution, mlResult.FraudProbability);
                }
            }
            catch (Exception ex)
            {
                // ML failure is non-fatal — fall back to rule-only scoring
                _logger.LogWarning(ex, "ML scoring failed for {TransactionId}, using rule-only score",
                    transaction.Id);
            }
        }

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
            TenantId = _tenantContext.TenantId,
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

        // Alert on High and Critical risk — compliance team needs to know immediately
        if (riskLevel >= RiskLevel.High)
        {
            await _alertingService.AlertTransactionAsync(
                tenantId: _tenantContext.TenantId,
                externalId: transaction.ExternalId,
                riskScore: totalScore,
                riskLevel: riskLevel.ToString(),
                amount: transaction.Amount,
                currency: transaction.SourceCurrency,
                senderId: transaction.SenderId,
                cancellationToken);

            // Send email alert in parallel with Slack
            await _emailNotificationService.SendRiskAlertEmailAsync(
                tenantId: _tenantContext.TenantId,
                externalId: transaction.ExternalId,
                riskScore: totalScore,
                riskLevel: riskLevel.ToString(),
                amount: transaction.Amount,
                currency: transaction.SourceCurrency,
                senderId: transaction.SenderId,
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
            "HIGH_RISK_CORRIDOR" => await EvaluateHighRiskCorridorAsync(rule, transaction, cancellationToken),
            "ROUND_AMOUNT" => EvaluateRoundAmount(rule, transaction),
            "UNUSUAL_TIME" => EvaluateUnusualTime(rule, transaction),
            _ => !string.IsNullOrEmpty(rule.ExpressionField)
                ? EvaluateExpressionRule(rule, transaction, profile)
                : null
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

    private async Task<RiskFactor?> EvaluateHighRiskCorridorAsync(RiskRule rule, Transaction transaction, CancellationToken cancellationToken)
    {
        // Load tenant-configurable high-risk country list from OrganizationSettings.
        // Falls back to the static OFAC/FATF default if no tenant settings exist.
        HashSet<string> highRiskCountries;
        try
        {
            var settings = await _context.OrganizationSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);

            highRiskCountries = settings?.GetHighRiskCountrySet()
                ?? OrganizationSettings.DefaultHighRiskCountries;
        }
        catch
        {
            // If the column doesn't exist yet (pre-migration), use defaults
            highRiskCountries = OrganizationSettings.DefaultHighRiskCountries;
        }

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

    // ── Expression rule evaluation ──────────────────────────────────

    /// <summary>
    /// Evaluates a user-defined expression rule dynamically.
    /// Extracts the field value from the transaction/profile, applies the operator,
    /// and compares against the configured value.
    /// </summary>
    private RiskFactor? EvaluateExpressionRule(RiskRule rule, Transaction transaction, CustomerProfile profile)
    {
        if (string.IsNullOrEmpty(rule.ExpressionField) ||
            string.IsNullOrEmpty(rule.ExpressionOperator) ||
            string.IsNullOrEmpty(rule.ExpressionValue))
        {
            return null;
        }

        try
        {
            var fieldValue = GetExpressionFieldValue(rule.ExpressionField, transaction, profile);
            if (fieldValue == null) return null;

            bool triggered = EvaluateCondition(fieldValue, rule.ExpressionOperator, rule.ExpressionValue);

            if (triggered)
            {
                var fieldInfo = RiskRule.ExpressionFields.TryGetValue(rule.ExpressionField, out var info)
                    ? info.DisplayName
                    : rule.ExpressionField;

                var opDisplay = rule.ExpressionOperator switch
                {
                    ">=" => "≥",
                    "<=" => "≤",
                    ">"  => ">",
                    "<"  => "<",
                    "==" => "=",
                    "!=" => "≠",
                    "contains" => "contains",
                    "not_contains" => "does not contain",
                    _ => rule.ExpressionOperator
                };

                return new RiskFactor
                {
                    Category = rule.Category,
                    RuleName = rule.Name,
                    Description = $"{fieldInfo} ({fieldValue}) {opDisplay} {rule.ExpressionValue}",
                    ScoreContribution = rule.ScoreWeight,
                    Severity = rule.ScoreWeight >= 25 ? FactorSeverity.Warning : FactorSeverity.Info,
                    ContextData = $"{{\"field\": \"{rule.ExpressionField}\", \"operator\": \"{rule.ExpressionOperator}\", " +
                                  $"\"expected\": \"{rule.ExpressionValue}\", \"actual\": \"{fieldValue}\"}}"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Expression rule '{RuleCode}' failed to evaluate: {Error}",
                rule.RuleCode, ex.Message);
        }

        return null;
    }

    // ── Compound rule evaluation ────────────────────────────────────

    /// <summary>
    /// Evaluates a compound rule (RuleGroup) with AND/OR logic.
    /// AND = all conditions must match; OR = any condition matches.
    /// </summary>
    private RiskFactor? EvaluateCompoundRule(RuleGroup group, Transaction transaction, CustomerProfile profile)
    {
        if (group.Conditions == null || group.Conditions.Count == 0) return null;

        try
        {
            var conditions = group.Conditions.OrderBy(c => c.OrderIndex).ToList();
            var conditionResults = new List<(RuleGroupCondition condition, bool matched, object? actualValue)>();

            foreach (var condition in conditions)
            {
                var fieldValue = GetExpressionFieldValue(condition.ExpressionField, transaction, profile);
                bool matched = fieldValue != null && EvaluateCondition(fieldValue, condition.ExpressionOperator, condition.ExpressionValue);
                conditionResults.Add((condition, matched, fieldValue));
            }

            bool triggered = group.LogicalOperator.Equals("AND", StringComparison.OrdinalIgnoreCase)
                ? conditionResults.All(r => r.matched)
                : conditionResults.Any(r => r.matched);

            if (triggered)
            {
                // Build a description showing which conditions matched
                var matchedConditions = conditionResults
                    .Where(r => r.matched)
                    .Select(r =>
                    {
                        var fieldName = RiskRule.ExpressionFields.TryGetValue(r.condition.ExpressionField, out var info)
                            ? info.DisplayName
                            : r.condition.ExpressionField;
                        var opDisplay = r.condition.ExpressionOperator switch
                        {
                            ">=" => "≥", "<=" => "≤", ">" => ">", "<" => "<",
                            "==" => "=", "!=" => "≠",
                            "contains" => "contains", "not_contains" => "doesn't contain",
                            _ => r.condition.ExpressionOperator
                        };
                        return $"{fieldName} ({r.actualValue}) {opDisplay} {r.condition.ExpressionValue}";
                    })
                    .ToList();

                var operatorLabel = group.LogicalOperator.Equals("AND", StringComparison.OrdinalIgnoreCase) ? "AND" : "OR";
                var conditionSummary = string.Join($" {operatorLabel} ", matchedConditions);

                return new RiskFactor
                {
                    Category = group.Category,
                    RuleName = $"⚡ {group.Name}",
                    Description = $"Compound rule ({operatorLabel}): {conditionSummary}",
                    ScoreContribution = group.RiskPoints,
                    Severity = group.RiskPoints >= 30 ? FactorSeverity.Critical
                             : group.RiskPoints >= 20 ? FactorSeverity.Warning
                             : FactorSeverity.Info,
                    ContextData = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        ruleGroupId = group.Id,
                        ruleGroupName = group.Name,
                        logicalOperator = group.LogicalOperator,
                        conditionsEvaluated = conditionResults.Count,
                        conditionsMatched = conditionResults.Count(r => r.matched),
                        conditions = conditionResults.Select(r => new
                        {
                            field = r.condition.ExpressionField,
                            op = r.condition.ExpressionOperator,
                            expected = r.condition.ExpressionValue,
                            actual = r.actualValue?.ToString(),
                            matched = r.matched
                        })
                    })
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Compound rule '{RuleName}' failed to evaluate: {Error}",
                group.Name, ex.Message);
        }

        return null;
    }

    /// <summary>
    /// Extracts a field value from the transaction or customer profile.
    /// </summary>
    private static object? GetExpressionFieldValue(string field, Transaction transaction, CustomerProfile profile)
    {
        return field switch
        {
            "Amount"              => transaction.Amount,
            "SourceCountry"       => transaction.SourceCountry,
            "DestinationCountry"  => transaction.DestinationCountry,
            "SourceCurrency"      => transaction.SourceCurrency,
            "DestinationCurrency" => transaction.DestinationCurrency,
            "TransactionHour"     => transaction.CreatedAt.Hour,
            "TotalTransactions"   => profile.TotalTransactions,
            "TotalVolume"         => profile.TotalVolume,
            "AvgTransaction"      => profile.AverageTransactionAmount,
            "MaxTransaction"      => profile.MaxTransactionAmount,
            "FlaggedCount"        => profile.FlaggedTransactionCount,
            _ => null
        };
    }

    /// <summary>
    /// Evaluates a comparison between a field value and a target value.
    /// Handles numeric (decimal, int) and string comparisons.
    /// </summary>
    private static bool EvaluateCondition(object fieldValue, string op, string targetValue)
    {
        // Try numeric comparison first
        if (fieldValue is decimal decVal && decimal.TryParse(targetValue, out var decTarget))
        {
            return op switch
            {
                ">="  => decVal >= decTarget,
                "<="  => decVal <= decTarget,
                ">"   => decVal > decTarget,
                "<"   => decVal < decTarget,
                "=="  => decVal == decTarget,
                "!="  => decVal != decTarget,
                _ => false
            };
        }

        if (fieldValue is int intVal && int.TryParse(targetValue, out var intTarget))
        {
            return op switch
            {
                ">="  => intVal >= intTarget,
                "<="  => intVal <= intTarget,
                ">"   => intVal > intTarget,
                "<"   => intVal < intTarget,
                "=="  => intVal == intTarget,
                "!="  => intVal != intTarget,
                _ => false
            };
        }

        // String comparison (case-insensitive)
        var strVal = fieldValue.ToString() ?? "";
        var strTarget = targetValue;

        return op switch
        {
            "=="           => strVal.Equals(strTarget, StringComparison.OrdinalIgnoreCase),
            "!="           => !strVal.Equals(strTarget, StringComparison.OrdinalIgnoreCase),
            "contains"     => strVal.Contains(strTarget, StringComparison.OrdinalIgnoreCase),
            "not_contains" => !strVal.Contains(strTarget, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
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
                TenantId = _tenantContext.TenantId,
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
