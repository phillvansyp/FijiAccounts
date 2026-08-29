using System.Data;
using System.Text.Json;
using FijiAccounts.Domain.Fiscalisation;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed class FiscalisedSalesCreditNoteReversalPostingService(
    ApplicationDbContext db,
    TenantAccessService access,
    SalesCreditNoteService creditNoteService,
    FiscalCreditNoteReversalSubmissionFactory submissionFactory,
    FiscalisationWorkflowService workflow,
    FiscalisationOrchestratorService orchestrator)
{
    public async Task<SalesCreditNoteReversal> CreateDraftAsync(
        string userId,
        Guid organisationId,
        Guid creditNoteId,
        DateOnly reversalDate,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId))
            throw new UnauthorizedAccessException("You cannot reverse credit notes for this organisation.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Enter a reason for reversing the credit note.");
        var credit = await db.SalesCreditNotes.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == creditNoteId && x.OrganisationId == organisationId, cancellationToken)
            ?? throw new InvalidOperationException("Sales credit note not found.");
        if (credit.Status != SalesCreditNoteStatus.Posted || credit.PostedJournalId is null)
            throw new InvalidOperationException("Only a posted sales credit note can be reversed.");
        if (!await db.FiscalisationRecords.AsNoTracking().AnyAsync(x =>
                x.SalesCreditNoteId == creditNoteId &&
                x.OrganisationId == organisationId &&
                x.Status == FiscalisationStatus.Accepted,
                cancellationToken))
            throw new InvalidOperationException("Only a credit note with an accepted fiscal refund can use the fiscal reversal workflow.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (await db.SalesCreditNoteReversals.AnyAsync(x => x.SalesCreditNoteId == creditNoteId, cancellationToken))
            throw new InvalidOperationException("This sales credit note already has a reversal or pending reversal.");
        var reversal = new SalesCreditNoteReversal
        {
            OrganisationId = organisationId,
            SalesCreditNoteId = creditNoteId,
            ReversalDate = reversalDate,
            Reason = reason.Trim(),
            Status = SalesCreditNoteReversalStatus.Draft,
            CreatedByUserId = userId
        };
        db.SalesCreditNoteReversals.Add(reversal);
        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = organisationId,
            UserId = userId,
            EventType = "SalesCreditNoteReversalDraftCreated",
            EntityType = nameof(SalesCreditNoteReversal),
            EntityId = reversal.Id.ToString(),
            JsonData = JsonSerializer.Serialize(new { credit.CreditNoteNumber, reversal.ReversalDate, reversal.Reason })
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return reversal;
    }

    public async Task<SalesCreditNoteReversal> PostAsync(
        string userId,
        Guid organisationId,
        Guid reversalId,
        CancellationToken cancellationToken = default)
    {
        var configuration = await db.FiscalisationConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganisationId == organisationId, cancellationToken);
        if (configuration?.IsEnabled != true)
            throw new InvalidOperationException("Enable the fiscalisation gate before submitting this reversal.");
        var reversal = await db.SalesCreditNoteReversals
            .Include(x => x.SalesCreditNote)
                .ThenInclude(x => x.Lines)
            .Include(x => x.SalesCreditNote)
                .ThenInclude(x => x.SalesInvoice)
            .SingleOrDefaultAsync(x => x.Id == reversalId && x.OrganisationId == organisationId, cancellationToken)
            ?? throw new InvalidOperationException("Sales credit-note reversal not found.");
        if (reversal.Status == SalesCreditNoteReversalStatus.Posted)
            return reversal;

        var acceptedRefund = await db.FiscalisationRecords.AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.SalesCreditNoteId == reversal.SalesCreditNoteId &&
                x.OrganisationId == organisationId &&
                x.Status == FiscalisationStatus.Accepted,
                cancellationToken)
            ?? throw new InvalidOperationException("The credit note does not have an accepted fiscal refund response.");
        var record = await db.FiscalisationRecords.SingleOrDefaultAsync(x =>
            x.SalesCreditNoteReversalId == reversal.Id && x.OrganisationId == organisationId,
            cancellationToken);
        if (record is null ||
            record.Status is FiscalisationStatus.Prepared or FiscalisationStatus.Rejected)
        {
            await FiscalAccountingPeriodGuard.EnsureOpenAsync(
                db, organisationId, reversal.ReversalDate, cancellationToken);
        }
        if (record is null)
        {
            var submission = submissionFactory.Create(
                reversal,
                reversal.SalesCreditNote,
                acceptedRefund,
                FiscalisationConfigurationService.TaxLabels(configuration),
                configuration.DefaultPaymentType,
                userId);
            record = await workflow.PrepareCreditNoteReversalAsync(
                userId, organisationId, reversal.Id, submission, cancellationToken);
        }

        if (record.Status == FiscalisationStatus.Submitting)
            record = await workflow.MarkRecoveryRequiredAsync(
                userId, organisationId, record.Id, "INTERRUPTED_SUBMISSION",
                "The prior reversal submission was interrupted and must be recovered.", cancellationToken);
        if (record.Status == FiscalisationStatus.RecoveryRequired)
            record = await orchestrator.RecoverAsync(userId, organisationId, record.Id, cancellationToken);
        else if (record.Status is FiscalisationStatus.Prepared or FiscalisationStatus.Rejected)
            record = await orchestrator.SubmitAsync(userId, organisationId, record.Id, cancellationToken);
        if (record.Status != FiscalisationStatus.Accepted)
            throw new InvalidOperationException(record.Status == FiscalisationStatus.RecoveryRequired
                ? "The fiscal reversal response is uncertain. Recover it before posting the reversal."
                : record.ErrorMessage ?? "The fiscal reversal was not accepted.");

        await FiscalAccountingPeriodGuard.EnsureOpenAsync(
            db, organisationId, reversal.ReversalDate, cancellationToken);

        return await creditNoteService.PostDraftReversalAsync(
            userId, organisationId, reversal.Id, cancellationToken);
    }
}
