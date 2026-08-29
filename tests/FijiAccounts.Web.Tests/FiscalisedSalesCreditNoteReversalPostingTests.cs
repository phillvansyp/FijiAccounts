using System.Text.Json;
using FijiAccounts.Domain.Fiscalisation;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class FiscalisedSalesCreditNoteReversalPostingTests
{
    [Fact]
    public async Task AcceptedCorrection_PostsReversalOnlyAfterFiscalAcceptance()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var setup = await CreateFiscalCreditAsync(test);
        var service = CreateReversalService(test, setup.Workflow, new DevelopmentFiscalisationGateway());
        var draft = await service.CreateDraftAsync(
            test.UserId, test.Organisation.Id, setup.Credit.Id,
            new DateOnly(2026, 8, 31), "Customer retained credited supply");

        Assert.Equal(SalesCreditNoteReversalStatus.Draft, draft.Status);
        Assert.Null(draft.PostedJournalId);
        Assert.Equal(setup.Credit.Total, setup.Invoice.AmountCredited);
        Assert.Equal(0, await test.Db.PostedJournals.CountAsync(x => x.Reference == $"REV-{setup.Credit.CreditNoteNumber}"));

        var posted = await service.PostAsync(test.UserId, test.Organisation.Id, draft.Id);

        var fiscal = await test.Db.FiscalisationRecords.AsNoTracking()
            .SingleAsync(x => x.SalesCreditNoteReversalId == draft.Id);
        Assert.Equal(SalesCreditNoteReversalStatus.Posted, posted.Status);
        Assert.NotNull(posted.PostedJournalId);
        Assert.Equal(FiscalisationStatus.Accepted, fiscal.Status);
        Assert.Equal(0m, setup.Invoice.AmountCredited);
        using var request = JsonDocument.Parse(fiscal.RequestJson);
        Assert.Equal((int)FiscalTransactionType.Sale, request.RootElement.GetProperty("TransactionType").GetInt32());
        Assert.Equal(setup.AcceptedRefund.SdcInvoiceNumber,
            request.RootElement.GetProperty("ReferentDocumentNumber").GetString());
    }

    [Fact]
    public async Task UncertainCorrection_LeavesDraftThenRecoversWithoutASecondSubmission()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var setup = await CreateFiscalCreditAsync(test);
        var service = CreateReversalService(test, setup.Workflow, new TimeoutThenRecoverGateway());
        var draft = await service.CreateDraftAsync(
            test.UserId, test.Organisation.Id, setup.Credit.Id,
            new DateOnly(2026, 8, 31), "Recover uncertain correction");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PostAsync(test.UserId, test.Organisation.Id, draft.Id));
        var uncertain = await test.Db.FiscalisationRecords.AsNoTracking()
            .SingleAsync(x => x.SalesCreditNoteReversalId == draft.Id);
        Assert.Equal(FiscalisationStatus.RecoveryRequired, uncertain.Status);
        Assert.Equal(SalesCreditNoteReversalStatus.Draft,
            (await test.Db.SalesCreditNoteReversals.AsNoTracking().SingleAsync(x => x.Id == draft.Id)).Status);
        Assert.Equal(setup.Credit.Total, setup.Invoice.AmountCredited);

        var posted = await service.PostAsync(test.UserId, test.Organisation.Id, draft.Id);

        var recovered = await test.Db.FiscalisationRecords.AsNoTracking()
            .SingleAsync(x => x.SalesCreditNoteReversalId == draft.Id);
        Assert.Equal(SalesCreditNoteReversalStatus.Posted, posted.Status);
        Assert.Equal(FiscalisationStatus.Accepted, recovered.Status);
        Assert.Equal(1, recovered.AttemptCount);
        Assert.Equal("RECOVERED-REVERSAL-1", recovered.SdcInvoiceNumber);
    }

    [Fact]
    public async Task RejectedCorrection_LeavesAccountingAndStockUntouched()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var setup = await CreateFiscalCreditAsync(test);
        var service = CreateReversalService(test, setup.Workflow, new RejectedGateway());
        var draft = await service.CreateDraftAsync(
            test.UserId, test.Organisation.Id, setup.Credit.Id,
            new DateOnly(2026, 8, 31), "Rejected correction");
        var journalCount = await test.Db.PostedJournals.CountAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PostAsync(test.UserId, test.Organisation.Id, draft.Id));

        var rejected = await test.Db.FiscalisationRecords.AsNoTracking()
            .SingleAsync(x => x.SalesCreditNoteReversalId == draft.Id);
        Assert.Contains("Correction rejected", error.Message);
        Assert.Equal(FiscalisationStatus.Rejected, rejected.Status);
        Assert.Equal(SalesCreditNoteReversalStatus.Draft,
            (await test.Db.SalesCreditNoteReversals.AsNoTracking().SingleAsync(x => x.Id == draft.Id)).Status);
        Assert.Equal(journalCount, await test.Db.PostedJournals.CountAsync());
        Assert.Equal(setup.Credit.Total, setup.Invoice.AmountCredited);
    }

    private static async Task<FiscalCreditSetup> CreateFiscalCreditAsync(AccountingTestDatabase test)
    {
        var invoice = await test.SalesInvoices.CreateAndPostAsync(test.UserId, new(
            test.Organisation.Id,
            test.Customer.Id,
            new DateOnly(2026, 8, 28),
            new DateOnly(2026, 9, 27),
            [new("Correction workflow sale", 1m, 100m, VatTreatment.Standard, test.Account("4000").Id)]));
        var workflow = new FiscalisationWorkflowService(test.Db, test.Access);
        var invoiceSubmission = new FiscalisationSubmissionFactory().Create(
            invoice,
            new Dictionary<VatTreatment, IReadOnlyCollection<string>> { [VatTreatment.Standard] = ["VERIFIED-STANDARD"] },
            [new FiscalPayment(invoice.TransactionTotal, FiscalPaymentType.Card)],
            DateTimeOffset.UtcNow,
            test.UserId);
        var originalRecord = await workflow.PrepareAsync(
            test.UserId, test.Organisation.Id, invoice.Id, invoiceSubmission);
        await workflow.BeginAttemptAsync(test.UserId, test.Organisation.Id, originalRecord.Id);
        await workflow.RecordAcceptedAsync(test.UserId, test.Organisation.Id, originalRecord.Id, Accepted("SDC-ORIGINAL-REVERSAL"));
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
        await test.Db.SaveChangesAsync();
        var creditService = new FiscalisedSalesCreditNotePostingService(
            test.Db,
            test.Access,
            test.Posting,
            new FiscalCreditNoteSubmissionFactory(),
            workflow,
            new FiscalisationOrchestratorService(test.Db, workflow, new DevelopmentFiscalisationGateway()));
        var draft = await creditService.CreateDraftAsync(test.UserId, new(
            test.Organisation.Id,
            invoice.Id,
            new DateOnly(2026, 8, 30),
            "Original fiscal refund",
            [new(invoice.Lines.Single().Id, invoice.Lines.Single().TransactionGrossAmount)],
            false));
        var credit = await creditService.PostAsync(test.UserId, test.Organisation.Id, draft.Id);
        var acceptedRefund = await test.Db.FiscalisationRecords.AsNoTracking()
            .SingleAsync(x => x.SalesCreditNoteId == credit.Id);
        return new(invoice, credit, acceptedRefund, workflow);
    }

    private static FiscalisedSalesCreditNoteReversalPostingService CreateReversalService(
        AccountingTestDatabase test,
        FiscalisationWorkflowService workflow,
        IFiscalisationGateway gateway) => new(
            test.Db,
            test.Access,
            new SalesCreditNoteService(test.Db, test.Access, test.Posting),
            new FiscalCreditNoteReversalSubmissionFactory(),
            workflow,
            new FiscalisationOrchestratorService(test.Db, workflow, gateway));

    private static FiscalisationResult Accepted(string number) => new(
        FiscalisationOutcome.Accepted,
        number,
        DateTimeOffset.UtcNow,
        $"https://example.invalid/{number}",
        "qr",
        "signed");

    private sealed record FiscalCreditSetup(
        SalesInvoice Invoice,
        SalesCreditNote Credit,
        FiscalisationRecord AcceptedRefund,
        FiscalisationWorkflowService Workflow);

    private sealed class TimeoutThenRecoverGateway : IFiscalisationGateway
    {
        public Task<FiscalisationResult> FiscaliseAsync(FiscalInvoiceSubmission submission, CancellationToken cancellationToken = default) =>
            throw new TimeoutException("Simulated lost correction response.");
        public Task<FiscalisationResult> RecoverLastResultAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Accepted("RECOVERED-REVERSAL-1"));
    }

    private sealed class RejectedGateway : IFiscalisationGateway
    {
        public Task<FiscalisationResult> FiscaliseAsync(FiscalInvoiceSubmission submission, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FiscalisationResult(
                FiscalisationOutcome.Rejected,
                ErrorCode: "CORRECTION-REJECTED",
                ErrorMessage: "Correction rejected by simulator"));
        public Task<FiscalisationResult> RecoverLastResultAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new FiscalisationResult(FiscalisationOutcome.Unknown));
    }
}
