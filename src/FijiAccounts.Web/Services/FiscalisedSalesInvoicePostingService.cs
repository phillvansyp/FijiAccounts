using FijiAccounts.Domain.Fiscalisation;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed class FiscalisedSalesInvoicePostingService(
    ApplicationDbContext db,
    SalesInvoiceService invoices,
    FiscalisationSubmissionFactory submissionFactory,
    FiscalisationWorkflowService workflow,
    FiscalisationOrchestratorService orchestrator)
{
    public async Task<SalesInvoice> PostAsync(
        string userId,
        Guid organisationId,
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        var configuration = await db.FiscalisationConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.OrganisationId == organisationId,
                cancellationToken);
        if (configuration?.IsEnabled != true)
        {
            return await invoices.PostDraftAsync(
                userId, organisationId, invoiceId, cancellationToken);
        }

        var invoiceDate = await db.SalesInvoices.AsNoTracking()
            .Where(x => x.Id == invoiceId && x.OrganisationId == organisationId)
            .Select(x => (DateOnly?)x.IssueDate)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Invoice not found.");
        var record = await db.FiscalisationRecords.SingleOrDefaultAsync(
            x => x.SalesInvoiceId == invoiceId && x.OrganisationId == organisationId,
            cancellationToken);
        if (record is null ||
            record.Status is FiscalisationStatus.Prepared or FiscalisationStatus.Rejected)
        {
            await FiscalAccountingPeriodGuard.EnsureOpenAsync(
                db, organisationId, invoiceDate, cancellationToken);
        }

        await invoices.ReserveFinalNumberAsync(
            userId, organisationId, invoiceId, cancellationToken);
        var invoice = await db.SalesInvoices.AsNoTracking()
            .Include(x => x.Lines)
            .SingleAsync(
                x => x.Id == invoiceId && x.OrganisationId == organisationId,
                cancellationToken);
        if (record is null)
        {
            var submission = submissionFactory.Create(
                invoice,
                FiscalisationConfigurationService.TaxLabels(configuration),
                [new FiscalPayment(invoice.TransactionTotal, configuration.DefaultPaymentType)],
                DateTimeOffset.UtcNow,
                userId);
            record = await workflow.PrepareAsync(
                userId, organisationId, invoiceId, submission, cancellationToken);
        }

        if (record.Status == FiscalisationStatus.Submitting)
        {
            record = await workflow.MarkRecoveryRequiredAsync(
                userId,
                organisationId,
                record.Id,
                "INTERRUPTED_SUBMISSION",
                "The prior submission was interrupted and must be recovered.",
                cancellationToken);
        }
        if (record.Status == FiscalisationStatus.RecoveryRequired)
        {
            record = await orchestrator.RecoverAsync(
                userId, organisationId, record.Id, cancellationToken);
        }
        else if (record.Status is FiscalisationStatus.Prepared or FiscalisationStatus.Rejected)
        {
            record = await orchestrator.SubmitAsync(
                userId, organisationId, record.Id, cancellationToken);
        }

        if (record.Status != FiscalisationStatus.Accepted)
        {
            throw new InvalidOperationException(record.Status == FiscalisationStatus.RecoveryRequired
                ? "The fiscal response is uncertain. Recover it before posting the invoice."
                : record.ErrorMessage ?? "The fiscal submission was not accepted.");
        }

        await FiscalAccountingPeriodGuard.EnsureOpenAsync(
            db, organisationId, invoice.IssueDate, cancellationToken);

        return await invoices.PostAcceptedFiscalDraftAsync(
            userId, organisationId, invoiceId, cancellationToken);
    }
}
