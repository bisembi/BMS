using System.Data;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Application.ReferenceData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

public sealed class OrganizationDirectoryRepository(BmsDbContext dbContext)
    : IOrganizationDirectoryRepository
{
    public async Task<ReferenceDataItemDto> CreateAsync(
        string code,
        string name,
        CancellationToken cancellationToken)
    {
        var transaction = dbContext.Database.CurrentTransaction;
        if (transaction is null
            || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
        {
            throw new InvalidOperationException(
                "An organization create requires a Serializable business transaction.");
        }

        var duplicate = await dbContext.Organizations
            .AsNoTracking()
            .AnyAsync(candidate => candidate.Code == code, cancellationToken);
        if (duplicate)
        {
            throw new OrganizationCodeConflictException();
        }

        var organization = new OrganizationReference
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            IsActive = true,
        };
        dbContext.Organizations.Add(organization);
        return new ReferenceDataItemDto(organization.Id, organization.Code, organization.Name);
    }

    public async Task<OrganizationDirectoryRecord?> FindAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return await dbContext.Organizations
            .AsNoTracking()
            .Where(organization => organization.Id == id)
            .Select(organization => new OrganizationDirectoryRecord(
                organization.Id,
                organization.Code,
                organization.Name,
                organization.IsActive))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ReferenceDataItemDto> UpdateAsync(
        Guid id,
        string code,
        string name,
        CancellationToken cancellationToken)
    {
        var transaction = dbContext.Database.CurrentTransaction;
        if (transaction is null
            || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
        {
            throw new InvalidOperationException(
                "An organization update requires a Serializable business transaction.");
        }

        var organization = await dbContext.Organizations
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
            ?? throw new OrganizationDirectoryNotFoundException();
        var duplicate = await dbContext.Organizations
            .AsNoTracking()
            .AnyAsync(candidate => candidate.Id != id && candidate.Code == code, cancellationToken);
        if (duplicate)
        {
            throw new OrganizationCodeConflictException();
        }

        organization.Code = code;
        organization.Name = name;
        return new ReferenceDataItemDto(organization.Id, organization.Code, organization.Name);
    }
}
