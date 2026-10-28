using Bms.Domain.Budgeting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bms.Infrastructure.Persistence.Configurations;

public sealed class BudgetInstanceConfiguration : IEntityTypeConfiguration<BudgetInstance>
{
    public void Configure(EntityTypeBuilder<BudgetInstance> builder)
    {
        builder.ToTable("BudgetInstances", "bms_web", table => table.UseSqlOutputClause(false));
        builder.HasKey(instance => instance.Id).HasName("PK_bms_web_BudgetInstances");
        builder.Property(instance => instance.Number)
            .HasMaxLength(40)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(instance => instance.ValidFrom).HasColumnType("date").IsRequired();
        builder.Property(instance => instance.ValidTo).HasColumnType("date").IsRequired();
        builder.Property(instance => instance.Status)
            .HasConversion(
                status => status.ToString(),
                value => Enum.Parse<BudgetInstanceStatus>(value))
            .HasMaxLength(40)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(instance => instance.OwnerExternalId)
            .HasMaxLength(256)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(instance => instance.ApprovalOrdinal).HasColumnType("bigint").IsRequired(false);
        builder.Property(instance => instance.ApprovedAtUtc).HasColumnType("datetimeoffset(3)").IsRequired(false);
        builder.Property(instance => instance.ApprovedByEmployeeId).HasColumnType("uniqueidentifier").IsRequired(false);
        builder.Property(instance => instance.ApprovedByExternalId)
            .HasMaxLength(256)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired(false);
        // Like the creator and CFO references, the approver FK is SQL-owned.
        // Mapping a relationship here would conventionally add an unwanted sixth storage index.
        builder.Property(instance => instance.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasIndex(instance => instance.Number)
            .IsUnique()
            .HasDatabaseName("UX_bms_web_BudgetInstances_Number");
        builder.HasIndex(instance => new { instance.OwnerEmployeeId, instance.Status, instance.ValidFrom })
            .HasDatabaseName("IX_bms_web_BudgetInstances_Owner_Status_Year");
        builder.HasIndex(instance => new { instance.CostCenterId, instance.ValidFrom, instance.Status })
            .HasDatabaseName("IX_bms_web_BudgetInstances_CostCenter_Year_Status");
        builder.HasIndex(instance => new { instance.CostCenterId, instance.ApprovalOrdinal })
            .IsUnique()
            .HasFilter("[ApprovalOrdinal] IS NOT NULL")
            .HasDatabaseName("UX_bms_web_BudgetInstances_CostCenter_ApprovalOrdinal");
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_bms_web_BudgetInstances_month_bounds",
                "DAY([ValidFrom]) = 1 AND [ValidTo] = EOMONTH([ValidTo]) AND [ValidFrom] <= [ValidTo]");
            table.HasCheckConstraint(
                "CK_bms_web_BudgetInstances_approval_state",
                "( [Status] COLLATE Latin1_General_100_BIN2 = N'Draft' AND DATALENGTH([Status]) = 10 AND [ApprovalOrdinal] IS NULL AND [ApprovedAtUtc] IS NULL AND [ApprovedByEmployeeId] IS NULL AND [ApprovedByExternalId] IS NULL ) OR ( [Status] COLLATE Latin1_General_100_BIN2 = N'Approved' AND DATALENGTH([Status]) = 16 AND [ApprovalOrdinal] IS NOT NULL AND [ApprovalOrdinal] > 0 AND [ApprovedAtUtc] IS NOT NULL AND DATEPART(TZOFFSET, [ApprovedAtUtc]) = 0 AND [ApprovedByEmployeeId] IS NOT NULL AND [ApprovedByEmployeeId] <> CONVERT(uniqueidentifier, '00000000-0000-0000-0000-000000000000') AND [ApprovedByExternalId] IS NOT NULL AND DATALENGTH([ApprovedByExternalId]) BETWEEN 2 AND 512 AND DATALENGTH([ApprovedByExternalId]) = DATALENGTH(LTRIM(RTRIM([ApprovedByExternalId]))) )");
        });
        builder.HasMany(instance => instance.Lines)
            .WithOne()
            .HasForeignKey(line => line.BudgetInstanceId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_bms_web_BudgetInstanceLines_Instance");
        var navigation = builder.Metadata.FindNavigation(nameof(BudgetInstance.Lines));
        navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class BudgetInstanceLineConfiguration : IEntityTypeConfiguration<BudgetInstanceLine>
{
    public void Configure(EntityTypeBuilder<BudgetInstanceLine> builder)
    {
        builder.ToTable("BudgetInstanceLines", "bms_web", table => table.UseSqlOutputClause(false));
        builder.HasKey(line => line.Id).HasName("PK_bms_web_BudgetInstanceLines");
        builder.Property(line => line.Id).ValueGeneratedNever();
        builder.Property(line => line.BudgetMonth).HasColumnType("date").IsRequired();
        builder.Property(line => line.LimitAmountKzt).HasPrecision(18, 2).IsRequired();
        var historicalSchemaValue = Environment.GetEnvironmentVariable("BMS_SQLSERVER_TEST_SCHEMA_VERSION");
        var mapsFlowDirection = !int.TryParse(historicalSchemaValue, out var historicalSchemaVersion)
            || historicalSchemaVersion >= 41;
        if (mapsFlowDirection)
        {
            builder.Property(line => line.FlowDirection)
                .HasConversion(
                    direction => direction.ToString(),
                    value => Enum.Parse<BudgetFlowDirection>(value))
                .HasMaxLength(10)
                .UseCollation("Latin1_General_100_BIN2")
                .HasDefaultValue(BudgetFlowDirection.Payment)
                .IsRequired();
        }
        else
        {
            // Historical SQL033-SQL035 acceptance runs the current assembly
            // against a schema captured before SQL041 introduced this column.
            builder.Ignore(line => line.FlowDirection);
        }
        builder.Property(line => line.SortOrder).IsRequired();
        // SQL035 is a deliberately historical acceptance stage that runs before
        // SQL037 adds the durable comment column. The stage sets this
        // process-only test marker so EF can exercise the SQL035 contract
        // without issuing INSERT/UPDATE statements for a column that does not
        // exist yet. Normal runtime contexts always map Comment.
        if (!string.Equals(
                Environment.GetEnvironmentVariable("BMS_SQLSERVER_TEST_SCHEMA_VERSION"),
                "35",
                StringComparison.Ordinal))
        {
            builder.Property(line => line.Comment).HasMaxLength(2000).IsRequired(false);
        }
        else
        {
            // EF maps public properties by convention even when no explicit
            // Property(...) call is made. Ignore the column completely for
            // the pre-SQL037 historical stage so INSERT/UPDATE statements do
            // not reference a column that does not exist yet.
            builder.Ignore(line => line.Comment);
        }
        if (mapsFlowDirection)
        {
            var index = builder.HasIndex(line => new { line.BudgetInstanceId, line.CashFlowItemId, line.BudgetMonth, line.FlowDirection })
                .IsUnique(int.TryParse(historicalSchemaValue, out var indexSchemaVersion) && indexSchemaVersion < 46)
                .HasDatabaseName("UX_bms_web_BudgetInstanceLines_Key");
            // SQL047 covers aggregate reads without losing separate operation rows.
            // Explicit historical stages retain their pre-covering physical shape.
            if (!int.TryParse(historicalSchemaValue, out var coveredSchemaVersion) || coveredSchemaVersion >= 47)
                index.IncludeProperties(line => line.LimitAmountKzt);
        }
        else
        {
            builder.HasIndex(line => new { line.BudgetInstanceId, line.CashFlowItemId, line.BudgetMonth })
                .IsUnique()
                .HasDatabaseName("UX_bms_web_BudgetInstanceLines_Key");
        }
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_bms_web_BudgetInstanceLines_month_start",
                "DAY([BudgetMonth]) = 1");
            table.HasCheckConstraint(
                "CK_bms_web_BudgetInstanceLines_limit",
                "[LimitAmountKzt] >= 0");
            if (mapsFlowDirection)
            {
                table.HasCheckConstraint(
                    "CK_bms_web_BudgetInstanceLines_direction",
                    "[FlowDirection] = N'Payment' OR [FlowDirection] = N'Receipt'");
            }
            table.HasCheckConstraint(
                "CK_bms_web_BudgetInstanceLines_sort",
                "[SortOrder] >= 0");
        });
    }
}
