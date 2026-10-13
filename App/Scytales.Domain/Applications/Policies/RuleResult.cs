namespace Scytales.Domain.Applications.Policies;

/// <summary>Outcome of a rule set: passes when no reasons were raised.</summary>
public sealed record RuleResult(IReadOnlyList<string> Reasons)
{
    public bool Passed => Reasons.Count == 0;

    /// <summary>The reasons joined for the audit trail's <c>details</c>; null when passed.</summary>
    public string? Summary => Passed ? null : string.Join("; ", Reasons);
}
