using System.Text.Json;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class NzPayInboundIntegrationServiceTests
{
    [Fact]
    public async Task Credential_is_one_time_and_can_read_only_the_NZ_organisations_active_assets()
    {
        await using var test = await NewZealandDatabaseAsync();
        var asset = AddVehicle(test);
        await test.Db.SaveChangesAsync();
        var service = Service(test);

        var issued = await service.RotateCredentialAsync(test.UserId, test.Organisation.Id);
        var stored = await test.Db.NzPayApiCredentials.AsNoTracking().SingleAsync();
        var authenticated = await service.AuthenticateAsync($"Bearer {issued.Token}", test.Organisation.Id,
            NzPayInboundIntegrationService.FixedAssetsReadScope);
        var assets = await service.SearchAssetsAsync(test.Organisation.Id, "ABC123");

        Assert.StartsWith("aizp_", issued.Token);
        Assert.DoesNotContain(issued.Token, stored.TokenSha256);
        Assert.NotNull(authenticated);
        Assert.Contains(asset.AssetNumber, JsonSerializer.Serialize(assets));
        Assert.Contains("ABC123", JsonSerializer.Serialize(assets));
        Assert.Single(await test.Db.AuditEvents.Where(x => x.EventType == "NzPayCredentialRotated").ToListAsync());
    }

    [Fact]
    public async Task Fbt_journal_is_duplicate_safe_and_requires_Account_Island_approval_before_posting()
    {
        await using var test = await NewZealandDatabaseAsync();
        var asset = AddVehicle(test);
        await test.Db.SaveChangesAsync();
        var service = Service(test);
        var issued = await service.RotateCredentialAsync(test.UserId, test.Organisation.Id);
        var credential = (await service.AuthenticateAsync($"Bearer {issued.Token}", test.Organisation.Id,
            NzPayInboundIntegrationService.FbtJournalsCreateScope))!;
        var payload = Payload(asset.Id, 639.30m);

        var first = await service.CreateDraftAsync(credential, "nz-pay-fbt:2026-q3:0123456789abcdef", payload);
        var retry = await service.CreateDraftAsync(credential, "nz-pay-fbt:2026-q3:0123456789abcdef", payload);

        Assert.Equal(first.JournalId, retry.JournalId);
        Assert.Equal("draft", first.Status);
        Assert.Empty(await test.Db.PostedJournals.ToListAsync());
        Assert.Single(await test.Db.NzPayFbtJournalDrafts.ToListAsync());

        var posted = await service.ApproveDraftAsync(test.UserId, test.Organisation.Id, first.JournalId);
        var stored = await test.LoadJournalAsync(posted.Id);
        Assert.Equal("NZD", stored.Currency);
        Assert.Equal(639.30m, stored.Lines.Sum(x => x.Debit));
        Assert.Equal(639.30m, stored.Lines.Sum(x => x.Credit));
        Assert.Equal(NzPayFbtJournalStatus.Posted,
            (await test.Db.NzPayFbtJournalDrafts.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Reusing_an_idempotency_key_for_changed_content_is_rejected()
    {
        await using var test = await NewZealandDatabaseAsync();
        var asset = AddVehicle(test);
        await test.Db.SaveChangesAsync();
        var service = Service(test);
        var issued = await service.RotateCredentialAsync(test.UserId, test.Organisation.Id);
        var credential = (await service.AuthenticateAsync($"Bearer {issued.Token}", test.Organisation.Id,
            NzPayInboundIntegrationService.FbtJournalsCreateScope))!;
        const string key = "nz-pay-fbt:2026-q3:0123456789abcdef";
        await service.CreateDraftAsync(credential, key, Payload(asset.Id, 639.30m));

        await Assert.ThrowsAsync<NzPayIntegrationConflictException>(() =>
            service.CreateDraftAsync(credential, key, Payload(asset.Id, 640m)));
    }

    [Fact]
    public async Task Retrying_an_external_journal_with_a_new_idempotency_key_returns_the_same_draft()
    {
        await using var test = await NewZealandDatabaseAsync();
        var asset = AddVehicle(test);
        await test.Db.SaveChangesAsync();
        var service = Service(test);
        var issued = await service.RotateCredentialAsync(test.UserId, test.Organisation.Id);
        var credential = (await service.AuthenticateAsync($"Bearer {issued.Token}", test.Organisation.Id,
            NzPayInboundIntegrationService.FbtJournalsCreateScope))!;
        var payload = Payload(asset.Id, 639.30m);

        var first = await service.CreateDraftAsync(credential, "nz-pay-fbt:2026-q3:0123456789abcdef", payload);
        var retry = await service.CreateDraftAsync(credential, "nz-pay-fbt:2026-q3:fedcba9876543210", payload);

        Assert.Equal(first.JournalId, retry.JournalId);
        Assert.Single(await test.Db.NzPayFbtJournalDrafts.ToListAsync());
    }

    [Fact]
    public async Task Rejected_fbt_journal_does_not_post_to_the_ledger()
    {
        await using var test = await NewZealandDatabaseAsync();
        var asset = AddVehicle(test);
        await test.Db.SaveChangesAsync();
        var service = Service(test);
        var issued = await service.RotateCredentialAsync(test.UserId, test.Organisation.Id);
        var credential = (await service.AuthenticateAsync($"Bearer {issued.Token}", test.Organisation.Id,
            NzPayInboundIntegrationService.FbtJournalsCreateScope))!;
        var draft = await service.CreateDraftAsync(credential, "nz-pay-fbt:2026-q3:0123456789abcdef",
            Payload(asset.Id, 639.30m));

        await service.RejectDraftAsync(test.UserId, test.Organisation.Id, draft.JournalId);

        Assert.Empty(await test.Db.PostedJournals.ToListAsync());
        Assert.Equal(NzPayFbtJournalStatus.Rejected,
            (await test.Db.NzPayFbtJournalDrafts.AsNoTracking().SingleAsync()).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApproveDraftAsync(test.UserId, test.Organisation.Id, draft.JournalId));
    }

    private static async Task<AccountingTestDatabase> NewZealandDatabaseAsync()
    {
        var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.CountryCode = "NZ";
        test.Organisation.BaseCurrency = "NZD";
        test.Organisation.TaxLabel = "GST";
        await test.Db.SaveChangesAsync();
        return test;
    }

    private static FixedAsset AddVehicle(AccountingTestDatabase test)
    {
        var asset = new FixedAsset
        {
            OrganisationId = test.Organisation.Id,
            AssetNumber = "FA-0042",
            Name = "2025 Toyota RAV4",
            VehicleRegistration = "ABC123",
            AcquisitionDate = new DateOnly(2025, 1, 15),
            Cost = 50_000m,
            ResidualValue = 10_000m,
            UsefulLifeMonths = 60,
            AssetAccountId = test.Account("1500").Id,
            DepreciationExpenseAccountId = test.Account("6900").Id,
            AccumulatedDepreciationAccountId = test.Account("1500").Id,
            CreatedByUserId = test.UserId
        };
        test.Db.FixedAssets.Add(asset);
        return asset;
    }

    private static NzPayFbtJournalPayload Payload(Guid assetId, decimal amount) => new(
        "NZPAY-FBT-2026-Q3",
        new DateOnly(2026, 12, 31),
        "FBT-20261231",
        "NZ Pay FBT · quarter 3",
        "NZD",
        amount,
        [
            new("6900", "FBT expense", amount, 0m),
            new("2200", "FBT payable", 0m, amount)
        ],
        [new("benefit-1", assetId.ToString(), "FA-0042", "2025 Toyota RAV4", "ABC123")]);

    private static NzPayInboundIntegrationService Service(AccountingTestDatabase test) =>
        new(test.Db, test.Access, test.Posting);
}
