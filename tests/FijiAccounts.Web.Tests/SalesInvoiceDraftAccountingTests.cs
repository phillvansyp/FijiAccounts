using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class SalesInvoiceDraftAccountingTests
{
    [Fact]
    public async Task CreateDraftAsync_PersistsDraftWithCalculatedTotals()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var invoice =
            await test.SalesInvoices.CreateDraftAsync(
                test.UserId,
                Request(
                    test,
                    description: "Draft consulting",
                    quantity: 1m,
                    unitPrice: 100m));

        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.StartsWith("DRAFT-", invoice.InvoiceNumber);
        Assert.Null(invoice.PostedJournalId);

        Assert.Equal(100m, invoice.Subtotal);
        Assert.Equal(12.50m, invoice.VatTotal);
        Assert.Equal(112.50m, invoice.Total);

        var stored =
            await test.Db.SalesInvoices
                .AsNoTracking()
                .Include(x => x.Lines)
                .SingleAsync(x => x.Id == invoice.Id);

        Assert.Single(stored.Lines);
        Assert.Equal(
            "Draft consulting",
            stored.Lines[0].Description);
    }

    [Fact]
    public async Task CreateDraftAsync_WritesDraftCreatedAudit()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var invoice =
            await test.SalesInvoices.CreateDraftAsync(
                test.UserId,
                Request(
                    test,
                    description: "Audit draft",
                    quantity: 1m,
                    unitPrice: 80m));

        var audit =
            await test.Db.AuditEvents
                .AsNoTracking()
                .SingleAsync(x =>
                    x.EntityType == nameof(SalesInvoice) &&
                    x.EntityId == invoice.Id.ToString() &&
                    x.EventType == "SalesInvoiceDraftCreated");

        Assert.Equal(test.UserId, audit.UserId);
        Assert.Equal(test.Organisation.Id, audit.OrganisationId);
    }

    [Fact]
    public async Task UpdateDraftAsync_ReplacesLinesAndRecalculatesTotals()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var invoice =
            await test.SalesInvoices.CreateDraftAsync(
                test.UserId,
                Request(
                    test,
                    description: "Original line",
                    quantity: 1m,
                    unitPrice: 100m));

        var updated =
            await test.SalesInvoices.UpdateDraftAsync(
                test.UserId,
                invoice.Id,
                new SalesInvoiceRequest(
                    OrganisationId: test.Organisation.Id,
                    CustomerId: test.Customer.Id,
                    IssueDate: new DateOnly(2026, 8, 21),
                    DueDate: new DateOnly(2026, 9, 20),
                    Lines:
                    [
                        new(
                            Description: "Replacement A",
                            Quantity: 2m,
                            UnitPrice: 50m,
                            VatTreatment: VatTreatment.Standard,
                            RevenueAccountId: test.Account("4000").Id),

                        new(
                            Description: "Replacement B",
                            Quantity: 1m,
                            UnitPrice: 40m,
                            VatTreatment: VatTreatment.Standard,
                            RevenueAccountId: test.Account("4000").Id)
                    ]));

        Assert.Equal(140m, updated.Subtotal);
        Assert.Equal(17.50m, updated.VatTotal);
        Assert.Equal(157.50m, updated.Total);

        var stored =
            await test.Db.SalesInvoices
                .AsNoTracking()
                .Include(x => x.Lines)
                .SingleAsync(x => x.Id == invoice.Id);

        Assert.Equal(2, stored.Lines.Count);

        Assert.DoesNotContain(
            stored.Lines,
            x => x.Description == "Original line");

        Assert.Contains(
            stored.Lines,
            x => x.Description == "Replacement A");

        Assert.Contains(
            stored.Lines,
            x => x.Description == "Replacement B");

        Assert.Equal(
            new DateOnly(2026, 8, 21),
            stored.IssueDate);

        Assert.Equal(
            new DateOnly(2026, 9, 20),
            stored.DueDate);
    }

    [Fact]
    public async Task UpdateDraftAsync_WritesDraftUpdatedAudit()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var invoice =
            await test.SalesInvoices.CreateDraftAsync(
                test.UserId,
                Request(
                    test,
                    description: "Before update",
                    quantity: 1m,
                    unitPrice: 100m));

        await test.SalesInvoices.UpdateDraftAsync(
            test.UserId,
            invoice.Id,
            Request(
                test,
                description: "After update",
                quantity: 1m,
                unitPrice: 120m));

        var audit =
            await test.Db.AuditEvents
                .AsNoTracking()
                .SingleAsync(x =>
                    x.EntityType == nameof(SalesInvoice) &&
                    x.EntityId == invoice.Id.ToString() &&
                    x.EventType == "SalesInvoiceDraftUpdated");

        Assert.Equal(test.UserId, audit.UserId);
    }

    [Fact]
    public async Task PostDraftAsync_ChangesNumberStatusAndPostsExactJournal()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var draft =
            await test.SalesInvoices.CreateDraftAsync(
                test.UserId,
                Request(
                    test,
                    description: "Draft posting",
                    quantity: 1m,
                    unitPrice: 100m));

        var originalSequence =
            draft.SequenceNumber;

        var posted =
            await test.SalesInvoices.PostDraftAsync(
                test.UserId,
                test.Organisation.Id,
                draft.Id);

        Assert.Equal(InvoiceStatus.Posted, posted.Status);
        Assert.Equal(originalSequence, posted.SequenceNumber);

        Assert.Equal(
            $"INV-{originalSequence:D6}",
            posted.InvoiceNumber);

        Assert.NotNull(posted.PostedJournalId);

        var journal =
            await test.LoadJournalAsync(
                posted.PostedJournalId!.Value);

        Assert.Equal(
            posted.InvoiceNumber,
            journal.Reference);

        var receivables =
            journal.Lines.Single(
                x => x.LedgerAccount.Code == "1100");

        var revenue =
            journal.Lines.Single(
                x => x.LedgerAccount.Code == "4000");

        var vatPayable =
            journal.Lines.Single(
                x => x.LedgerAccount.Code == "2100");

        Assert.Equal(112.50m, receivables.Debit);
        Assert.Equal(0m, receivables.Credit);

        Assert.Equal(0m, revenue.Debit);
        Assert.Equal(100m, revenue.Credit);

        Assert.Equal(0m, vatPayable.Debit);
        Assert.Equal(12.50m, vatPayable.Credit);
    }

    [Fact]
    public async Task PostDraftAsync_WritesSalesInvoicePostedAudit()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var draft =
            await test.SalesInvoices.CreateDraftAsync(
                test.UserId,
                Request(
                    test,
                    description: "Post audit",
                    quantity: 1m,
                    unitPrice: 100m));

        var posted =
            await test.SalesInvoices.PostDraftAsync(
                test.UserId,
                test.Organisation.Id,
                draft.Id);

        var audit =
            await test.Db.AuditEvents
                .AsNoTracking()
                .SingleAsync(x =>
                    x.EntityType == nameof(SalesInvoice) &&
                    x.EntityId == posted.Id.ToString() &&
                    x.EventType == "SalesInvoicePosted");

        Assert.Equal(test.UserId, audit.UserId);
    }

    [Fact]
    public async Task UpdateDraftAsync_WhenInvoiceAlreadyPosted_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var draft =
            await test.SalesInvoices.CreateDraftAsync(
                test.UserId,
                Request(
                    test,
                    description: "Posted before edit",
                    quantity: 1m,
                    unitPrice: 100m));

        await test.SalesInvoices.PostDraftAsync(
            test.UserId,
            test.Organisation.Id,
            draft.Id);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    test.SalesInvoices.UpdateDraftAsync(
                        test.UserId,
                        draft.Id,
                        Request(
                            test,
                            description: "Should fail",
                            quantity: 1m,
                            unitPrice: 120m)));

        Assert.Contains(
            "Only draft invoices",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostDraftAsync_WhenInvoiceAlreadyPosted_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var draft =
            await test.SalesInvoices.CreateDraftAsync(
                test.UserId,
                Request(
                    test,
                    description: "Double post",
                    quantity: 1m,
                    unitPrice: 100m));

        await test.SalesInvoices.PostDraftAsync(
            test.UserId,
            test.Organisation.Id,
            draft.Id);

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    test.SalesInvoices.PostDraftAsync(
                        test.UserId,
                        test.Organisation.Id,
                        draft.Id));

        Assert.Contains(
            "Only draft invoices",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());
    }

    [Fact]
    public async Task PostDraftAsync_WhenReceivablesControlAccountIsInactive_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var draft =
            await test.SalesInvoices.CreateDraftAsync(
                test.UserId,
                Request(
                    test,
                    description: "Inactive AR draft",
                    quantity: 1m,
                    unitPrice: 100m));

        var receivables =
            test.Account("1100");

        receivables.IsActive = false;

        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    test.SalesInvoices.PostDraftAsync(
                        test.UserId,
                        test.Organisation.Id,
                        draft.Id));

        Assert.Contains(
            "1100",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());

        var stored =
            await test.Db.SalesInvoices
                .AsNoTracking()
                .SingleAsync(x => x.Id == draft.Id);

        Assert.Equal(InvoiceStatus.Draft, stored.Status);
        Assert.StartsWith("DRAFT-", stored.InvoiceNumber);
    }

    private static SalesInvoiceRequest Request(
        AccountingTestDatabase test,
        string description,
        decimal quantity,
        decimal unitPrice)
    {
        return new SalesInvoiceRequest(
            OrganisationId: test.Organisation.Id,
            CustomerId: test.Customer.Id,
            IssueDate: new DateOnly(2026, 8, 20),
            DueDate: new DateOnly(2026, 9, 19),
            Lines:
            [
                new SalesInvoiceLineRequest(
                    Description: description,
                    Quantity: quantity,
                    UnitPrice: unitPrice,
                    VatTreatment: VatTreatment.Standard,
                    RevenueAccountId: test.Account("4000").Id)
            ]);
    }
}