using System.Text.Json;
using FijiAccounts.Domain.Fiscalisation;

namespace FijiAccounts.Web.Services;

/// <summary>
/// Exercises the fiscalisation boundary locally. This is deliberately registered
/// only in Development and never represents an FRCS or accredited SDC response.
/// </summary>
public sealed class DevelopmentFiscalisationGateway : IFiscalisationGateway
{
    private FiscalisationResult? lastResult;

    public Task<FiscalisationResult> FiscaliseAsync(
        FiscalInvoiceSubmission submission,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FiscalInvoiceSubmissionValidator.Validate(submission);

        var issuedAt = DateTimeOffset.UtcNow;
        var number = $"SIMULATED-{submission.SourceDocumentId:N}";
        lastResult = new FiscalisationResult(
            FiscalisationOutcome.Accepted,
            number,
            issuedAt,
            $"https://example.invalid/fiscalisation/{number}",
            $"SIMULATED-QR:{number}",
            JsonSerializer.Serialize(new
            {
                Simulated = true,
                Accredited = false,
                submission.SourceDocumentId,
                submission.SourceDocumentNumber,
                SdcInvoiceNumber = number,
                SdcIssuedAt = issuedAt
            }));

        return Task.FromResult(lastResult);
    }

    public Task<FiscalisationResult> RecoverLastResultAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(lastResult ?? new FiscalisationResult(
            FiscalisationOutcome.Unknown,
            ErrorCode: "SIMULATOR_NO_RESULT",
            ErrorMessage: "The development simulator has no prior result to recover."));
    }
}

public sealed class UnconfiguredFiscalisationGateway : IFiscalisationGateway
{
    private static readonly FiscalisationResult NotConfigured = new(
        FiscalisationOutcome.Rejected,
        ErrorCode: "FRCS_NOT_CONFIGURED",
        ErrorMessage: "No accredited FRCS SDC connection is configured.");

    public Task<FiscalisationResult> FiscaliseAsync(
        FiscalInvoiceSubmission submission,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(NotConfigured);

    public Task<FiscalisationResult> RecoverLastResultAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(NotConfigured);
}
