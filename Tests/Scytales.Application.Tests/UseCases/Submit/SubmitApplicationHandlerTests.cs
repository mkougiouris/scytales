using Scytales.Application.Exceptions;
using Scytales.Application.Messages;
using Scytales.Application.UseCases.Submit;
using Scytales.Domain.Applications.ValueObjects;
using Scytales.Domain.Applications.Workflow;
using static Scytales.Application.Tests.TestData;

namespace Scytales.Application.Tests.UseCases.Submit;

public class SubmitApplicationHandlerTests
{
    private readonly Harness _harness = new();

    private Task<SubmitApplicationResult> Submit(SubmitApplicationCommand command) =>
        _harness.Submit().HandleAsync(command, CancellationToken.None);

    [Fact]
    public async Task Stores_the_application_with_the_submitted_details_and_photo()
    {
        var upload = Upload();

        var result = await Submit(ValidCommand(upload));

        var application = _harness.Store.Applications.ShouldHaveSingleItem();
        application.Id.ShouldBe(result.ApplicationId);
        application.Applicant.ShouldBe(ValidApplicant());
        application.Photo.ShouldBe(new ApplicationPhoto(upload.FileName, upload.ContentType, upload.Content));
        application.Photo!.FileSize.ShouldBe(upload.Content.LongLength);
    }

    [Fact]
    public async Task Accepts_an_application_without_a_photo()
    {
        var result = await Submit(ValidCommand() with { Photo = null });

        result.Status.ShouldBe(ApplicationStatus.Submitted);
        _harness.Store.Applications.ShouldHaveSingleItem().Photo.ShouldBeNull();
    }

    [Fact]
    public async Task Accepts_incomplete_details_because_judging_them_is_the_workflows_job()
    {
        var command = new SubmitApplicationCommand(null, null, null, null, "not-an-email", null, null, "Z", null);

        var result = await Submit(command);

        result.Status.ShouldBe(ApplicationStatus.Submitted);
        _harness.Store.Applications.ShouldHaveSingleItem().Applicant.Email.ShouldBe("not-an-email");
    }

    [Fact]
    public async Task Publishes_ApplicationSubmitted_with_the_new_id()
    {
        var result = await Submit(ValidCommand());

        _harness.Store.Published.ShouldHaveSingleItem().ShouldBe(new ApplicationSubmitted(result.ApplicationId));
    }

