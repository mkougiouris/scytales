using Scytales.Domain.Applications.ValueObjects;
using static Scytales.Domain.Tests.TestData;

namespace Scytales.Domain.Tests.Applications.ValueObjects;

public class ApplicationPhotoTests
{
    [Fact]
    public void File_size_is_taken_from_the_content()
    {
        new ApplicationPhoto("face.jpg", "image/jpeg", Jpeg(12_345)).FileSize.ShouldBe(12_345);
    }

    [Fact]
    public void Equality_compares_the_bytes_not_the_array()
    {
        var a = new ApplicationPhoto("face.jpg", "image/jpeg", Jpeg());
        var b = new ApplicationPhoto("face.jpg", "image/jpeg", Jpeg());
        var c = new ApplicationPhoto("face.jpg", "image/jpeg", Png());

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
        a.ShouldNotBe(c);
    }

    [Fact]
    public void Missing_name_and_type_become_empty()
    {
        var photo = new ApplicationPhoto(null!, null!, Jpeg());

        photo.FileName.ShouldBeEmpty();
        photo.ContentType.ShouldBeEmpty();
    }
}
