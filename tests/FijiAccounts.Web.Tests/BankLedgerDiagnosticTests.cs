using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FijiAccounts.Web.Tests;

public sealed class BankLedgerDiagnosticTests
{
    [Fact]
    public async Task Show_live_bank_ledger()
    {
        const string databasePath =
            @"C:\Users\phill\Fiji Accounts\src\FijiAccounts.Web\Data\app.db";

        var options =
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

        await using var db =
            new ApplicationDbContext(options);

        var bankAccounts =
            await db.LedgerAccounts
                .AsNoTracking()
                .Where(x => x.IsBankAccount)
                .OrderBy(x => x.OrganisationId)
                .ThenBy(x => x.Code)
                .ToListAsync();

        Console.WriteLine();
        Console.WriteLine("===== BANK ACCOUNTS =====");

        foreach (var bank in bankAccounts)
        {
            var organisation =
                await db.Organisations
                    .AsNoTracking()
                    .SingleAsync(x =>
                        x.Id == bank.OrganisationId);

            Console.WriteLine();
            Console.WriteLine(
                $"{organisation.LegalName} | " +
                $"{bank.Code} | {bank.Name} | {bank.Id}");

            var lines =
                await db.PostedJournalLines
                    .AsNoTracking()
                    .Include(x => x.PostedJournal)
                    .Where(x =>
                        x.PostedJournal.OrganisationId ==
                            bank.OrganisationId &&
                        x.LedgerAccountId ==
                            bank.Id)
                    .OrderBy(x =>
                        x.PostedJournal.EntryDate)
                    .ThenBy(x =>
                        x.PostedJournal.SequenceNumber)
                    .ToListAsync();

            decimal running = 0m;

            Console.WriteLine(
                "Date        Reference                 Debit       Credit     Movement      Running");

            Console.WriteLine(
                "--------------------------------------------------------------------------------");

            foreach (var line in lines)
            {
                var movement =
                    line.Debit - line.Credit;

                running += movement;

                Console.WriteLine(
                    $"{line.PostedJournal.EntryDate:yyyy-MM-dd}  " +
                    $"{line.PostedJournal.Reference,-22} " +
                    $"{line.Debit,11:N2} " +
                    $"{line.Credit,11:N2} " +
                    $"{movement,12:N2} " +
                    $"{running,12:N2}");

                Console.WriteLine(
                    $"            {line.Description}");
            }

            Console.WriteLine(
                $"FINAL LEDGER BALANCE: {running:N2}");
        }
    }
}