    [Fact]
    public async Task Publishes_before_saving_so_the_message_commits_with_the_application()
    {
        await Submit(ValidCommand());

        _harness.Store.Calls.ShouldBe(["Add", "Publish:ApplicationSubmitted", "Save"]);
        _harness.Store.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task Timestamps_come_from_the_injected_clock()
    {
        await Submit(ValidCommand());

        var application = _harness.Store.Applications.ShouldHaveSingleItem();
        application.CreatedAt.ShouldBe(_harness.Clock.GetUtcNow());
        application.UpdatedAt.ShouldBe(_harness.Clock.GetUtcNow());
        application.History.ShouldHaveSingleItem().Timestamp.ShouldBe(_harness.Clock.GetUtcNow());
    }

    // BN-2: Idempotency-Key

    private const string Key = "c0ffee00-retry-key";

    [Fact]
    public async Task Stores_the_idempotency_key_with_a_hash_of_the_request()
    {
        await Submit(ValidCommand() with { IdempotencyKey = Key });

        var stored = _harness.Store.Applications.ShouldHaveSingleItem().IdempotencyKey.ShouldNotBeNull();
        stored.Key.ShouldBe(Key);
        stored.RequestHash.ShouldMatch("^[0-9a-f]{64}$");
    }

    [Fact]
    public async Task Without_a_key_nothing_is_looked_up_or_stored()
    {
        await Submit(ValidCommand());

        _harness.Store.Calls.ShouldNotContain("FindByIdempotencyKey");
        _harness.Store.Applications.ShouldHaveSingleItem().IdempotencyKey.ShouldBeNull();
    }

    [Fact]
    public async Task Repeating_a_request_with_its_key_returns_the_first_application_without_storing_or_publishing()
    {
        var first = await Submit(ValidCommand() with { IdempotencyKey = Key });
        _harness.Store.Calls.Clear();

        var repeat = await Submit(ValidCommand() with { IdempotencyKey = Key });

        repeat.ApplicationId.ShouldBe(first.ApplicationId);
        repeat.Replayed.ShouldBeTrue();
        first.Replayed.ShouldBeFalse();
        _harness.Store.Applications.ShouldHaveSingleItem();
        _harness.Store.Published.ShouldHaveSingleItem();
        _harness.Store.Calls.ShouldBe(["FindByIdempotencyKey"]);
    }

    [Fact]
    public async Task A_repeat_reports_the_current_status()
    {
        await Submit(ValidCommand() with { IdempotencyKey = Key });
        _harness.Store.Applications.Single().StartValidation(Now);

        var repeat = await Submit(ValidCommand() with { IdempotencyKey = Key });

        repeat.Status.ShouldBe(ApplicationStatus.ValidatingData);
    }

    [Theory]
    [InlineData("another email")]
    [InlineData("a missing field")]
    [InlineData("an empty field instead of a missing one")]
    [InlineData("other photo bytes")]
    [InlineData("another photo file name")]
    [InlineData("no photo")]
    public async Task Reusing_a_key_for_a_different_request_is_refused(string difference)
    {
        var original = ValidCommand() with { IdempotencyKey = Key };
        await Submit(original);
        var different = difference switch
        {
            "another email" => original with { Email = "someone.else@example.com" },
            "a missing field" => original with { Address = null },
            "an empty field instead of a missing one" => original with { Address = "" },
            "other photo bytes" => original with { Photo = original.Photo! with { Content = Jpeg(size: 20_001) } },
            "another photo file name" => original with { Photo = original.Photo! with { FileName = "other.jpg" } },
            "no photo" => original with { Photo = null },
            _ => throw new ArgumentOutOfRangeException(nameof(difference))
        };

        var exception = await Should.ThrowAsync<IdempotencyKeyMismatchException>(() => Submit(different));

        exception.IdempotencyKey.ShouldBe(Key);
        _harness.Store.Applications.ShouldHaveSingleItem();
        _harness.Store.Published.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Losing_a_race_on_the_same_key_returns_the_winner_and_rolls_back_our_message()
    {
        var command = ValidCommand() with { IdempotencyKey = Key };
        var winner = await SubmittedElsewhere(command);
        _harness.Store.BeforeNextSave = () => _harness.Store.Seed(winner);

        var result = await Submit(command);

        result.ApplicationId.ShouldBe(winner.Id);
        result.Replayed.ShouldBeTrue();
        _harness.Store.Applications.ShouldHaveSingleItem().ShouldBeSameAs(winner);
        _harness.Store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task Losing_a_race_to_a_different_request_with_the_same_key_is_refused()
    {
        var winner = await SubmittedElsewhere(ValidCommand() with { IdempotencyKey = Key, Email = "other@example.com" });
        _harness.Store.BeforeNextSave = () => _harness.Store.Seed(winner);

        await Should.ThrowAsync<IdempotencyKeyMismatchException>(() => Submit(ValidCommand() with { IdempotencyKey = Key }));

        _harness.Store.Applications.ShouldHaveSingleItem().ShouldBeSameAs(winner);
    }

    /// <summary>The application a concurrent request (another API instance) stored for <paramref name="command"/>.</summary>
    private static async Task<LicenceApplication> SubmittedElsewhere(SubmitApplicationCommand command)
    {
        var elsewhere = new Harness();
        await elsewhere.Submit().HandleAsync(command, CancellationToken.None);
        return elsewhere.Store.Applications.Single();
    }
}
