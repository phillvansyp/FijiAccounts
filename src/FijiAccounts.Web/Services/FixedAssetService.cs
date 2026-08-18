using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed record FixedAssetRequest(
    Guid OrganisationId,
    string Name,
    DateOnly AcquisitionDate,
    decimal Cost,
    decimal ResidualValue,
    int UsefulLifeMonths,
    Guid AssetAccountId,
    Guid DepreciationExpenseAccountId,
    Guid AccumulatedDepreciationAccountId,
    Guid? AcquisitionBankAccountId = null);

public sealed record FixedAssetDisposalRequest(
    Guid OrganisationId,
    Guid FixedAssetId,
    DateOnly DisposalDate,
    decimal Proceeds,
    Guid BankAccountId,
    Guid GainAccountId,
    Guid LossAccountId);

public sealed class FixedAssetService(ApplicationDbContext db, TenantAccessService access, JournalPostingService posting)
{
    public async Task<FixedAsset> CreateAsync(
    string userId,
    FixedAssetRequest request,
    CancellationToken ct = default)
{
    if (!await access.CanPostJournalsAsync(
            userId,
            request.OrganisationId))
    {
        throw new UnauthorizedAccessException(
            "You cannot maintain fixed assets for this organisation.");
    }

    var ids = new[]
    {
        request.AssetAccountId,
        request.AccumulatedDepreciationAccountId,
        request.DepreciationExpenseAccountId
    };

    var accounts =
        await db.LedgerAccounts
            .Where(x =>
                x.OrganisationId == request.OrganisationId &&
                x.IsActive &&
                ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

    if (accounts.Count != 3 ||
        accounts[request.AssetAccountId].Type != AccountType.Asset ||
        accounts[request.AccumulatedDepreciationAccountId].Type != AccountType.Asset ||
        accounts[request.DepreciationExpenseAccountId].Type != AccountType.Expense)
    {
        throw new InvalidOperationException(
            "Select valid asset, accumulated depreciation and expense accounts.");
    }

    LedgerAccount? acquisitionBank = null;

    if (request.AcquisitionBankAccountId is not null)
    {
        acquisitionBank =
            await db.LedgerAccounts
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == request.AcquisitionBankAccountId.Value &&
                        x.OrganisationId == request.OrganisationId &&
                        x.IsActive &&
                        x.IsBankAccount,
                    ct)
            ?? throw new InvalidOperationException(
                "Select an active bank account for the asset acquisition.");
    }

    await using var transaction =
        await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            ct);

            var existingNumbers =
    await db.FixedAssets
        .Where(x => x.OrganisationId == request.OrganisationId)
        .Select(x => x.AssetNumber)
        .ToListAsync(ct);

var nextNumber =
    existingNumbers
        .Where(x =>
            x.StartsWith("FA-") &&
            int.TryParse(x[3..], out _))
        .Select(x =>
            int.TryParse(x[3..], out var number)
                ? number
                : 0)
        .DefaultIfEmpty(0)
        .Max() + 1;

var assetNumber =
    $"FA-{nextNumber:0000}";

    PostedJournal? acquisitionJournal = null;

    if (acquisitionBank is not null)
    {
        acquisitionJournal =
            await posting.PostAsync(
                userId,
                new(
                    request.OrganisationId,
                    request.AcquisitionDate,
                    $"ACQ-{assetNumber}",
                    $"Fixed asset acquisition · {request.Name.Trim()}",
                    [
                        new(
                            request.AssetAccountId,
                            request.Name.Trim(),
                            request.Cost,
                            0),
                        new(
                            acquisitionBank.Id,
                            request.Name.Trim(),
                            0,
                            request.Cost)
                    ]),
                ct);
    }

