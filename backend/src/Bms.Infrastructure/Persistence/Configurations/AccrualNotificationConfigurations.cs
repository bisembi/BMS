using Bms.Domain.ExpenseRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bms.Infrastructure.Persistence.Configurations;

public sealed class AccrualNotificationConfiguration : IEntityTypeConfiguration<AccrualNotification>
{
    public void Configure(EntityTypeBuilder<AccrualNotification> builder)
    {
        builder.ToTable("AccrualNotifications", "bms_web", table =>
        {
            table.HasCheckConstraint("CK_bms_web_AccrualNotifications_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' " +
                "AND [ExpenseRequestId] <> '00000000-0000-0000-0000-000000000000' " +
                "AND [AccrualCycleId] <> '00000000-0000-0000-0000-000000000000' " +
                "AND [RecipientEmployeeId] <> '00000000-0000-0000-0000-000000000000' " +
                "AND [OrganizationId] <> '00000000-0000-0000-0000-000000000000' " +
                "AND [NotificationType] = N'ACCRUAL_RED_ZONE' " +
                "AND DATALENGTH([NotificationType]) = 32 " +
                "AND ([RecipientWasInitiator] = 1 OR [RecipientWasResponsibleAccountant] = 1)");
            table.HasCheckConstraint("CK_bms_web_AccrualNotifications_Age",
                "[RedFromDays] > 0 AND [AgeDays] >= [RedFromDays] " +
                "AND DATEDIFF(day, [PaymentBusinessDate], [AsOfDate]) = [AgeDays] " +
                "AND [RemainingAmountKzt] > 0");
            table.HasCheckConstraint("CK_bms_web_AccrualNotifications_Time",
                "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 " +
                "AND DATEPART(TZOFFSET, [FullyPaidAtUtc]) = 0 " +
                "AND [CreatedAtUtc] >= [FullyPaidAtUtc]");
        });
        builder.HasKey(item => item.Id).HasName("PK_bms_web_AccrualNotifications");
        builder.Property(item => item.NotificationType).HasMaxLength(32)
            .UseCollation("Latin1_General_100_BIN2").IsRequired();
        builder.Property(item => item.CreatedAtUtc).HasColumnType("datetimeoffset(3)");
        builder.Property(item => item.FullyPaidAtUtc).HasColumnType("datetimeoffset(3)");
        builder.Property(item => item.PaymentBusinessDate).HasColumnType("date");
        builder.Property(item => item.AsOfDate).HasColumnType("date");
        builder.Property(item => item.RemainingAmountKzt).HasPrecision(18, 2);
        builder.HasIndex(item => new
        {
            item.NotificationType,
            item.ExpenseRequestId,
            item.AccrualCycleId,
            item.RecipientEmployeeId
        }).IsUnique().HasDatabaseName("UQ_bms_web_AccrualNotifications_BusinessKey");
        builder.HasIndex(item => new { item.RecipientEmployeeId, item.CreatedAtUtc, item.Id })
            .HasDatabaseName("IX_bms_web_AccrualNotifications_Recipient_Created");
        builder.HasIndex(item => new { item.ExpenseRequestId, item.AccrualCycleId })
            .HasDatabaseName("IX_bms_web_AccrualNotifications_Request_Cycle");
        builder.HasIndex(item => item.OrganizationId)
            .HasDatabaseName("IX_bms_web_AccrualNotifications_Organization");
        builder.HasOne<PersistedAccrualClosingCycleSnapshot>().WithMany()
            .HasForeignKey(item => new { item.ExpenseRequestId, item.AccrualCycleId })
            .HasPrincipalKey(item => new { item.ExpenseRequestId, item.CycleId })
            .HasConstraintName("FK_bms_web_AccrualNotifications_Cycle")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<EmployeeReference>().WithMany()
            .HasForeignKey(item => item.RecipientEmployeeId)
            .HasConstraintName("FK_bms_web_AccrualNotifications_Employee")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<OrganizationReference>().WithMany()
            .HasForeignKey(item => item.OrganizationId)
            .HasConstraintName("FK_bms_web_AccrualNotifications_Organization")
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class AccrualNotificationIssueConfiguration : IEntityTypeConfiguration<AccrualNotificationIssue>
{
    public void Configure(EntityTypeBuilder<AccrualNotificationIssue> builder)
    {
        builder.ToTable("AccrualNotificationIssues", "bms_web", table =>
        {
            table.HasCheckConstraint("CK_bms_web_AccrualNotificationIssues_Identity",
                "[ExpenseRequestId] <> '00000000-0000-0000-0000-000000000000' " +
                "AND ([AccrualCycleId] IS NULL OR [AccrualCycleId] <> '00000000-0000-0000-0000-000000000000') " +
                "AND (([Code] = N'NO_RESPONSIBLE_ACCOUNTANT' AND DATALENGTH([Code]) = 50) " +
                "OR ([Code] = N'INITIATOR_MAPPING_UNAVAILABLE' AND DATALENGTH([Code]) = 58))");
            table.HasCheckConstraint("CK_bms_web_AccrualNotificationIssues_Time",
                "DATEPART(TZOFFSET, [FirstDetectedAtUtc]) = 0 " +
                "AND DATEPART(TZOFFSET, [LastDetectedAtUtc]) = 0 " +
                "AND [LastDetectedAtUtc] >= [FirstDetectedAtUtc] " +
                "AND ([ResolvedAtUtc] IS NULL OR (DATEPART(TZOFFSET, [ResolvedAtUtc]) = 0 " +
                "AND [ResolvedAtUtc] >= [FirstDetectedAtUtc] AND [ResolvedAtUtc] = [LastDetectedAtUtc]))");
        });
        builder.HasKey(item => new { item.ExpenseRequestId, item.Code })
            .HasName("PK_bms_web_AccrualNotificationIssues");
        builder.Property(item => item.Code).HasMaxLength(64)
            .UseCollation("Latin1_General_100_BIN2").IsRequired();
        builder.Property(item => item.FirstDetectedAtUtc).HasColumnType("datetimeoffset(3)");
        builder.Property(item => item.LastDetectedAtUtc).HasColumnType("datetimeoffset(3)");
        builder.Property(item => item.ResolvedAtUtc).HasColumnType("datetimeoffset(3)");
        builder.HasIndex(item => new { item.ResolvedAtUtc, item.Code })
            .HasDatabaseName("IX_bms_web_AccrualNotificationIssues_Unresolved");
        builder.HasOne<ExpenseRequest>().WithMany()
            .HasForeignKey(item => item.ExpenseRequestId)
            .HasConstraintName("FK_bms_web_AccrualNotificationIssues_Request")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
