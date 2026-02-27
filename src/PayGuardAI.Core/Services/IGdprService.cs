namespace PayGuardAI.Core.Services;

/// <summary>
/// GDPR compliance service — data subject access requests, right to erasure,
/// data portability, and consent/request audit logging.
/// </summary>
public interface IGdprService
{
    // ── Data Subject Access Request (DSAR) ─────────────────────────────

    /// <summary>
    /// Exports ALL personal data associated with a customer/sender ID as
    /// a machine-readable JSON bundle (GDPR Art. 15 &amp; 20 — Right of
    /// Access + Data Portability).
    /// </summary>
    Task<GdprDataExport> ExportSubjectDataAsync(string tenantId, string subjectId, string requestedBy, CancellationToken ct = default);

    /// <summary>
    /// Exports the same data bundle as CSV for human-readable download.
    /// </summary>
    Task<byte[]> ExportSubjectDataCsvAsync(string tenantId, string subjectId, string requestedBy, CancellationToken ct = default);

    // ── Right to Erasure (Right to Be Forgotten) ───────────────────────

    /// <summary>
    /// Anonymizes all personal data for a subject. PII fields (sender/receiver
    /// IDs, raw payloads) are replaced with irreversible hashes while keeping
    /// transaction structure intact for AML compliance (GDPR Art. 17).
    /// Returns the number of records anonymized.
    /// </summary>
    Task<GdprErasureResult> AnonymizeSubjectDataAsync(string tenantId, string subjectId, string requestedBy, CancellationToken ct = default);

    // ── GDPR Request Log ───────────────────────────────────────────────

    /// <summary>
    /// Returns the GDPR request history for a tenant (audit trail).
    /// </summary>
    Task<List<GdprRequest>> GetRequestHistoryAsync(string tenantId, int limit = 50, CancellationToken ct = default);

    /// <summary>
    /// Searches for subjects matching a partial ID, returning distinct
    /// sender/receiver IDs.
    /// </summary>
    Task<List<string>> SearchSubjectsAsync(string tenantId, string query, CancellationToken ct = default);
}

// ── DTOs ────────────────────────────────────────────────────────────────

/// <summary>
/// Complete data export bundle for a data subject (GDPR Art. 15/20).
/// </summary>
public class GdprDataExport
{
    public string SubjectId { get; set; } = "";
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
    public string TenantId { get; set; } = "";

    /// <summary>Customer profile (if one exists).</summary>
    public GdprCustomerData? CustomerProfile { get; set; }

    /// <summary>All transactions where the subject is sender or receiver.</summary>
    public List<GdprTransactionData> Transactions { get; set; } = [];

    /// <summary>All risk analyses linked to those transactions.</summary>
    public List<GdprRiskAnalysisData> RiskAnalyses { get; set; } = [];

    /// <summary>Audit log entries mentioning this subject.</summary>
    public List<GdprAuditEntry> AuditEntries { get; set; } = [];

    public int TotalRecords => Transactions.Count + RiskAnalyses.Count + AuditEntries.Count + (CustomerProfile != null ? 1 : 0);

    /// <summary>Raw JSON bytes for download.</summary>
    public byte[]? JsonBytes { get; set; }
}

/// <summary>Customer profile subset for DSAR export.</summary>
public class GdprCustomerData
{
    public string ExternalId { get; set; } = "";
    public int TotalTransactions { get; set; }
    public decimal TotalVolume { get; set; }
    public decimal AverageTransactionAmount { get; set; }
    public string FrequentCountries { get; set; } = "";
    public string KycLevel { get; set; } = "";
    public string RiskTier { get; set; } = "";
    public int FlaggedTransactionCount { get; set; }
    public int RejectedTransactionCount { get; set; }
    public DateTime? FirstTransactionAt { get; set; }
    public DateTime? LastTransactionAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Transaction subset for DSAR export (no raw payload).</summary>
public class GdprTransactionData
{
    public Guid Id { get; set; }
    public string ExternalId { get; set; } = "";
    public string Type { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal Amount { get; set; }
    public string SourceCurrency { get; set; } = "";
    public string DestinationCurrency { get; set; } = "";
    public string SenderId { get; set; } = "";
    public string? ReceiverId { get; set; }
    public string SourceCountry { get; set; } = "";
    public string DestinationCountry { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

/// <summary>Risk analysis subset for DSAR export.</summary>
public class GdprRiskAnalysisData
{
    public Guid TransactionId { get; set; }
    public int RiskScore { get; set; }
    public string RiskLevel { get; set; } = "";
    public string ReviewStatus { get; set; } = "";
    public string Explanation { get; set; } = "";
    public DateTime AnalyzedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
}

/// <summary>Audit log subset for DSAR export.</summary>
public class GdprAuditEntry
{
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string PerformedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Result of an anonymization (erasure) operation.</summary>
public class GdprErasureResult
{
    public string SubjectId { get; set; } = "";
    public int TransactionsAnonymized { get; set; }
    public int RiskAnalysesAnonymized { get; set; }
    public int AuditEntriesAnonymized { get; set; }
    public bool CustomerProfileRemoved { get; set; }
    public DateTime ErasedAt { get; set; } = DateTime.UtcNow;
    public int TotalRecords => TransactionsAnonymized + RiskAnalysesAnonymized + AuditEntriesAnonymized + (CustomerProfileRemoved ? 1 : 0);
}

/// <summary>
/// Tracks GDPR data-subject requests for the audit trail.
/// Persisted in the GdprRequests table.
/// </summary>
public class GdprRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantId { get; set; } = "";
    public string SubjectId { get; set; } = "";

    /// <summary>DSAR_EXPORT, DSAR_CSV, ERASURE</summary>
    public string RequestType { get; set; } = "";
    public string RequestedBy { get; set; } = "";
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Completed, Failed</summary>
    public string Status { get; set; } = "Completed";

    /// <summary>Number of records affected.</summary>
    public int RecordsAffected { get; set; }

    /// <summary>Optional notes or error message.</summary>
    public string? Notes { get; set; }
}
