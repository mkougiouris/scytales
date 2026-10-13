namespace Scytales.Api.Contracts;

/// <summary>
/// <c>multipart/form-data</c> body of <c>POST /api/applications</c> (D-1): the personal details as form fields
/// and the face photo as a file part, so the whole application arrives (and is stored) in one go.
/// Nothing here is required: incomplete or invalid data is accepted and judged by the workflow,
/// which records why it failed (FR-5).
/// </summary>
public sealed class SubmitApplicationForm
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    /// <example>1990-05-20</example>
    public DateOnly? DateOfBirth { get; set; }

    public string? NationalId { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Address { get; set; }

    /// <example>B</example>
    public string? LicenceCategory { get; set; }

    /// <summary>Face photo, JPEG or PNG.</summary>
    public IFormFile? Photo { get; set; }
}

/// <summary>Optional body of <c>POST /api/applications/{id}/reject</c>.</summary>
public sealed record RejectApplicationRequest(string? Reason);
