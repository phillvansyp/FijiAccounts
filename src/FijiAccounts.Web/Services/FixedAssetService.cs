using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed record FixedAssetRequest(
    Guid OrganisationId,
    string AssetNumber,
    string Name,
    DateOnly AcquisitionDate,
    decimal Cost,
    decimal ResidualValue,
    int UsefulLifeMonths,
    Guid AssetAccountId,
    Guid DepreciationExpenseAccountId,
    Guid AccumulatedDepreciationAccountId,
    Guid? AcquisitionBankAccountId = null);

public sealed class FixedAssetService(ApplicationDbContext db, TenantAccessService access, JournalPostingService posting)
{
    public async Task<FixedAsset> CreateAsync(string userId, FixedAssetRequest request, CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, request.OrganisationId)) throw new UnauthorizedAccessException("You cannot maintain fixed assets for this organisation.");
        if (string.IsNullOrWhiteSpace(request.AssetNumber) || string.IsNullOrWhiteSpace(request.Name) || request.Cost <= 0 || request.ResidualValue < 0 || request.ResidualValue >= request.Cost || request.UsefulLifeMonths < 1) throw new InvalidOperationException("Enter valid asset details, cost, residual value and useful life.");
        var ids = new[] { request.AssetAccountId, request.AccumulatedDepreciationAccountId, request.DepreciationExpenseAccountId }; var accounts = await db.LedgerAccounts.Where(x => x.OrganisationId == request.OrganisationId && x.IsActive && ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        if (accounts.Count != 3 || accounts[request.AssetAccountId].Type != AccountType.Asset || accounts[request.AccumulatedDepreciationAccountId].Type != AccountType.Asset || accounts[request.DepreciationExpenseAccountId].Type != AccountType.Expense) throw new InvalidOperationException("Select valid asset, accumulated depreciation and expense accounts.");
        var asset = new FixedAsset { OrganisationId = request.OrganisationId, AssetNumber = request.AssetNumber.Trim().ToUpperInvariant(), Name = request.Name.Trim(), AcquisitionDate = request.AcquisitionDate, Cost = request.Cost, ResidualValue = request.ResidualValue, UsefulLifeMonths = request.UsefulLifeMonths, AssetAccountId = request.AssetAccountId, DepreciationExpenseAccountId = request.DepreciationExpenseAccountId, AccumulatedDepreciationAccountId = request.AccumulatedDepreciationAccountId, CreatedByUserId = userId };
        db.FixedAssets.Add(asset); db.AuditEvents.Add(Audit(request.OrganisationId, userId, "FixedAssetCreated", asset.Id, new { asset.AssetNumber, asset.Cost })); await db.SaveChangesAsync(ct); return asset;
    }

    public async Task<FixedAssetDepreciation> DepreciateThroughAsync(string userId, Guid organisationId, Guid assetId, DateOnly throughDate, CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId)) throw new UnauthorizedAccessException("You cannot post depreciation for this organisation.");
        var asset = await db.FixedAssets.Include(x => x.DepreciationEntries).SingleOrDefaultAsync(x => x.Id == assetId && x.OrganisationId == organisationId && x.IsActive, ct) ?? throw new InvalidOperationException("Active fixed asset not found."); if (throughDate < asset.AcquisitionDate) throw new InvalidOperationException("Depreciation date cannot precede acquisition.");
        var months = Math.Min(asset.UsefulLifeMonths, (throughDate.Year - asset.AcquisitionDate.Year) * 12 + throughDate.Month - asset.AcquisitionDate.Month + 1); var target = Math.Round((asset.Cost - asset.ResidualValue) * months / asset.UsefulLifeMonths, 2); var posted = asset.DepreciationEntries.Sum(x => x.Amount); var amount = target - posted; if (amount <= 0) throw new InvalidOperationException("No additional book depreciation is due through this date.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct); var journal = await posting.PostAsync(userId, new(organisationId, throughDate, $"DEP-{asset.AssetNumber}-{throughDate:yyyyMM}", $"Book depreciation through {throughDate:dd MMM yyyy}", [new(asset.DepreciationExpenseAccountId, asset.Name, amount, 0), new(asset.AccumulatedDepreciationAccountId, asset.Name, 0, amount)]), ct); var entry = new FixedAssetDepreciation { FixedAssetId = asset.Id, ThroughDate = throughDate, Amount = amount, PostedJournalId = journal.Id, PostedByUserId = userId }; db.FixedAssetDepreciations.Add(entry); db.AuditEvents.Add(Audit(organisationId, userId, "FixedAssetDepreciationPosted", asset.Id, new { asset.AssetNumber, throughDate, amount })); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return entry;
    }
    private static AuditEvent Audit(Guid organisationId, string userId, string eventType, Guid id, object data) => new() { OrganisationId = organisationId, UserId = userId, EventType = eventType, EntityType = nameof(FixedAsset), EntityId = id.ToString(), JsonData = JsonSerializer.Serialize(data) };
}
