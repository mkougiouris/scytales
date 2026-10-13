using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Scytales.Application.Abstractions;
using Scytales.Application.Configuration;
using Scytales.Application.Tests.Fakes;
using Scytales.Application.UseCases.Fail;
using Scytales.Application.UseCases.Process;
using Scytales.Application.UseCases.Review;
using Scytales.Application.UseCases.Submit;
using Scytales.Domain.Applications.Policies;
using Scytales.Domain.Applications.ValueObjects;

namespace Scytales.Application.Tests.DependencyInjection;

public class AddApplicationTests
{
    private static ServiceProvider Build(
        Dictionary<string, string?>? settings = null,
        Action<IServiceCollection>? before = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        // What Infrastructure will provide.
        var store = new InMemoryApplicationStore();
        services.AddSingleton<IApplicationRepository>(store);
        services.AddSingleton<IUnitOfWork>(store);
        services.AddSingleton<IMessagePublisher>(store);
        services.AddSingleton<IPhotoQualityScorer>(new FakePhotoQualityScorer());
        services.AddSingleton<IWatchlist>(new FakeWatchlist());

        before?.Invoke(services);
        services.AddApplication();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    [Fact]
    public void Every_handler_resolves_in_a_scope()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        // GetRequiredService throws when something in the graph is missing.
        scope.ServiceProvider.GetRequiredService<SubmitApplicationHandler>();
        scope.ServiceProvider.GetRequiredService<ProcessApplicationHandler>();
        scope.ServiceProvider.GetRequiredService<ReviewApplicationHandler>();
        scope.ServiceProvider.GetRequiredService<FailApplicationHandler>();
    }

    [Fact]
    public void Default_policies_are_registered()
    {
        using var provider = Build();

        provider.GetRequiredService<IDataValidationPolicy>().ShouldBeOfType<DataValidationPolicy>();
        provider.GetRequiredService<IPhotoCheckPolicy>().ShouldBeOfType<PhotoCheckPolicy>();
        provider.GetRequiredService<IRiskAssessmentPolicy>().ShouldBeOfType<RiskAssessmentPolicy>();
    }

    [Fact]
    public void Workflow_rules_are_bound_from_the_WorkflowRules_section()
    {
        using var provider = Build(new()
        {
            ["WorkflowRules:MinimumAge"] = "17",
            ["WorkflowRules:Photo:AllowedFormats:0"] = "Png"
        });

        var rules = provider.GetRequiredService<WorkflowRules>();

        rules.MinimumAge.ShouldBe(17);
        rules.Photo.AllowedFormats.ShouldBe([PhotoFormat.Png]);
    }

    [Fact]
    public void Inconsistent_configuration_is_rejected_with_the_domains_reason()
    {
        using var provider = Build(new() { ["WorkflowRules:Photo:ManualReviewQualityThreshold"] = "10" });

        var ex = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<WorkflowOptions>>().Value);

        ex.Failures.ShouldContain("ManualReviewQualityThreshold must be above MinQualityScore.");
    }

    [Fact]
    public void Inconsistent_configuration_also_fails_resolving_the_rules()
    {
        using var provider = Build(new() { ["WorkflowRules:MinimumAge"] = "-1" });

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<WorkflowRules>());
    }

    [Fact]
    public void A_policy_registered_before_AddApplication_replaces_the_default()
    {
        using var provider = Build(before: services => services.AddSingleton<IDataValidationPolicy, AlwaysPasses>());

        provider.GetRequiredService<IDataValidationPolicy>().ShouldBeOfType<AlwaysPasses>();
    }

    [Fact]
    public void The_system_clock_is_used_unless_another_is_registered()
    {
        using (var provider = Build())
            provider.GetRequiredService<TimeProvider>().ShouldBeSameAs(TimeProvider.System);

        var fake = new FakeTimeProvider();
        using (var provider = Build(before: services => services.AddSingleton<TimeProvider>(fake)))
            provider.GetRequiredService<TimeProvider>().ShouldBeSameAs(fake);
    }

    private sealed class AlwaysPasses : IDataValidationPolicy
    {
        public RuleResult Evaluate(ApplicantDetails applicant, DateTimeOffset submittedAt) => new([]);
    }
}
