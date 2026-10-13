using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Scytales.Application.UseCases.Submit;

/// <summary>
/// SHA-256 over everything the client sent, photo bytes included, so a retry with the same
/// <c>Idempotency-Key</c> can be told apart from a different request reusing it (BN-2).
/// </summary>
internal static class SubmissionRequestHash
{
    public static string Compute(SubmitApplicationCommand command)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        Append(hash, command.FirstName);
        Append(hash, command.LastName);
        Append(hash, command.DateOfBirth?.ToString("O", CultureInfo.InvariantCulture));
        Append(hash, command.NationalId);
        Append(hash, command.Email);
        Append(hash, command.PhoneNumber);
        Append(hash, command.Address);
        Append(hash, command.LicenceCategory);
        Append(hash, command.Photo?.FileName);
        Append(hash, command.Photo?.ContentType);
        Append(hash, command.Photo?.Content);

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string? value) =>
        Append(hash, value is null ? null : Encoding.UTF8.GetBytes(value));

    // Length-prefixed, with -1 for null, so ("ab", "c") and ("a", "bc") or null and "" never hash alike.
    private static void Append(IncrementalHash hash, byte[]? value)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, value?.Length ?? -1);
        hash.AppendData(length);
        if (value is not null)
            hash.AppendData(value);
    }
}
