using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

/// <summary>
/// Human-in-the-Loop review service implementation.
/// Handles approval, rejection, and escalation workflows.
/// </summary>
public class ReviewService : IReviewService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ReviewService> _logger;
    private readonly IMemoryCache _cache;
    private readonly ITenantContext _tenantContext;
    private readonly IMetricsService _metrics;
    private readonly IEmailNotificationService _emailService;
    private readonly IAlertingService _alertingService;

    private const string DashboardCacheKey = "dashboard-stats";

    public ReviewService(
        ApplicationDbContext context,
        ILogger<ReviewService> logger,
        IMemoryCache cache,
        ITenantContext tenantContext,
        IMetricsService metrics,
        IEmailNotificationService emailService,
        IAlertingService alertingService)
    {
        _context = context;
        _logger = logger;
        _cache = cache;
        _tenantContext = tenantContext;
        _metrics = metrics;
        _emailService = emailService;
        _alertingService = alertingService;
    }

    public async Task<RiskAnalysis> ApproveAsync(
        Guid analysisId, 
        string reviewedBy, 
        string? notes = null, 
        CancellationToken cancellationToken = default)
    {
        var analysis = await GetAnalysisForReviewAsync(analysisId, cancellationToken);

        var oldStatus = analysis.ReviewStatus;
        analysis.ReviewStatus = ReviewStatus.Approved;
        analysis.ReviewedBy = reviewedBy;
        analysis.ReviewedAt = DateTime.UtcNow;
        analysis.ReviewNotes = notes;

        await _context.SaveChangesAsync(cancellationToken);
        InvalidateCaches();

        // Log audit trail
        await LogAuditAsync("REVIEW_APPROVED", "RiskAnalysis", analysisId.ToString(), reviewedBy,
            $"{{\"oldStatus\": \"{oldStatus}\"}}", 
            $"{{\"newStatus\": \"Approved\"}}",
            notes, cancellationToken);

        // Update customer profile
        await UpdateCustomerAfterReviewAsync(analysis.Transaction.SenderId, false, cancellationToken);

        _metrics.RecordReviewCompleted("approved");
        _logger.LogInformation("Transaction {TransactionId} approved by {Reviewer}", 
            analysis.TransactionId, reviewedBy);

        return analysis;
    }

    public async Task<RiskAnalysis> RejectAsync(
        Guid analysisId, 
        string reviewedBy, 
        string? notes = null, 
        CancellationToken cancellationToken = default)
    {
        var analysis = await GetAnalysisForReviewAsync(analysisId, cancellationToken);

        var oldStatus = analysis.ReviewStatus;
        analysis.ReviewStatus = ReviewStatus.Rejected;
        analysis.ReviewedBy = reviewedBy;
        analysis.ReviewedAt = DateTime.UtcNow;
        analysis.ReviewNotes = notes;

        await _context.SaveChangesAsync(cancellationToken);
        InvalidateCaches();

        // Log audit trail
        await LogAuditAsync("REVIEW_REJECTED", "RiskAnalysis", analysisId.ToString(), reviewedBy,
            $"{{\"oldStatus\": \"{oldStatus}\"}}", 
            $"{{\"newStatus\": \"Rejected\"}}",
            notes, cancellationToken);

        // Update customer profile (this was a rejection)
        await UpdateCustomerAfterReviewAsync(analysis.Transaction.SenderId, true, cancellationToken);

        _metrics.RecordReviewCompleted("rejected");
        _logger.LogInformation("Transaction {TransactionId} rejected by {Reviewer}", 
            analysis.TransactionId, reviewedBy);

        return analysis;
    }

    public async Task<RiskAnalysis> EscalateAsync(
        Guid analysisId, 
        string reviewedBy, 
        string? notes = null, 
        CancellationToken cancellationToken = default)
    {
        var analysis = await GetAnalysisForReviewAsync(analysisId, cancellationToken);

        var oldStatus = analysis.ReviewStatus;
        analysis.ReviewStatus = ReviewStatus.Escalated;
        analysis.ReviewedBy = reviewedBy;
        analysis.ReviewedAt = DateTime.UtcNow;
        analysis.ReviewNotes = notes ?? "Escalated for senior review";

        await _context.SaveChangesAsync(cancellationToken);
        InvalidateCaches();

        // Log audit trail
        await LogAuditAsync("REVIEW_ESCALATED", "RiskAnalysis", analysisId.ToString(), reviewedBy,
            $"{{\"oldStatus\": \"{oldStatus}\"}}", 
            $"{{\"newStatus\": \"Escalated\"}}",
            notes, cancellationToken);

        _metrics.RecordReviewCompleted("escalated");
        _logger.LogInformation("Transaction {TransactionId} escalated by {Reviewer}", 
            analysis.TransactionId, reviewedBy);

        // Notify managers/admins about the escalation so they can act on it
        await NotifyEscalationAsync(analysis, reviewedBy, notes, cancellationToken);

        return analysis;
    }

    /// <summary>
    /// When a transaction is escalated, notify all managers and admins in the tenant
    /// so someone with authority picks it up for senior review.
    /// </summary>
    private async Task NotifyEscalationAsync(
        RiskAnalysis analysis, 
        string escalatedBy, 
        string? notes, 
        CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = _tenantContext.TenantId;
            
            // Find all managers/admins/superadmins in this tenant
            var managers = await _context.TeamMembers
                .Where(m => m.TenantId == tenantId 
                         && m.Status == "active"
                         && (m.Role == "Admin" || m.Role == "SuperAdmin" || m.Role == "Manager")
                         && m.Email.ToLower() != escalatedBy.ToLower())
                .ToListAsync(cancellationToken);

            if (!managers.Any())
            {
                _logger.LogWarning("No managers found to notify for escalation of transaction {TransactionId}", 
                    analysis.TransactionId);
                return;
            }

            // Send email to each manager
            foreach (var manager in managers)
            {
                await _emailService.SendReviewAssignmentEmailAsync(
                    tenantId,
                    manager.Email,
                    manager.DisplayName ?? manager.Email,
                    analysis.Transaction.ExternalId ?? analysis.TransactionId.ToString(),
                    analysis.RiskScore,
                    analysis.RiskLevel.ToString(),
                    cancellationToken);
            }

            // Also send an alert
            await _alertingService.AlertAsync(
                $"⚠️ Escalation: Transaction {analysis.Transaction.ExternalId ?? analysis.TransactionId.ToString()} "
                + $"(Risk Score: {analysis.RiskScore}, Level: {analysis.RiskLevel}) "
                + $"was escalated by {escalatedBy}."
                + (string.IsNullOrWhiteSpace(notes) ? "" : $" Reason: {notes}"),
                cancellationToken);

            _logger.LogInformation(
                "Escalation notifications sent to {Count} managers for transaction {TransactionId}",
                managers.Count, analysis.TransactionId);
        }
        catch (Exception ex)
        {
            // Don't fail the escalation itself if notification fails
            _logger.LogError(ex, "Failed to send escalation notifications for transaction {TransactionId}",
                analysis.TransactionId);
        }
    }

    public async Task<IEnumerable<RiskAnalysis>> GetPendingReviewsAsync(
        RiskLevel? minRiskLevel = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.RiskAnalyses
            .Include(r => r.Transaction)
            .Include(r => r.RiskFactors)
            .Where(r => r.ReviewStatus == ReviewStatus.Pending || r.ReviewStatus == ReviewStatus.Escalated);

        if (minRiskLevel.HasValue)
        {
            query = query.Where(r => r.RiskLevel >= minRiskLevel.Value);
        }

        query = query.OrderByDescending(r => r.RiskScore)
                     .ThenBy(r => r.AnalyzedAt);

        if (limit.HasValue)
        {
            query = query.Take(limit.Value);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<RiskAnalysis>> GetReviewHistoryAsync(
        DateTime? fromDate = null,
        DateTime? toDate = null,
        string? reviewedBy = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.RiskAnalyses
            .Include(r => r.Transaction)
            .Include(r => r.RiskFactors)
            .Where(r => r.ReviewedAt != null);

        if (fromDate.HasValue)
        {
            query = query.Where(r => r.ReviewedAt >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(r => r.ReviewedAt <= toDate.Value);
        }

        if (!string.IsNullOrEmpty(reviewedBy))
        {
            query = query.Where(r => r.ReviewedBy == reviewedBy);
        }

        return await query
            .OrderByDescending(r => r.ReviewedAt)
            .ToListAsync(cancellationToken);
    }

    private async Task<RiskAnalysis> GetAnalysisForReviewAsync(Guid analysisId, CancellationToken cancellationToken)
    {
        var analysis = await _context.RiskAnalyses
            .Include(r => r.Transaction)
            .Include(r => r.RiskFactors)
            .FirstOrDefaultAsync(r => r.Id == analysisId, cancellationToken)
            ?? throw new ArgumentException($"Risk analysis {analysisId} not found");

        if (analysis.ReviewStatus == ReviewStatus.Approved || analysis.ReviewStatus == ReviewStatus.Rejected)
        {
            throw new InvalidOperationException($"Analysis {analysisId} has already been reviewed");
        }

        return analysis;
    }

    private async Task UpdateCustomerAfterReviewAsync(string customerId, bool wasRejected, CancellationToken cancellationToken)
    {
        var profile = await _context.CustomerProfiles
            .FirstOrDefaultAsync(p => p.ExternalId == customerId, cancellationToken);

        if (profile == null) return;

        if (wasRejected)
        {
            profile.RejectedTransactionCount++;
        }

        // Recalculate risk tier
        if (profile.TotalTransactions >= 5)
        {
            var flagRate = (double)profile.FlaggedTransactionCount / profile.TotalTransactions;
            var rejectionRate = (double)profile.RejectedTransactionCount / profile.TotalTransactions;

            profile.RiskTier = (flagRate, rejectionRate) switch
            {
                ( > 0.5, _) or (_, > 0.2) => CustomerRiskTier.HighRisk,
                ( > 0.3, _) or (_, > 0.1) => CustomerRiskTier.Elevated,
                ( > 0.1, _) => CustomerRiskTier.Standard,
                _ => CustomerRiskTier.Trusted
            };
        }

        profile.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task LogAuditAsync(
        string action,
        string entityType,
        string entityId,
        string performedBy,
        string? oldValues,
        string? newValues,
        string? notes,
        CancellationToken cancellationToken)
    {
        var auditLog = new AuditLog
        {
            TenantId = _tenantContext.TenantId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            PerformedBy = performedBy,
            OldValues = oldValues,
            NewValues = newValues,
            Notes = notes
        };

        _context.AuditLogs.Add(auditLog);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private void InvalidateCaches()
    {
        _cache.Remove(GetCacheKey(DashboardCacheKey));
    }

    private string GetCacheKey(string key) => $"{_tenantContext.TenantId}:{key}";
}
