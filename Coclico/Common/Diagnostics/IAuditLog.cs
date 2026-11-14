namespace Coclico.Services;

internal sealed record AiDecisionContext(
    string Prompt,
    string RawResponse,
    string DecisionMode);

internal sealed record AuditEntry(
    DateTimeOffset Timestamp,
    string Actor,
    string Action,
    string Target,
    bool Success,
    string? Details = null,
    AiDecisionContext? AiDecision = null);

internal interface IAuditLog
{
    Task LogAsync(AuditEntry entry);

    Task<IReadOnlyList<AuditEntry>> GetRecentAsync(int maxCount = 100);

    void Prune(TimeSpan olderThan);
}
