using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Scytales.Application.Configuration;
using Scytales.Application.UseCases.Fail;
using Scytales.Application.UseCases.Process;
using Scytales.Application.UseCases.Review;
using Scytales.Application.UseCases.Submit;
using Scytales.Domain.Applications.Policies;

namespace Scytales.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the use cases, the workflow rules (bound from <c>WorkflowRules</c>, validated at startup)
    /// and the default policies. The ports in <c>Abstractions</c> are registered by Infrastructure.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<WorkflowOptions>()
            .BindConfiguration(WorkflowOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<WorkflowOptions>, WorkflowOptionsValidator>();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<WorkflowOptions>>().Value.ToRules());

        // Default policies; replace any of them by registering another implementation.
        services.TryAddSingleton<IDataValidationPolicy, DataValidationPolicy>();
        services.TryAddSingleton<IPhotoCheckPolicy, PhotoCheckPolicy>();
        services.TryAddSingleton<IRiskAssessmentPolicy, RiskAssessmentPolicy>();

        services.AddScoped<SubmitApplicationHandler>();
        services.AddScoped<ProcessApplicationHandler>();
        services.AddScoped<ReviewApplicationHandler>();
        services.AddScoped<FailApplicationHandler>();

        return services;
    }
}
