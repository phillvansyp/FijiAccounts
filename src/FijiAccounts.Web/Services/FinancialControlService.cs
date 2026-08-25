using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public enum FinancialControlAlertType
{
    DuplicateSupplierBill,
    DuplicateSupplierPayment,
    UnverifiedSupplierBankAccount,
    RecentSupplierBankAccountChange
}

public enum FinancialControlSeverity
{
    Watch,
    High
}

public sealed record FinancialControlAlert(
    FinancialControlAlertType Type,
    FinancialControlSeverity Severity,
    string Title,
    string Explanation,
    DateOnly TransactionDate,
    decimal Amount,
    int MatchingTransactions);

public sealed record FinancialControlSummary(
    IReadOnlyList<FinancialControlAlert> Alerts)
{
    public int HighRiskCount =>
        Alerts.Count(x => x.Severity == FinancialControlSeverity.High);
}

public sealed class FinancialControlService(
    ApplicationDbContext db,
    TenantAccessService access)
{
    public async Task<FinancialControlSummary> GetAsync(
        string userId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        if (await access.FindAsync(userId, organisationId) is null)
        {
            throw new UnauthorizedAccessException(
                "You cannot view financial controls for this organisation.");
        }

        var divisionScope = await access.GetReportDivisionScopeAsync(
            userId, organisationId, cancellationToken);
        var reviewFrom = DateOnly.FromDateTime(DateTime.Today).AddYears(-1);

        var billQuery = db.SupplierBills
            .AsNoTracking()
            .Where(x =>
                x.OrganisationId == organisationId &&
                x.BillDate >= reviewFrom &&
                x.Status != BillStatus.Voided);
        var paymentQuery = db.SupplierPayments
            .AsNoTracking()
            .Where(x =>
                x.OrganisationId == organisationId &&
                x.PaymentDate >= reviewFrom);

        if (divisionScope is not null)
        {
            billQuery = billQuery.Where(x =>
                x.DivisionId != null && divisionScope.Contains(x.DivisionId.Value));
            paymentQuery = paymentQuery.Where(x =>
                x.DivisionId != null && divisionScope.Contains(x.DivisionId.Value));
        }

        var bills = await billQuery
            .Select(x => new BillCandidate(
                x.Id,
                x.SupplierId,
                x.Supplier.Name,
                x.SupplierReference,
                x.BillDate,
                x.Total,
                x.Total - x.AmountPaid - x.AmountCredited))
            .ToListAsync(cancellationToken);

        var suppliersWithOutstandingBills = bills
            .Where(x => x.OutstandingAmount > 0)
            .Select(x => x.SupplierId)
            .Distinct()
            .ToList();
        var supplierBankAccounts = await db.SupplierBankAccounts
            .AsNoTracking()
            .Where(x => x.OrganisationId == organisationId &&
                        x.IsActive &&
                        suppliersWithOutstandingBills.Contains(x.SupplierId))
            .Select(x => new SupplierBankAccountCandidate(
                x.SupplierId,
                x.Supplier.Name,
                x.IsDefault,
                x.SubmittedAt,
                x.VerifiedAt))
            .ToListAsync(cancellationToken);

        var reversedPaymentIds = await db.SupplierPaymentReversals
            .AsNoTracking()
            .Where(x => x.OrganisationId == organisationId)
            .Select(x => x.SupplierPaymentId)
            .ToListAsync(cancellationToken);
        var payments = await paymentQuery
            .Where(x => !reversedPaymentIds.Contains(x.Id))
            .Select(x => new PaymentCandidate(
                x.Id,
                x.SupplierId,
                x.Supplier.Name,
                x.SupplierBillId,
                x.Reference,
                x.PaymentDate,
                x.Amount))
            .ToListAsync(cancellationToken);

        var alerts = FindBillAlerts(bills)
            .Concat(FindPaymentAlerts(payments))
            .Concat(FindSupplierBankAccountAlerts(bills, supplierBankAccounts))
            .OrderByDescending(x => x.Severity)
            .ThenByDescending(x => x.TransactionDate)
            .ThenByDescending(x => x.Amount)
            .ToList();

        return new FinancialControlSummary(alerts);
    }

    private static IEnumerable<FinancialControlAlert> FindBillAlerts(
        IReadOnlyCollection<BillCandidate> bills)
    {
        var exactReferenceIds = new HashSet<Guid>();
        foreach (var group in bills
                     .Where(x => NormaliseReference(x.Reference).Length > 0)
                     .GroupBy(x => new
                     {
                         x.SupplierId,
                         Reference = NormaliseReference(x.Reference)
                     })
                     .Where(x => x.Count() > 1))
        {
            var matches = group.OrderBy(x => x.Date).ToList();
            foreach (var match in matches)
            {
                exactReferenceIds.Add(match.Id);
            }

            yield return new FinancialControlAlert(
                FinancialControlAlertType.DuplicateSupplierBill,
                FinancialControlSeverity.High,
                $"Repeated supplier reference · {matches[0].SupplierName}",
                $"Reference {matches[0].Reference.Trim()} appears on {matches.Count} non-voided supplier bills.",
                matches.Max(x => x.Date),
                matches.Sum(x => x.Amount),
                matches.Count);
        }

        foreach (var group in bills
                     .Where(x => !exactReferenceIds.Contains(x.Id))
                     .GroupBy(x => new { x.SupplierId, x.Date, x.Amount })
                     .Where(x => x.Count() > 1))
        {
            var matches = group.ToList();
            yield return new FinancialControlAlert(
                FinancialControlAlertType.DuplicateSupplierBill,
                FinancialControlSeverity.Watch,
                $"Similar supplier bills · {matches[0].SupplierName}",
                $"{matches.Count} bills share the same date and amount but use different references.",
                group.Key.Date,
                group.Key.Amount,
                matches.Count);
        }
    }

    private static IEnumerable<FinancialControlAlert> FindPaymentAlerts(
        IReadOnlyCollection<PaymentCandidate> payments)
    {
        foreach (var group in payments
                     .GroupBy(x => new { x.SupplierId, x.Date, x.Amount })
                     .Where(x => x.Count() > 1))
        {
            var matches = group.ToList();
            var sameBill = matches.Select(x => x.SupplierBillId).Distinct().Count() == 1;
            var references = matches
                .Select(x => NormaliseReference(x.Reference))
                .Where(x => x.Length > 0)
                .ToList();
            var repeatedReference = references.Count > 1 &&
                                    references.Distinct().Count() < references.Count;
            var highRisk = sameBill || repeatedReference;

            yield return new FinancialControlAlert(
                FinancialControlAlertType.DuplicateSupplierPayment,
                highRisk ? FinancialControlSeverity.High : FinancialControlSeverity.Watch,
                $"Possible duplicate payment · {matches[0].SupplierName}",
                highRisk
                    ? $"{matches.Count} active payments share the same supplier, date and amount, and also repeat the bill or payment reference."
                    : $"{matches.Count} active payments share the same supplier, date and amount.",
                group.Key.Date,
                group.Key.Amount,
                matches.Count);
        }
    }

    private static IEnumerable<FinancialControlAlert> FindSupplierBankAccountAlerts(
        IReadOnlyCollection<BillCandidate> bills,
        IReadOnlyCollection<SupplierBankAccountCandidate> bankAccounts)
    {
        var recentFrom = DateTimeOffset.UtcNow.AddDays(-7);
        foreach (var supplier in bankAccounts.GroupBy(x => new
                 {
                     x.SupplierId,
                     x.SupplierName
                 }))
        {
            var outstandingBills = bills
                .Where(x => x.SupplierId == supplier.Key.SupplierId &&
                            x.OutstandingAmount > 0)
                .ToList();
            if (outstandingBills.Count == 0)
            {
                continue;
            }

            var outstandingAmount = outstandingBills.Sum(x => x.OutstandingAmount);
            var pendingAccounts = supplier.Where(x => x.VerifiedAt is null).ToList();
            if (pendingAccounts.Count > 0)
            {
                var changeLabel = pendingAccounts.Count == 1
                    ? "payment destination change has"
                    : "payment destination changes have";
                yield return new FinancialControlAlert(
                    FinancialControlAlertType.UnverifiedSupplierBankAccount,
                    FinancialControlSeverity.High,
                    $"Bank details awaiting verification · {supplier.Key.SupplierName}",
                    $"{pendingAccounts.Count} {changeLabel} not been independently verified while {outstandingBills.Count} supplier bill(s) remain outstanding.",
                    DateOnly.FromDateTime(pendingAccounts.Max(x => x.SubmittedAt).LocalDateTime),
                    outstandingAmount,
                    outstandingBills.Count);
            }

            var recentDefault = supplier
                .Where(x => x.IsDefault && x.VerifiedAt >= recentFrom)
                .OrderByDescending(x => x.VerifiedAt)
                .FirstOrDefault();
            if (recentDefault is not null)
            {
                yield return new FinancialControlAlert(
                    FinancialControlAlertType.RecentSupplierBankAccountChange,
                    FinancialControlSeverity.Watch,
                    $"Recently verified bank details · {supplier.Key.SupplierName}",
                    $"The default payment destination changed within the last 7 days while {outstandingBills.Count} supplier bill(s) remain outstanding.",
                    DateOnly.FromDateTime(recentDefault.VerifiedAt!.Value.LocalDateTime),
                    outstandingAmount,
                    outstandingBills.Count);
            }
        }
    }

    private static string NormaliseReference(string? value) =>
        string.Concat((value ?? string.Empty)
            .Where(char.IsLetterOrDigit))
            .ToUpperInvariant();

    private sealed record BillCandidate(
        Guid Id,
        Guid SupplierId,
        string SupplierName,
        string Reference,
        DateOnly Date,
        decimal Amount,
        decimal OutstandingAmount);

    private sealed record PaymentCandidate(
        Guid Id,
        Guid SupplierId,
        string SupplierName,
        Guid SupplierBillId,
        string Reference,
        DateOnly Date,
        decimal Amount);

    private sealed record SupplierBankAccountCandidate(
        Guid SupplierId,
        string SupplierName,
        bool IsDefault,
        DateTimeOffset SubmittedAt,
        DateTimeOffset? VerifiedAt);
}
