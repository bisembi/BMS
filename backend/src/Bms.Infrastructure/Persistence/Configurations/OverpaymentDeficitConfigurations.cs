using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bms.Infrastructure.Persistence.Configurations;

public sealed class OverpaymentDeficitConfiguration
    : IEntityTypeConfiguration<OverpaymentDeficit>
{
    public void Configure(EntityTypeBuilder<OverpaymentDeficit> builder)
    {
        builder.ToTable("OverpaymentDeficits", "bms_web", table =>
        {
            table.HasCheckConstraint(
                "CK_bms_web_OverpaymentDeficits_Amounts",
                "[OriginalAmount] > 0 AND [OriginalAmountKzt] > 0 " +
                "AND [CurrentAmount] >= 0 AND [CurrentAmount] <= [OriginalAmount] " +
                "AND [CurrentAmountKzt] >= 0 AND [CurrentAmountKzt] <= [OriginalAmountKzt]");
            table.HasCheckConstraint(
                "CK_bms_web_OverpaymentDeficits_PairedState",
                "([CurrentAmount] = 0 AND [CurrentAmountKzt] = 0 AND [SettledAt] IS NOT NULL) " +
                "OR ([CurrentAmount] > 0 AND [CurrentAmountKzt] > 0 AND [SettledAt] IS NULL)");
            table.HasCheckConstraint(
                "CK_bms_web_OverpaymentDeficits_ExternalReversalId",
                "LEN(LTRIM(RTRIM([ExternalReversalId]))) > 0 " +
                "AND DATALENGTH([ExternalReversalId]) = DATALENGTH(LTRIM(RTRIM([ExternalReversalId])))");
        });
        builder.HasKey(deficit => new
        {
            deficit.OrganizationId,
            deficit.CounterpartyId,
            deficit.CurrencyId,
            deficit.DeficitId
        }).HasName("PK_bms_web_OverpaymentDeficits").IsClustered();
        builder.Property(deficit => deficit.OriginalAmount).HasPrecision(18, 2);
        builder.Property(deficit => deficit.OriginalAmountKzt).HasPrecision(18, 2);
        builder.Property(deficit => deficit.CurrentAmount).HasPrecision(18, 2);
        builder.Property(deficit => deficit.CurrentAmountKzt).HasPrecision(18, 2);
        builder.Property(deficit => deficit.ExternalReversalId)
            .HasMaxLength(150)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(deficit => deficit.OccurredAt).HasColumnType("datetimeoffset(3)");
        builder.Property(deficit => deficit.SettledAt).HasColumnType("datetimeoffset(3)");
        builder.HasAlternateKey(deficit => deficit.DeficitId)
            .HasName("UQ_bms_web_OverpaymentDeficits_DeficitId");
        builder.HasIndex(deficit => deficit.CreationCommandId)
            .IsUnique()
            .HasDatabaseName("UQ_bms_web_OverpaymentDeficits_CreationCommandId");
        builder.HasIndex(deficit => deficit.OccurredAt)
            .HasDatabaseName("IX_bms_web_OverpaymentDeficits_OccurredAt");
        builder.HasIndex(deficit => deficit.SourceOverpaymentId)
            .HasDatabaseName("IX_bms_web_OverpaymentDeficits_SourceOverpaymentId");
        builder.HasIndex(deficit => deficit.ExternalReversalId)
            .HasDatabaseName("IX_bms_web_OverpaymentDeficits_ExternalReversalId");
        builder.HasIndex(deficit => deficit.CounterpartyId)
            .HasDatabaseName("IX_bms_web_OverpaymentDeficits_Counterparty");
        builder.HasIndex(deficit => deficit.CurrencyId)
            .HasDatabaseName("IX_bms_web_OverpaymentDeficits_Currency");
        builder.HasIndex(deficit => deficit.SourceExpenseRequestId)
            .HasDatabaseName("IX_bms_web_OverpaymentDeficits_SourceExpenseRequest");
        builder.HasIndex(deficit => deficit.ActualCorrectionExecutorId)
            .HasDatabaseName("IX_bms_web_OverpaymentDeficits_ActualCorrectionExecutor");
        builder.HasOne<OrganizationReference>()
            .WithMany()
            .HasForeignKey(deficit => deficit.OrganizationId)
            .HasConstraintName("FK_bms_web_OverpaymentDeficits_Organization")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<CounterpartyReference>()
            .WithMany()
            .HasForeignKey(deficit => deficit.CounterpartyId)
            .HasConstraintName("FK_bms_web_OverpaymentDeficits_Counterparty")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<CurrencyReference>()
            .WithMany()
            .HasForeignKey(deficit => deficit.CurrencyId)
            .HasConstraintName("FK_bms_web_OverpaymentDeficits_Currency")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Bms.Domain.ExpenseRequests.ExpenseRequest>()
            .WithMany()
            .HasForeignKey(deficit => deficit.SourceExpenseRequestId)
            .HasConstraintName("FK_bms_web_OverpaymentDeficits_SourceExpenseRequest")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<EmployeeReference>()
            .WithMany()
            .HasForeignKey(deficit => deficit.ActualCorrectionExecutorId)
            .HasConstraintName("FK_bms_web_OverpaymentDeficits_ActualCorrectionExecutor")
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class ManualOverpaymentDeficitCorrectionConfiguration
    : IEntityTypeConfiguration<ManualOverpaymentDeficitCorrection>
{
    public void Configure(EntityTypeBuilder<ManualOverpaymentDeficitCorrection> builder)
    {
        builder.ToTable("ManualOverpaymentDeficitCorrections", "bms_web", table =>
        {
            table.HasCheckConstraint(
                "CK_bms_web_ManualOverpaymentDeficitCorrections_Amounts",
                "[DeficitAmountAtCreation] > 0 AND [DeficitAmountKztAtCreation] > 0 " +
                "AND [CorrectionAmount] > 0 AND [CorrectionAmount] <= [DeficitAmountAtCreation] " +
                "AND [CorrectionAmountKzt] > 0 AND [CorrectionAmountKzt] <= [DeficitAmountKztAtCreation] " +
                "AND (([DeficitAmountAtCreation] - [CorrectionAmount] = 0 " +
                "AND [DeficitAmountKztAtCreation] - [CorrectionAmountKzt] = 0) " +
                "OR ([DeficitAmountAtCreation] - [CorrectionAmount] > 0 " +
                "AND [DeficitAmountKztAtCreation] - [CorrectionAmountKzt] > 0))");
            table.HasCheckConstraint(
                "CK_bms_web_ManualOverpaymentDeficitCorrections_Basis",
                "LEN(LTRIM(RTRIM([Basis]))) > 0");
            table.HasCheckConstraint(
                "CK_bms_web_ManualOverpaymentDeficitCorrections_State",
                "([State] = N'PendingApproval' AND [FinancialDirectorId] IS NULL " +
                "AND [DecidedAt] IS NULL AND [DecisionCommandId] IS NULL " +
                "AND [DecisionComment] IS NULL AND [RejectionReason] IS NULL) OR " +
                "([State] = N'ApprovedAndApplied' AND [FinancialDirectorId] IS NOT NULL " +
                "AND [DecidedAt] IS NOT NULL AND [DecisionCommandId] IS NOT NULL " +
                "AND [RejectionReason] IS NULL) OR " +
                "([State] = N'Rejected' AND [FinancialDirectorId] IS NOT NULL " +
                "AND [DecidedAt] IS NOT NULL AND [DecisionCommandId] IS NOT NULL " +
                "AND LEN(LTRIM(RTRIM([RejectionReason]))) > 0) OR " +
                "([State] = N'Annulled' AND [FinancialDirectorId] IS NOT NULL " +
                "AND [DecidedAt] IS NOT NULL AND [DecisionCommandId] IS NOT NULL " +
                "AND [RejectionReason] IS NULL)");
        });
        builder.HasKey(correction => correction.CorrectionId)
            .HasName("PK_bms_web_ManualOverpaymentDeficitCorrections")
            .IsClustered();
        builder.Property(correction => correction.DeficitAmountAtCreation).HasPrecision(18, 2);
        builder.Property(correction => correction.DeficitAmountKztAtCreation).HasPrecision(18, 2);
        builder.Property(correction => correction.CorrectionAmount).HasPrecision(18, 2);
        builder.Property(correction => correction.CorrectionAmountKzt).HasPrecision(18, 2);
        builder.Property(correction => correction.Basis).HasMaxLength(1000).IsRequired();
        builder.Property(correction => correction.State)
            .HasMaxLength(30)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(correction => correction.DecidedAt).HasColumnType("datetimeoffset(3)");
        builder.Property(correction => correction.DecisionComment).HasMaxLength(1000);
        builder.Property(correction => correction.RejectionReason).HasMaxLength(1000);
        builder.HasIndex(correction => correction.CreationCommandId)
            .IsUnique()
            .HasDatabaseName("UQ_bms_web_ManualOverpaymentDeficitCorrections_CreationCommandId");
        builder.HasIndex(correction => correction.DeficitId)
            .IsUnique()
            .HasFilter("[State] = N'PendingApproval'")
            .HasDatabaseName("UX_bms_web_ManualOverpaymentDeficitCorrections_PendingDeficit");
        builder.HasIndex(correction => new
        {
            correction.OrganizationId,
            correction.CounterpartyId,
            correction.CurrencyId,
            correction.DeficitId
        }).HasDatabaseName("IX_bms_web_ManualOverpaymentDeficitCorrections_DeficitScope");
        builder.HasIndex(correction => correction.ResponsibleAccountantId)
            .HasDatabaseName("IX_bms_web_ManualOverpaymentDeficitCorrections_ResponsibleAccountant");
        builder.HasIndex(correction => correction.FinancialDirectorId)
            .HasDatabaseName("IX_bms_web_ManualOverpaymentDeficitCorrections_FinancialDirector");
        builder.HasOne<OverpaymentDeficit>()
            .WithMany()
            .HasForeignKey(correction => new
            {
                correction.OrganizationId,
                correction.CounterpartyId,
                correction.CurrencyId,
                correction.DeficitId
            })
            .HasConstraintName("FK_bms_web_ManualOverpaymentDeficitCorrections_DeficitScope")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<EmployeeReference>()
            .WithMany()
            .HasForeignKey(correction => correction.ResponsibleAccountantId)
            .HasConstraintName("FK_bms_web_ManualOverpaymentDeficitCorrections_ResponsibleAccountant")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<EmployeeReference>()
            .WithMany()
            .HasForeignKey(correction => correction.FinancialDirectorId)
            .HasConstraintName("FK_bms_web_ManualOverpaymentDeficitCorrections_FinancialDirector")
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class OverpaymentDeficitSettlementConfiguration
    : IEntityTypeConfiguration<OverpaymentDeficitSettlement>
{
    public void Configure(EntityTypeBuilder<OverpaymentDeficitSettlement> builder)
    {
        builder.ToTable("OverpaymentDeficitSettlements", "bms_web", table =>
        {
            table.HasCheckConstraint(
                "CK_bms_web_OverpaymentDeficitSettlements_Amounts",
                "[Amount] > 0 AND [AmountKzt] > 0");
            table.HasCheckConstraint(
                "CK_bms_web_OverpaymentDeficitSettlements_Source",
                "([Kind] IN (N'NewOverpayment', N'AdditionalPayment', " +
                "N'RestoredReversedPayment') AND [SourceReference] IS NOT NULL " +
                "AND LEN(LTRIM(RTRIM([SourceReference]))) > 0 " +
                "AND DATALENGTH([SourceReference]) = " +
                "DATALENGTH(LTRIM(RTRIM([SourceReference]))) " +
                "AND [CorrectionId] IS NULL) OR " +
                "([Kind] = N'ManualCorrection' AND [SourceReference] IS NULL " +
                "AND [SourceExpenseRequestId] IS NULL AND [CorrectionId] IS NOT NULL) OR " +
                "([Kind] = N'ReconsideredPriorRequest' AND [SourceReference] IS NULL " +
                "AND [SourceExpenseRequestId] IS NOT NULL AND [CorrectionId] IS NULL)");
        });
        builder.HasKey(settlement => new { settlement.DeficitId, settlement.CommandId })
            .HasName("PK_bms_web_OverpaymentDeficitSettlements")
            .IsClustered();
        builder.Property(settlement => settlement.Kind)
            .HasMaxLength(40)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(settlement => settlement.SourceReference)
            .HasMaxLength(150)
            .UseCollation("Latin1_General_100_BIN2");
        builder.Property(settlement => settlement.Amount).HasPrecision(18, 2);
        builder.Property(settlement => settlement.AmountKzt).HasPrecision(18, 2);
        builder.Property(settlement => settlement.AppliedAt).HasColumnType("datetimeoffset(3)");
        builder.HasIndex(settlement => settlement.SourceExpenseRequestId)
            .HasDatabaseName("IX_bms_web_OverpaymentDeficitSettlements_SourceExpenseRequest");
        builder.HasIndex(settlement => settlement.CorrectionId)
            .IsUnique()
            .HasFilter("[CorrectionId] IS NOT NULL")
            .HasDatabaseName("UX_bms_web_OverpaymentDeficitSettlements_Correction");
        builder.HasIndex(settlement => settlement.AppliedAt)
            .HasDatabaseName("IX_bms_web_OverpaymentDeficitSettlements_AppliedAt");
        builder.HasIndex(settlement => settlement.UserId)
            .HasDatabaseName("IX_bms_web_OverpaymentDeficitSettlements_User");
        builder.HasOne<OverpaymentDeficit>()
            .WithMany()
            .HasForeignKey(settlement => settlement.DeficitId)
            .HasPrincipalKey(deficit => deficit.DeficitId)
            .HasConstraintName("FK_bms_web_OverpaymentDeficitSettlements_Deficit")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Bms.Domain.ExpenseRequests.ExpenseRequest>()
            .WithMany()
            .HasForeignKey(settlement => settlement.SourceExpenseRequestId)
            .HasConstraintName("FK_bms_web_OverpaymentDeficitSettlements_SourceExpenseRequest")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ManualOverpaymentDeficitCorrection>()
            .WithMany()
            .HasForeignKey(settlement => settlement.CorrectionId)
            .HasConstraintName("FK_bms_web_OverpaymentDeficitSettlements_Correction")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<EmployeeReference>()
            .WithMany()
            .HasForeignKey(settlement => settlement.UserId)
            .HasConstraintName("FK_bms_web_OverpaymentDeficitSettlements_User")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
