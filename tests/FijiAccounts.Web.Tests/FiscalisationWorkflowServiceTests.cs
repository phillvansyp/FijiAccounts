using FijiAccounts.Domain.Fiscalisation;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class FiscalisationWorkflowServiceTests
{
    [Fact]
    public async Task Prepare_IsIdempotentButRejectsChangedPayload()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var invoice = await AddInvoiceAsync(test, 112.50m);
        var service = new FiscalisationWorkflowService(test.Db, test.Access);
        var submission = Submission(invoice, 112.50m);

        var first = await service.PrepareAsync(
            test.UserId, test.Organisation.Id, invoice.Id, submission);
        var second = await service.PrepareAsync(
            test.UserId, test.Organisation.Id, invoice.Id, submission);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(FiscalisationStatus.Prepared, first.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PrepareAsync(
                test.UserId,
                test.Organisation.Id,
                invoice.Id,
                submission with { CashierId = "another-cashier" }));
    }

    [Fact]
    public async Task UncertainAttemptMustBeRecoveredBeforeRetry()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var invoice = await AddInvoiceAsync(test, 112.50m);
        var service = new FiscalisationWorkflowService(test.Db, test.Access);
        var record = await service.PrepareAsync(
            test.UserId, test.Organisation.Id, invoice.Id, Submission(invoice, 112.50m));
        await service.BeginAttemptAsync(test.UserId, test.Organisation.Id, record.Id);

        await service.MarkRecoveryRequiredAsync(
            test.UserId, test.Organisation.Id, record.Id, "TIMEOUT", "Response unknown");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.BeginAttemptAsync(test.UserId, test.Organisation.Id, record.Id));
        var saved = await test.Db.FiscalisationRecords.AsNoTracking().SingleAsync();
        Assert.Equal(FiscalisationStatus.RecoveryRequired, saved.Status);
        Assert.Equal(1, saved.AttemptCount);
    }

    [Fact]
    public async Task AcceptedSignedResponseIsStoredAndCannotBeReplaced()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var invoice = await AddInvoiceAsync(test, 112.50m);
        var service = new FiscalisationWorkflowService(test.Db, test.Access);
        var record = await service.PrepareAsync(
            test.UserId, test.Organisation.Id, invoice.Id, Submission(invoice, 112.50m));
        await service.BeginAttemptAsync(test.UserId, test.Organisation.Id, record.Id);
        var accepted = new FiscalisationResult(
            FiscalisationOutcome.Accepted,
            "SDC-123",
            DateTimeOffset.UtcNow,
            "https://verify.example/SDC-123",
            "qr-data",
            "signed-payload");

        await service.RecordAcceptedAsync(
            test.UserId, test.Organisation.Id, record.Id, accepted);
        await service.RecordAcceptedAsync(
            test.UserId, test.Organisation.Id, record.Id, accepted);

        var saved = await test.Db.FiscalisationRecords.AsNoTracking().SingleAsync();
        Assert.Equal(FiscalisationStatus.Accepted, saved.Status);
        Assert.Equal("signed-payload", saved.SignedPayload);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecordAcceptedAsync(
                test.UserId,
                test.Organisation.Id,
                record.Id,
                accepted with { SdcInvoiceNumber = "SDC-OTHER" }));
    }

    [Fact]
    public async Task OrchestratorStoresAnAcceptedSimulatorResult()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var invoice = await AddInvoiceAsync(test, 112.50m);
        var workflow = new FiscalisationWorkflowService(test.Db, test.Access);
        var record = await workflow.PrepareAsync(
            test.UserId, test.Organisation.Id, invoice.Id, Submission(invoice, 112.50m));
        var orchestrator = new FiscalisationOrchestratorService(
            test.Db, workflow, new DevelopmentFiscalisationGateway());

        var result = await orchestrator.SubmitAsync(
            test.UserId, test.Organisation.Id, record.Id);

        Assert.Equal(FiscalisationStatus.Accepted, result.Status);
        Assert.StartsWith("SIMULATED-", result.SdcInvoiceNumber);
        Assert.Contains("\"Simulated\":true", result.SignedPayload);
        Assert.Equal(1, result.AttemptCount);
    }

    [Fact]
    public async Task OrchestratorRecoversAfterAnUncertainTransportFailure()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var invoice = await AddInvoiceAsync(test, 112.50m);
        var workflow = new FiscalisationWorkflowService(test.Db, test.Access);
        var record = await workflow.PrepareAsync(
            test.UserId, test.Organisation.Id, invoice.Id, Submission(invoice, 112.50m));
        var orchestrator = new FiscalisationOrchestratorService(
            test.Db, workflow, new TimeoutThenRecoverGateway());

        var uncertain = await orchestrator.SubmitAsync(
            test.UserId, test.Organisation.Id, record.Id);
        var uncertainStatus = uncertain.Status;
        var recovered = await orchestrator.RecoverAsync(
            test.UserId, test.Organisation.Id, record.Id);

        Assert.Equal(FiscalisationStatus.RecoveryRequired, uncertainStatus);
        Assert.Equal(FiscalisationStatus.Accepted, recovered.Status);
        Assert.Equal("RECOVERED-1", recovered.SdcInvoiceNumber);
        Assert.Equal(1, recovered.AttemptCount);
    }

    [Fact]
    public async Task EnabledSimulatorGateFiscalisesBeforeLedgerPosting()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
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
        var draft = await test.SalesInvoices.CreateDraftAsync(
            test.UserId,
            new SalesInvoiceRequest(
                test.Organisation.Id,
                test.Customer.Id,
                new DateOnly(2026, 8, 29),
                new DateOnly(2026, 9, 28),
                [new SalesInvoiceLineRequest(
                    "Fiscal service",
                    1m,
                    100m,
                    FijiAccounts.Domain.Tax.VatTreatment.Standard,
                    test.Account("4000").Id)]));
        var workflow = new FiscalisationWorkflowService(test.Db, test.Access);
        var orchestrator = new FiscalisationOrchestratorService(
            test.Db, workflow, new DevelopmentFiscalisationGateway());
        var posting = new FiscalisedSalesInvoicePostingService(
            test.Db,
            test.SalesInvoices,
            new FiscalisationSubmissionFactory(),
            workflow,
            orchestrator);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            test.SalesInvoices.PostDraftAsync(
                test.UserId, test.Organisation.Id, draft.Id));
        var posted = await posting.PostAsync(
            test.UserId, test.Organisation.Id, draft.Id);

        Assert.Equal(InvoiceStatus.Posted, posted.Status);
        Assert.StartsWith("INV-", posted.InvoiceNumber);
        var fiscalRecord = await test.Db.FiscalisationRecords.AsNoTracking()
            .SingleAsync(x => x.SalesInvoiceId == draft.Id);
        Assert.Equal(FiscalisationStatus.Accepted, fiscalRecord.Status);
        Assert.StartsWith("SIMULATED-", fiscalRecord.SdcInvoiceNumber);
        Assert.Equal(1, fiscalRecord.AttemptCount);
    }

    [Fact]
    public async Task CreditRefundIsDurableIdempotentAndRecoverableThroughTheOrchestrator()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var draft = await test.SalesInvoices.CreateDraftAsync(
            test.UserId,
            new SalesInvoiceRequest(
                test.Organisation.Id,
                test.Customer.Id,
                new DateOnly(2026, 8, 29),
                new DateOnly(2026, 9, 28),
                [new SalesInvoiceLineRequest(
                    "Refundable service",
                    1m,
                    100m,
                    FijiAccounts.Domain.Tax.VatTreatment.Standard,
                    test.Account("4000").Id)]));
        var invoice = await test.SalesInvoices.PostDraftAsync(
            test.UserId, test.Organisation.Id, draft.Id);
        var workflow = new FiscalisationWorkflowService(test.Db, test.Access);
        var labels = new Dictionary<FijiAccounts.Domain.Tax.VatTreatment, IReadOnlyCollection<string>>
        {
            [FijiAccounts.Domain.Tax.VatTreatment.Standard] = ["VERIFIED-STANDARD"]
        };
        var invoiceSubmission = new FiscalisationSubmissionFactory().Create(
            invoice,
            labels,
            [new FiscalPayment(invoice.TransactionTotal, FiscalPaymentType.Card)],
            DateTimeOffset.UtcNow,
            test.UserId);
        var originalRecord = await workflow.PrepareAsync(
            test.UserId, test.Organisation.Id, invoice.Id, invoiceSubmission);
        await workflow.BeginAttemptAsync(
            test.UserId, test.Organisation.Id, originalRecord.Id);
        originalRecord = await workflow.RecordAcceptedAsync(
            test.UserId,
            test.Organisation.Id,
            originalRecord.Id,
            new FiscalisationResult(
                FiscalisationOutcome.Accepted,
                "SDC-ORIGINAL",
                DateTimeOffset.UtcNow,
                "https://verify.example/original",
                "qr",
                "signed"));
        var creditService = new SalesCreditNoteService(
            test.Db, test.Access, test.Posting);
        var credit = await creditService.CreateAsync(
            test.UserId,
            new SalesCreditNoteRequest(
                test.Organisation.Id,
                invoice.Id,
                new DateOnly(2026, 8, 30),
                "Price adjustment",
                56.25m,
                false));
        var refundSubmission = new FiscalCreditNoteSubmissionFactory().Create(
            credit,
            invoice,
            originalRecord,
            labels,
            [new FiscalPayment(credit.Total, FiscalPaymentType.Card)],
            DateTimeOffset.UtcNow,
            test.UserId);

        var first = await workflow.PrepareCreditNoteAsync(
            test.UserId, test.Organisation.Id, credit.Id, refundSubmission);
        var second = await workflow.PrepareCreditNoteAsync(
            test.UserId, test.Organisation.Id, credit.Id, refundSubmission);
        var accepted = await new FiscalisationOrchestratorService(
                test.Db, workflow, new DevelopmentFiscalisationGateway())
            .SubmitAsync(test.UserId, test.Organisation.Id, first.Id);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(FiscalSourceDocumentKind.SalesCreditNote, accepted.SourceDocumentKind);
        Assert.Equal(credit.Id, accepted.SalesCreditNoteId);
        Assert.Null(accepted.SalesInvoiceId);
        Assert.Equal(FiscalisationStatus.Accepted, accepted.Status);
        Assert.StartsWith("SIMULATED-", accepted.SdcInvoiceNumber);
    }

    private static async Task<SalesInvoice> AddInvoiceAsync(
        AccountingTestDatabase test,
        decimal transactionTotal)
    {
        var invoice = new SalesInvoice
        {
            OrganisationId = test.Organisation.Id,
            CustomerId = test.Customer.Id,
            SequenceNumber = 1,
            InvoiceNumber = "DRAFT-000001",
            IssueDate = new DateOnly(2026, 8, 29),
            DueDate = new DateOnly(2026, 9, 28),
            Currency = "FJD",
            TransactionSubtotal = 100m,
            TransactionVatTotal = 12.50m,
            TransactionTotal = transactionTotal,
            Subtotal = 100m,
            VatTotal = 12.50m,
            Total = 112.50m,
            Status = InvoiceStatus.Draft,
            CreatedByUserId = test.UserId
        };
        test.Db.SalesInvoices.Add(invoice);
        await test.Db.SaveChangesAsync();
        return invoice;
    }

    private static FiscalInvoiceSubmission Submission(
        SalesInvoice invoice,
        decimal total) =>
        new(
            invoice.Id,
            invoice.InvoiceNumber,
            DateTimeOffset.UtcNow,
            invoice.Currency,
            FiscalInvoiceType.Normal,
            FiscalTransactionType.Sale,
            [new("Service", 1m, total, total, ["FJ-STANDARD"])],
            [new(total, FiscalPaymentType.Other)]);

    private sealed class TimeoutThenRecoverGateway : IFiscalisationGateway
    {
        public Task<FiscalisationResult> FiscaliseAsync(
            FiscalInvoiceSubmission submission,
            CancellationToken cancellationToken = default) =>
            throw new TimeoutException("Simulated lost response.");

        public Task<FiscalisationResult> RecoverLastResultAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(new FiscalisationResult(
                FiscalisationOutcome.Accepted,
                "RECOVERED-1",
                DateTimeOffset.UtcNow,
                "https://example.invalid/recovered",
                "recovered-qr",
                "recovered-signed-payload"));
    }
}
