using FijiAccounts.Domain.Fiscalisation;

namespace FijiAccounts.Web.Tests;

public sealed class FiscalisationContractTests
{
    [Fact]
    public void PaymentTypesMatchPublishedPosToSdcValues()
    {
        Assert.Equal(0, (int)FiscalPaymentType.Other);
        Assert.Equal(1, (int)FiscalPaymentType.Cash);
        Assert.Equal(2, (int)FiscalPaymentType.Card);
        Assert.Equal(3, (int)FiscalPaymentType.Check);
        Assert.Equal(4, (int)FiscalPaymentType.WireTransfer);
        Assert.Equal(5, (int)FiscalPaymentType.Voucher);
        Assert.Equal(6, (int)FiscalPaymentType.MobileMoney);
    }

    [Fact]
    public void ValidSubmissionPassesProviderNeutralValidation()
    {
        var submission = Submission(
            [new("Professional services", 2m, 56.252m, 112.50m, ["FJ-STANDARD"])],
            [new(62.50m, FiscalPaymentType.Card),
             new(50m, FiscalPaymentType.MobileMoney)]);

        FiscalInvoiceSubmissionValidator.Validate(submission);
    }

    [Fact]
    public void ItemWithoutDynamicSdcTaxLabelIsRejected()
    {
        var submission = Submission(
            [new("Professional services", 1m, 112.50m, 112.50m, [])],
            [new(112.50m, FiscalPaymentType.WireTransfer)]);

        var exception = Assert.Throws<FiscalisationValidationException>(() =>
            FiscalInvoiceSubmissionValidator.Validate(submission));

        Assert.Contains("tax label", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PaymentMismatchIsRejectedBeforeCallingAnSdc()
    {
        var submission = Submission(
            [new("Professional services", 1m, 112.50m, 112.50m, ["FJ-STANDARD"])],
            [new(100m, FiscalPaymentType.Cash)]);

        var exception = Assert.Throws<FiscalisationValidationException>(() =>
            FiscalInvoiceSubmissionValidator.Validate(submission));

        Assert.Contains("payments", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static FiscalInvoiceSubmission Submission(
        IReadOnlyCollection<FiscalInvoiceItem> items,
        IReadOnlyCollection<FiscalPayment> payments) =>
        new(
            Guid.NewGuid(),
            "INV-000001",
            new DateTimeOffset(2026, 8, 29, 10, 30, 0, TimeSpan.FromHours(12)),
            "FJD",
            FiscalInvoiceType.Normal,
            FiscalTransactionType.Sale,
            items,
            payments,
            CashierId: "demo@accountisland.com",
            BuyerId: "50-12345-0");
}
