using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class HoldConfiguration : IEntityTypeConfiguration<Hold>
{
    public void Configure(EntityTypeBuilder<Hold> builder)
    {
        builder.ToTable("holds");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).HasMaxLength(64);
        builder.Property(h => h.ClassId).IsRequired().HasMaxLength(64);
        builder.Property(h => h.StudentId).IsRequired().HasMaxLength(17);
        builder.Property(h => h.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(h => h.CreatedAt).IsRequired();
        builder.Property(h => h.ExpiresAt).IsRequired();
        builder.Property(h => h.ConfirmedAt);

        builder.HasOne<Class>()
            .WithMany()
            .HasForeignKey(h => h.ClassId)
            .OnDelete(DeleteBehavior.Restrict);

        // "Does this student already have an active hold / confirmed seat in this class?"
        builder.HasIndex(h => new { h.ClassId, h.StudentId, h.Status })
            .HasDatabaseName("ix_holds_class_student_status");

        // Backstop for the one-active-hold rule: at most one HELD/CONFIRMED row per student per class.
        builder.HasIndex(h => new { h.ClassId, h.StudentId })
            .IsUnique()
            .HasFilter("\"Status\" IN ('Held', 'Confirmed')")
            .HasDatabaseName("ux_holds_active_student_class");

        // Seat counting per class (held/confirmed totals).
        builder.HasIndex(h => new { h.ClassId, h.Status })
            .HasDatabaseName("ix_holds_class_status");

        // Expiry sweeps: find HELD holds past their ExpiresAt.
        builder.HasIndex(h => new { h.Status, h.ExpiresAt })
            .HasDatabaseName("ix_holds_status_expires_at");

        // GET /me/holds
        builder.HasIndex(h => h.StudentId)
            .HasDatabaseName("ix_holds_student");
    }
}
