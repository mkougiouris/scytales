using Scytales.Domain.Applications;
using Scytales.Domain.Applications.ValueObjects;
using static Scytales.Domain.Tests.TestData;

namespace Scytales.Domain.Tests.Applications.ValueObjects;

public class IdempotencyKeyTests
{
    private static readonly string Hash = new('a', IdempotencyKey.RequestHashLength);

    [Fact]
    public void Submit_keeps_the_key_with_the_application()
    {
        var key = new IdempotencyKey("retry-1", Hash);

        var application = Application.Submit(ValidApplicant(), Photo(), Now, key);

        application.IdempotencyKey.ShouldBe(key);
    }

    [Fact]
    public void Submit_without_a_key_has_none()
    {
        TestData.Submit().IdempotencyKey.ShouldBeNull();
    }

    [Fact]
    public void Accepts_a_key_of_the_maximum_length()
    {
        new IdempotencyKey(new string('k', IdempotencyKey.MaxLength), Hash).Key.Length.ShouldBe(IdempotencyKey.MaxLength);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Refuses_a_blank_key(string key)
    {
        Should.Throw<ArgumentException>(() => new IdempotencyKey(key, Hash));
    }

    [Fact]
    public void Refuses_a_key_longer_than_the_maximum()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new IdempotencyKey(new string('k', IdempotencyKey.MaxLength + 1), Hash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    public void Refuses_a_request_hash_that_is_not_a_sha256(string hash)
    {
        Should.Throw<ArgumentException>(() => new IdempotencyKey("retry-1", hash));
    }
}
