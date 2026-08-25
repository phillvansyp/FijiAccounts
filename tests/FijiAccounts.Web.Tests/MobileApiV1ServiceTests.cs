using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Api.Mobile.V1;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class MobileApiV1ServiceTests
{
    [Fact]
    public async Task OwnerReceivesOrganisationAndFullDimensionCapabilities()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var mobileApi = CreateMobileApi(test);

        var organisations = await mobileApi.ListOrganisationsAsync(test.UserId);
        var capabilities = await mobileApi.GetCapabilitiesAsync(
            test.UserId,
            test.Organisation.Id);

        var organisation = Assert.Single(organisations);
        Assert.Equal(test.Organisation.Id, organisation.Id);
        Assert.Equal("Owner", organisation.Access);
        Assert.False(organisation.IsAccountantClient);
        Assert.NotNull(capabilities);
        Assert.True(capabilities.CanRead);
        Assert.True(capabilities.CanPostJournals);
        Assert.True(capabilities.CanManageContacts);
        Assert.True(capabilities.CanManageTeam);
        Assert.Single(capabilities.Branches);
        Assert.Single(capabilities.Branches[0].Divisions);
    }

    [Fact]
    public async Task InaccessibleOrganisationIsNotDisclosed()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var mobileApi = CreateMobileApi(test);
        var outsiderId = Guid.NewGuid().ToString();

        var organisations = await mobileApi.ListOrganisationsAsync(outsiderId);
        var capabilities = await mobileApi.GetCapabilitiesAsync(
            outsiderId,
            test.Organisation.Id);

        Assert.Empty(organisations);
        Assert.Null(capabilities);
    }

    [Fact]
    public async Task RestrictedMemberReceivesOnlyGrantedDimensions()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var structures = new EnterpriseStructureService(test.Db);
        var grantedBranch = await structures.AddBranchAsync(
            test.UserId,
            test.Organisation.Id,
            "NADI",
            "Nadi Branch");
        var grantedDivision = await structures.AddDivisionAsync(
            test.UserId,
            test.Organisation.Id,
            grantedBranch.Id,
            "OPS",
            "Operations");
        var member = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "mobile-restricted@example.com",
            NormalizedUserName = "MOBILE-RESTRICTED@EXAMPLE.COM",
            Email = "mobile-restricted@example.com",
            NormalizedEmail = "MOBILE-RESTRICTED@EXAMPLE.COM",
            EmailConfirmed = true
        };
        test.Db.Users.Add(member);
        test.Db.OrganisationMemberships.Add(new OrganisationMembership
        {
            OrganisationId = test.Organisation.Id,
            UserId = member.Id,
            User = member,
            Role = OrganisationRole.Bookkeeper
        });
        await test.Db.SaveChangesAsync();
        await test.Access.SetDimensionAccessModeAsync(
            test.UserId,
            test.Organisation.Id,
            member.Id,
            DimensionAccessMode.Restricted);
        await test.Access.AddDimensionAccessGrantAsync(
            test.UserId,
            test.Organisation.Id,
            member.Id,
            grantedBranch.Id,
            grantedDivision.Id);
        var mobileApi = CreateMobileApi(test);

        var capabilities = await mobileApi.GetCapabilitiesAsync(
            member.Id,
            test.Organisation.Id);

        Assert.NotNull(capabilities);
        var branch = Assert.Single(capabilities.Branches);
        Assert.Equal(grantedBranch.Id, branch.Id);
        var division = Assert.Single(branch.Divisions);
        Assert.Equal(grantedDivision.Id, division.Id);
        Assert.True(capabilities.CanPostJournals);
        Assert.True(capabilities.CanManageContacts);
        Assert.False(capabilities.CanManageTeam);
    }

    [Fact]
    public async Task DashboardAndNotificationsRequireTenantAccessAndSupportReadCommand()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var mobileApi = CreateMobileApi(test);
        var notification = await test.Notifications.CreateAsync(
            new CreateNotificationRequest(
                test.Organisation.Id,
                "Mobile alert",
                "Review this item.",
                NotificationType.System,
                NotificationSeverity.Warning));

        var dashboard = await mobileApi.GetDashboardAsync(
            test.UserId,
            test.Organisation.Id);
        var notifications = await mobileApi.ListNotificationsAsync(
            test.UserId,
            test.Organisation.Id,
            null,
            Guid.Empty,
            25);
        var markedRead = await mobileApi.MarkNotificationReadAsync(
            test.UserId,
            test.Organisation.Id,
            notification.Id,
            "mobile-read-test-1");

        Assert.NotNull(dashboard);
        Assert.Equal(1, dashboard.UnreadNotifications);
        Assert.Equal(notification.Id, Assert.Single(notifications!.Items).Id);
        Assert.Equal(204, markedRead!.StatusCode);
        Assert.False(markedRead.Replayed);
        var emptyPage = await mobileApi.ListNotificationsAsync(
            test.UserId,
            test.Organisation.Id,
            null,
            Guid.Empty,
            25);
        Assert.Empty(emptyPage!.Items);

        var outsiderId = Guid.NewGuid().ToString();
        Assert.Null(await mobileApi.GetDashboardAsync(outsiderId, test.Organisation.Id));
        Assert.Null(await mobileApi.ListNotificationsAsync(
            outsiderId,
            test.Organisation.Id,
            null,
            Guid.Empty,
            25));
        Assert.Null(await mobileApi.MarkNotificationReadAsync(
            outsiderId,
            test.Organisation.Id,
            notification.Id,
            "mobile-read-test-2"));
    }

    [Fact]
    public async Task RestrictedDashboardExcludesOtherDivisionBalances()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var structures = new EnterpriseStructureService(test.Db);
        var branch = await structures.AddBranchAsync(
            test.UserId,
            test.Organisation.Id,
            "WEST",
            "Western Branch");
        var grantedDivision = await structures.AddDivisionAsync(
            test.UserId,
            test.Organisation.Id,
            branch.Id,
            "MOBILE",
            "Mobile Division");
        var defaultDivision = await test.Db.Divisions
            .AsNoTracking()
            .Include(x => x.Branch)
            .SingleAsync(x =>
                x.Branch.OrganisationId == test.Organisation.Id &&
                x.Branch.IsDefault &&
                x.IsDefault);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var grantedInvoice = await test.SalesInvoices.CreateAndPostAsync(
            test.UserId,
            new SalesInvoiceRequest(
                test.Organisation.Id,
                test.Customer.Id,
                today.AddDays(-2),
                today.AddDays(-1),
                [new("Granted sale", 1m, 100m, VatTreatment.Standard, test.Account("4000").Id)],
                branch.Id,
                grantedDivision.Id));
        await test.SalesInvoices.CreateAndPostAsync(
            test.UserId,
            new SalesInvoiceRequest(
                test.Organisation.Id,
                test.Customer.Id,
                today.AddDays(-2),
                today.AddDays(-1),
                [new("Hidden sale", 1m, 200m, VatTreatment.Standard, test.Account("4000").Id)],
                defaultDivision.BranchId,
                defaultDivision.Id));

        var member = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "mobile-dashboard@example.com",
            NormalizedUserName = "MOBILE-DASHBOARD@EXAMPLE.COM",
            Email = "mobile-dashboard@example.com",
            NormalizedEmail = "MOBILE-DASHBOARD@EXAMPLE.COM",
            EmailConfirmed = true
        };
        test.Db.Users.Add(member);
        test.Db.OrganisationMemberships.Add(new OrganisationMembership
        {
            OrganisationId = test.Organisation.Id,
            UserId = member.Id,
            User = member,
            Role = OrganisationRole.Bookkeeper
        });
        await test.Db.SaveChangesAsync();
        await test.Access.AddDimensionAccessGrantAsync(
            test.UserId,
            test.Organisation.Id,
            member.Id,
            branch.Id,
            grantedDivision.Id);

        var dashboard = await CreateMobileApi(test).GetDashboardAsync(
            member.Id,
            test.Organisation.Id);

        Assert.NotNull(dashboard);
        Assert.Equal(grantedInvoice.Total, dashboard.Receivables);
        Assert.Equal(1, dashboard.OverdueSalesInvoices);
    }

    [Fact]
    public async Task NotificationCursorPagesAreStableAndDoNotRepeatItems()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var created = new List<Notification>();
        for (var index = 0; index < 3; index++)
        {
            var notification = await test.Notifications.CreateAsync(
                new CreateNotificationRequest(
                    test.Organisation.Id,
                    $"Page alert {index}",
                    "Cursor paging test.",
                    NotificationType.System,
                    NotificationSeverity.Info));
            notification.CreatedAtTicks = 30 - index;
            created.Add(notification);
        }

        await test.Db.SaveChangesAsync();
        var mobileApi = CreateMobileApi(test);
        var firstPage = await mobileApi.ListNotificationsAsync(
            test.UserId,
            test.Organisation.Id,
            null,
            Guid.Empty,
            2);

        Assert.NotNull(firstPage);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.NotNull(firstPage.NextCursor);
        Assert.True(MobileApiCursor.TryDecode(
            firstPage.NextCursor,
            out var beforeTicks,
            out var beforeId));

        var secondPage = await mobileApi.ListNotificationsAsync(
            test.UserId,
            test.Organisation.Id,
            beforeTicks,
            beforeId,
            2);

        Assert.NotNull(secondPage);
        Assert.Single(secondPage.Items);
        Assert.Null(secondPage.NextCursor);
        Assert.Equal(
            created.OrderByDescending(x => x.CreatedAtTicks).Select(x => x.Id),
            firstPage.Items.Concat(secondPage.Items).Select(x => x.Id));
    }

    [Fact]
    public async Task IdempotencyReplayReturnsStoredResultAndRejectsKeyReuse()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var firstNotification = await test.Notifications.CreateAsync(
            new CreateNotificationRequest(
                test.Organisation.Id,
                "First alert",
                "First idempotency test item.",
                NotificationType.System,
                NotificationSeverity.Info));
        var secondNotification = await test.Notifications.CreateAsync(
            new CreateNotificationRequest(
                test.Organisation.Id,
                "Second alert",
                "Second idempotency test item.",
                NotificationType.System,
                NotificationSeverity.Info));
        var mobileApi = CreateMobileApi(test);
        const string key = "same-mobile-command-key";

        var first = await mobileApi.MarkNotificationReadAsync(
            test.UserId,
            test.Organisation.Id,
            firstNotification.Id,
            key);
        var replay = await mobileApi.MarkNotificationReadAsync(
            test.UserId,
            test.Organisation.Id,
            firstNotification.Id,
            key);
        var conflict = await mobileApi.MarkNotificationReadAsync(
            test.UserId,
            test.Organisation.Id,
            secondNotification.Id,
            key);

        Assert.NotNull(first);
        Assert.False(first.Replayed);
        Assert.Equal(204, first.StatusCode);
        Assert.NotNull(replay);
        Assert.True(replay.Replayed);
        Assert.False(replay.KeyConflict);
        Assert.Equal(204, replay.StatusCode);
        Assert.NotNull(conflict);
        Assert.True(conflict.Replayed);
        Assert.True(conflict.KeyConflict);
        Assert.Equal(409, conflict.StatusCode);
        Assert.Equal("idempotency_key_reused", conflict.ResultCode);
        Assert.Single(await test.Db.MobileIdempotencyRecords.ToListAsync());
        Assert.Single(await test.Db.AuditEvents
            .Where(x => x.EventType == "NotificationRead")
            .ToListAsync());
        Assert.False((await test.Db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.Id == secondNotification.Id)).IsRead);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("contains space")]
    public void InvalidIdempotencyKeysAreRejected(string? key) =>
        Assert.False(MobileIdempotencyService.IsValidKey(key));

    private static MobileApiV1Service CreateMobileApi(AccountingTestDatabase test) =>
        new(
            test.Db,
            test.Access,
            test.Notifications,
            new MobileIdempotencyService(test.Db));
}
