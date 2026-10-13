using Scytales.Domain.Applications.ValueObjects;
using static Scytales.Domain.Tests.TestData;

namespace Scytales.Domain.Tests.Applications.ValueObjects;

public class PhotoFormatsTests
{
    [Fact]
    public void Detects_formats_from_magic_bytes()
    {
        PhotoFormats.Detect(Jpeg()).ShouldBe(PhotoFormat.Jpeg);
        PhotoFormats.Detect(Png()).ShouldBe(PhotoFormat.Png);
        PhotoFormats.Detect(Gif()).ShouldBeNull();
        PhotoFormats.Detect([]).ShouldBeNull();
        PhotoFormats.Detect([0xFF, 0xD8]).ShouldBeNull();
    }
}
