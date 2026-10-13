namespace Scytales.Domain.Applications.ValueObjects;

/// <summary>
/// The uploaded face photo: everything needed to reconstruct the original upload.
/// Owned by <see cref="Application"/>; it has no identity or lifecycle of its own.
/// </summary>
public sealed record ApplicationPhoto
{
    public string FileName { get; private set; } = null!;

    /// <summary>As declared by the client; the real format is detected from the bytes.</summary>
    public string ContentType { get; private set; } = null!;

    public long FileSize { get; private set; }
    public byte[] Content { get; private set; } = null!;

    private ApplicationPhoto() { }

    public ApplicationPhoto(string fileName, string contentType, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        FileName = fileName ?? string.Empty;
        ContentType = contentType ?? string.Empty;
        FileSize = content.LongLength;
        Content = content;
    }

    public PhotoFormat? DetectFormat() => PhotoFormats.Detect(Content);

    // Value equality must compare the bytes, not the array reference.
    public bool Equals(ApplicationPhoto? other) =>
        other is not null
        && FileName == other.FileName
        && ContentType == other.ContentType
        && Content.AsSpan().SequenceEqual(other.Content);

    public override int GetHashCode() => HashCode.Combine(FileName, ContentType, FileSize);
}
