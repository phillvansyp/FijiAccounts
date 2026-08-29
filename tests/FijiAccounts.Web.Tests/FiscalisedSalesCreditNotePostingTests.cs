using System.Text.Json;
using FijiAccounts.Domain.Fiscalisation;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class FiscalisedSalesCreditNotePostingTests
{
    [Fact]
    public async Task MixedVatDraft_IsFiscalisedBeforeItsJournalIsPosted()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var invoice = await CreateMixedInvoiceAsync(test);
        var workflow = new FiscalisationWorkflowService(test.Db, test.Access);
        await AcceptOriginalAsync(test, invoice, workflow);
        EnableFiscalisation(test);
        await test.Db.SaveChangesAsync();
        var service = CreateService(test, workflow);

        var draft = await service.CreateDraftAsync(test.UserId, new(
            test.Organisation.Id,
            invoice.Id,
            new DateOnly(2026, 8, 30),
            "Mixed VAT refund",
            [
                new(invoice.Lines[0].Id, invoice.Lines[0].TransactionGrossAmount),
                new(invoice.Lines[1].Id, invoice.Lines[1].TransactionGrossAmount)
            ],
            false));

        Assert.Equal(SalesCreditNoteStatus.Draft, draft.Status);
        Assert.Null(draft.PostedJournalId);
        Assert.Equal(2, draft.Lines.Count);
        Assert.Equal(0m, invoice.AmountCredited);
        Assert.Equal(0, await test.Db.PostedJournals.CountAsync(x => x.Reference == draft.CreditNoteNumber));

        var posted = await service.PostAsync(test.UserId, test.Organisation.Id, draft.Id);

        Assert.Equal(SalesCreditNoteStatus.Posted, posted.Status);
        Assert.NotNull(posted.PostedJournalId);
        Assert.Equal(draft.Total, invoice.AmountCredited);
        var fiscal = await test.Db.FiscalisationRecords.AsNoTracking()
            .SingleAsync(x => x.SalesCreditNoteId == draft.Id);
        Assert.Equal(FiscalisationStatus.Accepted, fiscal.Status);
        using var request = JsonDocument.Parse(fiscal.RequestJson);
        Assert.Equal(2, request.RootElement.GetProperty("Items").GetArrayLength());
    }

    [Fact]
    public async Task MissingOriginalFiscalResponse_LeavesRecoverableDraftUnposted()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var invoice = await CreateMixedInvoiceAsync(test);
        EnableFiscalisation(test);
        await test.Db.SaveChangesAsync();
        var workflow = new FiscalisationWorkflowService(test.Db, test.Access);
        var service = CreateService(test, workflow);
        var draft = await service.CreateDraftAsync(test.UserId, new(
            test.Organisation.Id,
            invoice.Id,
            new DateOnly(2026, 8, 30),
            "Await original response",
            [new(invoice.Lines[0].Id, 56.25m)],
            false));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PostAsync(test.UserId, test.Organisation.Id, draft.Id));

        Assert.Contains("original invoice", error.Message, StringComparison.OrdinalIgnoreCase);
        var saved = await test.Db.SalesCreditNotes.AsNoTracking().SingleAsync(x => x.Id == draft.Id);
        Assert.Equal(SalesCreditNoteStatus.Draft, saved.Status);
        Assert.Null(saved.PostedJournalId);
        Assert.Equal(0m, invoice.AmountCredited);
    }

    [Fact]
    public async Task AcceptedTrackedItemCredit_RestoresStockAndPostsCostReversalAtomically()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var catalog = new ProductCatalogService(test.Db, test.Access);
        var inventory = new InventoryService(test.Db, test.Access, test.Posting);
        var item = await catalog.CreateAsync(test.UserId, new(
            test.Organisation.Id,
            "FISCAL-RETURN-001",
            "Fiscal return item",
            null,
            ProductKind.TrackedItem,
            100m,
            20m,
            VatTreatment.Standard,
            VatTreatment.Standard,
            test.Account("4000").Id,
            test.Account("5000").Id));
        await inventory.AdjustAsync(test.UserId, new(
            test.Organisation.Id,
            item.Id,
            new DateOnly(2026, 8, 27),
            10m,
            20m,
            0m,
            test.Account("1200").Id,
            test.Account("5000").Id,
            "FISCAL-RETURN-OPEN",
            "Opening stock"));
        var invoice = await test.SalesInvoices.CreateAndPostAsync(test.UserId, new(
            test.Organisation.Id,
            test.Customer.Id,
            new DateOnly(2026, 8, 28),
            new DateOnly(2026, 9, 27),
            [new("Tracked sale", 2m, 100m, VatTreatment.Standard, test.Account("4000").Id, ProductItemId: item.Id)]));
        var workflow = new FiscalisationWorkflowService(test.Db, test.Access);
        await AcceptOriginalAsync(test, invoice, workflow);
        EnableFiscalisation(test);
        await test.Db.SaveChangesAsync();
        var service = CreateService(test, workflow);
        var draft = await service.CreateDraftAsync(test.UserId, new(
            test.Organisation.Id,
            invoice.Id,
            new DateOnly(2026, 8, 30),
            "Return one item",
            [new(invoice.Lines.Single().Id, invoice.Lines.Single().TransactionGrossAmount / 2m)],
            true));

        var posted = await service.PostAsync(test.UserId, test.Organisation.Id, draft.Id);

        var reloadedItem = await test.Db.ProductItems.AsNoTracking().SingleAsync(x => x.Id == item.Id);
        var stockReturn = await test.Db.InventoryMovements.AsNoTracking().SingleAsync(x =>
            x.Reference == draft.CreditNoteNumber && x.Type == InventoryMovementType.SalesReturn);
        var journal = await test.Db.PostedJournals.AsNoTracking()
            .Include(x => x.Lines)
            .SingleAsync(x => x.Id == posted.PostedJournalId);
        Assert.Equal(9m, reloadedItem.QuantityOnHand);
        Assert.Equal(1m, stockReturn.QuantityChange);
        Assert.Equal(20m, stockReturn.ValueChange);
        Assert.Contains(journal.Lines, x => x.LedgerAccountId == test.Account("1200").Id && x.Debit == 20m);
        Assert.Contains(journal.Lines, x => x.LedgerAccountId == test.Account("5000").Id && x.Credit == 20m);
    }

    [Fact]
    public async Task UncertainRefundResponse_LeavesDraftUnpostedThenRecoversWithoutResubmission()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var invoice = await CreateMixedInvoiceAsync(test);
        var workflow = new FiscalisationWorkflowService(test.Db, test.Access);
        await AcceptOriginalAsync(test, invoice, workflow);
        EnableFiscalisation(test);
        await test.Db.SaveChangesAsync();
        var service = CreateService(test, workflow, new TimeoutThenRecoverGateway());
        var draft = await service.CreateDraftAsync(test.UserId, new(
            test.Organisation.Id,
            invoice.Id,
            new DateOnly(2026, 8, 30),
            "Recover uncertain refund",
            [new(invoice.Lines[0].Id, 56.25m)],
            false));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PostAsync(test.UserId, test.Organisation.Id, draft.Id));
        var uncertain = await test.Db.FiscalisationRecords.AsNoTracking()
            .SingleAsync(x => x.SalesCreditNoteId == draft.Id);
        Assert.Equal(FiscalisationStatus.RecoveryRequired, uncertain.Status);
        Assert.Equal(0, await test.Db.PostedJournals.CountAsync(x => x.Reference == draft.CreditNoteNumber));

        var posted = await service.PostAsync(test.UserId, test.Organisation.Id, draft.Id);

        var recovered = await test.Db.FiscalisationRecords.AsNoTracking()
            .SingleAsync(x => x.SalesCreditNoteId == draft.Id);
        Assert.Equal(SalesCreditNoteStatus.Posted, posted.Status);
        Assert.Equal(FiscalisationStatus.Accepted, recovered.Status);
        Assert.Equal(1, recovered.AttemptCount);
        Assert.Equal("RECOVERED-CREDIT-1", recovered.SdcInvoiceNumber);
    }

    [Fact]
    public async Task RejectedRefundResponse_RemainsAnUnpostedRetryableDraft()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var invoice = await CreateMixedInvoiceAsync(test);
        var workflow = new FiscalisationWorkflowService(test.Db, test.Access);
        await AcceptOriginalAsync(test, invoice, workflow);
        EnableFiscalisation(test);
        await test.Db.SaveChangesAsync();
        var service = CreateService(test, workflow, new RejectedGateway());
        var draft = await service.CreateDraftAsync(test.UserId, new(
            test.Organisation.Id,
            invoice.Id,
            new DateOnly(2026, 8, 30),
            "Rejected refund",
            [new(invoice.Lines[0].Id, 56.25m)],
            false));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PostAsync(test.UserId, test.Organisation.Id, draft.Id));

        var rejected = await test.Db.FiscalisationRecords.AsNoTracking()
            .SingleAsync(x => x.SalesCreditNoteId == draft.Id);
        Assert.Contains("Rejected by simulator", error.Message);
        Assert.Equal(FiscalisationStatus.Rejected, rejected.Status);
        Assert.Equal("SIM-REJECTED", rejected.ErrorCode);
        Assert.Equal(0, await test.Db.PostedJournals.CountAsync(x => x.Reference == draft.CreditNoteNumber));
        Assert.Equal(SalesCreditNoteStatus.Draft,
            (await test.Db.SalesCreditNotes.AsNoTracking().SingleAsync(x => x.Id == draft.Id)).Status);
    }

    [Fact]
    public async Task LockedPeriod_PreventsCreditNoteGatewaySubmission()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var invoice = await CreateMixedInvoiceAsync(test);
        var workflow = new FiscalisationWorkflowService(test.Db, test.Access);
        await AcceptOriginalAsync(test, invoice, workflow);
        EnableFiscalisation(test);
        await test.Db.SaveChangesAsync();
        var gateway = new CountingFiscalisationGateway();
        var service = CreateService(test, workflow, gateway);
        var draft = await service.CreateDraftAsync(test.UserId, new(
            test.Organisation.Id,
            invoice.Id,
            new DateOnly(2026, 8, 30),
            "Locked-period refund",
            [new(invoice.Lines[0].Id, 56.25m)],
            false));
        test.Db.AccountingPeriods.Add(new AccountingPeriod
        {
            OrganisationId = test.Organisation.Id,
            Name = "August 2026",
            StartsOn = new DateOnly(2026, 8, 1),
            EndsOn = new DateOnly(2026, 8, 31),
            IsLocked = true
        });
        await test.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PostAsync(test.UserId, test.Organisation.Id, draft.Id));

        Assert.Contains("accounting period is locked", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, gateway.SubmissionCount);
        Assert.Equal(0, gateway.RecoveryCount);
        Assert.False(await test.Db.FiscalisationRecords.AnyAsync(x => x.SalesCreditNoteId == draft.Id));
    }

    private static FiscalisedSalesCreditNotePostingService CreateService(
        AccountingTestDatabase test,
        FiscalisationWorkflowService workflow,
        IFiscalisationGateway? gateway = null) => new(
            test.Db,
            test.Access,
            test.Posting,
            new FiscalCreditNoteSubmissionFactory(),
            workflow,
            new FiscalisationOrchestratorService(test.Db, workflow, gateway ?? new DevelopmentFiscalisationGateway()));

    private static void EnableFiscalisation(AccountingTestDatabase test) =>
        test.Db.FiscalisationConfigurations.Add(new FiscalisationConfiguration
        {
            OrganisationId = test.Organisation.Id,
            IsEnabled = true,
            DefaultPaymentType = FiscalPaymentType.Card,
            StandardTaxLabel = "VERIFIED-STANDARD",
            ZeroRatedTaxLabel = "VERIFIED-ZERO",
            ExemptTaxLabel = "VERIFIED-EXEMPT",
            OutOfScopeTaxLabel = "VERIFIED-OUT",
            UpdatedByUserId = test.UserId
        });

    private static async Task AcceptOriginalAsync(
        AccountingTestDatabase test,
        SalesInvoice invoice,
        FiscalisationWorkflowService workflow)
    {
        var labels = new Dictionary<VatTreatment, IReadOnlyCollection<string>>
        {
            [VatTreatment.Standard] = ["VERIFIED-STANDARD"],
            [VatTreatment.ZeroRated] = ["VERIFIED-ZERO"]
        };
        var submission = new FiscalisationSubmissionFactory().Create(
            invoice,
            labels,
            [new FiscalPayment(invoice.TransactionTotal, FiscalPaymentType.Card)],
            DateTimeOffset.UtcNow,
            test.UserId);
        var record = await workflow.PrepareAsync(test.UserId, test.Organisation.Id, invoice.Id, submission);
        await workflow.BeginAttemptAsync(test.UserId, test.Organisation.Id, record.Id);
        await workflow.RecordAcceptedAsync(test.UserId, test.Organisation.Id, record.Id, new(
            FiscalisationOutcome.Accepted,
            "SDC-ORIGINAL-MIXED",
            DateTimeOffset.UtcNow,
            "https://verify.example/original",
            "qr",
            "signed"));
    }

    private static Task<SalesInvoice> CreateMixedInvoiceAsync(AccountingTestDatabase test) =>
        test.SalesInvoices.CreateAndPostAsync(test.UserId, new(
            test.Organisation.Id,
            test.Customer.Id,
            new DateOnly(2026, 8, 29),
            new DateOnly(2026, 9, 28),
            [
                new("Standard service", 1m, 100m, VatTreatment.Standard, test.Account("4000").Id),
                new("Zero-rated service", 1m, 40m, VatTreatment.ZeroRated, test.Account("4000").Id)
            ]));

    private sealed class TimeoutThenRecoverGateway : IFiscalisationGateway
    {
        public Task<FiscalisationResult> FiscaliseAsync(
            FiscalInvoiceSubmission submission,
            CancellationToken cancellationToken = default) =>
            throw new TimeoutException("Simulated lost response.");

        public Task<FiscalisationResult> RecoverLastResultAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(new FiscalisationResult(
                FiscalisationOutcome.Accepted,
                "RECOVERED-CREDIT-1",
                DateTimeOffset.UtcNow,
                "https://example.invalid/recovered-credit",
                "recovered-credit-qr",
                "recovered-credit-payload"));
    }

    private sealed class RejectedGateway : IFiscalisationGateway
    {
        public Task<FiscalisationResult> FiscaliseAsync(
            FiscalInvoiceSubmission submission,
            CancellationToken cancellationToken = default) => Task.FromResult(new FiscalisationResult(
                FiscalisationOutcome.Rejected,
                ErrorCode: "SIM-REJECTED",
                ErrorMessage: "Rejected by simulator"));

        public Task<FiscalisationResult> RecoverLastResultAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(new FiscalisationResult(
                FiscalisationOutcome.Unknown));
    }
}
