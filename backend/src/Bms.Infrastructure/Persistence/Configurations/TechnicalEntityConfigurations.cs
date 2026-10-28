using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bms.Infrastructure.Persistence.Configurations;

public sealed class BusinessAuditEntryConfiguration : IEntityTypeConfiguration<BusinessAuditEntry>
{
    public void Configure(EntityTypeBuilder<BusinessAuditEntry> builder)
    {
        builder.ToTable("BusinessAudit", "bms_web");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.ActorExternalId)
            .HasMaxLength(256)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(entry => entry.ObjectType).HasMaxLength(100).IsRequired();
        builder.Property(entry => entry.ObjectId).HasMaxLength(100).IsRequired();
        builder.Property(entry => entry.Action).HasMaxLength(100).IsRequired();
        builder.Property(entry => entry.OccurredAtUtc).HasPrecision(3);
        builder.Property(entry => entry.BeforeJson).HasColumnType("nvarchar(max)");
        builder.Property(entry => entry.AfterJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(entry => entry.CorrelationId).HasMaxLength(64).IsRequired();
        builder.HasIndex(entry => new { entry.ObjectType, entry.ObjectId, entry.OccurredAtUtc });
    }
}

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages", "bms_web");
        builder.HasKey(message => message.Id);
        builder.Property(message => message.MessageType).HasMaxLength(200).IsRequired();
        builder.Property(message => message.OccurredAtUtc).HasPrecision(3);
        builder.Property(message => message.ProcessedAtUtc).HasPrecision(3);
        builder.Property(message => message.PayloadJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(message => message.CorrelationId).HasMaxLength(64).IsRequired();
        builder.Property(message => message.LastError).HasMaxLength(4_000);
        builder.HasIndex(message => new { message.ProcessedAtUtc, message.OccurredAtUtc });
    }
}

public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords", "bms_web");
        builder.HasKey(record => new { record.Scope, record.Key })
            .IsClustered();
        builder.Property(record => record.Scope)
            .HasMaxLength(323)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(record => record.Scope).Metadata.SetValueComparer(
            IdentifierValueComparers.OrdinalKey);
        builder.Property(record => record.Key)
            .HasMaxLength(127)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(record => record.Key).Metadata.SetValueComparer(
            IdentifierValueComparers.OrdinalKey);
        builder.Property(record => record.RequestHash)
            .HasColumnType("char(64)")
            .HasMaxLength(64)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(record => record.ResponseJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(record => record.CreatedAtUtc).HasPrecision(3);
        builder.HasIndex(record => record.CreatedAtUtc);
    }
}
