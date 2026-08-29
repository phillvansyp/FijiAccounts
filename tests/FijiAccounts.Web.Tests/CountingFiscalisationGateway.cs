using FijiAccounts.Domain.Fiscalisation;

namespace FijiAccounts.Web.Tests;

internal sealed class CountingFiscalisationGateway : IFiscalisationGateway
{
    public int SubmissionCount { get; private set; }
    public int RecoveryCount { get; private set; }

    public Task<FiscalisationResult> FiscaliseAsync(
        FiscalInvoiceSubmission submission,
        CancellationToken cancellationToken = default)
    {
        SubmissionCount++;
        return Task.FromResult(
            new FiscalisationResult(FiscalisationOutcome.Unknown));
    }

    public Task<FiscalisationResult> RecoverLastResultAsync(
        CancellationToken cancellationToken = default)
    {
        RecoveryCount++;
        return Task.FromResult(
            new FiscalisationResult(FiscalisationOutcome.Unknown));
    }
}
