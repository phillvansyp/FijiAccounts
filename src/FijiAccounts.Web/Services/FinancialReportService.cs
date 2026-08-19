using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record FinancialAccountBalance(
    string Code,
    string Name,
    AccountType Type,
    decimal DisplayAmount);

public sealed record TrialBalanceRow(
    string Code,
    string Name,
    decimal Debit,
    decimal Credit);

public sealed record FinancialReportData(
    IReadOnlyList<FinancialAccountBalance> Balances,
    IReadOnlyList<TrialBalanceRow> TrialBalance)
{
    public static FinancialReportData Empty { get; } =
        new([], []);
}

public sealed class FinancialReportService(
    ApplicationDbContext db)
{
    public async Task<FinancialReportData> GetAsync(
        Guid organisationId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
    {
        if (from > to)
        {
            throw new InvalidOperationException(
                "The report start date cannot be after the end date.");
        }

        var all =
            await db.PostedJournalLines
                .AsNoTracking()
                .Where(x =>
                    x.PostedJournal.OrganisationId ==
                        organisationId &&
                    x.PostedJournal.EntryDate <= to)
                .GroupBy(x => new
                {
                    x.LedgerAccount.Code,
                    x.LedgerAccount.Name,
                    x.LedgerAccount.Type
                })
                .Select(x => new
                {
                    x.Key.Code,
                    x.Key.Name,
                    x.Key.Type,
                    Debit = x.Sum(y => y.Debit),
                    Credit = x.Sum(y => y.Credit)
                })
                .OrderBy(x => x.Code)
                .ToListAsync(ct);

        var trialBalance =
            all.Select(x =>
            {
                var net =
                    x.Debit - x.Credit;

                return new TrialBalanceRow(
                    x.Code,
                    x.Name,
                    Math.Max(net, 0m),
                    Math.Max(-net, 0m));
            })
            .ToList();

        var profit =
            await db.PostedJournalLines
                .AsNoTracking()
                .Where(x =>
                    x.PostedJournal.OrganisationId ==
                        organisationId &&
                    x.PostedJournal.EntryDate >= from &&
                    x.PostedJournal.EntryDate <= to &&
                    (x.LedgerAccount.Type ==
                        AccountType.Revenue ||
                     x.LedgerAccount.Type ==
                        AccountType.Expense))
                .GroupBy(x => new
                {
                    x.LedgerAccount.Code,
                    x.LedgerAccount.Name,
                    x.LedgerAccount.Type
                })
                .Select(x => new
                {
                    x.Key.Code,
                    x.Key.Name,
                    x.Key.Type,
                    Debit = x.Sum(y => y.Debit),
                    Credit = x.Sum(y => y.Credit)
                })
                .ToListAsync(ct);

        var accumulatedProfit =
    all
        .Where(x =>
            x.Type is AccountType.Revenue or AccountType.Expense)
        .Sum(x => x.Credit - x.Debit);

        var balances =
    all
        .Where(x =>
            x.Type is AccountType.Asset
                or AccountType.Liability
                or AccountType.Equity)
        .Select(x =>
            new FinancialAccountBalance(
                x.Code,
                x.Name,
                x.Type,
                x.Type == AccountType.Asset
                    ? x.Debit - x.Credit
                    : x.Credit - x.Debit))
        .Concat(
            accumulatedProfit == 0m
                ? []
                :
                [
                    new FinancialAccountBalance(
                        "",
                        "Accumulated earnings",
                        AccountType.Equity,
                        accumulatedProfit)
                ])
        .Concat(
            profit.Select(x =>
                new FinancialAccountBalance(
                    x.Code,
                    x.Name,
                    x.Type,
                    x.Type == AccountType.Revenue
                        ? x.Credit - x.Debit
                        : x.Debit - x.Credit)))
        .OrderBy(x => x.Code)
        .ToList();

        return new FinancialReportData(
            balances,
            trialBalance);
    }
}