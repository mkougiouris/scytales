using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scytales.Domain.Applications.ValueObjects;

namespace Scytales.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationConfiguration : IEntityTypeConfiguration<LicenceApplication>
{
    private const string ApplicationIdColumn = "ApplicationId";

    /// <summary>Its violation means a concurrent submission with the same key won (BN-2).</summary>
    internal const string IdempotencyKeyIndex = "ux_applications_idempotency_key";

    public void Configure(EntityTypeBuilder<LicenceApplication> builder)
    {
        builder.ToTable("applications");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Status);
        builder.Property(a => a.PhotoQualityScore);
        builder.Property(a => a.CreatedAt);
        builder.Property(a => a.UpdatedAt);

        // Optimistic concurrency (D-4): every UPDATE carries WHERE version = @original.
        builder.Property(a => a.Version).IsConcurrencyToken();

        builder.HasIndex(a => a.Status);

        // In the applications table itself, so the key commits atomically with the application. PostgreSQL
        // unique indexes let any number of NULLs through, so submissions without a key are unaffected.
        builder.OwnsOne(a => a.IdempotencyKey, key =>
        {
            key.Property(k => k.Key).HasColumnName("idempotency_key").HasMaxLength(IdempotencyKey.MaxLength);
            key.Property(k => k.RequestHash).HasColumnName("idempotency_request_hash")
                .HasMaxLength(IdempotencyKey.RequestHashLength).IsFixedLength();
            key.HasIndex(k => k.Key).IsUnique().HasDatabaseName(IdempotencyKeyIndex);
        });

        // Personal data in its own table (D-1 / owned value object). Text columns without length limits:
        // the API accepts incomplete or invalid data on purpose, and the request size is capped there.
        builder.OwnsOne(a => a.Applicant, applicant =>
        {
            applicant.ToTable("applicant_details");
            applicant.WithOwner().HasForeignKey(ApplicationIdColumn);
            applicant.HasKey(ApplicationIdColumn);
            applicant.Property<Guid>(ApplicationIdColumn).HasColumnName("application_id");
        });
        builder.Navigation(a => a.Applicant).IsRequired();

        // The photo in its own table, keyed by the application id: at most one per application,
        // no row when none was uploaded (D-1).
        builder.OwnsOne(a => a.Photo, photo =>
        {
            photo.ToTable("application_photos");
            photo.WithOwner().HasForeignKey(ApplicationIdColumn);
            photo.HasKey(ApplicationIdColumn);
            photo.Property<Guid>(ApplicationIdColumn).HasColumnName("application_id");
            photo.Property(p => p.FileName);
            photo.Property(p => p.ContentType);
            photo.Property(p => p.FileSize);
            photo.Property(p => p.Content);
        });

        // Append-only audit trail. Not loaded with the aggregate; entries added through it are inserted on save.
        builder.HasMany(a => a.History)
            .WithOne()
            .HasForeignKey(e => e.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(a => a.History)
            .HasField("_history")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
