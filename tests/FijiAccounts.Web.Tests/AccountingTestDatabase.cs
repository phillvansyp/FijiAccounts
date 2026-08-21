using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class AccountingTestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection connection;

    private AccountingTestDatabase(
        SqliteConnection connection,
        ApplicationDbContext db,
        string userId,
        Organisation organisation,
        BusinessParty customer,
        BusinessParty supplier)
    {
        this.connection = connection;

        Db = db;
        UserId = userId;
        Organisation = organisation;
        Customer = customer;
        Supplier = supplier;

        Access =
    new TenantAccessService(
        Db);

    Reconciliation =
    new BankReconciliationService(
        Db,
        Access);

Posting =
    new JournalPostingService(
        Db,
        Access,
        Reconciliation);


PurchaseOrders =
    new PurchaseOrderService(
        Db,
        Access);

BankAccounts =
    new BankAccountService(
        Db,
        Access,
        Posting);

SalesInvoices =
    new SalesInvoiceService(
        Db,
        Access,
        Posting);

Purchasing =
    new PurchasingService(
        Db,
        Access,
        Posting,
        Reconciliation);

BankCoding =
    new BankTransactionCodingService(
        Db,
        Access,
        Posting,
        Reconciliation);
    }

    public ApplicationDbContext Db { get; }

    public string UserId { get; }

    public Organisation Organisation { get; }

    public BusinessParty Customer { get; }

    public BusinessParty Supplier { get; }

    public TenantAccessService Access { get; }

    public JournalPostingService Posting { get; }

    public SalesInvoiceService SalesInvoices { get; }

    public PurchasingService Purchasing { get; }

    public PurchaseOrderService PurchaseOrders { get; }

    public BankReconciliationService Reconciliation { get; }

public BankTransactionCodingService BankCoding { get; }

    public BankAccountService BankAccounts { get; }

    public static async Task<AccountingTestDatabase> CreateAsync()
    {
        var connection = new SqliteConnection(
            "Data Source=:memory:");

        await connection.OpenAsync();

        var options =
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .EnableSensitiveDataLogging()
                .Options;

        var db = new ApplicationDbContext(options);

        await db.Database.EnsureCreatedAsync();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "accounting-test@example.com",
            NormalizedUserName = "ACCOUNTING-TEST@EXAMPLE.COM",
            Email = "accounting-test@example.com",
            NormalizedEmail = "ACCOUNTING-TEST@EXAMPLE.COM",
            EmailConfirmed = true
        };

        var organisation = new Organisation
        {
            LegalName = "Accounting Test Limited",
            TradingName = "Accounting Test",
            CountryCode = "FJ",
            BaseCurrency = "FJD",
            TaxLabel = "VAT",
            Kind = OrganisationKind.Business
        };

        db.Users.Add(user);

        db.Organisations.Add(organisation);

        db.OrganisationMemberships.Add(
            new OrganisationMembership
            {
                OrganisationId = organisation.Id,
                Organisation = organisation,
                UserId = user.Id,
                User = user,
                Role = OrganisationRole.Owner
            });

        var accounts =
            FijiStarterChart.For(organisation.Id)
                .ToList();

        db.LedgerAccounts.AddRange(accounts);

        var customer = new BusinessParty
        {
            OrganisationId = organisation.Id,
            Organisation = organisation,
            Name = "Test Customer",
            Type = PartyType.Customer,
            IsActive = true
        };

        var supplier = new BusinessParty
        {
            OrganisationId = organisation.Id,
            Organisation = organisation,
            Name = "Test Supplier",
            Type = PartyType.Supplier,
            IsActive = true
        };

        db.BusinessParties.AddRange(
            customer,
            supplier);

        await db.SaveChangesAsync();

        return new AccountingTestDatabase(
            connection,
            db,
            user.Id,
            organisation,
            customer,
            supplier);
    }

    public LedgerAccount Account(string code)
    {
        return Db.LedgerAccounts.Local
            .Single(x =>
                x.OrganisationId == Organisation.Id &&
                x.Code == code);
    }

    public async Task<PostedJournal> LoadJournalAsync(
        Guid journalId)
    {
        return await Db.PostedJournals
            .AsNoTracking()
            .Include(x => x.Lines)
            .ThenInclude(x => x.LedgerAccount)
            .SingleAsync(x => x.Id == journalId);
    }

    public async Task<decimal> AccountBalanceAsync(
        string code)
    {
        var accountId = await Db.LedgerAccounts
            .Where(x =>
                x.OrganisationId == Organisation.Id &&
                x.Code == code)
            .Select(x => x.Id)
            .SingleAsync();

        return await Db.PostedJournalLines
            .Where(x =>
                x.LedgerAccountId == accountId)
            .SumAsync(x => x.Debit - x.Credit);
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await connection.DisposeAsync();
    }
}
