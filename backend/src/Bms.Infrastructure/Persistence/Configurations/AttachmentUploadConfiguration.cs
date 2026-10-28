using Bms.Domain.ExpenseRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bms.Infrastructure.Persistence.Configurations;

public sealed class AttachmentUploadSessionConfiguration : IEntityTypeConfiguration<AttachmentUploadSession>
{
    public void Configure(EntityTypeBuilder<AttachmentUploadSession> b)
    {
        b.ToTable("AttachmentUploadSessions", "bms_web");
        b.HasKey(x => x.Id);
        b.Property(x => x.ActorExternalId).HasMaxLength(256).UseCollation("Latin1_General_100_BIN2").IsRequired();
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetimeoffset(7)");
        b.HasOne<ExpenseRequest>().WithMany().HasForeignKey(x => x.BoundRequestId).OnDelete(DeleteBehavior.NoAction);
    }
}
public sealed class AttachmentFileConfiguration : IEntityTypeConfiguration<AttachmentFile>
{
    public void Configure(EntityTypeBuilder<AttachmentFile> b)
    {
        b.ToTable("AttachmentFiles", "bms_web");
        b.HasKey(x => x.Id);
        b.Property(x => x.StorageKey).HasMaxLength(101).UseCollation("Latin1_General_100_BIN2").IsRequired();
        b.Property(x => x.StorageRoot).HasMaxLength(512).UseCollation("Latin1_General_100_BIN2").IsRequired();
        b.HasIndex(x => x.StorageKey).IsUnique();
        b.HasIndex(x => x.SessionId);
        b.HasIndex(x => x.ExpenseRequestId);
        b.Property(x => x.Kind).HasMaxLength(30).UseCollation("Latin1_General_100_BIN2").IsRequired();
        b.Property(x => x.FileName).HasMaxLength(255).UseCollation("Latin1_General_100_BIN2").IsRequired();
        b.Property(x => x.ContentSha256).HasColumnType("binary(32)").IsRequired();
        b.Property(x => x.UploadedAtUtc).HasColumnType("datetimeoffset(7)");
        b.Property(x => x.UploadedByExternalId).HasMaxLength(256).UseCollation("Latin1_General_100_BIN2").IsRequired();
        b.HasOne<AttachmentUploadSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<ExpenseRequest>().WithMany().HasForeignKey(x => x.ExpenseRequestId).OnDelete(DeleteBehavior.NoAction);
    }
}
