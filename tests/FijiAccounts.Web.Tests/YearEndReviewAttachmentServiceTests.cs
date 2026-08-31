using System.Text;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class YearEndReviewAttachmentServiceTests
{
    [Fact]
    public async Task AttachmentLifecycle_IsImmutableTenantScopedAndAudited()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var period = new AccountingPeriod
        {
            OrganisationId = test.Organisation.Id,
            Name = "Year ended 31 July 2026",
            StartsOn = new DateOnly(2025, 8, 1),
            EndsOn = new DateOnly(2026, 7, 31)
        };
        var assignee = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "review-evidence@example.com",
            NormalizedUserName = "REVIEW-EVIDENCE@EXAMPLE.COM",
            Email = "review-evidence@example.com",
            NormalizedEmail = "REVIEW-EVIDENCE@EXAMPLE.COM",
            EmailConfirmed = true
        };
        test.Db.AccountingPeriods.Add(period);
        test.Db.Users.Add(assignee);
        test.Db.OrganisationMemberships.Add(new OrganisationMembership
        {
            OrganisationId = test.Organisation.Id,
            UserId = assignee.Id,
            User = assignee,
            Role = OrganisationRole.Bookkeeper
        });
        await test.Db.SaveChangesAsync();

        var reviews = new YearEndReviewService(test.Db, test.Access);
        await reviews.StartAsync(test.UserId, test.Organisation.Id, period.Id);
        await reviews.UpdateItemAsync(
            test.UserId,
            test.Organisation.Id,
            period.Id,
            YearEndReviewArea.AgedReceivables,
            YearEndReviewStatus.QueryRaised,
            "Provide the post-year-end receipt.",
            assignee.Id,
            new DateOnly(2026, 8, 14));

        var documents = new YearEndReviewAttachmentService(
            test.Db,
            test.Access,
            new DatabaseImmutableDocumentStore(test.Db));
        var content = Encoding.UTF8.GetBytes("%PDF-1.7\nreview evidence");
        var attachment = await documents.AddAsync(
            assignee.Id,
            test.Organisation.Id,
            period.Id,
            YearEndReviewArea.AgedReceivables,
            new YearEndReviewAttachmentRequest(
                "receipt.pdf",
                "application/pdf",
                content.LongLength,
                content,
                false));

        var review = await reviews.GetAsync(assignee.Id, test.Organisation.Id, period.Id);
        Assert.Single(review!.Items.Single(x =>
            x.Area == YearEndReviewArea.AgedReceivables).Attachments);
        var download = await documents.DownloadAsync(
            assignee.Id,
            test.Organisation.Id,
            period.Id,
            attachment.Id);
        Assert.NotNull(download);
        Assert.Equal(content, download.Content);
        Assert.Equal("receipt.pdf", download.FileName);

        await reviews.RespondAsync(
            assignee.Id,
            test.Organisation.Id,
            period.Id,
            YearEndReviewArea.AgedReceivables,
            "Receipt attached.");
        await reviews.ResolveQueryAsync(
            test.UserId,
            test.Organisation.Id,
            period.Id,
            YearEndReviewArea.AgedReceivables);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            documents.AddAsync(
                assignee.Id,
                test.Organisation.Id,
                period.Id,
                YearEndReviewArea.AgedReceivables,
                new YearEndReviewAttachmentRequest(
                    "late.pdf",
                    "application/pdf",
                    content.LongLength,
                    content,
                    false)));

        Assert.True(await test.Db.AuditEvents.AnyAsync(x =>
            x.EventType == "YearEndReviewAttachmentAdded" &&
            x.EntityId == period.Id.ToString()));
        Assert.True(await test.Db.AuditEvents.AnyAsync(x =>
            x.EventType == "YearEndReviewAttachmentDownloaded" &&
            x.EntityId == period.Id.ToString()));
        Assert.True(await test.Db.ImmutableDocumentObjects.AnyAsync(x =>
            x.Id == attachment.ImmutableDocumentObjectId &&
            x.OrganisationId == test.Organisation.Id));

        attachment.FileName = "changed.pdf";
        var immutableError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            test.Db.SaveChangesAsync());
        Assert.Contains("append-only", immutableError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddAsync_RejectsInvalidPdfEvidence()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var period = new AccountingPeriod
        {
            OrganisationId = test.Organisation.Id,
            Name = "July 2026",
            StartsOn = new DateOnly(2026, 7, 1),
            EndsOn = new DateOnly(2026, 7, 31)
        };
        test.Db.AccountingPeriods.Add(period);
        await test.Db.SaveChangesAsync();
        await new YearEndReviewService(test.Db, test.Access).StartAsync(
            test.UserId,
            test.Organisation.Id,
            period.Id);
        var documents = new YearEndReviewAttachmentService(
            test.Db,
            test.Access,
            new DatabaseImmutableDocumentStore(test.Db));
        var invalid = Encoding.UTF8.GetBytes("not a pdf");

        await Assert.ThrowsAsync<InvalidDataException>(() => documents.AddAsync(
            test.UserId,
            test.Organisation.Id,
            period.Id,
            YearEndReviewArea.TrialBalance,
            new YearEndReviewAttachmentRequest(
                "fake.pdf",
                "application/pdf",
                invalid.LongLength,
                invalid,
                false)));
    }
}
