using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class XeroEvidenceClassifierTests
{
    [Theory]
    [InlineData("General-Ledger.csv", "General ledger")]
    [InlineData("Trial_Balance_18-10-2026.xlsx", "Trial balance")]
    [InlineData("Balance-Sheet-and-Profit-Loss.zip", "Balance Sheet and Profit & Loss")]
    [InlineData("Receivable Invoice Detail.pdf", "Receivable Invoice Detail")]
    [InlineData("Payable-Invoice-Detail.csv", "Payable Invoice Detail")]
    [InlineData("Van-Syp-Investments-Bank-Reconciliation.zip", "Bank statements and reconciliation")]
    [InlineData("Fixed Asset Reconciliation.pdf", "Fixed assets and depreciation reconciliation")]
    [InlineData("GST-Returns-and-Reconciliation.zip", "GST returns and reconciliation")]
    [InlineData("Payroll-History-No-Activity-Support.zip", "Payroll history reports")]
    [InlineData("Xero-document-attachments.zip", "Document attachments")]
    public void ClassifiesRecognisedXeroExports(string fileName, string expected)
    {
        Assert.Equal(expected, XeroEvidenceClassifier.Classify(fileName));
    }

    [Fact]
    public void LeavesAmbiguousFilesForCustomerReview()
    {
        Assert.Null(XeroEvidenceClassifier.Classify("October information.zip"));
    }
}
