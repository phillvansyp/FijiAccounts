using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Organisation> Organisations => Set<Organisation>();
    public DbSet<OrganisationGroup> OrganisationGroups => Set<OrganisationGroup>();
    public DbSet<OrganisationGroupMembership> OrganisationGroupMemberships =>
        Set<OrganisationGroupMembership>();
    public DbSet<GroupExchangeRate> GroupExchangeRates => Set<GroupExchangeRate>();
    public DbSet<GroupEliminationJournal> GroupEliminationJournals =>
        Set<GroupEliminationJournal>();
    public DbSet<GroupEliminationJournalLine> GroupEliminationJournalLines =>
        Set<GroupEliminationJournalLine>();
    public DbSet<CashflowScenario> CashflowScenarios => Set<CashflowScenario>();
    public DbSet<CashflowScenarioEvent> CashflowScenarioEvents =>
        Set<CashflowScenarioEvent>();
    public DbSet<PlatformAuditEvent> PlatformAuditEvents => Set<PlatformAuditEvent>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Division> Divisions => Set<Division>();
    public DbSet<OrganisationMembership> OrganisationMemberships => Set<OrganisationMembership>();
    public DbSet<OrganisationDimensionAccessGrant> OrganisationDimensionAccessGrants =>
        Set<OrganisationDimensionAccessGrant>();
    public DbSet<AccountantEngagement> AccountantEngagements => Set<AccountantEngagement>();
    public DbSet<LedgerAccount> LedgerAccounts => Set<LedgerAccount>();
    public DbSet<OrganisationInvitation> OrganisationInvitations => Set<OrganisationInvitation>();
    public DbSet<AccountingPeriod> AccountingPeriods => Set<AccountingPeriod>();
    public DbSet<PostedJournal> PostedJournals => Set<PostedJournal>();
    public DbSet<PostedJournalLine> PostedJournalLines => Set<PostedJournalLine>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<MobileIdempotencyRecord> MobileIdempotencyRecords =>
        Set<MobileIdempotencyRecord>();
    public DbSet<MobileDeviceSession> MobileDeviceSessions =>
        Set<MobileDeviceSession>();
    public DbSet<BusinessParty> BusinessParties => Set<BusinessParty>();
    public DbSet<SupplierAccountProfile> SupplierAccountProfiles => Set<SupplierAccountProfile>();
    public DbSet<SupplierBankAccount> SupplierBankAccounts => Set<SupplierBankAccount>();
    public DbSet<BusinessPartyDocument> BusinessPartyDocuments =>
        Set<BusinessPartyDocument>();
    public DbSet<SalesInvoice> SalesInvoices => Set<SalesInvoice>();
    public DbSet<SalesInvoiceVoid> SalesInvoiceVoids =>
    Set<SalesInvoiceVoid>();
    public DbSet<SalesInvoiceLine> SalesInvoiceLines => Set<SalesInvoiceLine>();
    public DbSet<RecurringSalesInvoice> RecurringSalesInvoices =>
        Set<RecurringSalesInvoice>();

    public DbSet<RecurringSalesInvoiceLine> RecurringSalesInvoiceLines =>
        Set<RecurringSalesInvoiceLine>();

    public DbSet<RecurringSalesInvoiceGeneration> RecurringSalesInvoiceGenerations =>
        Set<RecurringSalesInvoiceGeneration>();
    public DbSet<SalesCreditNote> SalesCreditNotes => Set<SalesCreditNote>();
    public DbSet<SalesCreditNoteReversal> SalesCreditNoteReversals =>
    Set<SalesCreditNoteReversal>();
    public DbSet<CustomerReceipt> CustomerReceipts => Set<CustomerReceipt>();
    public DbSet<CustomerReceiptAllocation> CustomerReceiptAllocations => Set<CustomerReceiptAllocation>();
    public DbSet<CustomerReceiptReversal> CustomerReceiptReversals => Set<CustomerReceiptReversal>();
    public DbSet<SupplierBill> SupplierBills => Set<SupplierBill>();
    public DbSet<RecurringInvoiceAutomationRun> RecurringInvoiceAutomationRuns =>
        Set<RecurringInvoiceAutomationRun>();
    public DbSet<RecurringSupplierBill> RecurringSupplierBills =>
    Set<RecurringSupplierBill>();

public DbSet<RecurringSupplierBillLine> RecurringSupplierBillLines =>
    Set<RecurringSupplierBillLine>();

