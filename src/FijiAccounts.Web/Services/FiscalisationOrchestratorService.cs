using System.Net.Http;
using System.Text.Json;
using FijiAccounts.Domain.Fiscalisation;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed class FiscalisationOrchestratorService(
    ApplicationDbContext db,
    FiscalisationWorkflowService workflow,
    IFiscalisationGateway gateway)
{
    public async Task<FiscalisationRecord> SubmitAsync(
        string userId,
        Guid organisationId,
        Guid recordId,
        CancellationToken cancellationToken = default)
    {
        var submission = await LoadSubmissionAsync(
            organisationId, recordId, cancellationToken);
        await workflow.BeginAttemptAsync(
            userId, organisationId, recordId, cancellationToken);

        try
        {
            var result = await gateway.FiscaliseAsync(submission, cancellationToken);
            return await ApplyResultAsync(
                userId, organisationId, recordId, result, cancellationToken);
        }
        catch (Exception ex) when (IsUncertainTransportFailure(ex, cancellationToken))
        {
            return await workflow.MarkRecoveryRequiredAsync(
                userId,
                organisationId,
                recordId,
                "SDC_RESPONSE_UNKNOWN",
                "The SDC response was not received. Recover the last result before retrying.",
                CancellationToken.None);
        }
    }

    public async Task<FiscalisationRecord> RecoverAsync(
        string userId,
        Guid organisationId,
        Guid recordId,
        CancellationToken cancellationToken = default)
    {
        var record = await db.FiscalisationRecords.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == recordId && x.OrganisationId == organisationId,
            cancellationToken)
            ?? throw new InvalidOperationException("Fiscalisation record not found.");
        if (record.Status != FiscalisationStatus.RecoveryRequired)
        {
            throw new InvalidOperationException(
                "Only a submission with an uncertain SDC response can be recovered.");
        }

        try
        {
            var result = await gateway.RecoverLastResultAsync(cancellationToken);
            if (result.Outcome == FiscalisationOutcome.Unknown)
            {
                return record;
            }

            return await ApplyResultAsync(
                userId, organisationId, recordId, result, cancellationToken);
        }
        catch (Exception ex) when (IsUncertainTransportFailure(ex, cancellationToken))
        {
            return record;
        }
    }

    private async Task<FiscalInvoiceSubmission> LoadSubmissionAsync(
        Guid organisationId,
        Guid recordId,
        CancellationToken cancellationToken)
    {
        var requestJson = await db.FiscalisationRecords.AsNoTracking()
            .Where(x => x.Id == recordId && x.OrganisationId == organisationId)
            .Select(x => x.RequestJson)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Fiscalisation record not found.");

        return JsonSerializer.Deserialize<FiscalInvoiceSubmission>(requestJson)
            ?? throw new InvalidOperationException("The stored fiscal submission is invalid.");
    }

    private Task<FiscalisationRecord> ApplyResultAsync(
        string userId,
        Guid organisationId,
        Guid recordId,
        FiscalisationResult result,
        CancellationToken cancellationToken) => result.Outcome switch
        {
            FiscalisationOutcome.Accepted => workflow.RecordAcceptedAsync(
                userId, organisationId, recordId, result, cancellationToken),
            FiscalisationOutcome.Rejected => workflow.RecordRejectedAsync(
                userId, organisationId, recordId, result, cancellationToken),
            _ => workflow.MarkRecoveryRequiredAsync(
                userId,
                organisationId,
                recordId,
                result.ErrorCode ?? "SDC_RESPONSE_UNKNOWN",
                result.ErrorMessage ?? "The SDC outcome is unknown.",
                cancellationToken)
        };

    private static bool IsUncertainTransportFailure(
        Exception exception,
        CancellationToken cancellationToken) =>
        exception is TimeoutException or HttpRequestException ||
        exception is TaskCanceledException && !cancellationToken.IsCancellationRequested;
}
