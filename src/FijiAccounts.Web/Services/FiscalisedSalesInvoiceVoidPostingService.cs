using System.Data;
using System.Text.Json;
using FijiAccounts.Domain.Fiscalisation;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed class FiscalisedSalesInvoiceVoidPostingService(
    ApplicationDbContext db,
    TenantAccessService access,
    SalesInvoiceService invoiceService,
    FiscalSalesInvoiceVoidSubmissionFactory submissionFactory,
    FiscalisationWorkflowService workflow,
    FiscalisationOrchestratorService orchestrator)
{
    public async Task<SalesInvoiceVoid> CreateDraftAsync(
        string userId,
        Guid organisationId,
        Guid invoiceId,
        DateOnly voidDate,
        CancellationToken cancellationToken = default)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId))
            throw new UnauthorizedAccessException("You cannot void invoices for this organisation.");
        var invoice = await db.SalesInvoices.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == invoiceId && x.OrganisationId == organisationId, cancellationToken)
            ?? throw new InvalidOperationException("Invoice not found.");
        if (invoice.Status != InvoiceStatus.Posted || invoice.PostedJournalId is null ||
            invoice.AmountPaid > 0 || invoice.AmountCredited > 0)
            throw new InvalidOperationException("Only an unpaid posted invoice can use the fiscal void workflow.");
        if (!await db.FiscalisationRecords.AsNoTracking().AnyAsync(x =>
                x.SalesInvoiceId == invoiceId && x.OrganisationId == organisationId &&
                x.Status == FiscalisationStatus.Accepted, cancellationToken))
            throw new InvalidOperationException("Only an invoice with an accepted fiscal response can use the fiscal void workflow.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (await db.SalesInvoiceVoids.AnyAsync(x => x.SalesInvoiceId == invoiceId, cancellationToken))
            throw new InvalidOperationException("This sales invoice already has a void or pending void.");
        var invoiceVoid = new SalesInvoiceVoid
        {
            OrganisationId = organisationId,
            SalesInvoiceId = invoiceId,
            VoidDate = voidDate,
            Status = SalesInvoiceVoidStatus.Draft,
            CreatedByUserId = userId
        };
        db.SalesInvoiceVoids.Add(invoiceVoid);
        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = organisationId,
            UserId = userId,
            EventType = "SalesInvoiceVoidDraftCreated",
            EntityType = nameof(SalesInvoiceVoid),
            EntityId = invoiceVoid.Id.ToString(),
            JsonData = JsonSerializer.Serialize(new { invoice.InvoiceNumber, invoiceVoid.VoidDate })
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return invoiceVoid;
    }

    public async Task<SalesInvoice> PostAsync(
        string userId,
        Guid organisationId,
        Guid invoiceVoidId,
        CancellationToken cancellationToken = default)
    {
        var configuration = await db.FiscalisationConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganisationId == organisationId, cancellationToken);
        if (configuration?.IsEnabled != true)
            throw new InvalidOperationException("Enable the fiscalisation gate before submitting this invoice void.");
        var invoiceVoid = await db.SalesInvoiceVoids
            .Include(x => x.SalesInvoice).ThenInclude(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == invoiceVoidId && x.OrganisationId == organisationId, cancellationToken)
            ?? throw new InvalidOperationException("Invoice-void draft not found.");
        if (invoiceVoid.Status == SalesInvoiceVoidStatus.Posted)
            return invoiceVoid.SalesInvoice;

        var acceptedInvoice = await db.FiscalisationRecords.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SalesInvoiceId == invoiceVoid.SalesInvoiceId &&
                x.OrganisationId == organisationId && x.Status == FiscalisationStatus.Accepted, cancellationToken)
            ?? throw new InvalidOperationException("The invoice does not have an accepted fiscal response.");
        var record = await db.FiscalisationRecords.SingleOrDefaultAsync(x =>
            x.SalesInvoiceVoidId == invoiceVoid.Id && x.OrganisationId == organisationId, cancellationToken);
        if (record is null)
        {
            var submission = submissionFactory.Create(
                invoiceVoid, invoiceVoid.SalesInvoice, acceptedInvoice,
                FiscalisationConfigurationService.TaxLabels(configuration),
                configuration.DefaultPaymentType, userId);
            record = await workflow.PrepareSalesInvoiceVoidAsync(
                userId, organisationId, invoiceVoid.Id, submission, cancellationToken);
        }
        if (record.Status == FiscalisationStatus.Submitting)
            record = await workflow.MarkRecoveryRequiredAsync(
                userId, organisationId, record.Id, "INTERRUPTED_SUBMISSION",
                "The prior invoice-void submission was interrupted and must be recovered.", cancellationToken);
        if (record.Status == FiscalisationStatus.RecoveryRequired)
            record = await orchestrator.RecoverAsync(userId, organisationId, record.Id, cancellationToken);
        else if (record.Status is FiscalisationStatus.Prepared or FiscalisationStatus.Rejected)
            record = await orchestrator.SubmitAsync(userId, organisationId, record.Id, cancellationToken);
        if (record.Status != FiscalisationStatus.Accepted)
            throw new InvalidOperationException(record.Status == FiscalisationStatus.RecoveryRequired
                ? "The fiscal void response is uncertain. Recover it before posting the reversing journal."
                : record.ErrorMessage ?? "The fiscal invoice void was not accepted.");
        return await invoiceService.PostDraftVoidAsync(userId, organisationId, invoiceVoid.Id, cancellationToken);
    }
}
