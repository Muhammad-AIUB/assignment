using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class ClassConfiguration : IEntityTypeConfiguration<Class>
{
    public void Configure(EntityTypeBuilder<Class> builder)
    {
        builder.ToTable("classes");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasMaxLength(64);
        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.Property(c => c.TotalSeats).IsRequired();
        builder.Property(c => c.HeldSeats).IsRequired().HasDefaultValue(0);
        builder.Property(c => c.ConfirmedSeats).IsRequired().HasDefaultValue(0);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_classes_total_seats_positive", "\"TotalSeats\" > 0");
            // Last line of defence: the DB itself refuses to oversell, whatever the app does.
            t.HasCheckConstraint("ck_classes_seats_not_oversold",
                "\"HeldSeats\" >= 0 AND \"ConfirmedSeats\" >= 0 AND \"HeldSeats\" + \"ConfirmedSeats\" <= \"TotalSeats\"");
        });
    }
}
