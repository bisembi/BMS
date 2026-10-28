using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bms.Infrastructure.Persistence.Configurations;

public sealed class ExpenseRequestPaymentFactConfiguration
    : IEntityTypeConfiguration<ExpenseRequestPaymentFact>
{
    public void Configure(EntityTypeBuilder<ExpenseRequestPaymentFact> builder)
    {
        builder.ToTable("ExpenseRequestPaymentFacts", "bms_web", table =>
        {
            table.HasCheckConstraint(
                "CK_bms_web_ExpenseRequestPaymentFacts_Identifiers",
                "LEN(LTRIM(RTRIM([SourceSystem]))) > 0 " +
                "AND DATALENGTH([SourceSystem]) = DATALENGTH(LTRIM(RTRIM([SourceSystem]))) " +
                "AND LEN(LTRIM(RTRIM([ExternalId]))) > 0 " +
                "AND DATALENGTH([ExternalId]) = DATALENGTH(LTRIM(RTRIM([ExternalId])))");
            table.HasCheckConstraint(
                "CK_bms_web_ExpenseRequestPaymentFacts_Amounts",
                "[Amount] > 0 AND [AmountKzt] > 0");
            table.HasCheckConstraint(
                "CK_bms_web_ExpenseRequestPaymentFacts_Reversal",
                "([IsReversal] = 0 AND [ReversedEventId] IS NULL) OR " +
                "([IsReversal] = 1 AND [ReversedEventId] IS NOT NULL " +
                "AND [ReversedEventId] <> [EventId])");
        });
        builder.HasKey(fact => new { fact.SourceSystem, fact.ExternalId })
            .HasName("PK_bms_web_ExpenseRequestPaymentFacts")
            .IsClustered();
        builder.Property(fact => fact.SourceSystem)
            .HasMaxLength(100)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(fact => fact.ExternalId)
            .HasMaxLength(150)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(fact => fact.Amount).HasPrecision(18, 2);
        builder.Property(fact => fact.AmountKzt).HasPrecision(18, 2);
        builder.Property(fact => fact.PaidAt).HasColumnType("datetimeoffset(3)");
        builder.Property(fact => fact.PaymentNumber).HasMaxLength(100);
        builder.Property(fact => fact.ProcessedAt).HasColumnType("datetimeoffset(3)");
        builder.HasAlternateKey(fact => fact.EventId)
            .HasName("UQ_bms_web_ExpenseRequestPaymentFacts_EventId");
        builder.HasIndex(fact => fact.ExpenseRequestId)
            .HasDatabaseName("IX_bms_web_ExpenseRequestPaymentFacts_ExpenseRequest");
        builder.HasIndex(fact => fact.ReversedEventId)
            .HasDatabaseName("IX_bms_web_ExpenseRequestPaymentFacts_ReversedEvent");
        builder.HasOne<Bms.Domain.ExpenseRequests.ExpenseRequest>()
            .WithMany()
            .HasForeignKey(fact => fact.ExpenseRequestId)
            .HasConstraintName("FK_bms_web_ExpenseRequestPaymentFacts_ExpenseRequest")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<CurrencyReference>()
            .WithMany()
            .HasForeignKey(fact => fact.CurrencyId)
            .HasConstraintName("FK_bms_web_ExpenseRequestPaymentFacts_Currency")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ExpenseRequestPaymentFact>()
            .WithMany()
            .HasForeignKey(fact => fact.ReversedEventId)
            .HasPrincipalKey(fact => fact.EventId)
            .HasConstraintName("FK_bms_web_ExpenseRequestPaymentFacts_ReversedEvent")
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class OverpaymentSourceConfiguration
    : IEntityTypeConfiguration<OverpaymentSource>
{
    public void Configure(EntityTypeBuilder<OverpaymentSource> builder)
    {
        builder.ToTable("OverpaymentSources", "bms_web", table =>
        {
            table.HasCheckConstraint(
                "CK_bms_web_OverpaymentSources_Amounts",
                "[OriginalAmount] > 0 AND [OriginalAmountKzt] > 0 " +
                "AND [ReversedAmount] >= 0 AND [ReversedAmount] <= [OriginalAmount] " +
                "AND [ReversedAmountKzt] >= 0 AND [ReversedAmountKzt] <= [OriginalAmountKzt] " +
                "AND [DeficitTransferredAmount] >= 0 " +
                "AND [DeficitTransferredAmount] <= [ReversedAmount] " +
                "AND [DeficitTransferredAmountKzt] >= 0 " +
                "AND [DeficitTransferredAmountKzt] <= [ReversedAmountKzt] " +
                "AND [CurrentRemainingAmount] >= 0 " +
                "AND [CurrentRemainingAmount] <= [OriginalAmount] " +
                "AND [CurrentRemainingAmountKzt] >= 0 " +
                "AND [CurrentRemainingAmountKzt] <= [OriginalAmountKzt] " +
                "AND [ReservedAmount] >= 0 AND [ReservedAmount] <= [CurrentRemainingAmount] " +
                "AND [ReservedAmountKzt] >= 0 " +
                "AND [ReservedAmountKzt] <= [CurrentRemainingAmountKzt]");
            table.HasCheckConstraint(
                "CK_bms_web_OverpaymentSources_PairedState",
                "(([ReversedAmount] = 0 AND [ReversedAmountKzt] = 0) OR " +
                "([ReversedAmount] > 0 AND [ReversedAmountKzt] > 0)) AND " +
                "(([DeficitTransferredAmount] = 0 AND [DeficitTransferredAmountKzt] = 0) OR " +
                "([DeficitTransferredAmount] > 0 AND [DeficitTransferredAmountKzt] > 0)) AND " +
                "(([CurrentRemainingAmount] = 0 AND [CurrentRemainingAmountKzt] = 0) OR " +
                "([CurrentRemainingAmount] > 0 AND [CurrentRemainingAmountKzt] > 0)) AND " +
                "(([ReservedAmount] = 0 AND [ReservedAmountKzt] = 0) OR " +
                "([ReservedAmount] > 0 AND [ReservedAmountKzt] > 0))");
        });
        builder.HasKey(source => new
        {
            source.OrganizationId,
            source.CounterpartyId,
            source.CurrencyId,
            source.SourceId
        }).HasName("PK_bms_web_OverpaymentSources").IsClustered();
        builder.Property(source => source.OriginalAmount).HasPrecision(18, 2);
        builder.Property(source => source.OriginalAmountKzt).HasPrecision(18, 2);
        builder.Property(source => source.ReversedAmount).HasPrecision(18, 2);
        builder.Property(source => source.ReversedAmountKzt).HasPrecision(18, 2);
        builder.Property(source => source.DeficitTransferredAmount).HasPrecision(18, 2);
        builder.Property(source => source.DeficitTransferredAmountKzt).HasPrecision(18, 2);
        builder.Property(source => source.CurrentRemainingAmount).HasPrecision(18, 2);
        builder.Property(source => source.CurrentRemainingAmountKzt).HasPrecision(18, 2);
        builder.Property(source => source.ReservedAmount).HasPrecision(18, 2);
        builder.Property(source => source.ReservedAmountKzt).HasPrecision(18, 2);
        builder.Property(source => source.OccurredAt).HasColumnType("datetimeoffset(3)");
        builder.HasAlternateKey(source => source.SourceId)
            .HasName("UQ_bms_web_OverpaymentSources_SourceId");
        builder.HasIndex(source => source.SourceEventId)
            .IsUnique()
            .HasDatabaseName("UQ_bms_web_OverpaymentSources_SourceEventId");
        builder.HasIndex(source => source.OccurredAt)
            .HasDatabaseName("IX_bms_web_OverpaymentSources_OccurredAt");
        builder.HasIndex(source => source.SourceExpenseRequestId)
            .HasDatabaseName("IX_bms_web_OverpaymentSources_SourceExpenseRequest");
        builder.HasOne<OrganizationReference>()
            .WithMany()
            .HasForeignKey(source => source.OrganizationId)
            .HasConstraintName("FK_bms_web_OverpaymentSources_Organization")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<CounterpartyReference>()
            .WithMany()
            .HasForeignKey(source => source.CounterpartyId)
            .HasConstraintName("FK_bms_web_OverpaymentSources_Counterparty")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<CurrencyReference>()
            .WithMany()
            .HasForeignKey(source => source.CurrencyId)
            .HasConstraintName("FK_bms_web_OverpaymentSources_Currency")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Bms.Domain.ExpenseRequests.ExpenseRequest>()
            .WithMany()
            .HasForeignKey(source => source.SourceExpenseRequestId)
            .HasConstraintName("FK_bms_web_OverpaymentSources_SourceExpenseRequest")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ExpenseRequestPaymentFact>()
            .WithMany()
            .HasForeignKey(source => source.SourceEventId)
            .HasPrincipalKey(fact => fact.EventId)
            .HasConstraintName("FK_bms_web_OverpaymentSources_SourceEvent")
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class OverpaymentAllocationConfiguration
    : IEntityTypeConfiguration<OverpaymentAllocation>
{
    public void Configure(EntityTypeBuilder<OverpaymentAllocation> builder)
    {
        builder.ToTable("OverpaymentAllocations", "bms_web", table =>
        {
            table.HasCheckConstraint(
                "CK_bms_web_OverpaymentAllocations_Amounts",
                "[Amount] > 0 AND [AmountKzt] > 0");
            table.HasCheckConstraint(
                "CK_bms_web_OverpaymentAllocations_State",
                "([Status] = N'Reserved' AND [AppliedOrReleasedAt] IS NULL) OR " +
                "([Status] IN (N'Applied', N'Released') " +
                "AND [AppliedOrReleasedAt] IS NOT NULL)");
        });
        builder.HasKey(allocation => new
        {
            allocation.TargetExpenseRequestId,
            allocation.SourceId,
            allocation.AllocationId
        }).HasName("PK_bms_web_OverpaymentAllocations").IsClustered();
        builder.Property(allocation => allocation.Amount).HasPrecision(18, 2);
        builder.Property(allocation => allocation.AmountKzt).HasPrecision(18, 2);
        builder.Property(allocation => allocation.Status)
            .HasMaxLength(30)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(allocation => allocation.ReservedAt).HasColumnType("datetimeoffset(3)");
        builder.Property(allocation => allocation.AppliedOrReleasedAt)
            .HasColumnType("datetimeoffset(3)");
        builder.HasIndex(allocation => allocation.AllocationId)
            .IsUnique()
            .HasDatabaseName("UQ_bms_web_OverpaymentAllocations_AllocationId");
        builder.HasIndex(allocation => allocation.SourceId)
            .HasDatabaseName("IX_bms_web_OverpaymentAllocations_SourceId");
        builder.HasIndex(allocation => allocation.Status)
            .HasDatabaseName("IX_bms_web_OverpaymentAllocations_Status");
        builder.HasIndex(allocation => allocation.ReservedAt)
            .HasDatabaseName("IX_bms_web_OverpaymentAllocations_ReservedAt");
        builder.HasOne<Bms.Domain.ExpenseRequests.ExpenseRequest>()
            .WithMany()
            .HasForeignKey(allocation => allocation.TargetExpenseRequestId)
            .HasConstraintName("FK_bms_web_OverpaymentAllocations_TargetExpenseRequest")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<OverpaymentSource>()
            .WithMany()
            .HasForeignKey(allocation => allocation.SourceId)
            .HasPrincipalKey(source => source.SourceId)
            .HasConstraintName("FK_bms_web_OverpaymentAllocations_Source")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<EmployeeReference>()
            .WithMany()
            .HasForeignKey(allocation => allocation.UserId)
            .HasConstraintName("FK_bms_web_OverpaymentAllocations_User")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
