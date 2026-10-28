using Bms.Domain.ExpenseRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bms.Infrastructure.Persistence.Configurations;

public sealed class ExpenseRequestAttachmentConfiguration :
    IEntityTypeConfiguration<ExpenseRequestAttachmentEntity>
{
    public void Configure(EntityTypeBuilder<ExpenseRequestAttachmentEntity> builder)
    {
        builder.ToTable("ExpenseRequestAttachments", "bms_web");
        builder.HasKey(attachment => attachment.Id);
        builder.Property(attachment => attachment.Kind)
            .HasMaxLength(30)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(attachment => attachment.FileName)
            .HasMaxLength(ExpenseRequestAttachmentUploadPolicy.MaximumFileNameLength)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(attachment => attachment.ContentSha256)
            .HasColumnType("binary(32)")
            .IsRequired();
        builder.Property(attachment => attachment.Content)
            .HasColumnType("varbinary(max)")
            .IsRequired();
        builder.Property(attachment => attachment.UploadedAtUtc)
            .HasColumnType("datetimeoffset(3)");
        builder.Property(attachment => attachment.UploadedByExternalId)
            .HasMaxLength(256)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(attachment => attachment.Comment)
            .HasMaxLength(1_000)
            .UseCollation("Latin1_General_100_BIN2");
        builder.HasIndex(attachment => new
        {
            attachment.ExpenseRequestId,
            attachment.UploadedAtUtc,
            attachment.Id
        })
            .HasDatabaseName("IX_bms_web_ExpenseRequestAttachments_Request_Time_Id");
        builder.HasOne<ExpenseRequest>()
            .WithMany()
            .HasForeignKey(attachment => attachment.ExpenseRequestId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_bms_web_ExpenseRequestAttachments_Request");
    }
}
