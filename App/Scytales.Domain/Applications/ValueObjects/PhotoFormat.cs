namespace Scytales.Domain.Applications.ValueObjects;

public enum PhotoFormat
{
    Jpeg,
    Png
}

public static class PhotoFormats
{
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Identifies the format from the file's magic bytes; null when unrecognised.</summary>
    public static PhotoFormat? Detect(ReadOnlySpan<byte> content) =>
        content.StartsWith(JpegSignature) ? PhotoFormat.Jpeg
        : content.StartsWith(PngSignature) ? PhotoFormat.Png
        : null;
}
