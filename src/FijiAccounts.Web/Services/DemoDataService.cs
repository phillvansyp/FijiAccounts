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
    int DepartmentCount,
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
        await db.CustomerReceipts
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .ExecuteDeleteAsync(ct);
        await db.SupplierPayments
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
        await db.OrganisationUnits
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
        db.Branches.AddRange(branches);

        var departments = new[]
        {
            CreateDepartment("a-sales", companies[0], "SALES", "Sales", generatedAt),
            CreateDepartment("a-purchasing", companies[0], "PURCH", "Purchasing", generatedAt),
            CreateDepartment("a-operations", companies[0], "OPS", "Operations", generatedAt),
            CreateDepartment("b-finance", companies[1], "FIN", "Finance", generatedAt),
            CreateDepartment("b-admin", companies[1], "ADMIN", "Administration", generatedAt)
        };
        db.OrganisationUnits.AddRange(departments);

        var accountsByCompany = new Dictionary<Guid, Dictionary<string, LedgerAccount>>();
        foreach (var company in companies)
        {
            var accounts = FijiStarterChart.For(company.Id).ToList();
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
            long journalSequence = 0;

            for (var i = 0; i < invoiceCount; i++)
            {
                var issueDate = i == 0
                    ? startDate
                    : i == invoiceCount - 1
                        ? asOfDate
                        : RandomDate(random, startDate, asOfDate);
                var branch = companyBranches[i % companyBranches.Length];
                var net = invoiceNets[i];
                var vat = Vat(net, issueDate);
                var invoice = new SalesInvoice
                {
                    Id = StableGuid($"{company.Id}:invoice:{i}"),
                    OrganisationId = company.Id,
                    BranchId = branch.Id,
                    DivisionId = branch.Divisions.Single().Id,
                    CustomerId = customers[WeightedIndex(random, customers.Count)].Id,
                    SequenceNumber = i + 1,
                    InvoiceNumber = $"INV-{i + 1:D6}",
                    IssueDate = issueDate,
                    DueDate = issueDate.AddDays(i % 7 == 0 ? 14 : 30),
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
                    VatTreatment = VatTreatment.Standard,
                    VatRate = vat == 0 ? 0 : vat / net,
                    NetAmount = net,
                    VatAmount = vat,
                    GrossAmount = net + vat,
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
                var net = billNets[i];
                var vat = Vat(net, billDate);
                var expenseCode = ExpenseCode(i);
                var bill = new SupplierBill
                {
                    Id = StableGuid($"{company.Id}:bill:{i}"),
                    OrganisationId = company.Id,
                    BranchId = branch.Id,
                    DivisionId = branch.Divisions.Single().Id,
                    SupplierId = suppliers[i % suppliers.Count].Id,
                    SequenceNumber = i + 1,
                    BillNumber = $"BILL-{i + 1:D6}",
                    SupplierReference = $"SUP-{billDate:yyMM}-{i + 1200}",
                    BillDate = billDate,
                    DueDate = billDate.AddDays(i % 5 == 0 ? 14 : 30),
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
                    VatTreatment = VatTreatment.Standard,
                    VatRate = vat == 0 ? 0 : vat / net,
                    NetAmount = net,
                    VatAmount = vat,
                    GrossAmount = net + vat,
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

            company.NextSalesInvoiceNumber = invoiceCount + 1;
            company.NextSupplierBillNumber = billCount + 1;
            company.NextSalesCreditNoteNumber = 4;
        }

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
            .Where(x => organisationIds.Contains(x.OrganisationId))
            .SumAsync(x => (decimal?)x.Subtotal, ct) ?? 0;
        var netSales = grossNetSales - credits;

        return new DemoDataSummary(
            asOf, start,
            organisationIds.Length,
            await db.Branches.CountAsync(x => organisationIds.Contains(x.OrganisationId), ct),
            await db.OrganisationUnits.CountAsync(x => organisationIds.Contains(x.OrganisationId) && x.Type == OrganisationUnitType.Department, ct),
            await db.BusinessParties.CountAsync(x => organisationIds.Contains(x.OrganisationId) && (x.Type & PartyType.Customer) != 0, ct),
            await db.BusinessParties.CountAsync(x => organisationIds.Contains(x.OrganisationId) && (x.Type & PartyType.Supplier) != 0, ct),
            invoiceDates.Count,
            await db.SupplierBills.CountAsync(x => organisationIds.Contains(x.OrganisationId), ct),
            await db.CustomerReceipts.CountAsync(x => organisationIds.Contains(x.OrganisationId), ct),
            await db.SupplierPayments.CountAsync(x => organisationIds.Contains(x.OrganisationId), ct),
            await db.SalesCreditNotes.CountAsync(x => organisationIds.Contains(x.OrganisationId), ct),
            netSales,
            decimal.Round(netSales * 4, 2));
    }

    private static Organisation CreateCompany(Guid id, Guid groupId, string legalName, string tradingName, string tin, DateTimeOffset createdAt) =>
        new()
        {
            Id = id, OrganisationGroupId = groupId, LegalName = legalName,
            TradingName = tradingName, Tin = tin, CountryCode = "FJ", BaseCurrency = "FJD",
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

    private static OrganisationUnit CreateDepartment(string key, Organisation company, string code, string name, DateTimeOffset createdAt) =>
        new() { Id = StableGuid($"{company.Id}:department:{key}"), OrganisationId = company.Id, Type = OrganisationUnitType.Department, Code = code, Name = name, CreatedAt = createdAt };

    private static PostedJournal SalesJournal(Organisation company, Branch branch, SalesInvoice invoice, IReadOnlyDictionary<string, LedgerAccount> accounts, long sequence, string userId) =>
        Journal(company, branch, invoice.IssueDate, invoice.InvoiceNumber, $"Sales invoice {invoice.InvoiceNumber}", sequence, userId,
            (accounts["1100"].Id, invoice.Total, 0), (invoice.Lines.Single().RevenueAccountId, 0, invoice.Subtotal), (accounts["2100"].Id, 0, invoice.VatTotal));

    private static PostedJournal BillJournal(Organisation company, Branch branch, SupplierBill bill, IReadOnlyDictionary<string, LedgerAccount> accounts, long sequence, string userId) =>
        Journal(company, branch, bill.BillDate, bill.BillNumber, $"Supplier bill {bill.SupplierReference}", sequence, userId,
            (bill.Lines.Single().ExpenseAccountId, bill.Subtotal, 0), (accounts["1150"].Id, bill.VatTotal, 0), (accounts["2000"].Id, 0, bill.Total));

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
        Journal(company, branch, date, reference, description, sequence, userId, values, branch.Divisions.SingleOrDefault()?.Id);

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
    private static string SalesDescription(int index) => new[] { "Wholesale goods", "Project services", "Monthly service contract", "Equipment supply", "Hospitality supplies", "Distribution services" }[index % 6];
    private static string PurchaseDescription(int index) => new[] { "Inventory and materials", "Premises rental", "Utilities", "Professional services", "Office supplies", "Cloud software and IT", "Operating expenses" }[index % 7];
    private static string Slug(string value) => string.Concat(value.ToLowerInvariant().Where(char.IsLetterOrDigit));
    private static int StableSeed(DateOnly asOfDate) => BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes($"{DemoSeed}:{asOfDate:yyyy-MM-dd}")));
    private static Guid StableGuid(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"{DemoSeed}:{key}"))[..16]);
}