public DbSet<RecurringSupplierBillGeneration> RecurringSupplierBillGenerations =>
    Set<RecurringSupplierBillGeneration>();
    public DbSet<SupplierBillVoid> SupplierBillVoids =>
    Set<SupplierBillVoid>();
    public DbSet<SupplierBillLine> SupplierBillLines => Set<SupplierBillLine>();
    public DbSet<SupplierBillAttachment> SupplierBillAttachments => Set<SupplierBillAttachment>();
    public DbSet<SupplierBillDraft> SupplierBillDrafts => Set<SupplierBillDraft>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<PurchaseRequisition> PurchaseRequisitions => Set<PurchaseRequisition>();
    public DbSet<PurchaseRequisitionLine> PurchaseRequisitionLines => Set<PurchaseRequisitionLine>();
    public DbSet<ProjectVariation> ProjectVariations => Set<ProjectVariation>();
    public DbSet<ProjectProgressClaim> ProjectProgressClaims => Set<ProjectProgressClaim>();
    public DbSet<PurchaseApprovalPolicy> PurchaseApprovalPolicies => Set<PurchaseApprovalPolicy>();
    public DbSet<SupplierPayment> SupplierPayments => Set<SupplierPayment>();
    public DbSet<SupplierPaymentApproval> SupplierPaymentApprovals => Set<SupplierPaymentApproval>();
    public DbSet<SupplierPaymentReversal> SupplierPaymentReversals => Set<SupplierPaymentReversal>();
    public DbSet<SupplierCreditNote> SupplierCreditNotes => Set<SupplierCreditNote>();
    public DbSet<SupplierCreditNoteReversal> SupplierCreditNoteReversals =>
    Set<SupplierCreditNoteReversal>();
    public DbSet<BankStatementLine> BankStatementLines => Set<BankStatementLine>();
    public DbSet<BankStatementImportDocument> BankStatementImportDocuments =>
        Set<BankStatementImportDocument>();
    public DbSet<BankReconciliationSession> BankReconciliationSessions =>
    Set<BankReconciliationSession>();
    public DbSet<BankTransfer> BankTransfers => Set<BankTransfer>();
    public DbSet<BankTransferReversal> BankTransferReversals =>
        Set<BankTransferReversal>();
    public DbSet<AccountBudget> AccountBudgets => Set<AccountBudget>();
    public DbSet<SalesQuote> SalesQuotes => Set<SalesQuote>();
    public DbSet<SalesQuoteLine> SalesQuoteLines => Set<SalesQuoteLine>();
    public DbSet<FixedAsset> FixedAssets => Set<FixedAsset>();
    public DbSet<FixedAssetDepreciation> FixedAssetDepreciations => Set<FixedAssetDepreciation>();
    public DbSet<BankRule> BankRules => Set<BankRule>();
    public DbSet<ProductItem> ProductItems => Set<ProductItem>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<FixedAssetDisposal> FixedAssetDisposals =>
        Set<FixedAssetDisposal>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectCostCode> ProjectCostCodes => Set<ProjectCostCode>();
    public DbSet<ProjectWipPosting> ProjectWipPostings => Set<ProjectWipPosting>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.UseOpenIddict();
        builder.Entity<Organisation>()
            .HasOne(x => x.OrganisationGroup)
            .WithMany(x => x.Companies)
            .HasForeignKey(x => x.OrganisationGroupId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Organisation>()
            .HasIndex(x => x.OrganisationGroupId);
        builder.Entity<Organisation>()
            .Property(x => x.PurchaseQuantityTolerancePercent)
            .HasPrecision(8, 4);
        builder.Entity<Organisation>()
            .Property(x => x.PurchasePriceTolerancePercent)
            .HasPrecision(8, 4);
        builder.Entity<Organisation>()
            .Property(x => x.PurchaseTotalToleranceAmount)
            .HasPrecision(18, 2);
        builder.Entity<Organisation>().HasOne(x => x.ProjectContractAssetAccount)
            .WithMany().HasForeignKey(x => x.ProjectContractAssetAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Organisation>().HasOne(x => x.ProjectContractLiabilityAccount)
            .WithMany().HasForeignKey(x => x.ProjectContractLiabilityAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Organisation>().HasOne(x => x.ProjectRevenueRecognitionAccount)
            .WithMany().HasForeignKey(x => x.ProjectRevenueRecognitionAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<OrganisationGroupMembership>()
            .HasKey(x => new { x.OrganisationGroupId, x.UserId });
        builder.Entity<OrganisationGroupMembership>()
            .HasIndex(x => x.UserId);
        builder.Entity<OrganisationGroupMembership>()
            .HasOne(x => x.OrganisationGroup)
            .WithMany(x => x.Memberships)
            .HasForeignKey(x => x.OrganisationGroupId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<GroupExchangeRate>()
            .HasIndex(x => new
            {
                x.OrganisationGroupId,
                x.FromCurrency,
                x.ToCurrency,
                x.Type,
                x.EffectiveDate
            })
            .IsUnique();
        builder.Entity<GroupExchangeRate>()
            .Property(x => x.Rate)
            .HasPrecision(18, 8);
        builder.Entity<GroupExchangeRate>()
            .HasOne(x => x.OrganisationGroup)
            .WithMany(x => x.ExchangeRates)
            .HasForeignKey(x => x.OrganisationGroupId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<GroupEliminationJournal>()
            .HasIndex(x => new { x.OrganisationGroupId, x.Reference })
            .IsUnique();
        builder.Entity<GroupEliminationJournal>()
            .HasIndex(x => new { x.OrganisationGroupId, x.EntryDate });
        builder.Entity<GroupEliminationJournal>()
            .HasOne(x => x.OrganisationGroup)
            .WithMany(x => x.EliminationJournals)
            .HasForeignKey(x => x.OrganisationGroupId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<GroupEliminationJournalLine>()
            .Property(x => x.Debit)
            .HasPrecision(18, 2);
        builder.Entity<GroupEliminationJournalLine>()
            .Property(x => x.Credit)
            .HasPrecision(18, 2);
        builder.Entity<GroupEliminationJournalLine>()
            .HasOne(x => x.GroupEliminationJournal)
            .WithMany(x => x.Lines)
            .HasForeignKey(x => x.GroupEliminationJournalId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<CashflowScenario>()
            .HasIndex(x => new { x.OrganisationId, x.Name })
            .IsUnique();
        builder.Entity<CashflowScenario>()
            .HasOne(x => x.Organisation)
            .WithMany()
            .HasForeignKey(x => x.OrganisationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CashflowScenarioEvent>()
            .Property(x => x.Amount)
            .HasPrecision(18, 2);
        builder.Entity<CashflowScenarioEvent>()
            .HasOne(x => x.CashflowScenario)
            .WithMany(x => x.Events)
            .HasForeignKey(x => x.CashflowScenarioId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<CashflowScenarioEvent>()
            .HasOne(x => x.SalesInvoice)
            .WithMany()
            .HasForeignKey(x => x.SalesInvoiceId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.Entity<PlatformAuditEvent>()
            .HasIndex(x => new { x.OrganisationGroupId, x.OccurredAt });
        builder.Entity<PlatformAuditEvent>()
            .HasIndex(x => x.AdministratorUserId);
        builder.Entity<Branch>()
            .HasIndex(x => new { x.OrganisationId, x.Code })
            .IsUnique();
        builder.Entity<Branch>()
            .HasIndex(x => new { x.OrganisationId, x.Name })
            .IsUnique();
        builder.Entity<Branch>()
            .HasOne(x => x.Organisation)
            .WithMany()
            .HasForeignKey(x => x.OrganisationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Division>()
            .HasIndex(x => new { x.BranchId, x.Code })
            .IsUnique();
        builder.Entity<Division>()
            .HasIndex(x => new { x.BranchId, x.Name })
            .IsUnique();
        builder.Entity<Division>()
            .HasOne(x => x.Branch)
            .WithMany(x => x.Divisions)
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<OrganisationMembership>().HasKey(x => new { x.OrganisationId, x.UserId });
        builder.Entity<OrganisationMembership>().HasIndex(x => x.UserId);
        builder.Entity<OrganisationDimensionAccessGrant>()
            .HasIndex(x => new { x.OrganisationId, x.UserId, x.BranchId, x.DivisionId })
            .IsUnique();
        builder.Entity<OrganisationDimensionAccessGrant>()
            .HasOne<OrganisationMembership>()
            .WithMany()
            .HasForeignKey(x => new { x.OrganisationId, x.UserId })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<OrganisationDimensionAccessGrant>()
            .HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<OrganisationDimensionAccessGrant>()
            .HasOne(x => x.Division)
            .WithMany()
            .HasForeignKey(x => x.DivisionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<AccountantEngagement>().HasIndex(x => new { x.PracticeOrganisationId, x.ClientOrganisationId }).IsUnique();
        builder.Entity<AccountantEngagement>().HasOne(x => x.PracticeOrganisation).WithMany()
            .HasForeignKey(x => x.PracticeOrganisationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<AccountantEngagement>().HasOne(x => x.ClientOrganisation).WithMany()
            .HasForeignKey(x => x.ClientOrganisationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<LedgerAccount>().HasIndex(x => new { x.OrganisationId, x.Code }).IsUnique();
        builder.Entity<OrganisationInvitation>().HasIndex(x => x.TokenHash).IsUnique();
        builder.Entity<AccountingPeriod>().HasIndex(x => new { x.OrganisationId, x.StartsOn, x.EndsOn }).IsUnique();
        builder.Entity<PostedJournal>().HasIndex(x => new { x.OrganisationId, x.SequenceNumber }).IsUnique();
        builder.Entity<PostedJournal>().HasOne(x => x.Organisation).WithMany().HasForeignKey(x => x.OrganisationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PostedJournalLine>().Property(x => x.Debit).HasPrecision(18, 2);
        builder.Entity<PostedJournalLine>().Property(x => x.Credit).HasPrecision(18, 2);
        builder.Entity<PostedJournalLine>().HasOne(x => x.LedgerAccount).WithMany().HasForeignKey(x => x.LedgerAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PostedJournalLine>()
            .HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PostedJournalLine>()
            .HasOne(x => x.Division)
            .WithMany()
            .HasForeignKey(x => x.DivisionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PostedJournalLine>()
            .HasIndex(x => new { x.BranchId, x.DivisionId });
        builder.Entity<AuditEvent>().HasIndex(x => new { x.OrganisationId, x.OccurredAt });
        builder.Entity<Notification>()
            .HasIndex(x => new { x.OrganisationId, x.IsRead, x.CreatedAtTicks, x.Id });
        builder.Entity<MobileIdempotencyRecord>()
            .HasIndex(x => new { x.OrganisationId, x.UserId, x.Key })
            .IsUnique();
        builder.Entity<MobileIdempotencyRecord>()
            .HasIndex(x => x.ExpiresAt);
        builder.Entity<MobileDeviceSession>()
            .HasIndex(x => new { x.UserId, x.InstallationId })
            .IsUnique();
        builder.Entity<MobileDeviceSession>()
            .HasIndex(x => new { x.UserId, x.RevokedAt });
        builder.Entity<MobileDeviceSession>()
            .HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<BusinessParty>().HasIndex(x => new { x.OrganisationId, x.Name });
        builder.Entity<BusinessParty>().HasOne(x => x.DefaultSalesAccount).WithMany().HasForeignKey(x => x.DefaultSalesAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<BusinessParty>().HasOne(x => x.DefaultPurchaseAccount).WithMany().HasForeignKey(x => x.DefaultPurchaseAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierAccountProfile>()
            .HasOne(x => x.Supplier)
            .WithMany(x => x.SupplierAccounts)
            .HasForeignKey(x => x.SupplierId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<SupplierAccountProfile>()
            .HasIndex(x => new { x.OrganisationId, x.SupplierId, x.AccountNumber })
            .IsUnique();
        builder.Entity<SupplierBankAccount>()
            .HasOne(x => x.Supplier)
            .WithMany(x => x.SupplierBankAccounts)
            .HasForeignKey(x => x.SupplierId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<SupplierBankAccount>()
            .HasIndex(x => new { x.OrganisationId, x.SupplierId, x.AccountNumber })
            .IsUnique();
        builder.Entity<SalesInvoice>().HasIndex(x => new { x.OrganisationId, x.SequenceNumber }).IsUnique();
        builder.Entity<SalesInvoice>().HasIndex(x => new { x.OrganisationId, x.InvoiceNumber }).IsUnique();
        builder.Entity<SalesInvoice>().HasOne(x => x.Organisation).WithMany().HasForeignKey(x => x.OrganisationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SalesInvoice>().HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SalesInvoice>().HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SalesInvoice>().HasOne(x => x.Division).WithMany().HasForeignKey(x => x.DivisionId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SalesInvoice>().HasIndex(x => new { x.BranchId, x.DivisionId });
        builder.Entity<SalesInvoice>().Property(x => x.Subtotal).HasPrecision(18, 2);
        builder.Entity<SalesInvoice>().Property(x => x.VatTotal).HasPrecision(18, 2);
        builder.Entity<SalesInvoice>().Property(x => x.Total).HasPrecision(18, 2);
        builder.Entity<SalesInvoice>().Property(x => x.AmountPaid).HasPrecision(18, 2);
        builder.Entity<SalesInvoice>().Property(x => x.AmountCredited).HasPrecision(18, 2);
        builder.Entity<SalesInvoiceLine>().Property(x => x.Quantity).HasPrecision(18, 4);
        builder.Entity<SalesInvoiceLine>().Property(x => x.UnitPrice).HasPrecision(18, 4);
        builder.Entity<SalesInvoiceLine>().Property(x => x.VatRate).HasPrecision(8, 6);
        builder.Entity<SalesInvoiceLine>().Property(x => x.NetAmount).HasPrecision(18, 2);
        builder.Entity<SalesInvoiceLine>().Property(x => x.VatAmount).HasPrecision(18, 2);
        builder.Entity<SalesInvoiceLine>().Property(x => x.GrossAmount).HasPrecision(18, 2);
        builder.Entity<SalesInvoiceLine>().HasOne(x => x.RevenueAccount).WithMany().HasForeignKey(x => x.RevenueAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SalesInvoiceLine>().HasOne(x => x.ProductItem).WithMany().HasForeignKey(x => x.ProductItemId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SalesInvoiceLine>().HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SalesInvoiceLine>().HasOne(x => x.ProjectCostCode).WithMany().HasForeignKey(x => x.ProjectCostCodeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SalesCreditNote>().HasIndex(x => new { x.OrganisationId, x.SequenceNumber }).IsUnique();
        builder.Entity<SalesCreditNote>().HasIndex(x => new { x.OrganisationId, x.CreditNoteNumber }).IsUnique();
        builder.Entity<SalesCreditNote>().HasOne(x => x.Organisation).WithMany().HasForeignKey(x => x.OrganisationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SalesCreditNote>().HasOne(x => x.SalesInvoice).WithMany().HasForeignKey(x => x.SalesInvoiceId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in new[] { nameof(SalesCreditNote.Subtotal), nameof(SalesCreditNote.VatTotal), nameof(SalesCreditNote.Total), nameof(SalesCreditNote.OriginalInvoiceVatAmount), nameof(SalesCreditNote.AdjustedInvoiceVatAmount) }) builder.Entity<SalesCreditNote>().Property(property).HasPrecision(18, 2);
        builder.Entity<SalesInvoiceVoid>()
    .HasIndex(x => x.SalesInvoiceId)
    .IsUnique();

builder.Entity<SalesInvoiceVoid>()
    .HasOne(x => x.SalesInvoice)
    .WithMany()
    .HasForeignKey(x => x.SalesInvoiceId)
    .OnDelete(DeleteBehavior.Restrict);

builder.Entity<SalesInvoiceVoid>()
    .HasOne(x => x.PostedJournal)
    .WithMany()
    .HasForeignKey(x => x.PostedJournalId)
    .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CustomerReceipt>().Property(x => x.Amount).HasPrecision(18, 2);builder.Entity<SalesCreditNoteReversal>()
    .HasIndex(x => x.SalesCreditNoteId)
    .IsUnique();

builder.Entity<SalesCreditNoteReversal>()
    .HasOne(x => x.SalesCreditNote)
    .WithMany()
    .HasForeignKey(x => x.SalesCreditNoteId)
    .OnDelete(DeleteBehavior.Restrict);

builder.Entity<SalesCreditNoteReversal>()
    .HasOne(x => x.PostedJournal)
    .WithMany()
    .HasForeignKey(x => x.PostedJournalId)
    .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CustomerReceipt>().HasOne(x => x.Organisation).WithMany().HasForeignKey(x => x.OrganisationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CustomerReceipt>().HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CustomerReceipt>().HasOne(x => x.BankAccount).WithMany().HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CustomerReceipt>().HasOne(x => x.PostedJournal).WithMany().HasForeignKey(x => x.PostedJournalId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CustomerReceipt>().HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CustomerReceipt>().HasOne(x => x.Division).WithMany().HasForeignKey(x => x.DivisionId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CustomerReceipt>().HasIndex(x => new { x.BranchId, x.DivisionId });
        builder.Entity<CustomerReceiptAllocation>().Property(x => x.Amount).HasPrecision(18, 2);
        builder.Entity<CustomerReceiptAllocation>().HasIndex(x => new { x.CustomerReceiptId, x.SalesInvoiceId }).IsUnique();
        builder.Entity<CustomerReceiptAllocation>().HasOne(x => x.SalesInvoice).WithMany().HasForeignKey(x => x.SalesInvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CustomerReceiptReversal>().HasIndex(x => x.CustomerReceiptId).IsUnique(); builder.Entity<CustomerReceiptReversal>().HasOne(x => x.CustomerReceipt).WithMany().HasForeignKey(x => x.CustomerReceiptId).OnDelete(DeleteBehavior.Restrict); builder.Entity<CustomerReceiptReversal>().HasOne(x => x.PostedJournal).WithMany().HasForeignKey(x => x.PostedJournalId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierBill>().HasIndex(x => new { x.OrganisationId, x.SequenceNumber }).IsUnique();
        builder.Entity<SupplierBill>()
            .HasIndex(x => new { x.OrganisationId, x.SupplierId, x.SupplierReference })
            .IsUnique()
            .HasFilter("\"Status\" <> 4");
        builder.Entity<SupplierBill>().HasOne(x => x.Organisation).WithMany().HasForeignKey(x => x.OrganisationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierBill>().HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierBill>().HasOne<PostedJournal>().WithMany().HasForeignKey(x => x.PostedJournalId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierBill>().HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierBill>().HasOne(x => x.Division).WithMany().HasForeignKey(x => x.DivisionId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierBill>().HasIndex(x => new { x.BranchId, x.DivisionId });
        foreach (var property in new[] { nameof(SupplierBill.Subtotal), nameof(SupplierBill.VatTotal), nameof(SupplierBill.Total), nameof(SupplierBill.AmountPaid), nameof(SupplierBill.AmountCredited) }) builder.Entity<SupplierBill>().Property(property).HasPrecision(18, 2);
        builder.Entity<SupplierBillVoid>()
    .HasIndex(x => x.SupplierBillId)
    .IsUnique();

builder.Entity<SupplierBillVoid>()
    .HasOne(x => x.SupplierBill)
    .WithMany()
    .HasForeignKey(x => x.SupplierBillId)
    .OnDelete(DeleteBehavior.Restrict);

builder.Entity<SupplierBillVoid>()
    .HasOne(x => x.PostedJournal)
    .WithMany()
    .HasForeignKey(x => x.PostedJournalId)
    .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierBillLine>().Property(x => x.Quantity).HasPrecision(18, 4); builder.Entity<SupplierBillLine>().Property(x => x.UnitPrice).HasPrecision(18, 4); builder.Entity<SupplierBillLine>().Property(x => x.VatRate).HasPrecision(8, 6); builder.Entity<SupplierBillLine>().Property(x => x.NetAmount).HasPrecision(18, 2); builder.Entity<SupplierBillLine>().Property(x => x.VatAmount).HasPrecision(18, 2); builder.Entity<SupplierBillLine>().Property(x => x.GrossAmount).HasPrecision(18, 2);
        builder.Entity<SupplierBillLine>().HasOne(x => x.ExpenseAccount).WithMany().HasForeignKey(x => x.ExpenseAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierBillLine>().HasOne(x => x.ProductItem).WithMany().HasForeignKey(x => x.ProductItemId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierBillLine>().HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierBillLine>().HasOne(x => x.ProjectCostCode).WithMany().HasForeignKey(x => x.ProjectCostCodeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierBillAttachment>().HasIndex(x => new { x.OrganisationId, x.SupplierBillId });
        builder.Entity<SupplierBillAttachment>().HasOne(x => x.SupplierBill).WithMany(x => x.Attachments).HasForeignKey(x => x.SupplierBillId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<SupplierBillDraft>().HasIndex(x => new { x.OrganisationId, x.UpdatedAt });
        builder.Entity<SupplierBillDraft>().Property(x => x.Quantity).HasPrecision(18, 4);
        builder.Entity<SupplierBillDraft>().Property(x => x.UnitPrice).HasPrecision(18, 4);
        builder.Entity<SupplierBillDraft>().HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierBillDraft>().HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierBillDraft>().HasOne<Division>().WithMany().HasForeignKey(x => x.DivisionId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierBillDraft>().HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierBillDraft>().HasOne<ProjectCostCode>().WithMany().HasForeignKey(x => x.ProjectCostCodeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierBillDraft>().HasIndex(x => new { x.BranchId, x.DivisionId });

        builder.Entity<PurchaseOrder>()
            .HasIndex(x => new
            {
                x.OrganisationId,
                x.SequenceNumber
            })
            .IsUnique();

        builder.Entity<PurchaseOrder>()
            .HasIndex(x => x.PurchaseRequisitionId)
            .IsUnique();

        builder.Entity<PurchaseOrder>()
            .HasOne(x => x.PurchaseRequisition)
            .WithOne()
            .HasForeignKey<PurchaseOrder>(x => x.PurchaseRequisitionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<PurchaseRequisition>()
            .HasIndex(x => new { x.OrganisationId, x.SequenceNumber })
            .IsUnique();
        builder.Entity<PurchaseRequisition>()
            .HasIndex(x => new { x.OrganisationId, x.RequisitionNumber })
            .IsUnique();
        builder.Entity<PurchaseRequisition>()
            .HasIndex(x => new { x.OrganisationId, x.Status, x.RequestDate });
        builder.Entity<PurchaseRequisition>()
            .HasOne(x => x.Organisation).WithMany().HasForeignKey(x => x.OrganisationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PurchaseRequisition>()
            .HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PurchaseRequisition>()
            .HasOne(x => x.Division).WithMany().HasForeignKey(x => x.DivisionId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PurchaseRequisition>()
            .HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PurchaseRequisition>().Property(x => x.Total).HasPrecision(18, 2);
        builder.Entity<PurchaseRequisition>()
            .HasOne(x => x.PurchaseApprovalPolicy).WithMany().HasForeignKey(x => x.PurchaseApprovalPolicyId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<PurchaseApprovalPolicy>()
            .HasIndex(x => new { x.OrganisationId, x.IsActive, x.MinimumAmount });
        builder.Entity<PurchaseApprovalPolicy>()
            .HasOne(x => x.Organisation).WithMany().HasForeignKey(x => x.OrganisationId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<PurchaseApprovalPolicy>()
            .HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PurchaseApprovalPolicy>()
            .HasOne(x => x.Division).WithMany().HasForeignKey(x => x.DivisionId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PurchaseApprovalPolicy>().Property(x => x.MinimumAmount).HasPrecision(18, 2);
        builder.Entity<PurchaseApprovalPolicy>().Property(x => x.MaximumAmount).HasPrecision(18, 2);
        builder.Entity<PurchaseRequisitionLine>()
            .HasOne(x => x.PurchaseRequisition).WithMany(x => x.Lines).HasForeignKey(x => x.PurchaseRequisitionId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<PurchaseRequisitionLine>()
            .HasOne(x => x.ExpenseAccount).WithMany().HasForeignKey(x => x.ExpenseAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PurchaseRequisitionLine>()
            .HasOne(x => x.ProductItem).WithMany().HasForeignKey(x => x.ProductItemId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PurchaseRequisitionLine>()
            .HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PurchaseRequisitionLine>()
            .HasOne(x => x.ProjectCostCode).WithMany().HasForeignKey(x => x.ProjectCostCodeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PurchaseRequisitionLine>().Property(x => x.Quantity).HasPrecision(18, 4);
        builder.Entity<PurchaseRequisitionLine>().Property(x => x.EstimatedUnitPrice).HasPrecision(18, 4);
        builder.Entity<PurchaseRequisitionLine>().Property(x => x.EstimatedTotal).HasPrecision(18, 2);

        builder.Entity<PurchaseOrder>()
            .HasIndex(x => new
            {
                x.OrganisationId,
                x.PurchaseOrderNumber
            })
            .IsUnique();

        builder.Entity<PurchaseOrder>()
            .HasOne(x => x.Organisation)
            .WithMany()
            .HasForeignKey(x => x.OrganisationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<PurchaseOrder>()
            .HasOne(x => x.Supplier)
            .WithMany()
            .HasForeignKey(x => x.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<PurchaseOrder>()
            .HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);

        builder.Entity<PurchaseOrder>()
            .HasOne(x => x.Division).WithMany().HasForeignKey(x => x.DivisionId).OnDelete(DeleteBehavior.Restrict);

        builder.Entity<PurchaseOrder>()
            .HasOne(x => x.SupplierBillDraft)
            .WithMany()
            .HasForeignKey(x => x.SupplierBillDraftId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<PurchaseOrder>()
            .HasOne(x => x.SupplierBill)
            .WithMany()
            .HasForeignKey(x => x.SupplierBillId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<PurchaseOrder>()
            .Property(x => x.Subtotal)
            .HasPrecision(18, 2);

        builder.Entity<PurchaseOrder>()
            .Property(x => x.VatTotal)
            .HasPrecision(18, 2);

        builder.Entity<PurchaseOrder>()
            .Property(x => x.Total)
            .HasPrecision(18, 2);

        builder.Entity<PurchaseOrder>()
            .Property(x => x.MatchQuantityVariance)
            .HasPrecision(18, 4);

        builder.Entity<PurchaseOrder>()
            .Property(x => x.MatchPriceVariance)
            .HasPrecision(18, 2);

        builder.Entity<PurchaseOrder>()
            .Property(x => x.MatchTotalVariance)
            .HasPrecision(18, 2);

        builder.Entity<PurchaseOrderLine>()
            .Property(x => x.Quantity)
            .HasPrecision(18, 4);

        builder.Entity<PurchaseOrderLine>()
            .Property(x => x.QuantityReceived)
            .HasPrecision(18, 4);

        builder.Entity<PurchaseOrderLine>()
            .Property(x => x.UnitPrice)
            .HasPrecision(18, 4);

        builder.Entity<PurchaseOrderLine>()
            .Property(x => x.VatRate)
            .HasPrecision(8, 6);

        builder.Entity<PurchaseOrderLine>()
            .Property(x => x.NetAmount)
            .HasPrecision(18, 2);

        builder.Entity<PurchaseOrderLine>()
            .Property(x => x.VatAmount)
            .HasPrecision(18, 2);

        builder.Entity<PurchaseOrderLine>()
            .Property(x => x.GrossAmount)
            .HasPrecision(18, 2);

        builder.Entity<PurchaseOrderLine>()
            .HasOne(x => x.PurchaseOrder)
            .WithMany(x => x.Lines)
            .HasForeignKey(x => x.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<PurchaseOrderLine>()
            .HasOne(x => x.ExpenseAccount)
            .WithMany()
            .HasForeignKey(x => x.ExpenseAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<PurchaseOrderLine>()
            .HasOne(x => x.ProductItem)
            .WithMany()
            .HasForeignKey(x => x.ProductItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<PurchaseOrderLine>()
            .HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);

        builder.Entity<PurchaseOrderLine>()
            .HasOne(x => x.ProjectCostCode).WithMany().HasForeignKey(x => x.ProjectCostCodeId).OnDelete(DeleteBehavior.Restrict);

        builder.Entity<RecurringSalesInvoice>()
    .HasIndex(x => new { x.OrganisationId, x.NextInvoiceDate });

builder.Entity<RecurringSalesInvoice>()
    .HasOne(x => x.Customer)
    .WithMany()
    .HasForeignKey(x => x.CustomerId)
    .OnDelete(DeleteBehavior.Restrict);

builder.Entity<RecurringSalesInvoice>()
    .HasOne(x => x.Branch)
    .WithMany()
    .HasForeignKey(x => x.BranchId)
    .OnDelete(DeleteBehavior.Restrict);

builder.Entity<RecurringSalesInvoice>()
    .HasOne(x => x.Division)
    .WithMany()
    .HasForeignKey(x => x.DivisionId)
    .OnDelete(DeleteBehavior.Restrict);

builder.Entity<RecurringSalesInvoice>()
    .HasIndex(x => new { x.BranchId, x.DivisionId });

builder.Entity<RecurringSalesInvoiceLine>()
    .Property(x => x.Quantity)
    .HasPrecision(18, 4);

builder.Entity<RecurringSalesInvoiceLine>()
    .Property(x => x.UnitPrice)
    .HasPrecision(18, 4);

builder.Entity<RecurringSalesInvoiceLine>()
    .HasOne(x => x.RecurringSalesInvoice)
    .WithMany(x => x.Lines)
    .HasForeignKey(x => x.RecurringSalesInvoiceId)
    .OnDelete(DeleteBehavior.Cascade);

builder.Entity<RecurringSalesInvoiceLine>()
    .HasOne(x => x.RevenueAccount)
    .WithMany()
    .HasForeignKey(x => x.RevenueAccountId)
    .OnDelete(DeleteBehavior.Restrict);

builder.Entity<RecurringSalesInvoiceLine>()
    .HasOne(x => x.ProductItem)
    .WithMany()
    .HasForeignKey(x => x.ProductItemId)
    .OnDelete(DeleteBehavior.Restrict);

builder.Entity<RecurringSalesInvoiceLine>()
    .HasOne(x => x.Project)
    .WithMany()
    .HasForeignKey(x => x.ProjectId)
    .OnDelete(DeleteBehavior.Restrict);

builder.Entity<RecurringSalesInvoiceLine>()
    .HasOne(x => x.ProjectCostCode)
    .WithMany()
    .HasForeignKey(x => x.ProjectCostCodeId)
    .OnDelete(DeleteBehavior.Restrict);

builder.Entity<RecurringSalesInvoiceGeneration>()
    .HasIndex(x => new
    {
        x.RecurringSalesInvoiceId,
        x.ScheduledDate
    })
    .IsUnique();

builder.Entity<RecurringSalesInvoiceGeneration>()
    .HasOne(x => x.RecurringSalesInvoice)
    .WithMany(x => x.Generations)
    .HasForeignKey(x => x.RecurringSalesInvoiceId)
    .OnDelete(DeleteBehavior.Restrict);

builder.Entity<RecurringSalesInvoiceGeneration>()
    .HasOne(x => x.SalesInvoice)
    .WithMany()
    .HasForeignKey(x => x.SalesInvoiceId)
    .OnDelete(DeleteBehavior.Restrict);

builder.Entity<RecurringInvoiceAutomationRun>()
    .HasIndex(x => new
    {
        x.OrganisationId,
        x.RunDate
    })
    .IsUnique();
builder.Entity<RecurringSupplierBill>()
    .HasIndex(x => new { x.OrganisationId, x.NextBillDate });

builder.Entity<RecurringSupplierBill>()
    .HasOne(x => x.Supplier)
    .WithMany()
    .HasForeignKey(x => x.SupplierId)
    .OnDelete(DeleteBehavior.Restrict);

builder.Entity<RecurringSupplierBill>()
    .HasOne(x => x.Branch)
    .WithMany()
    .HasForeignKey(x => x.BranchId)
    .OnDelete(DeleteBehavior.Restrict);

builder.Entity<RecurringSupplierBill>()
    .HasOne(x => x.Division)
    .WithMany()
    .HasForeignKey(x => x.DivisionId)
    .OnDelete(DeleteBehavior.Restrict);

builder.Entity<RecurringSupplierBill>()
    .HasIndex(x => new { x.BranchId, x.DivisionId });

builder.Entity<RecurringSupplierBillLine>()
    .Property(x => x.Quantity)
    .HasPrecision(18, 4);

builder.Entity<RecurringSupplierBillLine>()
    .Property(x => x.UnitPrice)
    .HasPrecision(18, 4);

builder.Entity<RecurringSupplierBillLine>()
    .HasOne(x => x.RecurringSupplierBill)
    .WithMany(x => x.Lines)
    .HasForeignKey(x => x.RecurringSupplierBillId)
    .OnDelete(DeleteBehavior.Cascade);

builder.Entity<RecurringSupplierBillLine>()
    .HasOne(x => x.ExpenseAccount)
    .WithMany()
    .HasForeignKey(x => x.ExpenseAccountId)
    .OnDelete(DeleteBehavior.Restrict);

builder.Entity<RecurringSupplierBillLine>()
    .HasOne(x => x.ProductItem)
    .WithMany()
    .HasForeignKey(x => x.ProductItemId)
    .OnDelete(DeleteBehavior.Restrict);

builder.Entity<RecurringSupplierBillLine>()
    .HasOne(x => x.Project)
    .WithMany()
    .HasForeignKey(x => x.ProjectId)
    .OnDelete(DeleteBehavior.Restrict);

builder.Entity<RecurringSupplierBillLine>()
    .HasOne(x => x.ProjectCostCode)
    .WithMany()
    .HasForeignKey(x => x.ProjectCostCodeId)
    .OnDelete(DeleteBehavior.Restrict);

builder.Entity<RecurringSupplierBillGeneration>()
    .HasIndex(x => new
    {
        x.RecurringSupplierBillId,
        x.ScheduledDate
    })
    .IsUnique();

builder.Entity<RecurringSupplierBillGeneration>()
    .HasOne(x => x.RecurringSupplierBill)
    .WithMany(x => x.Generations)
    .HasForeignKey(x => x.RecurringSupplierBillId)
    .OnDelete(DeleteBehavior.Restrict);

builder.Entity<RecurringSupplierBillGeneration>()
    .HasOne(x => x.SupplierBill)
    .WithMany()
    .HasForeignKey(x => x.SupplierBillId)
    .OnDelete(DeleteBehavior.Restrict);

builder.Entity<RecurringSupplierBillGeneration>()
    .HasOne(x => x.SupplierBillDraft)
    .WithMany()
    .HasForeignKey(x => x.SupplierBillDraftId)
    .OnDelete(DeleteBehavior.Restrict);
    builder.Entity<SupplierPayment>().Property(x => x.Amount).HasPrecision(18, 2);
        builder.Entity<SupplierPayment>().HasOne(x => x.Organisation).WithMany().HasForeignKey(x => x.OrganisationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierPayment>().HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierPayment>().HasOne(x => x.BankAccount).WithMany().HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierPayment>().HasOne(x => x.SupplierBill).WithMany().HasForeignKey(x => x.SupplierBillId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierPayment>().HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierPayment>().HasOne(x => x.Division).WithMany().HasForeignKey(x => x.DivisionId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierPayment>().HasIndex(x => new { x.BranchId, x.DivisionId });
        builder.Entity<SupplierPaymentApproval>().Property(x => x.Amount).HasPrecision(18, 2);
        builder.Entity<SupplierPaymentApproval>().HasIndex(x => new { x.OrganisationId, x.Status, x.RequestedAt });
        builder.Entity<SupplierPaymentApproval>().HasOne(x => x.Organisation).WithMany().HasForeignKey(x => x.OrganisationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierPaymentApproval>().HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierPaymentApproval>().HasOne(x => x.Division).WithMany().HasForeignKey(x => x.DivisionId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierPaymentApproval>().HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierPaymentApproval>().HasOne(x => x.SupplierBill).WithMany().HasForeignKey(x => x.SupplierBillId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierPaymentApproval>().HasOne(x => x.BankAccount).WithMany().HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierPaymentApproval>().HasOne(x => x.StatementLine).WithMany().HasForeignKey(x => x.StatementLineId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierPaymentApproval>().HasOne(x => x.PurchaseApprovalPolicy).WithMany().HasForeignKey(x => x.PurchaseApprovalPolicyId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<SupplierPaymentApproval>().HasOne(x => x.SupplierPayment).WithMany().HasForeignKey(x => x.SupplierPaymentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierCreditNote>().HasIndex(x => new { x.OrganisationId, x.SequenceNumber }).IsUnique();
        builder.Entity<SupplierCreditNote>().HasIndex(x => new { x.OrganisationId, x.CreditNoteNumber }).IsUnique();
        builder.Entity<SupplierCreditNote>().HasOne(x => x.Organisation).WithMany().HasForeignKey(x => x.OrganisationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierCreditNote>().HasOne(x => x.SupplierBill).WithMany().HasForeignKey(x => x.SupplierBillId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierCreditNote>().HasOne(x => x.PostedJournal).WithMany().HasForeignKey(x => x.PostedJournalId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in new[] { nameof(SupplierCreditNote.Subtotal), nameof(SupplierCreditNote.VatTotal), nameof(SupplierCreditNote.Total) }) builder.Entity<SupplierCreditNote>().Property(property).HasPrecision(18, 2);
        builder.Entity<SupplierCreditNoteReversal>()
    .HasIndex(x => x.SupplierCreditNoteId)
    .IsUnique();

builder.Entity<SupplierCreditNoteReversal>()
    .HasOne(x => x.SupplierCreditNote)
    .WithMany()
    .HasForeignKey(x => x.SupplierCreditNoteId)
    .OnDelete(DeleteBehavior.Restrict);

builder.Entity<SupplierCreditNoteReversal>()
    .HasOne(x => x.PostedJournal)
    .WithMany()
    .HasForeignKey(x => x.PostedJournalId)
    .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierPayment>().HasOne(x => x.PostedJournal).WithMany().HasForeignKey(x => x.PostedJournalId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierPaymentReversal>().HasIndex(x => x.SupplierPaymentId).IsUnique(); builder.Entity<SupplierPaymentReversal>().HasOne(x => x.SupplierPayment).WithMany().HasForeignKey(x => x.SupplierPaymentId).OnDelete(DeleteBehavior.Restrict); builder.Entity<SupplierPaymentReversal>().HasOne(x => x.PostedJournal).WithMany().HasForeignKey(x => x.PostedJournalId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<BankStatementLine>().Property(x => x.Amount).HasPrecision(18, 2);
        builder.Entity<BankStatementLine>().HasIndex(x => new { x.OrganisationId, x.BankAccountId, x.TransactionDate });
        builder.Entity<BankStatementLine>().HasIndex(x => x.MatchedPostedJournalLineId).IsUnique();
        builder.Entity<BankStatementLine>().HasIndex(x => new { x.OrganisationId, x.BankAccountId, x.SourceHash }).IsUnique();
        builder.Entity<BankStatementLine>().HasOne(x => x.Organisation).WithMany().HasForeignKey(x => x.OrganisationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<BankStatementLine>().HasOne(x => x.BankAccount).WithMany().HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<BankStatementLine>().HasOne(x => x.MatchedPostedJournalLine).WithMany().HasForeignKey(x => x.MatchedPostedJournalLineId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<BankStatementImportDocument>()
            .HasIndex(x => new { x.OrganisationId, x.ImportBatchId })
            .IsUnique();
        builder.Entity<BankStatementImportDocument>()
            .HasOne(x => x.Organisation)
            .WithMany()
            .HasForeignKey(x => x.OrganisationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<BankStatementImportDocument>()
            .HasOne(x => x.BankAccount)
            .WithMany()
            .HasForeignKey(x => x.BankAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<BankReconciliationSession>()
    .HasIndex(x => new
    {
        x.OrganisationId,
        x.BankAccountId,
        x.StatementEndDate
    });

builder.Entity<BankReconciliationSession>()
    .Property(x => x.OpeningStatementBalance)
    .HasPrecision(18, 2);

builder.Entity<BankReconciliationSession>()
    .Property(x => x.ClosingStatementBalance)
    .HasPrecision(18, 2);

builder.Entity<BankReconciliationSession>()
    .Property(x => x.LedgerBalance)
    .HasPrecision(18, 2);

builder.Entity<BankReconciliationSession>()
    .Property(x => x.Difference)
    .HasPrecision(18, 2);

builder.Entity<BankReconciliationSession>()
    .HasOne(x => x.Organisation)
    .WithMany()
    .HasForeignKey(x => x.OrganisationId)
    .OnDelete(DeleteBehavior.Restrict);

builder.Entity<BankReconciliationSession>()
    .HasOne(x => x.BankAccount)
    .WithMany()
    .HasForeignKey(x => x.BankAccountId)
    .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<BankTransfer>().HasIndex(x => new { x.OrganisationId, x.Reference }).IsUnique(); builder.Entity<BankTransfer>().HasIndex(x => new { x.BranchId, x.DivisionId }); builder.Entity<BankTransfer>().Property(x => x.Amount).HasPrecision(18, 2); builder.Entity<BankTransfer>().HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict); builder.Entity<BankTransfer>().HasOne(x => x.Division).WithMany().HasForeignKey(x => x.DivisionId).OnDelete(DeleteBehavior.Restrict); builder.Entity<BankTransfer>().HasOne(x => x.FromBankAccount).WithMany().HasForeignKey(x => x.FromBankAccountId).OnDelete(DeleteBehavior.Restrict); builder.Entity<BankTransfer>().HasOne(x => x.ToBankAccount).WithMany().HasForeignKey(x => x.ToBankAccountId).OnDelete(DeleteBehavior.Restrict); builder.Entity<BankTransfer>().HasOne(x => x.PostedJournal).WithMany().HasForeignKey(x => x.PostedJournalId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<BankTransferReversal>()
            .HasIndex(x => x.BankTransferId)
            .IsUnique();
        builder.Entity<BankTransferReversal>()
            .HasOne(x => x.BankTransfer)
            .WithMany()
            .HasForeignKey(x => x.BankTransferId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<BankTransferReversal>()
            .HasOne(x => x.PostedJournal)
            .WithMany()
            .HasForeignKey(x => x.PostedJournalId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<AccountBudget>().HasIndex(x => new { x.OrganisationId, x.LedgerAccountId, x.Month, x.ScopeKey }).IsUnique();
        builder.Entity<AccountBudget>().Property(x => x.Amount).HasPrecision(18, 2);
        builder.Entity<AccountBudget>().HasOne(x => x.LedgerAccount).WithMany().HasForeignKey(x => x.LedgerAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<AccountBudget>().HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<AccountBudget>().HasOne(x => x.Division).WithMany().HasForeignKey(x => x.DivisionId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SalesQuote>().HasIndex(x => new { x.OrganisationId, x.SequenceNumber }).IsUnique();
        builder.Entity<SalesQuote>().HasIndex(x => new { x.OrganisationId, x.QuoteNumber }).IsUnique();
        builder.Entity<SalesQuote>().HasOne(x => x.Organisation).WithMany().HasForeignKey(x => x.OrganisationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SalesQuote>().HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SalesQuote>().HasOne(x => x.ConvertedInvoice).WithMany().HasForeignKey(x => x.ConvertedInvoiceId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in new[] { nameof(SalesQuote.Subtotal), nameof(SalesQuote.VatTotal), nameof(SalesQuote.Total) }) builder.Entity<SalesQuote>().Property(property).HasPrecision(18, 2);
        foreach (var property in new[] { nameof(SalesQuoteLine.Quantity), nameof(SalesQuoteLine.UnitPrice), nameof(SalesQuoteLine.VatRate), nameof(SalesQuoteLine.NetAmount), nameof(SalesQuoteLine.VatAmount), nameof(SalesQuoteLine.GrossAmount) }) builder.Entity<SalesQuoteLine>().Property(property).HasPrecision(18, property == nameof(SalesQuoteLine.Quantity) || property == nameof(SalesQuoteLine.UnitPrice) ? 4 : property == nameof(SalesQuoteLine.VatRate) ? 6 : 2);
        builder.Entity<SalesQuoteLine>().HasOne(x => x.RevenueAccount).WithMany().HasForeignKey(x => x.RevenueAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<FixedAsset>().HasIndex(x => new { x.OrganisationId, x.AssetNumber }).IsUnique();
        builder.Entity<FixedAsset>().HasOne(x => x.Organisation).WithMany().HasForeignKey(x => x.OrganisationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<FixedAsset>().HasOne(x => x.AssetAccount).WithMany().HasForeignKey(x => x.AssetAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<FixedAsset>().HasOne(x => x.DepreciationExpenseAccount).WithMany().HasForeignKey(x => x.DepreciationExpenseAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<FixedAsset>().HasOne(x => x.AccumulatedDepreciationAccount).WithMany().HasForeignKey(x => x.AccumulatedDepreciationAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<FixedAsset>().Property(x => x.Cost).HasPrecision(18, 2); builder.Entity<FixedAsset>().Property(x => x.ResidualValue).HasPrecision(18, 2);
        builder.Entity<FixedAssetDepreciation>().HasIndex(x => new { x.FixedAssetId, x.ThroughDate }).IsUnique(); builder.Entity<FixedAssetDepreciation>().Property(x => x.Amount).HasPrecision(18, 2);
        builder.Entity<FixedAssetDepreciation>().HasOne(x => x.PostedJournal).WithMany().HasForeignKey(x => x.PostedJournalId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<FixedAssetDisposal>()
    .HasIndex(x => x.FixedAssetId)
    .IsUnique();

builder.Entity<FixedAssetDisposal>()
    .Property(x => x.Proceeds)
    .HasPrecision(18, 2);

builder.Entity<FixedAssetDisposal>()
    .Property(x => x.AccumulatedDepreciation)
    .HasPrecision(18, 2);

builder.Entity<FixedAssetDisposal>()
    .Property(x => x.BookValue)
    .HasPrecision(18, 2);

builder.Entity<FixedAssetDisposal>()
    .Property(x => x.GainLoss)
    .HasPrecision(18, 2);

builder.Entity<FixedAssetDisposal>()
    .HasOne(x => x.FixedAsset)
    .WithOne(x => x.Disposal)
    .HasForeignKey<FixedAssetDisposal>(x => x.FixedAssetId)
    .OnDelete(DeleteBehavior.Restrict);

builder.Entity<FixedAssetDisposal>()
    .HasOne(x => x.BankAccount)
    .WithMany()
    .HasForeignKey(x => x.BankAccountId)
    .OnDelete(DeleteBehavior.Restrict);

builder.Entity<FixedAssetDisposal>()
    .HasOne(x => x.GainAccount)
    .WithMany()
    .HasForeignKey(x => x.GainAccountId)
    .OnDelete(DeleteBehavior.Restrict);

builder.Entity<FixedAssetDisposal>()
    .HasOne(x => x.LossAccount)
    .WithMany()
    .HasForeignKey(x => x.LossAccountId)
    .OnDelete(DeleteBehavior.Restrict);

builder.Entity<FixedAssetDisposal>()
    .HasOne(x => x.PostedJournal)
    .WithMany()
    .HasForeignKey(x => x.PostedJournalId)
    .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<FixedAsset>()
    .HasOne(x => x.AcquisitionJournal)
    .WithMany()
    .HasForeignKey(x => x.AcquisitionJournalId)
    .OnDelete(DeleteBehavior.Restrict);

builder.Entity<FixedAsset>()
    .HasOne(x => x.AcquisitionBankAccount)
    .WithMany()
    .HasForeignKey(x => x.AcquisitionBankAccountId)
    .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<BankRule>().HasIndex(x => new { x.OrganisationId, x.Name }).IsUnique(); builder.Entity<BankRule>().HasOne(x => x.TargetAccount).WithMany().HasForeignKey(x => x.TargetAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ProductItem>().HasIndex(x => new { x.OrganisationId, x.Code }).IsUnique(); builder.Entity<ProductItem>().Property(x => x.SalePrice).HasPrecision(18, 4); builder.Entity<ProductItem>().Property(x => x.PurchasePrice).HasPrecision(18, 4); builder.Entity<ProductItem>().Property(x => x.QuantityOnHand).HasPrecision(18, 4); builder.Entity<ProductItem>().Property(x => x.AverageCost).HasPrecision(18, 4); builder.Entity<ProductItem>().Property(x => x.ReorderLevel).HasPrecision(18, 4); builder.Entity<ProductItem>().HasOne(x => x.RevenueAccount).WithMany().HasForeignKey(x => x.RevenueAccountId).OnDelete(DeleteBehavior.Restrict); builder.Entity<ProductItem>().HasOne(x => x.ExpenseAccount).WithMany().HasForeignKey(x => x.ExpenseAccountId).OnDelete(DeleteBehavior.Restrict); builder.Entity<ProductItem>().HasOne(x => x.InventoryAccount).WithMany().HasForeignKey(x => x.InventoryAccountId).OnDelete(DeleteBehavior.Restrict); builder.Entity<ProductItem>().HasOne(x => x.CostAdjustmentAccount).WithMany().HasForeignKey(x => x.CostAdjustmentAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<InventoryMovement>().HasIndex(x => new { x.OrganisationId, x.ProductItemId, x.MovementDate }); builder.Entity<InventoryMovement>().HasIndex(x => new { x.BranchId, x.DivisionId }); builder.Entity<InventoryMovement>().Property(x => x.QuantityChange).HasPrecision(18, 4); builder.Entity<InventoryMovement>().Property(x => x.UnitCost).HasPrecision(18, 4); builder.Entity<InventoryMovement>().Property(x => x.ValueChange).HasPrecision(18, 2); builder.Entity<InventoryMovement>().HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict); builder.Entity<InventoryMovement>().HasOne(x => x.Division).WithMany().HasForeignKey(x => x.DivisionId).OnDelete(DeleteBehavior.Restrict); builder.Entity<InventoryMovement>().HasOne(x => x.ProductItem).WithMany().HasForeignKey(x => x.ProductItemId).OnDelete(DeleteBehavior.Restrict); builder.Entity<InventoryMovement>().HasOne(x => x.PostedJournal).WithMany().HasForeignKey(x => x.PostedJournalId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Project>().HasIndex(x => new { x.OrganisationId, x.ProjectNumber }).IsUnique();
        builder.Entity<Project>().HasIndex(x => new { x.BranchId, x.DivisionId, x.Status });
        builder.Entity<Project>().HasOne(x => x.Organisation).WithMany().HasForeignKey(x => x.OrganisationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Project>().HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Project>().HasOne(x => x.Division).WithMany().HasForeignKey(x => x.DivisionId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Project>().HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in new[] { nameof(Project.OriginalContractValue), nameof(Project.OpeningApprovedVariationValue), nameof(Project.ForecastCost) }) builder.Entity<Project>().Property(property).HasPrecision(18, 2);
        builder.Entity<Project>().Property(x => x.RetentionPercent).HasPrecision(8, 4);
        builder.Entity<ProjectCostCode>().HasIndex(x => new { x.ProjectId, x.Code }).IsUnique();
        builder.Entity<ProjectCostCode>().Property(x => x.Category)
            .HasDefaultValue(ProjectCostCategory.Other);
        builder.Entity<ProjectCostCode>().Property(x => x.BudgetAmount).HasPrecision(18, 2);
        builder.Entity<ProjectVariation>()
            .HasOne(x => x.Project).WithMany(x => x.Variations).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ProjectVariation>()
            .HasIndex(x => new { x.ProjectId, x.VariationNumber }).IsUnique();
        builder.Entity<ProjectVariation>().Property(x => x.Amount).HasPrecision(18, 2);
        builder.Entity<ProjectProgressClaim>()
            .HasOne(x => x.Project).WithMany(x => x.ProgressClaims).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ProjectProgressClaim>()
            .HasOne(x => x.RevenueAccount).WithMany().HasForeignKey(x => x.RevenueAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ProjectProgressClaim>()
            .HasOne(x => x.SalesInvoice).WithMany().HasForeignKey(x => x.SalesInvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ProjectProgressClaim>()
            .HasIndex(x => new { x.ProjectId, x.ClaimNumber }).IsUnique();
        builder.Entity<ProjectProgressClaim>().HasIndex(x => x.SalesInvoiceId).IsUnique();
        foreach (var property in new[]
        {
            nameof(ProjectProgressClaim.WorkCompletedAmount),
            nameof(ProjectProgressClaim.RetentionHeldAmount),
            nameof(ProjectProgressClaim.RetentionReleasedAmount)
        }) builder.Entity<ProjectProgressClaim>().Property(property).HasPrecision(18, 2);
        builder.Entity<ProjectProgressClaim>().Property(x => x.RetentionRate).HasPrecision(8, 4);
        builder.Entity<ProjectCostCode>().HasOne(x => x.Project).WithMany(x => x.CostCodes).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ProjectWipPosting>().HasIndex(x => new { x.ProjectId, x.AsAt }).IsUnique();
        builder.Entity<ProjectWipPosting>().HasIndex(x => x.PostedJournalId).IsUnique();
        foreach (var property in new[]
        {
            nameof(ProjectWipPosting.PreviousWipAmount),
            nameof(ProjectWipPosting.RequiredWipAmount),
            nameof(ProjectWipPosting.MovementAmount)
        }) builder.Entity<ProjectWipPosting>().Property(property).HasPrecision(18, 2);
        builder.Entity<ProjectWipPosting>().HasOne(x => x.Organisation).WithMany().HasForeignKey(x => x.OrganisationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ProjectWipPosting>().HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ProjectWipPosting>().HasOne(x => x.PostedJournal).WithMany().HasForeignKey(x => x.PostedJournalId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PostedJournalLine>().HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PostedJournalLine>().HasOne(x => x.ProjectCostCode).WithMany().HasForeignKey(x => x.ProjectCostCodeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PostedJournalLine>().HasIndex(x => new { x.ProjectId, x.ProjectCostCodeId });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) { ProtectAppendOnlyRecords(); return base.SaveChanges(acceptAllChangesOnSuccess); }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) { ProtectAppendOnlyRecords(); return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken); }

    private void ProtectAppendOnlyRecords()
    {
        if (
    ChangeTracker.Entries<PostedJournal>()
        .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
    ChangeTracker.Entries<PostedJournalLine>()
        .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
    ChangeTracker.Entries<ProjectWipPosting>()
        .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
    ChangeTracker.Entries<AuditEvent>()
        .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
    ChangeTracker.Entries<PlatformAuditEvent>()
        .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
    ChangeTracker.Entries<SalesInvoice>()
        .Any(x =>
            x.State == EntityState.Deleted &&
            x.Entity.Status != InvoiceStatus.Draft) ||
    ChangeTracker.Entries<SalesInvoiceLine>()
    .Any(x =>
        (x.State is EntityState.Modified or EntityState.Deleted) &&
        !IsDraftSalesInvoiceLine(x)) ||
    ChangeTracker.Entries<SalesCreditNote>()
        .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
    ChangeTracker.Entries<SalesQuote>()
        .Any(x =>
            x.State == EntityState.Deleted &&
            x.Entity.Status != QuoteStatus.Draft) ||
    ChangeTracker.Entries<SalesQuoteLine>()
        .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
    ChangeTracker.Entries<FixedAssetDepreciation>()
        .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
    ChangeTracker.Entries<FixedAssetDisposal>()
        .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
    ChangeTracker.Entries<InventoryMovement>()
        .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
    ChangeTracker.Entries<BankTransfer>()
        .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
    ChangeTracker.Entries<BankTransferReversal>()
        .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
    ChangeTracker.Entries<CustomerReceipt>()
        .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
    ChangeTracker.Entries<CustomerReceiptAllocation>()
        .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
    ChangeTracker.Entries<CustomerReceiptReversal>()
        .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
    ChangeTracker.Entries<SupplierBill>()
        .Any(x => x.State == EntityState.Deleted) ||
    ChangeTracker.Entries<SupplierBillLine>()
        .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
    ChangeTracker.Entries<SupplierCreditNote>()
        .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
    ChangeTracker.Entries<SupplierPayment>()
        .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
    ChangeTracker.Entries<SupplierPaymentReversal>()
        .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
    ChangeTracker.Entries<SalesInvoiceVoid>()
        .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
    ChangeTracker.Entries<SupplierBillVoid>()
        .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
    ChangeTracker.Entries<SalesCreditNoteReversal>()
        .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
    ChangeTracker.Entries<SupplierCreditNoteReversal>()
        .Any(x => x.State is EntityState.Modified or EntityState.Deleted))
{
    throw new InvalidOperationException(
        "Posted journals and audit events are append-only.");
}
    }

    private bool IsDraftSalesInvoiceLine(
    Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<SalesInvoiceLine> entry)
{
    if (entry.Entity.SalesInvoice is not null)
    {
        return entry.Entity.SalesInvoice.Status == InvoiceStatus.Draft;
    }

    return ChangeTracker.Entries<SalesInvoice>()
        .Any(x =>
            x.Entity.Id == entry.Entity.SalesInvoiceId &&
            x.Entity.Status == InvoiceStatus.Draft);
}
}
