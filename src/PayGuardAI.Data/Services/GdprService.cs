using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

/// <summary>
/// GDPR compliance service implementation — DSAR exports, anonymization,
/// and request audit logging. All operations are tenant-scoped.
/// </summary>
public class GdprService : IGdprService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<GdprService> _logger;
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public GdprService(ApplicationDbContext db, ILogger<GdprService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── Data Subject Access Request ────────────────────────────────────

    public async Task<GdprDataExport> ExportSubjectDataAsync(
        string tenantId, string subjectId, string requestedBy, CancellationToken ct = default)
    {
        _logger.LogInformation("GDPR DSAR export requested for subject {SubjectId} by {RequestedBy}", subjectId, requestedBy);

        var export = await BuildExportAsync(tenantId, subjectId, ct);

        // Log once for the JSON export
        await LogRequestAsync(tenantId, subjectId, "DSAR_EXPORT", requestedBy, export.TotalRecords, ct);

        _logger.LogInformation("GDPR DSAR export completed: {TotalRecords} records for subject {SubjectId}", export.TotalRecords, subjectId);
        return export;
    }

    /// <summary>
    /// Builds the export payload without logging — shared by JSON and CSV paths
    /// to avoid duplicate audit-trail entries.
    /// </summary>
    private async Task<GdprDataExport> BuildExportAsync(
        string tenantId, string subjectId, CancellationToken ct)
    {
        var export = new GdprDataExport
        {
            SubjectId = subjectId,
            TenantId = tenantId,
            ExportedAt = DateTime.UtcNow
        };

        // 1. Customer profile
        var profile = await _db.CustomerProfiles
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.ExternalId == subjectId, ct);
        if (profile != null)
        {
            export.CustomerProfile = new GdprCustomerData
            {
                ExternalId = profile.ExternalId,
                TotalTransactions = profile.TotalTransactions,
                TotalVolume = profile.TotalVolume,
                AverageTransactionAmount = profile.AverageTransactionAmount,
                FrequentCountries = profile.FrequentCountries,
                KycLevel = profile.KycLevel.ToString(),
                RiskTier = profile.RiskTier.ToString(),
                FlaggedTransactionCount = profile.FlaggedTransactionCount,
                RejectedTransactionCount = profile.RejectedTransactionCount,
                FirstTransactionAt = profile.FirstTransactionAt,
                LastTransactionAt = profile.LastTransactionAt,
                UpdatedAt = profile.UpdatedAt
            };
        }

        // 2. Transactions (sender or receiver)
        var transactions = await _db.Transactions
            .Where(t => t.TenantId == tenantId && (t.SenderId == subjectId || t.ReceiverId == subjectId))
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        export.Transactions = transactions.Select(t => new GdprTransactionData
        {
            Id = t.Id,
            ExternalId = t.ExternalId,
            Type = t.Type,
            Status = t.Status,
            Amount = t.Amount,
            SourceCurrency = t.SourceCurrency,
            DestinationCurrency = t.DestinationCurrency,
            SenderId = t.SenderId,
            ReceiverId = t.ReceiverId,
            SourceCountry = t.SourceCountry,
            DestinationCountry = t.DestinationCountry,
            CreatedAt = t.CreatedAt
        }).ToList();

        // 3. Risk analyses for those transactions
        var txIds = transactions.Select(t => t.Id).ToHashSet();
        var analyses = await _db.RiskAnalyses
            .Where(r => r.TenantId == tenantId && txIds.Contains(r.TransactionId))
            .ToListAsync(ct);

        export.RiskAnalyses = analyses.Select(r => new GdprRiskAnalysisData
        {
            TransactionId = r.TransactionId,
            RiskScore = r.RiskScore,
            RiskLevel = r.RiskLevel.ToString(),
            ReviewStatus = r.ReviewStatus.ToString(),
            Explanation = r.Explanation,
            AnalyzedAt = r.AnalyzedAt,
            ReviewedBy = r.ReviewedBy,
            ReviewedAt = r.ReviewedAt
        }).ToList();

        // 4. Audit entries mentioning this subject
        //    Reviews log the RiskAnalysis GUID as EntityId, not the sender ID,
        //    so we must also search for audit entries linked to analysis IDs
        //    and transaction IDs associated with this subject.
        var analysisIds = analyses.Select(a => a.Id.ToString()).ToHashSet();
        var txIdStrings = txIds.Select(id => id.ToString()).ToHashSet();

        // Combine all IDs that could appear as EntityId for this subject
        var allEntityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { subjectId };
        foreach (var id in analysisIds) allEntityIds.Add(id);
        foreach (var id in txIdStrings) allEntityIds.Add(id);

        var auditEntries = await _db.AuditLogs
            .Where(a => a.TenantId == tenantId && allEntityIds.Contains(a.EntityId))
            .OrderByDescending(a => a.CreatedAt)
            .Take(500)
            .ToListAsync(ct);

        export.AuditEntries = auditEntries.Select(a => new GdprAuditEntry
        {
            Action = a.Action,
            EntityType = a.EntityType,
            EntityId = a.EntityId,
            PerformedBy = a.PerformedBy,
            CreatedAt = a.CreatedAt,
            Notes = a.Notes
        }).ToList();

        // Serialize to JSON bytes
        export.JsonBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            export.SubjectId,
            export.TenantId,
            export.ExportedAt,
            export.CustomerProfile,
            export.Transactions,
            export.RiskAnalyses,
            export.AuditEntries,
            export.TotalRecords
        }, _jsonOpts);

        return export;
    }

    public async Task<byte[]> ExportSubjectDataCsvAsync(
        string tenantId, string subjectId, string requestedBy, CancellationToken ct = default)
    {
        _logger.LogInformation("GDPR DSAR CSV export requested for subject {SubjectId} by {RequestedBy}", subjectId, requestedBy);

        var export = await BuildExportAsync(tenantId, subjectId, ct);

        var sb = new StringBuilder();

        // Section: Customer Profile
        sb.AppendLine("=== CUSTOMER PROFILE ===");
        if (export.CustomerProfile is { } cp)
        {
            sb.AppendLine("ExternalId,TotalTransactions,TotalVolume,AvgAmount,KycLevel,RiskTier,FlaggedCount,RejectedCount,FirstTx,LastTx");
            sb.AppendLine($"{Escape(cp.ExternalId)},{cp.TotalTransactions},{cp.TotalVolume},{cp.AverageTransactionAmount},{cp.KycLevel},{cp.RiskTier},{cp.FlaggedTransactionCount},{cp.RejectedTransactionCount},{cp.FirstTransactionAt:O},{cp.LastTransactionAt:O}");
        }
        else
        {
            sb.AppendLine("No customer profile on record.");
        }
        sb.AppendLine();

        // Section: Transactions
        sb.AppendLine("=== TRANSACTIONS ===");
        sb.AppendLine("Id,ExternalId,Type,Status,Amount,SourceCurrency,DestCurrency,SenderId,ReceiverId,SourceCountry,DestCountry,CreatedAt");
        foreach (var tx in export.Transactions)
        {
            sb.AppendLine($"{tx.Id},{Escape(tx.ExternalId)},{tx.Type},{tx.Status},{tx.Amount},{tx.SourceCurrency},{tx.DestinationCurrency},{Escape(tx.SenderId)},{Escape(tx.ReceiverId ?? "")},{tx.SourceCountry},{tx.DestinationCountry},{tx.CreatedAt:O}");
        }
        sb.AppendLine();

        // Section: Risk Analyses
        sb.AppendLine("=== RISK ANALYSES ===");
        sb.AppendLine("TransactionId,RiskScore,RiskLevel,ReviewStatus,Explanation,AnalyzedAt,ReviewedBy,ReviewedAt");
        foreach (var ra in export.RiskAnalyses)
        {
            sb.AppendLine($"{ra.TransactionId},{ra.RiskScore},{ra.RiskLevel},{ra.ReviewStatus},{Escape(ra.Explanation)},{ra.AnalyzedAt:O},{Escape(ra.ReviewedBy ?? "")},{ra.ReviewedAt:O}");
        }
        sb.AppendLine();

        // Section: Audit Log
        sb.AppendLine("=== AUDIT LOG ===");
        sb.AppendLine("Action,EntityType,EntityId,PerformedBy,CreatedAt,Notes");
        foreach (var ae in export.AuditEntries)
        {
            sb.AppendLine($"{ae.Action},{ae.EntityType},{Escape(ae.EntityId)},{Escape(ae.PerformedBy)},{ae.CreatedAt:O},{Escape(ae.Notes ?? "")}");
        }

        // Update the logged request type to CSV
        await LogRequestAsync(tenantId, subjectId, "DSAR_CSV", requestedBy, export.TotalRecords, ct);

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    // ── Right to Erasure ───────────────────────────────────────────────

    public async Task<GdprErasureResult> AnonymizeSubjectDataAsync(
        string tenantId, string subjectId, string requestedBy, CancellationToken ct = default)
    {
        _logger.LogWarning("GDPR ERASURE requested for subject {SubjectId} by {RequestedBy} — anonymizing PII", subjectId, requestedBy);

        var anonymizedId = HashSubjectId(subjectId);
        var result = new GdprErasureResult { SubjectId = subjectId };

        // 1. Anonymize transactions (sender/receiver IDs + raw payload)
        var transactions = await _db.Transactions
            .Where(t => t.TenantId == tenantId && (t.SenderId == subjectId || t.ReceiverId == subjectId))
            .ToListAsync(ct);

        foreach (var tx in transactions)
        {
            if (tx.SenderId == subjectId) tx.SenderId = anonymizedId;
            if (tx.ReceiverId == subjectId) tx.ReceiverId = anonymizedId;
            tx.RawPayload = "[REDACTED — GDPR Art.17 Erasure]";
        }
        result.TransactionsAnonymized = transactions.Count;

        // 2. Anonymize risk analyses — clear reviewer identity if it matches
        var txIds = transactions.Select(t => t.Id).ToHashSet();
        var riskAnalyses = await _db.RiskAnalyses
            .Where(r => r.TenantId == tenantId && txIds.Contains(r.TransactionId))
            .ToListAsync(ct);

        foreach (var ra in riskAnalyses)
        {
            // The explanation may reference the subject ID — scrub it
            if (!string.IsNullOrEmpty(ra.Explanation) && ra.Explanation.Contains(subjectId))
                ra.Explanation = ra.Explanation.Replace(subjectId, anonymizedId);
        }
        result.RiskAnalysesAnonymized = riskAnalyses.Count;

        // 3. Anonymize audit log entries
        var auditEntries = await _db.AuditLogs
            .Where(a => a.TenantId == tenantId && a.EntityId == subjectId)
            .ToListAsync(ct);

        foreach (var ae in auditEntries)
        {
            ae.EntityId = anonymizedId;
            if (!string.IsNullOrEmpty(ae.OldValues) && ae.OldValues.Contains(subjectId))
                ae.OldValues = ae.OldValues.Replace(subjectId, anonymizedId);
            if (!string.IsNullOrEmpty(ae.NewValues) && ae.NewValues.Contains(subjectId))
                ae.NewValues = ae.NewValues.Replace(subjectId, anonymizedId);
            if (!string.IsNullOrEmpty(ae.Notes) && ae.Notes.Contains(subjectId))
                ae.Notes = ae.Notes.Replace(subjectId, anonymizedId);
        }
        result.AuditEntriesAnonymized = auditEntries.Count;

        // 4. Remove customer profile entirely
        var profile = await _db.CustomerProfiles
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.ExternalId == subjectId, ct);
        if (profile != null)
        {
            _db.CustomerProfiles.Remove(profile);
            result.CustomerProfileRemoved = true;
        }

        await _db.SaveChangesAsync(ct);

        // 5. Log the erasure request
        await LogRequestAsync(tenantId, subjectId, "ERASURE", requestedBy, result.TotalRecords, 
            $"Anonymized to hash: {anonymizedId}", ct);

        // 6. Write audit log for the erasure action itself
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            Action = "GDPR_ERASURE",
            EntityType = "DataSubject",
            EntityId = anonymizedId,
            PerformedBy = requestedBy,
            Notes = $"Right to Erasure exercised. {result.TotalRecords} records anonymized. Original subject ID irreversibly hashed.",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        _logger.LogWarning("GDPR ERASURE completed: {TotalRecords} records anonymized for subject {SubjectId}", result.TotalRecords, subjectId);
        return result;
    }

    // ── Request History ────────────────────────────────────────────────

    public async Task<List<GdprRequest>> GetRequestHistoryAsync(string tenantId, int limit = 50, CancellationToken ct = default)
    {
        return await _db.Set<GdprRequest>()
            .Where(r => r.TenantId == tenantId)
            .OrderByDescending(r => r.RequestedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    // ── Subject Search ─────────────────────────────────────────────────

    public async Task<List<string>> SearchSubjectsAsync(string tenantId, string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return [];

        var senderIds = await _db.Transactions
            .Where(t => t.TenantId == tenantId && t.SenderId.Contains(query))
            .Select(t => t.SenderId)
            .Distinct()
            .Take(20)
            .ToListAsync(ct);

        var receiverIds = await _db.Transactions
            .Where(t => t.TenantId == tenantId && t.ReceiverId != null && t.ReceiverId.Contains(query))
            .Select(t => t.ReceiverId!)
            .Distinct()
            .Take(20)
            .ToListAsync(ct);

        var profileIds = await _db.CustomerProfiles
            .Where(c => c.TenantId == tenantId && c.ExternalId.Contains(query))
            .Select(c => c.ExternalId)
            .Distinct()
            .Take(20)
            .ToListAsync(ct);

        return senderIds
            .Union(receiverIds)
            .Union(profileIds)
            .Distinct()
            .OrderBy(id => id)
            .Take(25)
            .ToList();
    }

    // ── Private Helpers ────────────────────────────────────────────────

    /// <summary>
    /// Irreversibly hashes a subject ID for anonymization.
    /// Uses SHA-256 truncated to 16 chars prefixed with "ANON-".
    /// </summary>
    private static string HashSubjectId(string subjectId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(subjectId + "-payguard-gdpr-salt"));
        return "ANON-" + Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private async Task LogRequestAsync(string tenantId, string subjectId, string requestType,
        string requestedBy, int recordsAffected, CancellationToken ct)
    {
        await LogRequestAsync(tenantId, subjectId, requestType, requestedBy, recordsAffected, null, ct);
    }

    private async Task LogRequestAsync(string tenantId, string subjectId, string requestType,
        string requestedBy, int recordsAffected, string? notes, CancellationToken ct)
    {
        _db.Set<GdprRequest>().Add(new GdprRequest
        {
            TenantId = tenantId,
            SubjectId = subjectId,
            RequestType = requestType,
            RequestedBy = requestedBy,
            RequestedAt = DateTime.UtcNow,
            Status = "Completed",
            RecordsAffected = recordsAffected,
            Notes = notes
        });
        await _db.SaveChangesAsync(ct);
    }
}
