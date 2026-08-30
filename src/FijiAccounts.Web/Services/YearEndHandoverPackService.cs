using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record YearEndHandoverPack(
    byte[] Content,
    string FileName);

public sealed class YearEndHandoverPackService(
    ApplicationDbContext db,
    TenantAccessService access,
    FinancialReportService financialReports,
    VatWorkpaperService vatWorkpapers)
{
    public async Task<YearEndHandoverPack> CreateAsync(
        string userId,
        Guid organisationId,
        Guid periodId,
        CancellationToken cancellationToken = default)
    {
        if (!await access.CanManageTeamAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException(
                "Only owners and administrators can export a year-end handover pack.");
        }

        var organisation = await db.Organisations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == organisationId, cancellationToken)
            ?? throw new InvalidOperationException("Organisation not found.");
        var period = await db.AccountingPeriods.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == periodId && x.OrganisationId == organisationId,
                cancellationToken)
            ?? throw new InvalidOperationException("Accounting period not found.");
        if (!period.IsLocked)
        {
            throw new InvalidOperationException(
                "Lock the accounting period before exporting its final handover pack.");
        }

        var report = await financialReports.GetAsync(
            organisationId,
            period.StartsOn,
            period.EndsOn,
            cancellationToken);
        var vat = await vatWorkpapers.GetAsync(
            organisationId,
            period.StartsOn,
            period.EndsOn,
            cancellationToken);

        var ledger = await db.PostedJournalLines.AsNoTracking()
            .Where(x =>
                x.PostedJournal.OrganisationId == organisationId &&
                x.PostedJournal.EntryDate >= period.StartsOn &&
                x.PostedJournal.EntryDate <= period.EndsOn)
            .OrderBy(x => x.PostedJournal.EntryDate)
            .ThenBy(x => x.PostedJournal.SequenceNumber)
            .ThenBy(x => x.LedgerAccount.Code)
            .Select(x => new
            {
                x.PostedJournal.EntryDate,
                x.PostedJournal.SequenceNumber,
                x.PostedJournal.Reference,
                AccountCode = x.LedgerAccount.Code,
                AccountName = x.LedgerAccount.Name,
                x.Description,
                x.Debit,
                x.Credit,
                BranchCode = x.Branch != null ? x.Branch.Code : "",
                DivisionCode = x.Division != null ? x.Division.Code : ""
            })
            .ToListAsync(cancellationToken);

        var reconciliations = await db.BankReconciliationSessions.AsNoTracking()
            .Where(x =>
                x.OrganisationId == organisationId &&
                x.StatementStartDate <= period.EndsOn &&
                x.StatementEndDate >= period.StartsOn)
            .OrderBy(x => x.StatementEndDate)
            .ThenBy(x => x.BankAccount.Code)
            .Select(x => new
            {
                AccountCode = x.BankAccount.Code,
                AccountName = x.BankAccount.Name,
                x.StatementStartDate,
                x.StatementEndDate,
                x.OpeningStatementBalance,
                x.ClosingStatementBalance,
                x.LedgerBalance,
                x.Difference,
                x.IsCompleted,
                x.CompletedAt,
                x.CompletedByUserId
            })
            .ToListAsync(cancellationToken);

        var periodAudit =
            (await db.AuditEvents.AsNoTracking()
                .Where(x =>
                    x.OrganisationId == organisationId &&
                    x.EntityType == nameof(AccountingPeriod) &&
                    x.EntityId == period.Id.ToString())
                .Select(x => new
                {
                    x.OccurredAt,
                    x.EventType,
                    x.UserId,
                    x.JsonData
                })
                .ToListAsync(cancellationToken))
            .OrderBy(x => x.OccurredAt)
            .ToList();

        var files = new List<PackFile>
        {
            Csv(
                "trial-balance.csv",
                "Account Code,Account Name,Debit,Credit",
                report.TrialBalance.Select(x => Row(x.Code, x.Name, x.Debit, x.Credit))),
            Csv(
                "profit-and-loss.csv",
                "Account Code,Account Name,Type,Amount",
                report.Balances
                    .Where(x => x.Type is AccountType.Revenue or AccountType.Expense)
                    .Select(x => Row(x.Code, x.Name, x.Type, x.DisplayAmount))),
            Csv(
                "balance-sheet.csv",
                "Account Code,Account Name,Type,Amount",
                report.Balances
                    .Where(x => x.Type is AccountType.Asset or AccountType.Liability or AccountType.Equity)
                    .Select(x => Row(x.Code, x.Name, x.Type, x.DisplayAmount))),
            Csv(
                "general-ledger.csv",
                "Date,Journal,Reference,Account Code,Account Name,Description,Debit,Credit,Branch,Division",
                ledger.Select(x => Row(
                    x.EntryDate,
                    $"J-{x.SequenceNumber:D6}",
                    x.Reference,
                    x.AccountCode,
                    x.AccountName,
                    x.Description,
                    x.Debit,
                    x.Credit,
                    x.BranchCode,
                    x.DivisionCode))),
            Csv(
                "vat-workpaper.csv",
                "Section,Standard Net,Tax,Zero Rated Net,Exempt Net,Out Of Scope Net",
                [
                    Row("Sales", vat.Sales.StandardNet, vat.Sales.StandardTax, vat.Sales.ZeroRatedNet, vat.Sales.ExemptNet, vat.Sales.OutOfScopeNet),
                    Row("Sales credits", vat.SalesCredits.Net, vat.SalesCredits.Tax, 0m, 0m, 0m),
                    Row("Purchases", vat.Purchases.StandardNet, vat.Purchases.StandardTax, vat.Purchases.ZeroRatedNet, vat.Purchases.ExemptNet, vat.Purchases.OutOfScopeNet),
                    Row("Supplier credits", vat.SupplierCredits.Net, vat.SupplierCredits.Tax, 0m, 0m, 0m),
                    Row("Net tax", 0m, vat.NetTax, 0m, 0m, 0m)
                ]),
            Csv(
                "bank-reconciliations.csv",
                "Account Code,Account Name,Statement Start,Statement End,Opening Balance,Closing Balance,Ledger Balance,Difference,Completed,Completed At,Completed By",
                reconciliations.Select(x => Row(
                    x.AccountCode,
                    x.AccountName,
                    x.StatementStartDate,
                    x.StatementEndDate,
                    x.OpeningStatementBalance,
                    x.ClosingStatementBalance,
                    x.LedgerBalance,
                    x.Difference,
                    x.IsCompleted,
                    x.CompletedAt,
                    x.CompletedByUserId))),
            Csv(
                "period-control-audit.csv",
                "Occurred At,Event,User,Evidence JSON",
                periodAudit.Select(x => Row(x.OccurredAt, x.EventType, x.UserId, x.JsonData)))
        };

        var generatedAt = DateTimeOffset.UtcNow;
        var manifest = new
        {
            Format = "Account Island year-end handover pack",
            Version = 1,
            OrganisationId = organisation.Id,
            organisation.LegalName,
            organisation.CountryCode,
            Currency = organisation.BaseCurrency,
            PeriodId = period.Id,
            period.Name,
            period.StartsOn,
            period.EndsOn,
            period.LockedAt,
            period.LockedByUserId,
            GeneratedAt = generatedAt,
            GeneratedByUserId = userId,
            Files = files.Select(x => new
            {
                x.Name,
                x.RowCount,
                Sha256 = Convert.ToHexString(SHA256.HashData(x.Content)).ToLowerInvariant()
            })
        };
        var manifestContent = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        files.Insert(0, new PackFile("manifest.json", manifestContent, 1));

        byte[] content;
        using (var output = new MemoryStream())
        {
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var file in files)
                {
                    var entry = archive.CreateEntry(file.Name, CompressionLevel.Optimal);
                    entry.LastWriteTime = generatedAt;
                    await using var stream = entry.Open();
                    await stream.WriteAsync(file.Content, cancellationToken);
                }
            }

            content = output.ToArray();
        }

        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = organisationId,
            UserId = userId,
            EventType = "YearEndHandoverPackExported",
            EntityType = nameof(AccountingPeriod),
            EntityId = period.Id.ToString(),
            JsonData = JsonSerializer.Serialize(new
            {
                period.Name,
                period.StartsOn,
                period.EndsOn,
                FileCount = files.Count,
                Size = content.Length,
                ManifestSha256 = Convert.ToHexString(SHA256.HashData(manifestContent)).ToLowerInvariant()
            })
        });
        await db.SaveChangesAsync(cancellationToken);

        return new YearEndHandoverPack(
            content,
            $"account-island-handover-{period.StartsOn:yyyyMMdd}-{period.EndsOn:yyyyMMdd}.zip");
    }

    private static PackFile Csv(
        string name,
        string header,
        IEnumerable<string> rows)
    {
        var materialised = rows.ToList();
        var text = new StringBuilder(header).Append("\r\n");
        foreach (var row in materialised)
        {
            text.Append(row).Append("\r\n");
        }

        return new PackFile(name, Encoding.UTF8.GetBytes(text.ToString()), materialised.Count);
    }

    private static string Row(params object?[] values) =>
        string.Join(',', values.Select(Value));

    private static string Value(object? value)
    {
        var text = value switch
        {
            null => "",
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset timestamp => timestamp.ToString("O", CultureInfo.InvariantCulture),
            decimal number => number.ToString("0.00", CultureInfo.InvariantCulture),
            bool boolean => boolean ? "true" : "false",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
        };

        return $"\"{text.Replace("\"", "\"\"")}\"";
    }

    private sealed record PackFile(string Name, byte[] Content, int RowCount);
}
