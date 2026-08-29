using System.Text.Json;
using FijiAccounts.Domain.Fiscalisation;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class FiscalisedSalesInvoiceVoidPostingTests
{
    [Fact]
    public async Task AcceptedRefund_PostsVoidOnlyAfterFiscalAcceptance()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var setup = await CreateFiscalInvoiceAsync(test);
        var service = CreateService(test, setup.Workflow, new DevelopmentFiscalisationGateway());
        var draft = await service.CreateDraftAsync(
            test.UserId, test.Organisation.Id, setup.Invoice.Id, new DateOnly(2026, 8, 31));

        Assert.Equal(SalesInvoiceVoidStatus.Draft, draft.Status);
        Assert.Null(draft.PostedJournalId);
        Assert.Equal(InvoiceStatus.Posted, setup.Invoice.Status);
        Assert.Equal(0, await test.Db.PostedJournals.CountAsync(x => x.Reference == $"VOID-{setup.Invoice.InvoiceNumber}"));

        var postedInvoice = await service.PostAsync(test.UserId, test.Organisation.Id, draft.Id);

        var postedVoid = await test.Db.SalesInvoiceVoids.AsNoTracking().SingleAsync(x => x.Id == draft.Id);
        var fiscal = await test.Db.FiscalisationRecords.AsNoTracking().SingleAsync(x => x.SalesInvoiceVoidId == draft.Id);
        Assert.Equal(InvoiceStatus.Voided, postedInvoice.Status);
        Assert.Equal(SalesInvoiceVoidStatus.Posted, postedVoid.Status);
        Assert.NotNull(postedVoid.PostedJournalId);
        Assert.Equal(FiscalisationStatus.Accepted, fiscal.Status);
        using var request = JsonDocument.Parse(fiscal.RequestJson);
        Assert.Equal((int)FiscalTransactionType.Refund, request.RootElement.GetProperty("TransactionType").GetInt32());
        Assert.Equal(setup.AcceptedInvoice.SdcInvoiceNumber,
            request.RootElement.GetProperty("ReferentDocumentNumber").GetString());
    }

    [Fact]
    public async Task UncertainRefund_LeavesInvoicePostedThenRecoversWithoutSecondSubmission()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var setup = await CreateFiscalInvoiceAsync(test);
        var service = CreateService(test, setup.Workflow, new TimeoutThenRecoverGateway());
        var draft = await service.CreateDraftAsync(
            test.UserId, test.Organisation.Id, setup.Invoice.Id, new DateOnly(2026, 8, 31));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PostAsync(test.UserId, test.Organisation.Id, draft.Id));
        var uncertain = await test.Db.FiscalisationRecords.AsNoTracking().SingleAsync(x => x.SalesInvoiceVoidId == draft.Id);
        Assert.Equal(FiscalisationStatus.RecoveryRequired, uncertain.Status);
        Assert.Equal(InvoiceStatus.Posted,
            (await test.Db.SalesInvoices.AsNoTracking().SingleAsync(x => x.Id == setup.Invoice.Id)).Status);
        Assert.Equal(SalesInvoiceVoidStatus.Draft,
            (await test.Db.SalesInvoiceVoids.AsNoTracking().SingleAsync(x => x.Id == draft.Id)).Status);

        var posted = await service.PostAsync(test.UserId, test.Organisation.Id, draft.Id);

        var recovered = await test.Db.FiscalisationRecords.AsNoTracking().SingleAsync(x => x.SalesInvoiceVoidId == draft.Id);
        Assert.Equal(InvoiceStatus.Voided, posted.Status);
        Assert.Equal(FiscalisationStatus.Accepted, recovered.Status);
        Assert.Equal(1, recovered.AttemptCount);
        Assert.Equal("RECOVERED-VOID-1", recovered.SdcInvoiceNumber);
    }

    [Fact]
    public async Task RejectedRefund_LeavesAccountingUntouched()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var setup = await CreateFiscalInvoiceAsync(test);
        var service = CreateService(test, setup.Workflow, new RejectedGateway());
        var draft = await service.CreateDraftAsync(
            test.UserId, test.Organisation.Id, setup.Invoice.Id, new DateOnly(2026, 8, 31));
        var journalCount = await test.Db.PostedJournals.CountAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PostAsync(test.UserId, test.Organisation.Id, draft.Id));

        var rejected = await test.Db.FiscalisationRecords.AsNoTracking().SingleAsync(x => x.SalesInvoiceVoidId == draft.Id);
        Assert.Contains("Void rejected", error.Message);
        Assert.Equal(FiscalisationStatus.Rejected, rejected.Status);
        Assert.Equal(InvoiceStatus.Posted,
            (await test.Db.SalesInvoices.AsNoTracking().SingleAsync(x => x.Id == setup.Invoice.Id)).Status);
        Assert.Equal(SalesInvoiceVoidStatus.Draft,
            (await test.Db.SalesInvoiceVoids.AsNoTracking().SingleAsync(x => x.Id == draft.Id)).Status);
        Assert.Equal(journalCount, await test.Db.PostedJournals.CountAsync());
    }

    private static async Task<FiscalInvoiceSetup> CreateFiscalInvoiceAsync(AccountingTestDatabase test)
    {
        var invoice = await test.SalesInvoices.CreateAndPostAsync(test.UserId, new(
            test.Organisation.Id,
            test.Customer.Id,
            new DateOnly(2026, 8, 28),
            new DateOnly(2026, 9, 27),
            [new("Fiscal void sale", 1m, 100m, VatTreatment.Standard, test.Account("4000").Id)]));
        var workflow = new FiscalisationWorkflowService(test.Db, test.Access);
        var submission = new FiscalisationSubmissionFactory().Create(
            invoice,
            new Dictionary<VatTreatment, IReadOnlyCollection<string>> { [VatTreatment.Standard] = ["VERIFIED-STANDARD"] },
            [new FiscalPayment(invoice.TransactionTotal, FiscalPaymentType.Card)],
            DateTimeOffset.UtcNow,
            test.UserId);
        var record = await workflow.PrepareAsync(test.UserId, test.Organisation.Id, invoice.Id, submission);
        await workflow.BeginAttemptAsync(test.UserId, test.Organisation.Id, record.Id);
        var accepted = await workflow.RecordAcceptedAsync(
            test.UserId, test.Organisation.Id, record.Id, Accepted("SDC-ORIGINAL-VOID"));
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
        return new(invoice, accepted, workflow);
    }

    private static FiscalisedSalesInvoiceVoidPostingService CreateService(
        AccountingTestDatabase test,
        FiscalisationWorkflowService workflow,
        IFiscalisationGateway gateway) => new(
            test.Db,
            test.Access,
            test.SalesInvoices,
            new FiscalSalesInvoiceVoidSubmissionFactory(),
            workflow,
            new FiscalisationOrchestratorService(test.Db, workflow, gateway));

    private static FiscalisationResult Accepted(string number) => new(
        FiscalisationOutcome.Accepted,
        number,
        DateTimeOffset.UtcNow,
        $"https://example.invalid/{number}",
        "qr",
        "signed");

    private sealed record FiscalInvoiceSetup(
        SalesInvoice Invoice,
        FiscalisationRecord AcceptedInvoice,
        FiscalisationWorkflowService Workflow);

    private sealed class TimeoutThenRecoverGateway : IFiscalisationGateway
    {
        public Task<FiscalisationResult> FiscaliseAsync(FiscalInvoiceSubmission submission, CancellationToken cancellationToken = default) =>
            throw new TimeoutException("Simulated lost void response.");
        public Task<FiscalisationResult> RecoverLastResultAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Accepted("RECOVERED-VOID-1"));
    }

    private sealed class RejectedGateway : IFiscalisationGateway
    {
        public Task<FiscalisationResult> FiscaliseAsync(FiscalInvoiceSubmission submission, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FiscalisationResult(
                FiscalisationOutcome.Rejected,
                ErrorCode: "VOID-REJECTED",
                ErrorMessage: "Void rejected by simulator"));
        public Task<FiscalisationResult> RecoverLastResultAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new FiscalisationResult(FiscalisationOutcome.Unknown));
    }
}
