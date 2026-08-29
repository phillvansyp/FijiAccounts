using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FijiAccounts.Domain.Fiscalisation;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed class FiscalisationWorkflowService(
    ApplicationDbContext db,
    TenantAccessService access)
{
    public async Task<FiscalisationRecord> PrepareAsync(
        string userId,
        Guid organisationId,
        Guid salesInvoiceId,
        FiscalInvoiceSubmission submission,
        CancellationToken cancellationToken = default)
    {
        await RequirePostingAccessAsync(userId, organisationId);
        FiscalInvoiceSubmissionValidator.Validate(submission);

        if (submission.SourceDocumentId != salesInvoiceId)
        {
            throw new InvalidOperationException(
                "The fiscal submission must identify the selected sales invoice.");
        }

        var invoice = await db.SalesInvoices
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.Id == salesInvoiceId &&
                x.OrganisationId == organisationId,
                cancellationToken)
            ?? throw new InvalidOperationException("Sales invoice not found.");
        var submissionTotal = submission.Items.Sum(x => x.TotalAmount);
        if (submission.Currency != invoice.Currency ||
            submissionTotal != invoice.TransactionTotal)
        {
            throw new InvalidOperationException(
                "The fiscal submission currency and total must match the sales invoice transaction values.");
        }

        var requestJson = JsonSerializer.Serialize(submission);
        var requestHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(requestJson)));
        var existing = await db.FiscalisationRecords.SingleOrDefaultAsync(
            x => x.SalesInvoiceId == salesInvoiceId,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.RequestHash != requestHash)
            {
                throw new InvalidOperationException(
                    "A different fiscal submission is already recorded for this invoice.");
            }

            return existing;
        }

        var record = new FiscalisationRecord
        {
            OrganisationId = organisationId,
            SourceDocumentKind = FiscalSourceDocumentKind.SalesInvoice,
            SalesInvoiceId = salesInvoiceId,
            Status = FiscalisationStatus.Prepared,
            RequestHash = requestHash,
            RequestJson = requestJson,
            CreatedByUserId = userId
        };
        db.FiscalisationRecords.Add(record);
        Audit(record, userId, "FiscalisationPrepared");
        await db.SaveChangesAsync(cancellationToken);
        return record;
    }

    public async Task<FiscalisationRecord> PrepareCreditNoteAsync(
        string userId,
        Guid organisationId,
        Guid salesCreditNoteId,
        FiscalInvoiceSubmission submission,
        CancellationToken cancellationToken = default)
    {
        await RequirePostingAccessAsync(userId, organisationId);
        FiscalInvoiceSubmissionValidator.Validate(submission);
        if (submission.SourceDocumentId != salesCreditNoteId ||
            submission.TransactionType != FiscalTransactionType.Refund)
        {
            throw new InvalidOperationException(
                "The fiscal refund must identify the selected sales credit note.");
        }

        var credit = await db.SalesCreditNotes.AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.Id == salesCreditNoteId && x.OrganisationId == organisationId,
                cancellationToken)
            ?? throw new InvalidOperationException("Sales credit note not found.");
        if (submission.Currency != credit.Currency ||
            submission.Items.Sum(x => x.TotalAmount) != credit.Total)
        {
            throw new InvalidOperationException(
                "The fiscal refund currency and total must match the sales credit note.");
        }

        var original = await db.FiscalisationRecords.AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.SalesInvoiceId == credit.SalesInvoiceId &&
                x.OrganisationId == organisationId &&
                x.Status == FiscalisationStatus.Accepted,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "The original invoice does not have an accepted fiscal response.");
        if (submission.ReferentDocumentNumber != original.SdcInvoiceNumber ||
            submission.ReferentDocumentIssuedAt != original.SdcIssuedAt)
        {
            throw new InvalidOperationException(
                "The fiscal refund must reference the original accepted SDC invoice.");
        }

        var requestJson = JsonSerializer.Serialize(submission);
        var requestHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(requestJson)));
        var existing = await db.FiscalisationRecords.SingleOrDefaultAsync(
            x => x.SalesCreditNoteId == salesCreditNoteId,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.RequestHash != requestHash)
            {
                throw new InvalidOperationException(
                    "A different fiscal refund is already recorded for this credit note.");
            }
            return existing;
        }

        var record = new FiscalisationRecord
        {
            OrganisationId = organisationId,
            SourceDocumentKind = FiscalSourceDocumentKind.SalesCreditNote,
            SalesCreditNoteId = salesCreditNoteId,
            Status = FiscalisationStatus.Prepared,
            RequestHash = requestHash,
            RequestJson = requestJson,
            CreatedByUserId = userId
        };
        db.FiscalisationRecords.Add(record);
        Audit(record, userId, "FiscalRefundPrepared");
        await db.SaveChangesAsync(cancellationToken);
        return record;
    }

    public async Task<FiscalisationRecord> BeginAttemptAsync(
        string userId,
        Guid organisationId,
        Guid recordId,
        CancellationToken cancellationToken = default)
    {
        var record = await RequireRecordAsync(userId, organisationId, recordId, cancellationToken);
        if (record.Status is not FiscalisationStatus.Prepared and
            not FiscalisationStatus.Rejected)
        {
            throw new InvalidOperationException(
                "Only prepared or rejected fiscal submissions can start an attempt.");
        }

        record.Status = FiscalisationStatus.Submitting;
        record.AttemptCount++;
        record.LastAttemptAt = DateTimeOffset.UtcNow;
        record.ErrorCode = null;
        record.ErrorMessage = null;
        Audit(record, userId, "FiscalisationAttemptStarted");
        await db.SaveChangesAsync(cancellationToken);
        return record;
    }

    public async Task<FiscalisationRecord> PrepareCreditNoteReversalAsync(
        string userId,
        Guid organisationId,
        Guid reversalId,
        FiscalInvoiceSubmission submission,
        CancellationToken cancellationToken = default)
    {
        await RequirePostingAccessAsync(userId, organisationId);
        FiscalInvoiceSubmissionValidator.Validate(submission);
        if (submission.SourceDocumentId != reversalId || submission.TransactionType != FiscalTransactionType.Sale)
            throw new InvalidOperationException("The fiscal correction must identify the selected credit-note reversal.");
        var reversal = await db.SalesCreditNoteReversals.AsNoTracking()
            .Include(x => x.SalesCreditNote)
            .SingleOrDefaultAsync(x => x.Id == reversalId && x.OrganisationId == organisationId, cancellationToken)
            ?? throw new InvalidOperationException("Sales credit-note reversal not found.");
        if (reversal.Status != SalesCreditNoteReversalStatus.Draft ||
            submission.Currency != reversal.SalesCreditNote.Currency ||
            submission.Items.Sum(x => x.TotalAmount) != reversal.SalesCreditNote.Total)
            throw new InvalidOperationException("The fiscal correction must match the pending credit-note reversal.");
        var acceptedRefund = await db.FiscalisationRecords.AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.SalesCreditNoteId == reversal.SalesCreditNoteId &&
                x.OrganisationId == organisationId &&
                x.Status == FiscalisationStatus.Accepted,
                cancellationToken)
            ?? throw new InvalidOperationException("The credit note does not have an accepted fiscal refund response.");
        if (submission.ReferentDocumentNumber != acceptedRefund.SdcInvoiceNumber ||
            submission.ReferentDocumentIssuedAt != acceptedRefund.SdcIssuedAt)
            throw new InvalidOperationException("The fiscal correction must reference the accepted SDC refund.");

        var requestJson = JsonSerializer.Serialize(submission);
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestJson)));
        var existing = await db.FiscalisationRecords.SingleOrDefaultAsync(
            x => x.SalesCreditNoteReversalId == reversalId, cancellationToken);
        if (existing is not null)
        {
            if (existing.RequestHash != requestHash)
                throw new InvalidOperationException("A different fiscal correction is already recorded for this reversal.");
            return existing;
        }
        var record = new FiscalisationRecord
        {
            OrganisationId = organisationId,
            SourceDocumentKind = FiscalSourceDocumentKind.SalesCreditNoteReversal,
            SalesCreditNoteReversalId = reversalId,
            Status = FiscalisationStatus.Prepared,
            RequestHash = requestHash,
            RequestJson = requestJson,
            CreatedByUserId = userId
        };
        db.FiscalisationRecords.Add(record);
        Audit(record, userId, "FiscalCreditReversalPrepared");
        await db.SaveChangesAsync(cancellationToken);
        return record;
    }

    public async Task<FiscalisationRecord> MarkRecoveryRequiredAsync(
        string userId,
        Guid organisationId,
        Guid recordId,
        string? errorCode,
        string? errorMessage,
        CancellationToken cancellationToken = default)
    {
        var record = await RequireRecordAsync(userId, organisationId, recordId, cancellationToken);
        RequireSubmitting(record);
        record.Status = FiscalisationStatus.RecoveryRequired;
        record.ErrorCode = errorCode;
        record.ErrorMessage = errorMessage;
        Audit(record, userId, "FiscalisationRecoveryRequired");
        await db.SaveChangesAsync(cancellationToken);
        return record;
    }

    public async Task<FiscalisationRecord> PrepareSalesInvoiceVoidAsync(
        string userId,
        Guid organisationId,
        Guid invoiceVoidId,
        FiscalInvoiceSubmission submission,
        CancellationToken cancellationToken = default)
    {
        await RequirePostingAccessAsync(userId, organisationId);
        FiscalInvoiceSubmissionValidator.Validate(submission);
        if (submission.SourceDocumentId != invoiceVoidId ||
            submission.TransactionType != FiscalTransactionType.Refund)
            throw new InvalidOperationException("The fiscal refund must identify the selected invoice void.");
        var invoiceVoid = await db.SalesInvoiceVoids.AsNoTracking()
            .Include(x => x.SalesInvoice)
            .SingleOrDefaultAsync(x => x.Id == invoiceVoidId && x.OrganisationId == organisationId, cancellationToken)
            ?? throw new InvalidOperationException("Invoice-void draft not found.");
        if (invoiceVoid.Status != SalesInvoiceVoidStatus.Draft ||
            submission.Currency != invoiceVoid.SalesInvoice.Currency ||
            submission.Payments.Sum(x => x.Amount) != invoiceVoid.SalesInvoice.TransactionTotal)
            throw new InvalidOperationException("The fiscal refund must match the pending invoice void.");
        var acceptedInvoice = await db.FiscalisationRecords.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SalesInvoiceId == invoiceVoid.SalesInvoiceId &&
                x.OrganisationId == organisationId && x.Status == FiscalisationStatus.Accepted, cancellationToken)
            ?? throw new InvalidOperationException("The invoice does not have an accepted fiscal response.");
        if (submission.ReferentDocumentNumber != acceptedInvoice.SdcInvoiceNumber ||
            submission.ReferentDocumentIssuedAt != acceptedInvoice.SdcIssuedAt)
            throw new InvalidOperationException("The fiscal refund must reference the accepted SDC invoice.");

        var requestJson = JsonSerializer.Serialize(submission);
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestJson)));
        var existing = await db.FiscalisationRecords.SingleOrDefaultAsync(
            x => x.SalesInvoiceVoidId == invoiceVoidId, cancellationToken);
        if (existing is not null)
        {
            if (existing.RequestHash != requestHash)
                throw new InvalidOperationException("A different fiscal refund is already recorded for this invoice void.");
            return existing;
        }
        var record = new FiscalisationRecord
        {
            OrganisationId = organisationId,
            SourceDocumentKind = FiscalSourceDocumentKind.SalesInvoiceVoid,
            SalesInvoiceVoidId = invoiceVoidId,
            Status = FiscalisationStatus.Prepared,
            RequestHash = requestHash,
            RequestJson = requestJson,
            CreatedByUserId = userId
        };
        db.FiscalisationRecords.Add(record);
        Audit(record, userId, "FiscalInvoiceVoidPrepared");
        await db.SaveChangesAsync(cancellationToken);
        return record;
    }

    public async Task<FiscalisationRecord> RecordRejectedAsync(
        string userId,
        Guid organisationId,
        Guid recordId,
        FiscalisationResult result,
        CancellationToken cancellationToken = default)
    {
        var record = await RequireRecordAsync(userId, organisationId, recordId, cancellationToken);
        if (record.Status is not FiscalisationStatus.Submitting and
            not FiscalisationStatus.RecoveryRequired)
        {
            throw new InvalidOperationException("The fiscal submission is not awaiting an SDC result.");
        }
        if (result.Outcome != FiscalisationOutcome.Rejected)
        {
            throw new InvalidOperationException("The supplied result is not a rejection.");
        }

        record.Status = FiscalisationStatus.Rejected;
        record.ErrorCode = result.ErrorCode;
        record.ErrorMessage = result.ErrorMessage;
        Audit(record, userId, "FiscalisationRejected");
        await db.SaveChangesAsync(cancellationToken);
        return record;
    }

    public async Task<FiscalisationRecord> RecordAcceptedAsync(
        string userId,
        Guid organisationId,
        Guid recordId,
        FiscalisationResult result,
        CancellationToken cancellationToken = default)
    {
        var record = await RequireRecordAsync(userId, organisationId, recordId, cancellationToken);
        if (record.Status == FiscalisationStatus.Accepted)
        {
            if (record.SdcInvoiceNumber == result.SdcInvoiceNumber)
            {
                return record;
            }
            throw new InvalidOperationException("An accepted fiscal response cannot be replaced.");
        }
        if (record.Status is not FiscalisationStatus.Submitting and
            not FiscalisationStatus.RecoveryRequired)
        {
            throw new InvalidOperationException("The fiscal submission is not awaiting an SDC result.");
        }
        if (result.Outcome != FiscalisationOutcome.Accepted ||
            string.IsNullOrWhiteSpace(result.SdcInvoiceNumber) ||
            result.SdcIssuedAt is null ||
            string.IsNullOrWhiteSpace(result.SignedPayload))
        {
            throw new InvalidOperationException(
                "An accepted SDC result requires its invoice number, issue time and signed payload.");
        }

        record.Status = FiscalisationStatus.Accepted;
        record.AcceptedAt = DateTimeOffset.UtcNow;
        record.SdcInvoiceNumber = result.SdcInvoiceNumber;
        record.SdcIssuedAt = result.SdcIssuedAt;
        record.VerificationUrl = result.VerificationUrl;
        record.VerificationQrCode = result.VerificationQrCode;
        record.SignedPayload = result.SignedPayload;
        record.ErrorCode = null;
        record.ErrorMessage = null;
        Audit(record, userId, "FiscalisationAccepted");
        await db.SaveChangesAsync(cancellationToken);
        return record;
    }

    private async Task<FiscalisationRecord> RequireRecordAsync(
        string userId,
        Guid organisationId,
        Guid recordId,
        CancellationToken cancellationToken)
    {
        await RequirePostingAccessAsync(userId, organisationId);
        return await db.FiscalisationRecords.SingleOrDefaultAsync(x =>
            x.Id == recordId && x.OrganisationId == organisationId,
            cancellationToken)
            ?? throw new InvalidOperationException("Fiscalisation record not found.");
    }

    private async Task RequirePostingAccessAsync(string userId, Guid organisationId)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot manage fiscal invoices for this organisation.");
        }
    }

    private static void RequireSubmitting(FiscalisationRecord record)
    {
        if (record.Status != FiscalisationStatus.Submitting)
        {
            throw new InvalidOperationException("The fiscal submission is not being submitted.");
        }
    }

    private void Audit(FiscalisationRecord record, string userId, string eventType)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = record.OrganisationId,
            EventType = eventType,
            EntityType = nameof(FiscalisationRecord),
            EntityId = record.Id.ToString(),
            UserId = userId,
            JsonData = JsonSerializer.Serialize(new
            {
                record.SalesInvoiceId,
                record.SalesCreditNoteId,
                record.SalesCreditNoteReversalId,
                record.SalesInvoiceVoidId,
                SourceDocumentKind = record.SourceDocumentKind.ToString(),
                Status = record.Status.ToString(),
                record.AttemptCount,
                record.RequestHash,
                record.SdcInvoiceNumber,
                record.ErrorCode
            })
        });
    }
}
