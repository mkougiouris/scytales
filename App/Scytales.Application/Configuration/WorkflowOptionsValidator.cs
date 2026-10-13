using Microsoft.Extensions.Options;

namespace Scytales.Application.Configuration;

/// <summary>Delegates to the Domain's own invariants, so a misconfigured host fails at startup.</summary>
internal sealed class WorkflowOptionsValidator : IValidateOptions<WorkflowOptions>
{
    public ValidateOptionsResult Validate(string? name, WorkflowOptions options)
    {
        var errors = options.ToRules().Validate().ToList();
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
