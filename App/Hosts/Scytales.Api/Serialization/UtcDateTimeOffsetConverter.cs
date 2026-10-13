using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scytales.Api.Serialization;

/// <summary>
/// Writes every timestamp in UTC with a <c>Z</c> suffix (<c>2026-06-09T10:30:00Z</c>), the form the brief's
/// examples use, instead of the default <c>+00:00</c> offset. Reading accepts any ISO 8601 offset.
/// </summary>
internal sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.UtcDateTime);
}
