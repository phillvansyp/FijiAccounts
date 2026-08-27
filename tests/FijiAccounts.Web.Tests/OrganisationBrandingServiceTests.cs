using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class OrganisationBrandingServiceTests
{
    private static readonly byte[] Png =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x00
    ];

    [Fact]
    public async Task Owner_CanAddReplaceAndRemoveLogoWithAuditTrail()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new OrganisationBrandingService(test.Db, test.Access);

        await service.SaveAsync(test.UserId, new(
            test.Organisation.Id, "company.png", "image/png", Png));
        var stored = await service.GetAsync(test.UserId, test.Organisation.Id);
        Assert.NotNull(stored);
        Assert.Equal("company.png", stored.LogoFileName);
        Assert.Equal(Png, stored.LogoContent);

        await service.SaveAsync(test.UserId, new(
            test.Organisation.Id, "new-company.png", "image/png", Png));
        Assert.True(await service.DeleteAsync(test.UserId, test.Organisation.Id));
        Assert.Null(await service.GetAsync(test.UserId, test.Organisation.Id));

        Assert.Equal(
            ["OrganisationLogoAdded", "OrganisationLogoReplaced", "OrganisationLogoRemoved"],
            await test.Db.AuditEvents.OrderBy(x => x.Id).Select(x => x.EventType).ToListAsync());
    }

    [Fact]
    public async Task InvalidOrUnauthorizedLogo_IsRejectedWithoutStorage()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new OrganisationBrandingService(test.Db, test.Access);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(
            test.UserId,
            new(test.Organisation.Id, "fake.png", "image/png", [1, 2, 3])));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveAsync(
            "not-a-member",
            new(test.Organisation.Id, "company.png", "image/png", Png)));

        Assert.Empty(await test.Db.OrganisationBrandings.ToListAsync());
        Assert.Empty(await test.Db.AuditEvents.ToListAsync());
    }
}
