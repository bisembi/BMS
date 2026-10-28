using Bms.Domain.Budgeting;
using Bms.Domain.ExpenseRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bms.Infrastructure.Persistence.Configurations;

public sealed class AccountingPaymentLinkConfiguration : IEntityTypeConfiguration<AccountingPaymentLink>
{
    public void Configure(EntityTypeBuilder<AccountingPaymentLink> b)
    {
        b.ToTable("AccountingPaymentLinks","bms_web",t=>t.UseSqlOutputClause(false));b.HasKey(x=>x.Id);
        b.Property(x=>x.DocumentKey).HasMaxLength(150).UseCollation("Latin1_General_100_BIN2");
        b.Property(x=>x.FinancialHash).HasMaxLength(64).UseCollation("Latin1_General_100_BIN2");
        b.Property(x=>x.CurrencyCode).HasMaxLength(3).UseCollation("Latin1_General_100_BIN2");
        b.Property(x=>x.Amount).HasPrecision(18,2);b.Property(x=>x.AmountKzt).HasPrecision(18,2);
        b.Property(x=>x.PaymentDay).HasColumnType("date");
        b.Property(x=>x.MatchedAtUtc).HasPrecision(3);b.Property(x=>x.ReversedAtUtc).HasPrecision(3);
        b.HasIndex(x=>x.DocumentKey).IsUnique().HasFilter("[ReversedAtUtc] IS NULL");
        b.HasIndex(x=>new{x.DocumentKey,x.Revision}).IsUnique();
        b.HasIndex(x=>x.EventId).IsUnique();b.HasIndex(x=>x.ExpenseRequestId);
        b.HasOne<ExpenseRequest>().WithMany().HasForeignKey(x=>x.ExpenseRequestId).OnDelete(DeleteBehavior.NoAction);
        b.ToTable(t=>t.HasCheckConstraint("CK_AccountingPaymentLinks_Content",
            "[Id]<>'00000000-0000-0000-0000-000000000000' AND [SourceBatchId]<>'00000000-0000-0000-0000-000000000000' AND [Amount]>0 AND [AmountKzt]>0 AND ISJSON([SourceLegsJson])=1 AND LEN([DocumentKey])>0 AND LEN([FinancialHash])=64"));
    }
}
public sealed class AccountingPaymentLinkLineConfiguration : IEntityTypeConfiguration<AccountingPaymentLinkLine>
{
    public void Configure(EntityTypeBuilder<AccountingPaymentLinkLine> b)
    {
        b.ToTable("AccountingPaymentLinkLines","bms_web",t=>t.UseSqlOutputClause(false));b.HasKey(x=>new{x.LinkId,x.ExpenseRequestLineId});
        b.Property(x=>x.BudgetMonth).HasColumnType("date");b.Property(x=>x.OriginalReservePaidKzt).HasPrecision(18,2);
        b.HasOne<AccountingPaymentLink>().WithMany().HasForeignKey(x=>x.LinkId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<ExpenseRequestLine>().WithMany().HasForeignKey(x=>x.ExpenseRequestLineId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<BudgetPosition>().WithMany().HasForeignKey(x=>new{x.CostCenterId,x.CashFlowItemId,x.BudgetMonth}).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x=>new{x.CostCenterId,x.CashFlowItemId,x.BudgetMonth});
        b.ToTable(t=>t.HasCheckConstraint("CK_AccountingPaymentLinkLines_Amount","[OriginalReservePaidKzt]>=0 AND DAY([BudgetMonth])=1"));
    }
}
