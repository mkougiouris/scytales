using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scytales.Domain.Applications;

namespace Scytales.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationHistoryEntryConfiguration : IEntityTypeConfiguration<ApplicationHistoryEntry>
{
    public void Configure(EntityTypeBuilder<ApplicationHistoryEntry> builder)
    {
        builder.ToTable("application_history");

        // One entry per version: a second writer for the same version is rejected by the key itself.
        builder.HasKey(e => new { e.ApplicationId, e.Version });
        builder.Property(e => e.Version).ValueGeneratedNever();

        builder.Property(e => e.Timestamp);
        builder.Property(e => e.FromStatus);
        builder.Property(e => e.ToStatus);
        builder.Property(e => e.Event);
        builder.Property(e => e.Details);
    }
}
