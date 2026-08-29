using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record DemoDataSummary(
    DateOnly AsOfDate,
    DateOnly StartDate,
    int CompanyCount,
    int BranchCount,
    int DivisionCount,
    int CustomerCount,
    int SupplierCount,
    int SalesInvoiceCount,
    int SupplierBillCount,
    int CustomerReceiptCount,
    int SupplierPaymentCount,
    int CreditNoteCount,
    decimal NetSales,
    decimal AnnualisedNetSales);

public sealed class DemoDataService(
    ApplicationDbContext db,
    IWebHostEnvironment environment,
    PlatformAdminAccessService platformAccess)
{
    public const string DemoGroupName = "Demo";

    private static readonly Guid LegacyDemoGroupId =
        Guid.Parse("8d13b614-47f4-50eb-a994-7e0ca5c49cc0");

    private const string LegacyDemoGroupName = "Account Island Demo Group";

    private const string DemoSeed = "AccountIslandDemoV1";

    public async Task<DemoDataSummary?> GetSummaryAsync(
        string userId,
        CancellationToken ct = default)
    {
        if (!environment.IsDevelopment())
        {
            return null;
        }

        if (!await platformAccess.IsPlatformAdministratorAsync(userId, ct))
        {
            return null;
        }

        var demoGroupId = await db.OrganisationGroups
            .Where(x => x.IsDemo && x.Name == DemoGroupName)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(ct);

        return demoGroupId is not null
            ? await BuildSummaryAsync(demoGroupId.Value, ct)
            : null;
    }

    public async Task<DemoDataSummary> ResetAndGenerateAsync(
        string userId,
        DateOnly asOfDate,
        CancellationToken ct = default)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Demo data can only be generated in the Development environment.");
        }

        if (!await db.Users.AnyAsync(x => x.Id == userId, ct))
        {
            throw new UnauthorizedAccessException("A signed-in user is required.");
        }

        if (!await platformAccess.IsPlatformAdministratorAsync(userId, ct))
        {
            throw new UnauthorizedAccessException(
                "Platform administrator access is required to reset demo data.");
        }

        var earliest = new DateOnly(2020, 1, 1);
        var latest = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(2);
        if (asOfDate < earliest || asOfDate > latest)
        {
            throw new InvalidOperationException(
                $"Choose an as-of date between {earliest:dd MMM yyyy} and {latest:dd MMM yyyy}.");
        }

        var demoGroupId = await db.OrganisationGroups
            .Where(x => x.IsDemo && x.Name == DemoGroupName)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(ct);
        if (demoGroupId is null)
        {
            throw new InvalidOperationException(
                "The Demo tenant does not exist. Create the Demo company and owner login first.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await DeleteLegacyDemoAsync(ct);
        await DeleteExistingDemoAsync(demoGroupId.Value, preserveTenant: true, ct);
        await GenerateAsync(demoGroupId.Value, userId, asOfDate, ct);
        await transaction.CommitAsync(ct);

        db.ChangeTracker.Clear();
        return await BuildSummaryAsync(demoGroupId.Value, ct);
    }

    private async Task DeleteLegacyDemoAsync(CancellationToken ct)
    {
        if (await db.OrganisationGroups.AnyAsync(x => x.Id == LegacyDemoGroupId, ct))
        {
            await DeleteExistingDemoAsync(
                LegacyDemoGroupId,
                preserveTenant: false,
                ct);
        }
    }

    private async Task DeleteExistingDemoAsync(
        Guid demoGroupId,
        bool preserveTenant,
        CancellationToken ct)
    {
        var existing = await db.OrganisationGroups
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == demoGroupId, ct);

        if (existing is null)
        {
            return;
        }

        var expectedName = preserveTenant ? DemoGroupName : LegacyDemoGroupName;
        if (!existing.IsDemo ||
            !string.Equals(existing.Name, expectedName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The selected tenant is not the dedicated Demo tenant.");
        }

        var organisationIds = await db.Organisations
            .Where(x => x.OrganisationGroupId == demoGroupId)
            .Select(x => x.Id)
            .ToArrayAsync(ct);

        if (organisationIds.Length == 0)
        {
            if (!preserveTenant)
            {
                await db.GroupEliminationJournalLines
                    .Where(x => x.GroupEliminationJournal.OrganisationGroupId == demoGroupId)
                    .ExecuteDeleteAsync(ct);
                await db.GroupEliminationJournals
                    .Where(x => x.OrganisationGroupId == demoGroupId)
                    .ExecuteDeleteAsync(ct);
                await db.OrganisationGroupMemberships
                    .Where(x => x.OrganisationGroupId == demoGroupId)
                    .ExecuteDeleteAsync(ct);
                await db.GroupExchangeRates
                    .Where(x => x.OrganisationGroupId == demoGroupId)
                    .ExecuteDeleteAsync(ct);
                await db.OrganisationGroups
                    .Where(x => x.Id == demoGroupId)
                    .ExecuteDeleteAsync(ct);
                return;
            }

            throw new InvalidOperationException(
                "The Demo tenant must contain its landing company before data can be generated.");
        }

        var invoiceIds = await db.SalesInvoices
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .Select(x => x.Id).ToArrayAsync(ct);
        var billIds = await db.SupplierBills
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .Select(x => x.Id).ToArrayAsync(ct);
        var receiptIds = await db.CustomerReceipts
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .Select(x => x.Id).ToArrayAsync(ct);
        var journalIds = await db.PostedJournals
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .Select(x => x.Id).ToArrayAsync(ct);
        var projectIds = await db.Projects
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .Select(x => x.Id).ToArrayAsync(ct);

        await db.CashflowScenarioEvents
            .Where(x => organisationIds.Contains(x.CashflowScenario.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.CashflowScenarios
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.SalesInvoiceVoids
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.SalesCreditNoteReversals
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.CustomerReceiptReversals
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.SupplierBillVoids
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.SupplierCreditNoteReversals
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.SupplierPaymentReversals
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.BankTransferReversals
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.FixedAssetDisposals
            .Where(x => organisationIds.Contains(x.FixedAsset.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.FixedAssetDepreciations
            .Where(x => organisationIds.Contains(x.FixedAsset.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.InventoryMovements
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.RecurringSalesInvoiceGenerations
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.RecurringSupplierBillGenerations
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.RecurringInvoiceAutomationRuns
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.ProjectWipPostings
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.ProjectProgressClaims
            .Where(x => projectIds.Contains(x.ProjectId))
            .ExecuteDeleteAsync(ct);
        await db.ProjectVariations
            .Where(x => projectIds.Contains(x.ProjectId))
            .ExecuteDeleteAsync(ct);
        await db.CustomerReceiptAllocations
            .Where(x => receiptIds.Contains(x.CustomerReceiptId))
            .ExecuteDeleteAsync(ct);
        await db.SalesQuoteLines
            .Where(x => organisationIds.Contains(x.SalesQuote.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.SalesQuotes
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.PurchaseOrderLines
            .Where(x => organisationIds.Contains(x.PurchaseOrder.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.PurchaseOrders
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.PurchaseRequisitionLines
            .Where(x => organisationIds.Contains(x.PurchaseRequisition.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.PurchaseRequisitions
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.PurchaseApprovalPolicies
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.RecurringSalesInvoiceLines
            .Where(x => organisationIds.Contains(x.RecurringSalesInvoice.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.RecurringSalesInvoices
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.RecurringSupplierBillLines
            .Where(x => organisationIds.Contains(x.RecurringSupplierBill.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.RecurringSupplierBills
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.SalesInvoiceLines
            .Where(x => invoiceIds.Contains(x.SalesInvoiceId))
            .ExecuteDeleteAsync(ct);
        await db.SupplierBillAttachments
            .Where(x => billIds.Contains(x.SupplierBillId))
            .ExecuteDeleteAsync(ct);
        await db.SupplierBillLines
            .Where(x => billIds.Contains(x.SupplierBillId))
            .ExecuteDeleteAsync(ct);
        await db.SalesCreditNotes
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.SupplierCreditNotes
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.SupplierPaymentApprovals
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.CustomerReceipts
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.SupplierPayments
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.FiscalisationRecords
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.SalesInvoices
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.SupplierBills
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.SupplierBillDrafts
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.BankReconciliationSessions
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.BankStatementLines
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.BankTransfers
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.FixedAssets
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.PostedJournalLines
            .Where(x => journalIds.Contains(x.PostedJournalId))
            .ExecuteDeleteAsync(ct);
        await db.PostedJournals
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.Notifications
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.AuditEvents
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.AccountBudgets
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.TransactionExchangeRates
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.OrganisationCurrencies
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.BankRules
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.BusinessPartyDocuments
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.BusinessParties
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.ProductItems
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.AccountingPeriods
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.OrganisationInvitations
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.OrganisationDimensionAccessGrants
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.AccountantEngagements
            .Where(x => organisationIds.Contains(x.PracticeOrganisationId) || organisationIds.Contains(x.ClientOrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.ProjectCostCodes
            .Where(x => projectIds.Contains(x.ProjectId))
            .ExecuteDeleteAsync(ct);
        await db.Projects
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.Divisions
            .Where(x => organisationIds.Contains(x.Branch.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.Branches
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        if (!preserveTenant)
        {
            await db.OrganisationMemberships
                .Where(x => organisationIds.Contains(x.OrganisationId))
                .ExecuteDeleteAsync(ct);
        }
        await db.LedgerAccounts
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        if (!preserveTenant)
        {
            await db.Organisations
                .Where(x => x.OrganisationGroupId == demoGroupId)
                .ExecuteDeleteAsync(ct);
        }
        await db.GroupExchangeRates
            .Where(x => x.OrganisationGroupId == demoGroupId)
            .ExecuteDeleteAsync(ct);
        await db.GroupEliminationJournalLines
            .Where(x => x.GroupEliminationJournal.OrganisationGroupId == demoGroupId)
            .ExecuteDeleteAsync(ct);
        await db.GroupEliminationJournals
            .Where(x => x.OrganisationGroupId == demoGroupId)
            .ExecuteDeleteAsync(ct);
        if (!preserveTenant)
        {
            await db.OrganisationGroupMemberships
                .Where(x => x.OrganisationGroupId == demoGroupId)
                .ExecuteDeleteAsync(ct);
            await db.OrganisationGroups
                .Where(x => x.Id == demoGroupId)
                .ExecuteDeleteAsync(ct);
        }

        db.ChangeTracker.Clear();
    }

    private async Task GenerateAsync(
        Guid demoGroupId,
        string userId,
        DateOnly asOfDate,
        CancellationToken ct)
    {
        var startDate = asOfDate.AddMonths(-3).AddDays(1);
        var generatedAt = new DateTimeOffset(
            asOfDate.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        var group = await db.OrganisationGroups
            .SingleAsync(x => x.Id == demoGroupId, ct);
        group.Name = DemoGroupName;
        group.PresentationCurrency = "FJD";
        group.IsDemo = true;
        group.Status = TenantStatus.Active;
        group.SuspendedAt = null;

        var existingCompanies = await db.Organisations
            .Where(x => x.OrganisationGroupId == demoGroupId)
            .ToListAsync(ct);
        existingCompanies = existingCompanies
            .OrderBy(x => x.CreatedAt)
            .ToList();
        var primaryCompany = existingCompanies
            .FirstOrDefault(x => x.LegalName == "Demo")
            ?? existingCompanies.First();
        ConfigureCompany(
            primaryCompany,
            group.Id,
            "Demo",
            "Demo Trading",
            "50-12345-0",
            generatedAt);

        var secondaryCompanyId = StableGuid($"{group.Id}:company-b");
        var secondaryCompany = existingCompanies
            .FirstOrDefault(x => x.Id == secondaryCompanyId)
            ?? existingCompanies.FirstOrDefault(x => x.Id != primaryCompany.Id);
        if (secondaryCompany is null)
        {
            secondaryCompany = CreateCompany(
                secondaryCompanyId,
                group.Id,
                "Demo Services Pte Limited",
                "Demo Services",
                "50-67890-0",
                generatedAt);
            db.Organisations.Add(secondaryCompany);
        }
        else
        {
            ConfigureCompany(
                secondaryCompany,
                group.Id,
                "Demo Services Pte Limited",
                "Demo Services",
                "50-67890-0",
                generatedAt);
        }

        var companies = new[] { primaryCompany, secondaryCompany };
        var extraCompanies = existingCompanies
            .Where(x => companies.All(company => company.Id != x.Id))
            .ToArray();
        if (extraCompanies.Length > 0)
        {
            var extraIds = extraCompanies.Select(x => x.Id).ToArray();
            await db.OrganisationMemberships
                .Where(x => extraIds.Contains(x.OrganisationId))
                .ExecuteDeleteAsync(ct);
            db.Organisations.RemoveRange(extraCompanies);
        }

        var groupMembers = await db.OrganisationGroupMemberships
            .Where(x => x.OrganisationGroupId == group.Id)
            .ToListAsync(ct);
        if (groupMembers.Count == 0)
        {
            throw new InvalidOperationException(
                "The Demo tenant must have an owner before data can be generated.");
        }

        var existingMemberships = await db.OrganisationMemberships
            .Where(x => companies.Select(company => company.Id).Contains(x.OrganisationId))
            .ToListAsync(ct);
        foreach (var company in companies)
        {
            foreach (var groupMember in groupMembers)
            {
                var membership = existingMemberships.SingleOrDefault(x =>
                    x.OrganisationId == company.Id && x.UserId == groupMember.UserId);
                if (membership is null)
                {
                    membership = new OrganisationMembership
                    {
                        OrganisationId = company.Id,
                        UserId = groupMember.UserId,
                        Role = MapOrganisationRole(groupMember.Role),
                        CreatedAt = generatedAt
                    };
                    db.OrganisationMemberships.Add(membership);
                    existingMemberships.Add(membership);
                }
                else
                {
                    membership.Role = MapOrganisationRole(groupMember.Role);
                    membership.DimensionAccessMode = DimensionAccessMode.All;
                }
            }
        }

        var branches = new[]
        {
            CreateBranch("a-suva", companies[0], "SUVA", "Suva Head Office", true, generatedAt),
            CreateBranch("a-nadi", companies[0], "NADI", "Nadi Branch", false, generatedAt),
            CreateBranch("b-suva", companies[1], "SUVA", "Suva Office", true, generatedAt),
            CreateBranch("b-lautoka", companies[1], "LAUTOKA", "Lautoka Office", false, generatedAt)
        };
        AddDivision(branches[0], "SALES", "Sales", generatedAt);
        AddDivision(branches[0], "PURCH", "Purchasing", generatedAt);
        AddDivision(branches[1], "OPS", "Operations", generatedAt);
        AddDivision(branches[2], "FIN", "Finance", generatedAt);
        AddDivision(branches[2], "ADMIN", "Administration", generatedAt);
        db.Branches.AddRange(branches);

        var accountsByCompany = new Dictionary<Guid, Dictionary<string, LedgerAccount>>();
        foreach (var company in companies)
        {
            var accounts = FijiStarterChart.For(company.Id).ToList();
            accounts.AddRange(
            [
                new LedgerAccount
                {
                    OrganisationId = company.Id,
                    Code = "1510",
                    Name = "Accumulated Depreciation",
                    Type = AccountType.Asset,
                    IsSystemAccount = true
                },
                new LedgerAccount
                {
                    OrganisationId = company.Id,
                    Code = "6700",
                    Name = "Depreciation Expense",
                    Type = AccountType.Expense,
                    IsSystemAccount = true
                }
            ]);
            foreach (var account in accounts)
            {
                account.Id = StableGuid($"{company.Id}:account:{account.Code}");
            }

            db.LedgerAccounts.AddRange(accounts);
            accountsByCompany[company.Id] = accounts.ToDictionary(x => x.Code);
        }

        var customerNames = new[]
        {
            "Pacific Harbour Resorts", "Suva City Hardware", "Blue Lagoon Marine", "Viti Foods Wholesale",
            "Nadi Airport Services", "Suncoast Property Group", "Koro Island Retail", "Navua Construction",
            "Tanoa Hospitality Supplies", "Rewa Distribution", "Denarau Events", "Lautoka Engineering",
            "South Seas Logistics", "Fiji Fresh Markets", "Mamanuca Adventures", "Ba Industrial Services",
            "Kadavu Eco Lodges", "Labasa Commercial", "Coral Reef Catering", "Sigatoka Town Traders"
        };
        var supplierNames = new[]
        {
            "Fiji Office Supplies", "Pacific Fuel Distributors", "Vodafone Fiji", "Energy Fiji Limited",
            "Island Freight Services", "Suva Commercial Properties", "Westpac Fiji", "Cloud Pacific",
            "Nadi Equipment Hire", "KPMG Fiji", "Vinod Patel", "Fiji Water Authority"
        };

        var customersByCompany = new Dictionary<Guid, List<BusinessParty>>();
        var suppliersByCompany = new Dictionary<Guid, List<BusinessParty>>();
        foreach (var company in companies)
        {
            var customers = customerNames.Select((name, index) => new BusinessParty
            {
                Id = StableGuid($"{company.Id}:customer:{index}"),
                OrganisationId = company.Id,
                Name = name,
                Email = $"accounts@{Slug(name)}.fj",
                Phone = $"+679 33{index + 10:D2} {index + 2100:D4}",
                Address = index % 2 == 0 ? "Suva, Fiji" : "Western Division, Fiji",
                Type = PartyType.Customer,
                DefaultSalesAccountId = accountsByCompany[company.Id]["4000"].Id,
                DefaultSalesVatTreatment = VatTreatment.Standard,
                DefaultSalesInvoiceDueDays = index % 5 == 0 ? 14 : 30
            }).ToList();
            var suppliers = supplierNames.Select((name, index) => new BusinessParty
            {
                Id = StableGuid($"{company.Id}:supplier:{index}"),
                OrganisationId = company.Id,
                Name = name,
                Email = $"billing@{Slug(name)}.fj",
                Phone = $"+679 32{index + 10:D2} {index + 3100:D4}",
                Address = index % 3 == 0 ? "Suva, Fiji" : "Nadi, Fiji",
                Type = PartyType.Supplier,
                DefaultPurchaseAccountId = accountsByCompany[company.Id][ExpenseCode(index)].Id,
                DefaultPurchaseVatTreatment = VatTreatment.Standard,
                DefaultSupplierBillDueDays = index % 4 == 0 ? 14 : 30
            }).ToList();
            customersByCompany[company.Id] = customers;
            suppliersByCompany[company.Id] = suppliers;
            db.BusinessParties.AddRange(customers);
            db.BusinessParties.AddRange(suppliers);
        }

        await db.SaveChangesAsync(ct);

        var random = new Random(StableSeed(asOfDate));
        foreach (var (company, companyIndex) in companies.Select((value, index) => (value, index)))
        {
            var companyBranches = branches.Where(x => x.OrganisationId == company.Id).ToArray();
            var accounts = accountsByCompany[company.Id];
            var customers = customersByCompany[company.Id];
            var suppliers = suppliersByCompany[company.Id];
            var invoiceCount = companyIndex == 0 ? 126 : 54;
            var companySalesTarget = companyIndex == 0 ? 875_000m : 375_000m;
            var invoiceNets = AllocateAmounts(random, invoiceCount, companySalesTarget, 0.45m, 1.9m);
            var invoices = new List<SalesInvoice>();
            var bankJournals = new List<PostedJournal>();
            long journalSequence = 0;
            AddCurrencyDemo(company, startDate, userId, generatedAt);

            for (var i = 0; i < invoiceCount; i++)
            {
                var issueDate = i == 0
                    ? startDate
                    : i == invoiceCount - 1
                        ? asOfDate
                        : RandomDate(random, startDate, asOfDate);
                var branch = companyBranches[i % companyBranches.Length];
                var division = SalesDivision(branch);
                var net = invoiceNets[i];
                var vat = Vat(net, issueDate);
                var currency = SalesCurrency(i);
                var exchangeRate = DemoExchangeRate(currency);
                var transactionNet = ConvertToTransactionCurrency(net, exchangeRate);
                var transactionVat = ConvertToTransactionCurrency(vat, exchangeRate);
                var invoice = new SalesInvoice
                {
                    Id = StableGuid($"{company.Id}:invoice:{i}"),
                    OrganisationId = company.Id,
                    BranchId = branch.Id,
                    DivisionId = division.Id,
                    CustomerId = customers[WeightedIndex(random, customers.Count)].Id,
                    SequenceNumber = i + 1,
                    InvoiceNumber = $"INV-{i + 1:D6}",
                    IssueDate = issueDate,
                    DueDate = issueDate.AddDays(i % 7 == 0 ? 14 : 30),
                    Currency = currency,
                    ExchangeRateToBase = exchangeRate,
                    TransactionSubtotal = transactionNet,
                    TransactionVatTotal = transactionVat,
                    TransactionTotal = transactionNet + transactionVat,
                    Status = InvoiceStatus.Posted,
                    Subtotal = net,
                    VatTotal = vat,
                    Total = net + vat,
                    CreatedAt = At(issueDate),
                    CreatedByUserId = userId
                };
                invoice.Lines.Add(new SalesInvoiceLine
                {
                    Id = StableGuid($"{company.Id}:invoice-line:{i}"),
                    Description = SalesDescription(i),
                    CustomerPurchaseOrderNumber = i % 3 == 0 ? $"PO-{issueDate.Year}-{i + 4100}" : null,
                    Quantity = 1,
                    UnitPrice = net,
                    TransactionUnitPrice = transactionNet,
                    VatTreatment = VatTreatment.Standard,
                    VatRate = vat == 0 ? 0 : vat / net,
                    NetAmount = net,
                    VatAmount = vat,
                    GrossAmount = net + vat,
                    TransactionNetAmount = transactionNet,
                    TransactionVatAmount = transactionVat,
                    TransactionGrossAmount = transactionNet + transactionVat,
                    RevenueAccountId = accounts[i % 11 == 0 ? "4100" : "4000"].Id
                });
                var journal = SalesJournal(company, branch, invoice, accounts, ++journalSequence, userId);
                invoice.PostedJournalId = journal.Id;
                invoices.Add(invoice);
                db.SalesInvoices.Add(invoice);
                db.PostedJournals.Add(journal);
            }

            var billCount = companyIndex == 0 ? 54 : 30;
            var billTarget = companySalesTarget * 0.56m;
            var billNets = AllocateAmounts(random, billCount, billTarget, 0.35m, 2.1m);
            var bills = new List<SupplierBill>();
            for (var i = 0; i < billCount; i++)
            {
                var billDate = RandomDate(random, startDate, asOfDate);
                var branch = companyBranches[(i + 1) % companyBranches.Length];
                var division = PurchaseDivision(branch);
                var net = billNets[i];
                var vat = Vat(net, billDate);
                var currency = PurchaseCurrency(i);
                var exchangeRate = DemoExchangeRate(currency);
                var transactionNet = ConvertToTransactionCurrency(net, exchangeRate);
                var transactionVat = ConvertToTransactionCurrency(vat, exchangeRate);
                var expenseCode = ExpenseCode(i);
                var bill = new SupplierBill
                {
                    Id = StableGuid($"{company.Id}:bill:{i}"),
                    OrganisationId = company.Id,
                    BranchId = branch.Id,
                    DivisionId = division.Id,
                    SupplierId = suppliers[i % suppliers.Count].Id,
                    SequenceNumber = i + 1,
                    BillNumber = $"BILL-{i + 1:D6}",
                    SupplierReference = $"SUP-{billDate:yyMM}-{i + 1200}",
                    BillDate = billDate,
                    DueDate = billDate.AddDays(i % 5 == 0 ? 14 : 30),
                    Currency = currency,
                    ExchangeRateToBase = exchangeRate,
                    TransactionSubtotal = transactionNet,
                    TransactionVatTotal = transactionVat,
                    TransactionTotal = transactionNet + transactionVat,
                    Status = BillStatus.Posted,
                    Subtotal = net,
                    VatTotal = vat,
                    Total = net + vat,
                    CreatedAt = At(billDate),
                    CreatedByUserId = userId
                };
                bill.Lines.Add(new SupplierBillLine
                {
                    Id = StableGuid($"{company.Id}:bill-line:{i}"),
                    Description = PurchaseDescription(i),
                    Quantity = 1,
                    UnitPrice = net,
                    TransactionUnitPrice = transactionNet,
                    VatTreatment = VatTreatment.Standard,
                    VatRate = vat == 0 ? 0 : vat / net,
                    NetAmount = net,
                    VatAmount = vat,
                    GrossAmount = net + vat,
                    TransactionNetAmount = transactionNet,
                    TransactionVatAmount = transactionVat,
                    TransactionGrossAmount = transactionNet + transactionVat,
                    ExpenseAccountId = accounts[expenseCode].Id
                });
                var journal = BillJournal(company, branch, bill, accounts, ++journalSequence, userId);
                bill.PostedJournalId = journal.Id;
                bills.Add(bill);
                db.SupplierBills.Add(bill);
                db.PostedJournals.Add(journal);
            }

            await db.SaveChangesAsync(ct);

            foreach (var (invoice, i) in invoices.Select((value, index) => (value, index)))
            {
                var paymentDate = invoice.IssueDate.AddDays(7 + i % 24);
                if (paymentDate > asOfDate || i % 7 == 0)
                {
                    continue;
                }

                var amount = i % 9 == 0 ? decimal.Round(invoice.Total * 0.5m, 2) : invoice.Total;
                var journal = ReceiptJournal(company, invoice, accounts, amount, paymentDate, ++journalSequence, userId);
                var receipt = new CustomerReceipt
                {
                    Id = StableGuid($"{company.Id}:receipt:{i}"),
                    OrganisationId = company.Id,
                    BranchId = invoice.BranchId,
                    DivisionId = invoice.DivisionId,
                    CustomerId = invoice.CustomerId,
                    ReceiptDate = paymentDate,
                    Reference = $"RCPT-{i + 1:D6}",
                    Amount = amount,
                    BankAccountId = accounts["1000"].Id,
                    PostedJournalId = journal.Id,
                    CreatedAt = At(paymentDate),
                    CreatedByUserId = userId,
                    Allocations =
                    [
                        new CustomerReceiptAllocation
                        {
                            Id = StableGuid($"{company.Id}:receipt-allocation:{i}"),
                            SalesInvoiceId = invoice.Id,
                            Amount = amount
                        }
                    ]
                };
                invoice.AmountPaid = amount;
                invoice.Status = amount == invoice.Total ? InvoiceStatus.Paid : InvoiceStatus.PartPaid;
                db.CustomerReceipts.Add(receipt);
                db.PostedJournals.Add(journal);
                bankJournals.Add(journal);
            }

            foreach (var (bill, i) in bills.Select((value, index) => (value, index)))
            {
                var paymentDate = bill.BillDate.AddDays(10 + i % 20);
                if (paymentDate > asOfDate || i % 6 == 0)
                {
                    continue;
                }

                var amount = i % 10 == 0 ? decimal.Round(bill.Total * 0.6m, 2) : bill.Total;
                var journal = PaymentJournal(company, bill, accounts, amount, paymentDate, ++journalSequence, userId);
                db.SupplierPayments.Add(new SupplierPayment
                {
                    Id = StableGuid($"{company.Id}:payment:{i}"),
                    OrganisationId = company.Id,
                    BranchId = bill.BranchId,
                    DivisionId = bill.DivisionId,
                    SupplierId = bill.SupplierId,
                    SupplierBillId = bill.Id,
                    PaymentDate = paymentDate,
                    Reference = $"PAY-{i + 1:D6}",
                    Amount = amount,
                    BankAccountId = accounts["1000"].Id,
                    PostedJournalId = journal.Id,
                    CreatedAt = At(paymentDate),
                    CreatedByUserId = userId
                });
                bill.AmountPaid = amount;
                bill.Status = amount == bill.Total ? BillStatus.Paid : BillStatus.PartPaid;
                db.PostedJournals.Add(journal);
                bankJournals.Add(journal);
            }

            for (var i = 0; i < 3; i++)
            {
                var invoice = invoices[8 + i * 17];
                var creditDate = Min(asOfDate, invoice.IssueDate.AddDays(12));
                var net = decimal.Round(invoice.Subtotal * 0.12m, 2);
                var vat = Vat(net, creditDate);
                var total = net + vat;
                if (invoice.AmountPaid + total > invoice.Total)
                {
                    continue;
                }

                var journal = CreditJournal(company, invoice, accounts, net, vat, creditDate, ++journalSequence, userId);
                db.SalesCreditNotes.Add(new SalesCreditNote
                {
                    Id = StableGuid($"{company.Id}:credit:{i}"),
                    OrganisationId = company.Id,
                    SalesInvoiceId = invoice.Id,
                    SequenceNumber = i + 1,
                    CreditNoteNumber = $"CN-{i + 1:D6}",
                    CreditDate = creditDate,
                    Reason = i == 0 ? "Pricing adjustment" : "Service allowance",
                    Subtotal = net,
                    VatTotal = vat,
                    Total = total,
                    PostedJournalId = journal.Id,
                    CreatedAt = At(creditDate),
                    CreatedByUserId = userId
                });
                invoice.AmountCredited = total;
                invoice.Status = invoice.AmountPaid + total == invoice.Total
                    ? InvoiceStatus.Credited
                    : InvoiceStatus.PartPaid;
                db.PostedJournals.Add(journal);
            }

            AddNotificationDemo(
                company,
                invoices,
                bills,
                customers,
                suppliers,
                asOfDate,
                generatedAt);

            AddInventoryDemo(
                company,
                companyBranches,
                accounts,
                startDate,
                asOfDate,
                userId,
                ref journalSequence);
            AddFixedAssetDemo(
                company,
                companyBranches,
                accounts,
                startDate,
                asOfDate,
                userId,
                generatedAt,
                ref journalSequence);

            AddBudgetDemo(
                company,
                invoices,
                bills,
                accounts,
                userId,
                generatedAt);

            AddBankingDemo(
                company,
                accounts["1000"],
                bankJournals,
                startDate,
                asOfDate,
                userId);

            AddAccountingPeriodDemo(
                company,
                startDate,
                asOfDate,
                userId,
                generatedAt);

            company.NextSalesInvoiceNumber = invoiceCount + 1;
            company.NextSupplierBillNumber = billCount + 1;
            company.NextSalesCreditNoteNumber = 4;
        }

        AddGroupEliminationDemo(group, asOfDate, userId, generatedAt);

        db.AuditEvents.AddRange(companies.Select(company => new AuditEvent
        {
            OrganisationId = company.Id,
            EventType = "DemoDataGenerated",
            EntityType = nameof(Organisation),
            EntityId = company.Id.ToString(),
            UserId = userId,
            OccurredAt = generatedAt,
            JsonData = JsonSerializer.Serialize(new { Seed = DemoSeed, AsOfDate = asOfDate, StartDate = startDate })
        }));
        db.PlatformAuditEvents.Add(new PlatformAuditEvent
        {
            AdministratorUserId = userId,
            EventType = "DemoDataReset",
            OrganisationGroupId = group.Id,
            Reason = $"Reset demo data as of {asOfDate:yyyy-MM-dd}",
            OccurredAt = generatedAt,
            JsonData = JsonSerializer.Serialize(new { Seed = DemoSeed, AsOfDate = asOfDate, StartDate = startDate })
        });

        await db.SaveChangesAsync(ct);
    }

    private void AddNotificationDemo(
        Organisation company,
        IReadOnlyCollection<SalesInvoice> invoices,
        IReadOnlyCollection<SupplierBill> bills,
        IReadOnlyCollection<BusinessParty> customers,
        IReadOnlyCollection<BusinessParty> suppliers,
        DateOnly asOfDate,
        DateTimeOffset generatedAt)
    {
        var reminderDate = asOfDate.AddDays(7);
        var customerNames = customers.ToDictionary(x => x.Id, x => x.Name);
        var supplierNames = suppliers.ToDictionary(x => x.Id, x => x.Name);

        foreach (var invoice in invoices.Where(x =>
                     x.DueDate > asOfDate &&
                     x.DueDate <= reminderDate &&
                     x.AmountPaid + x.AmountCredited < x.Total &&
                     x.Status is not InvoiceStatus.Paid and
                         not InvoiceStatus.Voided and
                         not InvoiceStatus.Credited and
                         not InvoiceStatus.Draft))
        {
            var daysUntilDue = invoice.DueDate.DayNumber - asOfDate.DayNumber;
            db.Notifications.Add(new Notification
            {
                Id = StableGuid($"{company.Id}:notification:invoice:{invoice.Id}"),
                OrganisationId = company.Id,
                Title = "Invoice due soon",
                Message = $"{invoice.InvoiceNumber} · {customerNames[invoice.CustomerId]} · due in {daysUntilDue} days.",
                Type = NotificationType.PaymentDueSoon,
                Severity = NotificationSeverity.Warning,
                RelatedEntityType = nameof(SalesInvoice),
                RelatedEntityId = invoice.Id.ToString(),
                Amount = invoice.Total - invoice.AmountPaid - invoice.AmountCredited,
                Currency = invoice.Currency,
                CreatedAt = generatedAt,
                CreatedAtTicks = generatedAt.UtcTicks
            });
        }

        foreach (var bill in bills.Where(x =>
                     x.DueDate > asOfDate &&
                     x.DueDate <= reminderDate &&
                     x.AmountPaid + x.AmountCredited < x.Total &&
                     x.Status is not BillStatus.Paid and
                         not BillStatus.Voided and
                         not BillStatus.Credited))
        {
            var daysUntilDue = bill.DueDate.DayNumber - asOfDate.DayNumber;
            db.Notifications.Add(new Notification
            {
                Id = StableGuid($"{company.Id}:notification:bill:{bill.Id}"),
                OrganisationId = company.Id,
                Title = "Supplier bill due soon",
                Message = $"{bill.BillNumber} · {supplierNames[bill.SupplierId]} · due in {daysUntilDue} days.",
                Type = NotificationType.PaymentDueSoon,
                Severity = NotificationSeverity.Warning,
                RelatedEntityType = nameof(SupplierBill),
                RelatedEntityId = bill.Id.ToString(),
                Amount = bill.Total - bill.AmountPaid - bill.AmountCredited,
                Currency = bill.Currency,
                CreatedAt = generatedAt,
                CreatedAtTicks = generatedAt.UtcTicks
            });
        }
    }

    private void AddAccountingPeriodDemo(
        Organisation company,
        DateOnly startDate,
        DateOnly asOfDate,
        string userId,
        DateTimeOffset generatedAt)
    {
        var month = new DateOnly(startDate.Year, startDate.Month, 1);
        var currentMonth = new DateOnly(asOfDate.Year, asOfDate.Month, 1);

        while (month <= currentMonth)
        {
            var end = month.AddMonths(1).AddDays(-1);
            var isLocked = month < currentMonth;
            var period = new AccountingPeriod
            {
                Id = StableGuid($"{company.Id}:accounting-period:{month:yyyy-MM}"),
                OrganisationId = company.Id,
                Name = month.ToString("MMMM yyyy"),
                StartsOn = month,
                EndsOn = end,
                IsLocked = isLocked,
                LockedAt = isLocked ? generatedAt : null,
                LockedByUserId = isLocked ? userId : null
            };
            db.AccountingPeriods.Add(period);

            if (isLocked)
            {
                db.AuditEvents.Add(new AuditEvent
                {
                    OrganisationId = company.Id,
                    EventType = "AccountingPeriodLocked",
                    EntityType = nameof(AccountingPeriod),
                    EntityId = period.Id.ToString(),
                    UserId = userId,
                    OccurredAt = generatedAt,
                    JsonData = JsonSerializer.Serialize(new
                    {
                        period.Name,
                        period.StartsOn,
                        period.EndsOn,
                        Locked = true,
                        UnreconciledBankStatementLines = 0,
                        IncompleteBankReconciliations = 0,
                        DraftSalesInvoices = 0,
                        DraftSupplierBills = 0,
                        FixedAssetsRequiringDepreciation = 0,
                        InventoryIntegrityWarnings = 0,
                        WarningsAcknowledged = false
                    })
                });
            }

            month = month.AddMonths(1);
        }
    }

    private void AddCurrencyDemo(
        Organisation company,
        DateOnly effectiveDate,
        string userId,
        DateTimeOffset generatedAt)
    {
        foreach (var (code, name) in new[]
        {
            ("AUD", "Australian dollar"),
            ("NZD", "New Zealand dollar"),
            ("USD", "United States dollar")
        })
        {
            db.OrganisationCurrencies.Add(new OrganisationCurrency
            {
                Id = StableGuid($"{company.Id}:currency:{code}"),
                OrganisationId = company.Id,
                Code = code,
                Name = name,
                IsActive = true,
                CreatedAt = generatedAt,
                CreatedByUserId = userId
            });
            db.TransactionExchangeRates.Add(new TransactionExchangeRate
            {
                Id = StableGuid($"{company.Id}:exchange-rate:{code}:{effectiveDate:yyyy-MM-dd}"),
                OrganisationId = company.Id,
                FromCurrency = code,
                ToCurrency = company.BaseCurrency,
                EffectiveDate = effectiveDate,
                Rate = DemoExchangeRate(code),
                Source = "Demo indicative rate",
                CreatedAt = generatedAt,
                CreatedByUserId = userId
            });
        }
    }

    private void AddInventoryDemo(
        Organisation company,
        IReadOnlyList<Branch> companyBranches,
        IReadOnlyDictionary<string, LedgerAccount> accounts,
        DateOnly startDate,
        DateOnly asOfDate,
        string userId,
        ref long journalSequence)
    {
        var specifications = new[]
        {
            (Code: "INV-CHAIR", Name: "Commercial office chair", Opening: 150m, Closing: 120m, UnitCost: 45m, Reorder: 50m),
            (Code: "INV-PUMP", Name: "Marine transfer pump", Opening: 30m, Closing: 18m, UnitCost: 220m, Reorder: 20m),
            (Code: "INV-CHILL", Name: "Hospitality display chiller", Opening: 12m, Closing: 8m, UnitCost: 680m, Reorder: 10m)
        };

        foreach (var (specification, index) in specifications.Select((value, index) => (value, index)))
        {
            var branch = companyBranches[index % companyBranches.Count];
            var division = SalesDivision(branch);
            var itemId = StableGuid($"{company.Id}:inventory-item:{specification.Code}");
            var openingValue = specification.Opening * specification.UnitCost;
            var issueQuantity = specification.Opening - specification.Closing;
            var issueValue = issueQuantity * specification.UnitCost;
            var openingDate = startDate.AddDays(index + 1);
            var issueDate = asOfDate.AddDays(-14 + index);
            var openingJournal = Journal(
                company,
                branch,
                openingDate,
                $"OPEN-{specification.Code}",
                $"Opening inventory · {specification.Name}",
                ++journalSequence,
                userId,
                (accounts["1200"].Id, openingValue, 0),
                (accounts["3200"].Id, 0, openingValue),
                division.Id);
            var issueJournal = Journal(
                company,
                branch,
                issueDate,
                $"ISSUE-{specification.Code}",
                $"Inventory issue · {specification.Name}",
                ++journalSequence,
                userId,
                (accounts["5000"].Id, issueValue, 0),
                (accounts["1200"].Id, 0, issueValue),
                division.Id);

            db.ProductItems.Add(new ProductItem
            {
                Id = itemId,
                OrganisationId = company.Id,
                Code = specification.Code,
                Name = specification.Name,
                Description = "Tracked Demo inventory item",
                Kind = ProductKind.TrackedItem,
                SalePrice = decimal.Round(specification.UnitCost * 1.65m, 2),
                PurchasePrice = specification.UnitCost,
                RevenueAccountId = accounts["4000"].Id,
                ExpenseAccountId = accounts["5000"].Id,
                QuantityOnHand = specification.Closing,
                AverageCost = specification.UnitCost,
                ReorderLevel = specification.Reorder,
                InventoryAccountId = accounts["1200"].Id,
                CostAdjustmentAccountId = accounts["5000"].Id,
                CreatedAt = At(openingDate)
            });
            db.InventoryMovements.AddRange(
                CreateInventoryMovement(itemId, branch, division, openingJournal, openingDate,
                    InventoryMovementType.OpeningBalance, specification.Opening, specification.UnitCost,
                    openingValue, userId, "Opening stock for the Demo period"),
                CreateInventoryMovement(itemId, branch, division, issueJournal, issueDate,
                    InventoryMovementType.AdjustmentDecrease, -issueQuantity, specification.UnitCost,
                    -issueValue, userId, "Demo stock issued to operations"));
            db.PostedJournals.AddRange(openingJournal, issueJournal);
        }
    }

    private static InventoryMovement CreateInventoryMovement(
        Guid itemId,
        Branch branch,
        Division division,
        PostedJournal journal,
        DateOnly date,
        InventoryMovementType type,
        decimal quantity,
        decimal unitCost,
        decimal value,
        string userId,
        string note) => new()
    {
        Id = StableGuid($"{journal.Id}:inventory-movement"),
        OrganisationId = branch.OrganisationId,
        BranchId = branch.Id,
        DivisionId = division.Id,
        ProductItemId = itemId,
        MovementDate = date,
        Type = type,
        QuantityChange = quantity,
        UnitCost = unitCost,
        ValueChange = value,
        Reference = journal.Reference,
        Note = note,
        PostedJournalId = journal.Id,
        PostedByUserId = userId,
        PostedAt = At(date)
    };

    private void AddFixedAssetDemo(
        Organisation company,
        IReadOnlyList<Branch> companyBranches,
        IReadOnlyDictionary<string, LedgerAccount> accounts,
        DateOnly startDate,
        DateOnly asOfDate,
        string userId,
        DateTimeOffset generatedAt,
        ref long journalSequence)
    {
        var specifications = new[]
        {
            (Number: "FA-0001", Name: "Delivery van", Cost: 75_000m, Residual: 15_000m, LifeMonths: 60),
            (Number: "FA-0002", Name: "Office and computer equipment", Cost: 24_000m, Residual: 4_000m, LifeMonths: 48)
        };

        foreach (var (specification, index) in specifications.Select((value, index) => (value, index)))
        {
            var branch = companyBranches[index % companyBranches.Count];
            var division = PurchaseDivision(branch);
            var acquisitionDate = startDate.AddDays(5 + index * 12);
            var assetId = StableGuid($"{company.Id}:fixed-asset:{specification.Number}");
            var acquisitionJournal = Journal(
                company,
                branch,
                acquisitionDate,
                $"ACQ-{specification.Number}",
                $"Fixed asset acquisition · {specification.Name}",
                ++journalSequence,
                userId,
                (accounts["1500"].Id, specification.Cost, 0),
                (accounts["2000"].Id, 0, specification.Cost),
                division.Id);
            var months = Math.Min(
                specification.LifeMonths,
                (asOfDate.Year - acquisitionDate.Year) * 12 +
                asOfDate.Month - acquisitionDate.Month + 1);
            var depreciation = decimal.Round(
                (specification.Cost - specification.Residual) * months / specification.LifeMonths,
                2,
                MidpointRounding.AwayFromZero);
            var depreciationJournal = Journal(
                company,
                branch,
                asOfDate,
                $"DEP-{specification.Number}-{asOfDate:yyyyMM}",
                $"Book depreciation through {asOfDate:dd MMM yyyy}",
                ++journalSequence,
                userId,
                (accounts["6700"].Id, depreciation, 0),
                (accounts["1510"].Id, 0, depreciation),
                division.Id);
            var asset = new FixedAsset
            {
                Id = assetId,
                OrganisationId = company.Id,
                AssetNumber = specification.Number,
                Name = specification.Name,
                AcquisitionDate = acquisitionDate,
                Cost = specification.Cost,
                ResidualValue = specification.Residual,
                UsefulLifeMonths = specification.LifeMonths,
                AssetAccountId = accounts["1500"].Id,
                DepreciationExpenseAccountId = accounts["6700"].Id,
                AccumulatedDepreciationAccountId = accounts["1510"].Id,
                AcquisitionJournalId = acquisitionJournal.Id,
                CreatedAt = generatedAt,
                CreatedByUserId = userId,
                DepreciationEntries =
                [
                    new FixedAssetDepreciation
                    {
                        Id = StableGuid($"{assetId}:depreciation:{asOfDate:yyyy-MM-dd}"),
                        ThroughDate = asOfDate,
                        Amount = depreciation,
                        PostedJournalId = depreciationJournal.Id,
                        PostedAt = generatedAt,
                        PostedByUserId = userId
                    }
                ]
            };
            db.FixedAssets.Add(asset);
            db.PostedJournals.AddRange(acquisitionJournal, depreciationJournal);
        }
    }

    private void AddGroupEliminationDemo(
        OrganisationGroup group,
        DateOnly asOfDate,
        string userId,
        DateTimeOffset generatedAt)
    {
        var journalId = StableGuid($"{group.Id}:elimination:{asOfDate.Year}:intercompany-trading");
        var journal = new GroupEliminationJournal
        {
            Id = journalId,
            OrganisationGroupId = group.Id,
            EntryDate = asOfDate.AddDays(-7),
            Reference = $"ELIM-{asOfDate.Year}-001",
            Description = "Eliminate Demo intercompany trading and settlement balances",
            Currency = group.PresentationCurrency,
            PostedByUserId = userId,
            PostedAt = generatedAt,
            Lines =
            [
                EliminationLine(journalId, 0, "4000", "Sales", AccountType.Revenue,
                    "Eliminate intercompany revenue", 25_000m, 0m),
                EliminationLine(journalId, 1, "5000", "Cost of Sales", AccountType.Expense,
                    "Eliminate intercompany purchases", 0m, 25_000m),
                EliminationLine(journalId, 2, "2000", "Accounts Payable", AccountType.Liability,
                    "Eliminate intercompany payable", 10_000m, 0m),
                EliminationLine(journalId, 3, "1100", "Accounts Receivable", AccountType.Asset,
                    "Eliminate intercompany receivable", 0m, 10_000m)
            ]
        };
        db.GroupEliminationJournals.Add(journal);
    }

    private static GroupEliminationJournalLine EliminationLine(
        Guid journalId,
        int index,
        string accountCode,
        string accountName,
        AccountType accountType,
        string description,
        decimal debit,
        decimal credit) => new()
    {
        Id = StableGuid($"{journalId}:line:{index}"),
        AccountCode = accountCode,
        AccountName = accountName,
        AccountType = accountType,
        Description = description,
        Debit = debit,
        Credit = credit
    };

    private void AddBudgetDemo(
        Organisation company,
        IReadOnlyCollection<SalesInvoice> invoices,
        IReadOnlyCollection<SupplierBill> bills,
        IReadOnlyDictionary<string, LedgerAccount> accounts,
        string userId,
        DateTimeOffset generatedAt)
    {
        var totals = new Dictionary<BudgetKey, decimal>();

        foreach (var invoice in invoices)
        {
            foreach (var line in invoice.Lines)
            {
                AddBudgetAmounts(
                    totals,
                    line.RevenueAccountId,
                    invoice.IssueDate,
                    invoice.BranchId,
                    invoice.DivisionId,
                    line.NetAmount);
            }
        }

        foreach (var bill in bills)
        {
            foreach (var line in bill.Lines)
            {
                AddBudgetAmounts(
                    totals,
                    line.ExpenseAccountId,
                    bill.BillDate,
                    bill.BranchId,
                    bill.DivisionId,
                    line.NetAmount);
            }
        }

        var accountsById = accounts.Values.ToDictionary(x => x.Id);
        db.AccountBudgets.AddRange(totals.Select(entry =>
        {
            var account = accountsById[entry.Key.AccountId];
            var amount = decimal.Round(
                entry.Value * DemoBudgetFactor(account.Code),
                2,
                MidpointRounding.AwayFromZero);
            return new AccountBudget
            {
                Id = StableGuid(
                    $"{company.Id}:budget:{entry.Key.ScopeKey}:{account.Code}:{entry.Key.Month:yyyy-MM}"),
                OrganisationId = company.Id,
                LedgerAccountId = account.Id,
                ScopeKey = entry.Key.ScopeKey,
                BranchId = entry.Key.BranchId,
                DivisionId = entry.Key.DivisionId,
                Month = entry.Key.Month,
                Amount = amount,
                UpdatedByUserId = userId,
                UpdatedAt = generatedAt
            };
        }));
    }

    private static void AddBudgetAmounts(
        IDictionary<BudgetKey, decimal> totals,
        Guid accountId,
        DateOnly transactionDate,
        Guid? branchId,
        Guid? divisionId,
        decimal amount)
    {
        var month = new DateOnly(transactionDate.Year, transactionDate.Month, 1);
        Add(new BudgetKey(accountId, month, "organisation", null, null));

        if (branchId is Guid branch)
        {
            Add(new BudgetKey(
                accountId,
                month,
                $"branch:{branch:N}",
                branch,
                null));
        }

        if (branchId is Guid divisionBranch && divisionId is Guid division)
        {
            Add(new BudgetKey(
                accountId,
                month,
                $"division:{division:N}",
                divisionBranch,
                division));
        }

        void Add(BudgetKey key)
        {
            totals.TryGetValue(key, out var existingAmount);
            totals[key] = existingAmount + amount;
        }
    }

    private static decimal DemoBudgetFactor(string accountCode) => accountCode switch
    {
        "4000" => 1.04m,
        "4100" => 0.96m,
        "5000" => 0.90m,
        "6100" => 1.03m,
        "6200" => 0.94m,
        "6300" => 1.05m,
        "6500" => 0.88m,
        "6600" => 1.08m,
        "6900" => 0.97m,
        _ => 1m
    };

    private sealed record BudgetKey(
        Guid AccountId,
        DateOnly Month,
        string ScopeKey,
        Guid? BranchId,
        Guid? DivisionId);

    private void AddBankingDemo(
        Organisation company,
        LedgerAccount bankAccount,
        IReadOnlyCollection<PostedJournal> bankJournals,
        DateOnly startDate,
        DateOnly asOfDate,
        string userId)
    {
        var historicalEndDate = asOfDate.AddMonths(-1);
        var currentStartDate = historicalEndDate.AddDays(1);
        var orderedJournals = bankJournals
            .OrderBy(x => x.EntryDate)
            .ThenBy(x => x.Reference)
            .ToArray();
        var currentJournalIds = orderedJournals
            .Where(x => x.EntryDate >= currentStartDate)
            .Select(x => x.Id)
            .ToArray();
        var unmatchedJournalIds = currentJournalIds
            .TakeLast(Math.Min(4, currentJournalIds.Length))
            .ToHashSet();
        var historicalImportBatchId = StableGuid(
            $"{company.Id}:bank-import:historical:{historicalEndDate:yyyy-MM-dd}");
        var currentImportBatchId = StableGuid(
            $"{company.Id}:bank-import:current:{asOfDate:yyyy-MM-dd}");

        var statementLines = orderedJournals.Select(journal =>
        {
            var ledgerLine = journal.Lines.Single(x =>
                x.LedgerAccountId == bankAccount.Id);
            var isReconciled = !unmatchedJournalIds.Contains(journal.Id);
            return new BankStatementLine
            {
                Id = StableGuid($"{company.Id}:bank-statement:{journal.Id}"),
                OrganisationId = company.Id,
                BankAccountId = bankAccount.Id,
                TransactionDate = journal.EntryDate,
                Description = journal.Description ?? journal.Reference,
                Reference = journal.Reference,
                Amount = ledgerLine.Debit - ledgerLine.Credit,
                MatchedPostedJournalLineId = isReconciled ? ledgerLine.Id : null,
                ReconciledAt = isReconciled
                    ? At(journal.EntryDate).AddHours(2)
                    : null,
                ReconciledByUserId = isReconciled ? userId : null,
                ImportedAt = At(journal.EntryDate).AddHours(1),
                Source = "Demo",
                ImportBatchId = journal.EntryDate <= historicalEndDate
                    ? historicalImportBatchId
                    : currentImportBatchId,
                SourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                    $"{DemoSeed}:{company.Id}:bank-statement:{journal.Id}")))
            };
        }).ToArray();
        db.BankStatementLines.AddRange(statementLines);

        decimal BalanceAt(DateOnly date) => orderedJournals
            .Where(x => x.EntryDate <= date)
            .SelectMany(x => x.Lines)
            .Where(x => x.LedgerAccountId == bankAccount.Id)
            .Sum(x => x.Debit - x.Credit);

        var openingBalance = BalanceAt(startDate.AddDays(-1));
        var historicalClosingBalance = BalanceAt(historicalEndDate);
        var currentClosingBalance = BalanceAt(asOfDate);
        db.BankReconciliationSessions.AddRange(
            new BankReconciliationSession
            {
                Id = StableGuid(
                    $"{company.Id}:bank-reconciliation:historical:{historicalEndDate:yyyy-MM-dd}"),
                OrganisationId = company.Id,
                BankAccountId = bankAccount.Id,
                StatementStartDate = startDate,
                StatementEndDate = historicalEndDate,
                OpeningStatementBalance = openingBalance,
                ClosingStatementBalance = historicalClosingBalance,
                LedgerBalance = historicalClosingBalance,
                Difference = 0,
                IsCompleted = true,
                CreatedAt = At(historicalEndDate).AddHours(3),
                CreatedByUserId = userId,
                CompletedAt = At(historicalEndDate).AddHours(4),
                CompletedByUserId = userId
            },
            new BankReconciliationSession
            {
                Id = StableGuid(
                    $"{company.Id}:bank-reconciliation:current:{asOfDate:yyyy-MM-dd}"),
                OrganisationId = company.Id,
                BankAccountId = bankAccount.Id,
                StatementStartDate = currentStartDate,
                StatementEndDate = asOfDate,
                OpeningStatementBalance = historicalClosingBalance,
                ClosingStatementBalance = currentClosingBalance,
                LedgerBalance = currentClosingBalance,
                Difference = 0,
                IsCompleted = false,
                CreatedAt = At(asOfDate),
                CreatedByUserId = userId
            });
    }

    private async Task<DemoDataSummary> BuildSummaryAsync(
        Guid demoGroupId,
        CancellationToken ct)
    {
        var organisationIds = await db.Organisations
            .Where(x => x.OrganisationGroupId == demoGroupId)
            .Select(x => x.Id).ToArrayAsync(ct);
        var invoiceDates = await db.SalesInvoices
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .Select(x => x.IssueDate).ToListAsync(ct);
        var asOf = invoiceDates.Count == 0 ? default : invoiceDates.Max();
        var start = invoiceDates.Count == 0 ? default : invoiceDates.Min();
        var grossNetSales = await db.SalesInvoices
            .Where(x => organisationIds.Contains(x.OrganisationId) && x.Status != InvoiceStatus.Voided)
            .SumAsync(x => (decimal?)x.Subtotal, ct) ?? 0;
        var credits = await db.SalesCreditNotes
            .Where(x => organisationIds.Contains(x.OrganisationId) && x.Status == SalesCreditNoteStatus.Posted)
            .SumAsync(x => (decimal?)x.Subtotal, ct) ?? 0;
        var netSales = grossNetSales - credits;

        return new DemoDataSummary(
            asOf, start,
            organisationIds.Length,
            await db.Branches.CountAsync(x => organisationIds.Contains(x.OrganisationId), ct),
            await db.Divisions.CountAsync(x => organisationIds.Contains(x.Branch.OrganisationId), ct),
            await db.BusinessParties.CountAsync(x => organisationIds.Contains(x.OrganisationId) && (x.Type & PartyType.Customer) != 0, ct),
            await db.BusinessParties.CountAsync(x => organisationIds.Contains(x.OrganisationId) && (x.Type & PartyType.Supplier) != 0, ct),
            invoiceDates.Count,
            await db.SupplierBills.CountAsync(x => organisationIds.Contains(x.OrganisationId), ct),
            await db.CustomerReceipts.CountAsync(x => organisationIds.Contains(x.OrganisationId), ct),
            await db.SupplierPayments.CountAsync(x => organisationIds.Contains(x.OrganisationId), ct),
            await db.SalesCreditNotes.CountAsync(x => organisationIds.Contains(x.OrganisationId) && x.Status == SalesCreditNoteStatus.Posted, ct),
            netSales,
            decimal.Round(netSales * 4, 2));
    }

    private static Organisation CreateCompany(Guid id, Guid groupId, string legalName, string tradingName, string tin, DateTimeOffset createdAt) =>
        new()
        {
            Id = id, OrganisationGroupId = groupId, LegalName = legalName,
            TradingName = tradingName, Tin = tin, CountryCode = "FJ", BaseCurrency = "FJD",
            BusinessAddress = "Level 2, Victoria Parade, Suva, Fiji",
            IsVatRegistered = true, VatRegistrationDate = new DateOnly(2020, 1, 1),
            TimeZoneId = "Pacific/Fiji", TaxLabel = "VAT", Kind = OrganisationKind.Business,
            FinancialYearEndMonth = 12, FinancialYearEndDay = 31, CreatedAt = createdAt
        };

    private static void ConfigureCompany(
        Organisation company,
        Guid groupId,
        string legalName,
        string tradingName,
        string tin,
        DateTimeOffset createdAt)
    {
        company.OrganisationGroupId = groupId;
        company.LegalName = legalName;
        company.TradingName = tradingName;
        company.Tin = tin;
        company.BusinessAddress = "Level 2, Victoria Parade, Suva, Fiji";
        company.IsVatRegistered = true;
        company.VatRegistrationDate = new DateOnly(2020, 1, 1);
        company.CountryCode = "FJ";
        company.BaseCurrency = "FJD";
        company.TimeZoneId = "Pacific/Fiji";
        company.TaxLabel = "VAT";
        company.Kind = OrganisationKind.Business;
        company.FinancialYearEndMonth = 12;
        company.FinancialYearEndDay = 31;
        company.SalesInvoicePrefix = "INV-";
        company.NextSalesInvoiceNumber = 1;
        company.SalesQuotePrefix = "QU-";
        company.NextSalesQuoteNumber = 1;
        company.SalesCreditNotePrefix = "CN-";
        company.NextSalesCreditNoteNumber = 1;
        company.PurchaseOrderPrefix = "PO-";
        company.NextPurchaseOrderNumber = 1;
        company.SupplierBillPrefix = "BILL-";
        company.NextSupplierBillNumber = 1;
        company.SupplierCreditNotePrefix = "SCN-";
        company.NextSupplierCreditNoteNumber = 1;
        company.RecurringInvoiceAutomationEnabled = true;
        company.CreatedAt = createdAt;
    }

    private static OrganisationRole MapOrganisationRole(OrganisationGroupRole role) =>
        role switch
        {
            OrganisationGroupRole.Owner => OrganisationRole.Owner,
            OrganisationGroupRole.Administrator => OrganisationRole.Administrator,
            _ => OrganisationRole.ReadOnly
        };

    private static Branch CreateBranch(string key, Organisation company, string code, string name, bool isDefault, DateTimeOffset createdAt)
    {
        var branchKey = $"{company.Id}:branch:{key}";
        var branch = new Branch { Id = StableGuid(branchKey), OrganisationId = company.Id, Code = code, Name = name, IsDefault = isDefault, CreatedAt = createdAt };
        branch.Divisions.Add(new Division { Id = StableGuid($"{branchKey}:general"), Code = "GENERAL", Name = "General", IsDefault = true, CreatedAt = createdAt });
        return branch;
    }

    private static void AddDivision(Branch branch, string code, string name, DateTimeOffset createdAt) =>
        branch.Divisions.Add(new Division { Id = StableGuid($"{branch.Id}:division:{code}"), Code = code, Name = name, CreatedAt = createdAt });

    private static Division SalesDivision(Branch branch) =>
        PreferredDivision(branch, "SALES", "OPS", "FIN");

    private static Division PurchaseDivision(Branch branch) =>
        PreferredDivision(branch, "PURCH", "ADMIN", "OPS");

    private static Division PreferredDivision(Branch branch, params string[] preferredCodes) =>
        preferredCodes
            .Select(code => branch.Divisions.FirstOrDefault(x => x.Code == code))
            .FirstOrDefault(x => x is not null)
        ?? branch.Divisions.Single(x => x.IsDefault);

    private static PostedJournal SalesJournal(Organisation company, Branch branch, SalesInvoice invoice, IReadOnlyDictionary<string, LedgerAccount> accounts, long sequence, string userId) =>
        Journal(company, branch, invoice.IssueDate, invoice.InvoiceNumber, $"Sales invoice {invoice.InvoiceNumber}", sequence, userId,
            (accounts["1100"].Id, invoice.Total, 0), (invoice.Lines.Single().RevenueAccountId, 0, invoice.Subtotal), (accounts["2100"].Id, 0, invoice.VatTotal), invoice.DivisionId);

    private static PostedJournal BillJournal(Organisation company, Branch branch, SupplierBill bill, IReadOnlyDictionary<string, LedgerAccount> accounts, long sequence, string userId) =>
        Journal(company, branch, bill.BillDate, bill.BillNumber, $"Supplier bill {bill.SupplierReference}", sequence, userId,
            (bill.Lines.Single().ExpenseAccountId, bill.Subtotal, 0), (accounts["1150"].Id, bill.VatTotal, 0), (accounts["2000"].Id, 0, bill.Total), bill.DivisionId);

    private static PostedJournal ReceiptJournal(Organisation company, SalesInvoice invoice, IReadOnlyDictionary<string, LedgerAccount> accounts, decimal amount, DateOnly date, long sequence, string userId) =>
        Journal(company, new Branch { Id = invoice.BranchId!.Value, OrganisationId = company.Id, Code = "", Name = "" }, date, $"RCPT-{invoice.SequenceNumber:D6}", $"Receipt for {invoice.InvoiceNumber}", sequence, userId,
            (accounts["1000"].Id, amount, 0), (accounts["1100"].Id, 0, amount), invoice.DivisionId);

    private static PostedJournal PaymentJournal(Organisation company, SupplierBill bill, IReadOnlyDictionary<string, LedgerAccount> accounts, decimal amount, DateOnly date, long sequence, string userId) =>
        Journal(company, new Branch { Id = bill.BranchId!.Value, OrganisationId = company.Id, Code = "", Name = "" }, date, $"PAY-{bill.SequenceNumber:D6}", $"Payment for {bill.BillNumber}", sequence, userId,
            (accounts["2000"].Id, amount, 0), (accounts["1000"].Id, 0, amount), bill.DivisionId);

    private static PostedJournal CreditJournal(Organisation company, SalesInvoice invoice, IReadOnlyDictionary<string, LedgerAccount> accounts, decimal net, decimal vat, DateOnly date, long sequence, string userId) =>
        Journal(company, new Branch { Id = invoice.BranchId!.Value, OrganisationId = company.Id, Code = "", Name = "" }, date, $"CN-{sequence:D6}", $"Credit for {invoice.InvoiceNumber}", sequence, userId,
            (invoice.Lines.Single().RevenueAccountId, net, 0), (accounts["2100"].Id, vat, 0), (accounts["1100"].Id, 0, net + vat), invoice.DivisionId);

    private static PostedJournal Journal(Organisation company, Branch branch, DateOnly date, string reference, string description, long sequence, string userId, params (Guid AccountId, decimal Debit, decimal Credit)[] values) =>
        Journal(company, branch, date, reference, description, sequence, userId, values, branch.Divisions.SingleOrDefault(x => x.IsDefault)?.Id);

    private static PostedJournal Journal(Organisation company, Branch branch, DateOnly date, string reference, string description, long sequence, string userId, (Guid AccountId, decimal Debit, decimal Credit) first, (Guid AccountId, decimal Debit, decimal Credit) second, Guid? divisionId) =>
        Journal(company, branch, date, reference, description, sequence, userId, new[] { first, second }, divisionId);

    private static PostedJournal Journal(Organisation company, Branch branch, DateOnly date, string reference, string description, long sequence, string userId, (Guid AccountId, decimal Debit, decimal Credit) first, (Guid AccountId, decimal Debit, decimal Credit) second, (Guid AccountId, decimal Debit, decimal Credit) third, Guid? divisionId) =>
        Journal(company, branch, date, reference, description, sequence, userId, new[] { first, second, third }, divisionId);

    private static PostedJournal Journal(Organisation company, Branch branch, DateOnly date, string reference, string description, long sequence, string userId, IEnumerable<(Guid AccountId, decimal Debit, decimal Credit)> values, Guid? divisionId)
    {
        var journal = new PostedJournal
        {
            Id = StableGuid($"{company.Id}:journal:{sequence}"), OrganisationId = company.Id,
            SequenceNumber = sequence, EntryDate = date, Reference = reference, Description = description,
            PostedAt = At(date), PostedByUserId = userId
        };
        journal.Lines = values.Where(x => x.Debit != 0 || x.Credit != 0).Select((value, index) => new PostedJournalLine
        {
            Id = StableGuid($"{journal.Id}:line:{index}"), LedgerAccountId = value.AccountId,
            BranchId = branch.Id, DivisionId = divisionId, Description = description,
            Debit = value.Debit, Credit = value.Credit
        }).ToList();
        return journal;
    }

    private static List<decimal> AllocateAmounts(Random random, int count, decimal target, decimal minWeight, decimal maxWeight)
    {
        var weights = Enumerable.Range(0, count).Select(_ => minWeight + (decimal)random.NextDouble() * (maxWeight - minWeight)).ToArray();
        var totalWeight = weights.Sum();
        var values = weights.Select(x => decimal.Round(target * x / totalWeight, 2)).ToList();
        values[^1] += target - values.Sum();
        return values;
    }

    private static int WeightedIndex(Random random, int count)
    {
        var value = random.NextDouble();
        return Math.Min(count - 1, (int)(value * value * count));
    }

    private static DateOnly RandomDate(Random random, DateOnly start, DateOnly end) =>
        start.AddDays(random.Next(end.DayNumber - start.DayNumber + 1));

    private static DateOnly Min(DateOnly left, DateOnly right) => left < right ? left : right;
    private static decimal Vat(decimal net, DateOnly date) =>
        new FijiVatSchedule()
            .CalculateFromExclusive(new Money(net, "FJD"), date, VatTreatment.Standard)
            .Vat.Amount;
    private static DateTimeOffset At(DateOnly date) => new(date.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);
    private static string ExpenseCode(int index) => new[] { "5000", "6100", "6200", "6300", "6500", "6600", "6900" }[index % 7];
    private static string SalesCurrency(int index) => index switch { 35 => "AUD", 42 => "NZD", 49 => "USD", _ => "FJD" };
    private static string PurchaseCurrency(int index) => index switch { 0 => "AUD", 6 => "NZD", 12 => "USD", _ => "FJD" };
    private static decimal DemoExchangeRate(string currency) => currency switch { "AUD" => 1.47m, "NZD" => 1.32m, "USD" => 2.22m, _ => 1m };
    private static decimal ConvertToTransactionCurrency(decimal baseAmount, decimal exchangeRate) =>
        decimal.Round(baseAmount / exchangeRate, 2, MidpointRounding.AwayFromZero);
    private static string SalesDescription(int index) => new[] { "Wholesale goods", "Project services", "Monthly service contract", "Equipment supply", "Hospitality supplies", "Distribution services" }[index % 6];
    private static string PurchaseDescription(int index) => new[] { "Inventory and materials", "Premises rental", "Utilities", "Professional services", "Office supplies", "Cloud software and IT", "Operating expenses" }[index % 7];
    private static string Slug(string value) => string.Concat(value.ToLowerInvariant().Where(char.IsLetterOrDigit));
    private static int StableSeed(DateOnly asOfDate) => BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes($"{DemoSeed}:{asOfDate:yyyy-MM-dd}")));
    private static Guid StableGuid(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"{DemoSeed}:{key}"))[..16]);
}
