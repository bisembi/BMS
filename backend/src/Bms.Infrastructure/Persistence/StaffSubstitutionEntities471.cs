using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Bms.Infrastructure.Persistence;
public sealed class StaffSubstitutionDocument471
{
    public Guid Id{get;set;}
    public long Number{get;set;}
    public Guid PrincipalEmployeeId{get;set;}
    public Guid DeputyEmployeeId{get;set;}
    public string RoleCode{get;set;}="";
    public DateOnly From{get;set;}
    public DateOnly? Through{get;set;}
    public DateTimeOffset ValidFromUtc{get;set;}
    public DateTimeOffset? ValidBeforeUtc{get;set;}
    public string Status{get;set;}="DRAFT";
    public int Version{get;set;}
    public DateTimeOffset CreatedAtUtc{get;set;}
    public DateTimeOffset UpdatedAtUtc{get;set;}
    public DateTimeOffset? AcceptedAtUtc{get;set;}
}
public sealed class StaffSubstitutionCommand471
{
    public Guid Id{get;set;}
    public Guid DocumentId{get;set;}
    public string ActorExternalId{get;set;}="";
    public string Action{get;set;}="";
    public string PayloadSHA{get;set;}="";
    public string ResponseJson{get;set;}="";
    public DateTimeOffset AtUtc{get;set;}
}
public sealed class StaffSubstitutionAction471
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid PrincipalEmployeeId { get; set; }
    public Guid DeputyEmployeeId { get; set; }
    public string ActorExternalId { get; set; } = "";
    public string RoleCode { get; set; } = "";
    public DateTimeOffset AtUtc { get; set; }
    public string ObjectType { get; set; } = "";
    public string ObjectId { get; set; } = "";
    public string Action { get; set; } = "";
}
public sealed class StaffSubstitutionActionConfiguration471 : IEntityTypeConfiguration<StaffSubstitutionAction471>
{
    public void Configure(EntityTypeBuilder<StaffSubstitutionAction471> b)
    {
        b.ToTable("StaffSubstitutionActions471", "bms_web"); b.HasKey(x => x.Id);
        b.Property(x => x.ActorExternalId).HasMaxLength(256).IsRequired();
        b.Property(x => x.RoleCode).HasMaxLength(100).IsRequired();
        b.Property(x => x.ObjectType).HasMaxLength(100).IsRequired();
        b.Property(x => x.ObjectId).HasMaxLength(100).IsRequired();
        b.Property(x => x.Action).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.DocumentId, x.AtUtc });
    }
}
public sealed class StaffSubstitutionDocumentConfiguration471:IEntityTypeConfiguration<StaffSubstitutionDocument471>
{
    public void Configure(EntityTypeBuilder<StaffSubstitutionDocument471>b)
    {
        b.ToTable("StaffSubstitutions471","bms_web");b.HasKey(x=>x.Id);b.Property(x=>x.Number).UseIdentityColumn();b.HasIndex(x=>x.Number).IsUnique();
        b.Property(x=>x.RoleCode).HasMaxLength(100).UseCollation("Latin1_General_100_BIN2").IsRequired();
        b.Property(x=>x.Status).HasMaxLength(20).UseCollation("Latin1_General_100_BIN2").IsRequired();
        b.Property(x=>x.Version).IsConcurrencyToken();
        b.HasIndex(x=>new{x.PrincipalEmployeeId,x.RoleCode,x.Status,x.ValidFromUtc});b.HasIndex(x=>new{x.DeputyEmployeeId,x.Status,x.ValidFromUtc});
    }
}
public sealed class StaffSubstitutionCommandConfiguration471:IEntityTypeConfiguration<StaffSubstitutionCommand471>
{
    public void Configure(EntityTypeBuilder<StaffSubstitutionCommand471>b)
    {
        b.ToTable("StaffSubstitutionCommands471","bms_web");b.HasKey(x=>x.Id);
        b.Property(x=>x.ActorExternalId).HasMaxLength(256).UseCollation("Latin1_General_100_BIN2").IsRequired();
        b.Property(x=>x.Action).HasMaxLength(20).IsRequired();b.Property(x=>x.PayloadSHA).HasMaxLength(64).IsRequired();b.Property(x=>x.ResponseJson).HasColumnType("nvarchar(max)").IsRequired();
        b.HasIndex(x=>new{x.DocumentId,x.AtUtc});
    }
}
