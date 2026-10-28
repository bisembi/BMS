using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bms.Infrastructure.Persistence.Configurations;

public sealed class CurrencyReferenceConfiguration : IEntityTypeConfiguration<CurrencyReference>
{
    public void Configure(EntityTypeBuilder<CurrencyReference> builder)
    {
        builder.ToTable("Currencies", "bms_web");
        builder.HasKey(currency => currency.Id)
            .HasName("PK_bms_web_Currencies")
            .IsClustered();
        builder.Property(currency => currency.Code)
            .HasMaxLength(3)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(currency => currency.Name)
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(currency => currency.NumericCode)
            .HasMaxLength(3)
            .IsUnicode()
            .IsFixedLength()
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.HasIndex(currency => currency.Code)
            .IsUnique()
            .HasDatabaseName("UQ_bms_web_Currencies_Code");
        builder.HasIndex(currency => new { currency.IsActive, currency.Code })
            .HasDatabaseName("IX_bms_web_Currencies_Active_Code");
    }
}

public sealed class CurrencyRateReferenceConfiguration
    : IEntityTypeConfiguration<CurrencyRateReference>
{
    public void Configure(EntityTypeBuilder<CurrencyRateReference> builder)
    {
        builder.ToTable("CurrencyRates", "bms_web");
        builder.HasKey(rate => new { rate.CurrencyId, rate.RateDate })
            .HasName("PK_bms_web_CurrencyRates")
            .IsClustered();
        builder.Property(rate => rate.RateDate)
            .HasColumnType("date");
        builder.Property(rate => rate.Rate)
            .HasPrecision(15, 6);
        builder.Property(rate => rate.Multiplicity)
            .HasPrecision(10, 0);
        builder.Property(rate => rate.PublishedAtSource)
            .HasColumnType("datetime2(0)");
        builder.Property(rate => rate.SourceName)
            .HasMaxLength(100)
            .IsRequired();
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_bms_web_CurrencyRates_Rate_Nonnegative",
            "[Rate] >= 0"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_bms_web_CurrencyRates_Multiplicity_Nonnegative",
            "[Multiplicity] >= 0"));
        builder.HasOne<CurrencyReference>()
            .WithMany()
            .HasForeignKey(rate => rate.CurrencyId)
            .HasConstraintName("FK_bms_web_CurrencyRates_Currency")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
