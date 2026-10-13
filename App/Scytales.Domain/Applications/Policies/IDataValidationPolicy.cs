using Scytales.Domain.Applications.ValueObjects;

namespace Scytales.Domain.Applications.Policies;

/// <summary>FR-5.1: any reason raised sends the application to <c>Failed</c>.</summary>
public interface IDataValidationPolicy
{
    /// <param name="submittedAt">
    /// When the application was submitted; age rules are judged on that date, so a re-run always reaches the same answer.
    /// </param>
    RuleResult Evaluate(ApplicantDetails applicant, DateTimeOffset submittedAt);
}
