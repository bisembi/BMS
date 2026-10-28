using Bms.Domain.ExpenseRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bms.Infrastructure.Persistence.Configurations;

public sealed class ExpenseRequestConfiguration : IEntityTypeConfiguration<ExpenseRequest>
{
    public void Configure(EntityTypeBuilder<ExpenseRequest> builder)
    {
        builder.ToTable("ExpenseRequests", "bms_web");
        builder.HasKey(request => request.Id);
        builder.Property(request => request.PaymentOperation)
            .HasConversion<string>(value => ExpensePaymentOperation.Serialize(value!), value => ExpensePaymentOperation.Deserialize(value))
            .HasColumnName("PaymentOperationJson").HasColumnType("nvarchar(max)").IsRequired(false);
        builder.Property(request => request.Status)
            .HasConversion(
                status => status.ToCode(),
                code => ExpenseRequestStatusCode.Parse(code))
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(request => request.ActiveApprovalRouteSnapshotNumber);
        builder.Property(request => request.ActiveApprovalRouteStageNumber).IsRequired();
        builder.Property(request => request.InitiatorExternalId)
            .HasMaxLength(256)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(request => request.RequestNumber)
            .HasMaxLength(160)
            .UseCollation("Latin1_General_100_BIN2");
        builder.Property(request => request.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(request => request.CurrencyRateDate).HasColumnType("date");
        builder.Property(request => request.TreasuryBudgetMonth).HasColumnType("date");
        builder.Ignore(request => request.EffectiveBudgetMonth);
        builder.Property(request => request.CurrencyRate).HasPrecision(18, 6);
        builder.Property(request => request.CurrencyMultiplicity).HasPrecision(10, 0);
        builder.Property(request => request.Purpose).HasMaxLength(1_000);
        builder.Property(request => request.Comment).HasMaxLength(2_000);
        builder.Property(request => request.BusinessJustification).HasMaxLength(2_000);
        builder.Property(request => request.UrgencyReason).HasMaxLength(1_000);
        builder.Property(request => request.PaymentBasisCode)
            .HasConversion<string>()
            .HasMaxLength(40);
        builder.Property(request => request.InvoiceNumber).HasMaxLength(100);
        builder.Property(request => request.PublicOfferName).HasMaxLength(300);
        builder.Property(request => request.PublicOfferIdentifier).HasMaxLength(1_000);
        builder.Property(request => request.OtherBasisExplanation).HasMaxLength(1_000);
        builder.Property(request => request.SupportingDocumentMissingExplanation).HasMaxLength(1_000);
        builder.Property(request => request.CandidateCounterpartyName).HasMaxLength(300);
        builder.Property(request => request.CandidateResidencyCode)
            .HasConversion<string>()
            .HasMaxLength(40);
        builder.Property(request => request.CandidateBinIin).HasMaxLength(12);
        builder.Property(request => request.CandidateCountry).HasMaxLength(100);
        builder.Property(request => request.CandidateForeignRegistrationNumber).HasMaxLength(100);
        builder.Property(request => request.CandidateRegistrationNumberMissingExplanation).HasMaxLength(1_000);
        builder.Property(request => request.CandidateContractNumber).HasMaxLength(100);
        builder.Property(request => request.CandidateContractSubject).HasMaxLength(300);
        builder.Property(request => request.CandidateContractTerms).HasMaxLength(1_000);
        builder.Property(request => request.NewContractDocumentMissingExplanation).HasMaxLength(1_000);
        builder.Property(request => request.CreatedAtUtc).HasPrecision(3);
        builder.Property(request => request.UpdatedAtUtc).HasPrecision(3);
        builder.Property(request => request.ConcurrencyStamp).IsConcurrencyToken();
        builder.Property(request => request.ClosedPartially).IsRequired();
        builder.Property(request => request.AccrualCycleId);
        builder.Property(request => request.LatestClosingDocumentsReceivedDate)
            .HasColumnType("date");
        builder.Property(request => request.ConfirmedClosingControlAmount)
            .HasPrecision(18, 2)
            .IsRequired();
        builder.Property(request => request.FullyPaidAtUtc)
            .HasColumnType("datetimeoffset(3)");
        builder.Property(request => request.CopiedFromExpenseRequestId);
        builder.Ignore(request => request.TotalAmount);
        builder.HasIndex(request => new { request.InitiatorExternalId, request.CreatedAtUtc });
        builder.HasIndex(request => new { request.Status, request.CreatedAtUtc });
        builder.HasIndex(request => request.RequestNumber)
            .HasDatabaseName("UX_bms_web_ExpenseRequests_RequestNumber")
            .IsUnique()
            .HasFilter("[RequestNumber] IS NOT NULL");
        builder.HasIndex(request => request.CopiedFromExpenseRequestId)
            .HasDatabaseName("IX_bms_web_ExpenseRequests_CopiedFromExpenseRequestId")
            .HasFilter("[CopiedFromExpenseRequestId] IS NOT NULL");
        builder.HasOne<ExpenseRequest>()
            .WithMany()
            .HasForeignKey(request => request.CopiedFromExpenseRequestId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_bms_web_ExpenseRequests_CopySource");
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_bms_web_ExpenseRequests_CopySourceNotSelf",
            "[CopiedFromExpenseRequestId] IS NULL OR [CopiedFromExpenseRequestId] <> [Id]"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_bms_web_ExpenseRequests_urgency",
            "([IsUrgent] = 0 AND [UrgencyReason] IS NULL) OR " +
            "([IsUrgent] = 1 AND LEN(LTRIM(RTRIM([UrgencyReason]))) > 0)"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_bms_web_ExpenseRequests_CurrencyRateSnapshot",
            "CASE " +
            "WHEN [CurrencyRateDate] IS NULL AND [CurrencyRate] IS NULL " +
            "AND [CurrencyMultiplicity] IS NULL THEN 1 " +
            "WHEN [CurrencyCode] = N'KZT' AND [CurrencyRateDate] IS NULL " +
            "AND [CurrencyRate] = 1 AND [CurrencyMultiplicity] = 1 THEN 1 " +
            "WHEN [CurrencyCode] <> N'KZT' AND [CurrencyRateDate] IS NOT NULL " +
            "AND [CurrencyRate] > 0 AND [CurrencyMultiplicity] > 0 THEN 1 " +
            "ELSE 0 END = 1"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_bms_web_ExpenseRequests_AccrualClosingState",
            "([AccrualCycleId] IS NULL OR [AccrualCycleId] <> " +
            "CONVERT(uniqueidentifier, '00000000-0000-0000-0000-000000000000')) " +
            "AND [ConfirmedClosingControlAmount] >= 0 " +
            "AND ([FullyPaidAtUtc] IS NULL OR DATEPART(TZOFFSET, [FullyPaidAtUtc]) = 0) " +
            "AND ([LatestClosingDocumentsReceivedDate] IS NULL OR [FullyPaidAtUtc] IS NOT NULL) " +
            "AND ([ConfirmedClosingControlAmount] = 0 OR " +
            "[LatestClosingDocumentsReceivedDate] IS NOT NULL) " +
            "AND ([ClosedPartially] = 0 OR ([Status] = N'PAID' " +
            "AND [FullyPaidAtUtc] IS NOT NULL " +
            "AND [LatestClosingDocumentsReceivedDate] IS NOT NULL " +
            "AND [ConfirmedClosingControlAmount] > 0)) " +
            "AND ([Status] <> N'CLOSED' OR ([ClosedPartially] = 0 " +
            "AND [FullyPaidAtUtc] IS NOT NULL " +
            "AND [LatestClosingDocumentsReceivedDate] IS NOT NULL))"));
        builder.HasOne<PersistedAccrualClosingCycleSnapshot>()
            .WithMany()
            .HasForeignKey(request => new { request.Id, request.AccrualCycleId })
            .HasPrincipalKey(snapshot => new
            {
                snapshot.ExpenseRequestId,
                snapshot.CycleId
            })
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName(
                "FK_bms_web_ExpenseRequests_CurrentAccrualClosingCycle");
        builder.HasMany(request => request.Lines)
            .WithOne()
            .HasForeignKey(line => line.ExpenseRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        var navigation = builder.Metadata.FindNavigation(nameof(ExpenseRequest.Lines));
        navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