    var asset =
        new FixedAsset
        {
            OrganisationId = request.OrganisationId,
            AssetNumber = assetNumber,
            Name = request.Name.Trim(),
            AcquisitionDate = request.AcquisitionDate,
            Cost = request.Cost,
            ResidualValue = request.ResidualValue,
            UsefulLifeMonths = request.UsefulLifeMonths,
            AssetAccountId = request.AssetAccountId,
            DepreciationExpenseAccountId =
                request.DepreciationExpenseAccountId,
            AccumulatedDepreciationAccountId =
                request.AccumulatedDepreciationAccountId,

            AcquisitionBankAccountId =
                acquisitionBank?.Id,

            AcquisitionJournalId =
                acquisitionJournal?.Id,

            CreatedByUserId = userId
        };

    db.FixedAssets.Add(asset);

    db.AuditEvents.Add(
        Audit(
            request.OrganisationId,
            userId,
            "FixedAssetCreated",
            asset.Id,
            new
            {
                asset.AssetNumber,
                asset.Cost,
                asset.AcquisitionBankAccountId,
                asset.AcquisitionJournalId
            }));

    await db.SaveChangesAsync(ct);
    await transaction.CommitAsync(ct);

    return asset;
}

    public async Task<FixedAssetDepreciation> DepreciateThroughAsync(string userId, Guid organisationId, Guid assetId, DateOnly throughDate, CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId)) throw new UnauthorizedAccessException("You cannot post depreciation for this organisation.");
        var asset = await db.FixedAssets.Include(x => x.DepreciationEntries).SingleOrDefaultAsync(x => x.Id == assetId && x.OrganisationId == organisationId && x.IsActive, ct) ?? throw new InvalidOperationException("Active fixed asset not found."); if (throughDate < asset.AcquisitionDate) throw new InvalidOperationException("Depreciation date cannot precede acquisition.");
        var months = Math.Min(asset.UsefulLifeMonths, (throughDate.Year - asset.AcquisitionDate.Year) * 12 + throughDate.Month - asset.AcquisitionDate.Month + 1); var target = Math.Round((asset.Cost - asset.ResidualValue) * months / asset.UsefulLifeMonths, 2); var posted = asset.DepreciationEntries.Sum(x => x.Amount); var amount = target - posted; if (amount <= 0) throw new InvalidOperationException("No additional book depreciation is due through this date.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct); var journal = await posting.PostAsync(userId, new(organisationId, throughDate, $"DEP-{asset.AssetNumber}-{throughDate:yyyyMM}", $"Book depreciation through {throughDate:dd MMM yyyy}", [new(asset.DepreciationExpenseAccountId, asset.Name, amount, 0), new(asset.AccumulatedDepreciationAccountId, asset.Name, 0, amount)]), ct); var entry = new FixedAssetDepreciation { FixedAssetId = asset.Id, ThroughDate = throughDate, Amount = amount, PostedJournalId = journal.Id, PostedByUserId = userId }; db.FixedAssetDepreciations.Add(entry); db.AuditEvents.Add(Audit(organisationId, userId, "FixedAssetDepreciationPosted", asset.Id, new { asset.AssetNumber, throughDate, amount })); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return entry;
    }

    public async Task<FixedAssetDisposal> DisposeAsync(
    string userId,
    FixedAssetDisposalRequest request,
    CancellationToken ct = default)
{
    if (!await access.CanPostJournalsAsync(
            userId,
            request.OrganisationId))
    {
        throw new UnauthorizedAccessException(
            "You cannot dispose fixed assets for this organisation.");
    }

    if (request.Proceeds < 0)
    {
        throw new InvalidOperationException(
            "Disposal proceeds cannot be negative.");
    }

    var asset =
        await db.FixedAssets
            .Include(x => x.DepreciationEntries)
            .SingleOrDefaultAsync(
                x =>
                    x.Id == request.FixedAssetId &&
                    x.OrganisationId == request.OrganisationId &&
                    x.IsActive,
                ct)
        ?? throw new InvalidOperationException(
            "Active fixed asset not found.");

    if (request.DisposalDate < asset.AcquisitionDate)
    {
        throw new InvalidOperationException(
            "Disposal date cannot precede acquisition.");
    }

    if (await db.FixedAssetDisposals.AnyAsync(
            x => x.FixedAssetId == asset.Id,
            ct))
    {
        throw new InvalidOperationException(
            "This asset has already been disposed.");
    }

    var bank =
        await db.LedgerAccounts.SingleOrDefaultAsync(
            x =>
                x.Id == request.BankAccountId &&
                x.OrganisationId == request.OrganisationId &&
                x.IsActive &&
                x.IsBankAccount,
            ct)
        ?? throw new InvalidOperationException(
            "Select a valid bank account for disposal proceeds.");

    var gainAccount =
        await db.LedgerAccounts.SingleOrDefaultAsync(
            x =>
                x.Id == request.GainAccountId &&
                x.OrganisationId == request.OrganisationId &&
                x.IsActive &&
                x.Type == AccountType.Revenue,
            ct)
        ?? throw new InvalidOperationException(
            "Select a valid gain account.");

    var lossAccount =
        await db.LedgerAccounts.SingleOrDefaultAsync(
            x =>
                x.Id == request.LossAccountId &&
                x.OrganisationId == request.OrganisationId &&
                x.IsActive &&
                x.Type == AccountType.Expense,
            ct)
        ?? throw new InvalidOperationException(
            "Select a valid loss account.");

    var accumulatedDepreciation =
        asset.DepreciationEntries.Sum(x => x.Amount);

    var bookValue =
        asset.Cost - accumulatedDepreciation;

    var gainLoss =
        request.Proceeds - bookValue;

    await using var transaction =
        await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            ct);

    var lines =
        new List<JournalLineInput>
        {
            new(
                asset.AccumulatedDepreciationAccountId,
                $"Dispose {asset.AssetNumber}",
                accumulatedDepreciation,
                0),

            new(
                asset.AssetAccountId,
                $"Dispose {asset.AssetNumber}",
                0,
                asset.Cost)
        };

    if (request.Proceeds > 0)
    {
        lines.Add(
            new JournalLineInput(
                bank.Id,
                $"Disposal proceeds {asset.AssetNumber}",
                request.Proceeds,
                0));
    }

    if (gainLoss > 0)
    {
        lines.Add(
            new JournalLineInput(
                gainAccount.Id,
                $"Gain on disposal {asset.AssetNumber}",
                0,
                gainLoss));
    }
    else if (gainLoss < 0)
    {
        lines.Add(
            new JournalLineInput(
                lossAccount.Id,
                $"Loss on disposal {asset.AssetNumber}",
                Math.Abs(gainLoss),
                0));
    }

    var journal =
        await posting.PostAsync(
            userId,
            new(
                request.OrganisationId,
                request.DisposalDate,
                $"DISP-{asset.AssetNumber}",
                $"Dispose fixed asset {asset.AssetNumber} · {asset.Name}",
                lines),
            ct);

    var disposal =
        new FixedAssetDisposal
        {
            FixedAssetId = asset.Id,
            DisposalDate = request.DisposalDate,
            Proceeds = request.Proceeds,
            AccumulatedDepreciation = accumulatedDepreciation,
            BookValue = bookValue,
            GainLoss = gainLoss,
            BankAccountId = bank.Id,
            GainAccountId = gainAccount.Id,
            LossAccountId = lossAccount.Id,
            PostedJournalId = journal.Id,
            PostedByUserId = userId
        };

    asset.IsActive = false;

    db.FixedAssetDisposals.Add(disposal);

    db.AuditEvents.Add(
        Audit(
            request.OrganisationId,
            userId,
            "FixedAssetDisposed",
            asset.Id,
            new
            {
                asset.AssetNumber,
                request.DisposalDate,
                request.Proceeds,
                accumulatedDepreciation,
                bookValue,
                gainLoss,
                DisposalJournalId = journal.Id
            }));

    await db.SaveChangesAsync(ct);
    await transaction.CommitAsync(ct);

    return disposal;
}
    private static AuditEvent Audit(Guid organisationId, string userId, string eventType, Guid id, object data) => new() { OrganisationId = organisationId, UserId = userId, EventType = eventType, EntityType = nameof(FixedAsset), EntityId = id.ToString(), JsonData = JsonSerializer.Serialize(data) };
}
