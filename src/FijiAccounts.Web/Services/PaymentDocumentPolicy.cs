using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public static class PaymentDocumentPolicy
{
    // Fees are supported by the bank statement. Tax/payroll/loan liabilities and
    // owner movements have their own ledger records rather than trade documents.
    public static bool RequiresTradeDocument(LedgerAccount account) =>
        !account.IsBankAccount && (account.Type switch
        {
            AccountType.Expense => account.Code != "6400",
            AccountType.Revenue => true,
            AccountType.Asset => account.Code != "1150",
            AccountType.Liability => account.Code == "2000",
            AccountType.Equity => false,
            _ => true
        });

    public static async Task ValidateMatchesAsync(ApplicationDbContext db, Guid organisationId,
        Guid[] journalIds, CancellationToken ct)
    {
        if (!await db.Organisations.AnyAsync(x => x.Id == organisationId && x.RequireTradePaymentDocuments, ct)) return;
        var supported = (await db.SupplierPayments.Where(x => x.OrganisationId == organisationId &&
            journalIds.Contains(x.PostedJournalId) && !db.SupplierPaymentReversals.Any(r => r.SupplierPaymentId == x.Id))
            .Select(x => x.PostedJournalId).ToListAsync(ct)).ToHashSet();
        supported.UnionWith(await db.CustomerReceipts.Where(x => x.OrganisationId == organisationId &&
            journalIds.Contains(x.PostedJournalId) && x.Allocations.Any() &&
            !db.CustomerReceiptReversals.Any(r => r.CustomerReceiptId == x.Id))
            .Select(x => x.PostedJournalId).ToListAsync(ct));
        // Receipt/payment services reconcile within the transaction that creates the payment.
        supported.UnionWith(db.ChangeTracker.Entries<SupplierPayment>().Where(x => x.State == EntityState.Added &&
            x.Entity.OrganisationId == organisationId).Select(x => x.Entity.PostedJournalId));
        supported.UnionWith(db.ChangeTracker.Entries<CustomerReceipt>().Where(x => x.State == EntityState.Added &&
            x.Entity.OrganisationId == organisationId && x.Entity.Allocations.Count > 0).Select(x => x.Entity.PostedJournalId));
        var remaining = journalIds.Where(x => !supported.Contains(x)).ToArray();
        var accounts = await db.PostedJournalLines.Where(x => remaining.Contains(x.PostedJournalId) &&
            x.PostedJournal.OrganisationId == organisationId).Select(x => x.LedgerAccount).ToListAsync(ct);
        if (accounts.Any(RequiresTradeDocument)) throw new InvalidOperationException(MissingDocumentMessage);
    }

    public const string MissingDocumentMessage =
        "Customer and supplier payments must be linked to an invoice or bill. " +
        "Choose the existing document, or create it first, then record its payment against this bank transaction. " +
        "Use payroll liabilities for payroll payments; bank fees, tax payments and transfers use their own records.";
}
