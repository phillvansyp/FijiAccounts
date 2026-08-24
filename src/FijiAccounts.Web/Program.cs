using FijiAccounts.Web.Components;
using FijiAccounts.Web.Components.Account;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Static web assets are required when the app is launched directly as well as
// through the Development launch profile.
builder.WebHost.UseStaticWebAssets();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(
        PlatformAdminAccessService.PolicyName,
        policy => policy.RequireRole(PlatformAdminAccessService.RoleName));

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
builder.Services.AddScoped<ILoginCodeEmailSender, LoginCodeEmailSender>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
});

builder.Services.AddScoped<TenantAccessService>();
builder.Services.AddScoped<OrganisationInvitationService>();
builder.Services.AddScoped<EnterpriseStructureService>();
builder.Services.AddScoped<OrganisationSettingsService>();
builder.Services.AddScoped<BusinessPartyService>();
builder.Services.AddScoped<JournalPostingService>();
builder.Services.AddScoped<SalesInvoiceService>();
builder.Services.AddScoped<RecurringSalesInvoiceService>();
builder.Services.AddScoped<RecurringInvoiceAutomationSettingsService>();
builder.Services.AddHostedService<RecurringInvoiceGenerationWorker>();
builder.Services.AddHostedService<DocumentExpiryWorker>();
builder.Services.AddHostedService<OverdueInvoiceWorker>();
builder.Services.AddHostedService<OverdueSupplierBillWorker>();
builder.Services.AddHostedService<UpcomingInvoiceWorker>();
builder.Services.AddHostedService<UpcomingSupplierBillWorker>();
builder.Services.AddScoped<CustomerReceiptService>();
builder.Services.AddScoped<BusinessPartyDocumentService>();
builder.Services.AddSingleton<OrganisationUpdateBroker>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<CashflowForecastService>();
builder.Services.AddScoped<FinancialRiskService>();
builder.Services.AddScoped<FinancialIntelligenceService>();
builder.Services.AddScoped<CashRunwayService>();
builder.Services.AddScoped<VatWorkpaperService>();
builder.Services.AddScoped<PurchasingService>();
builder.Services.AddScoped<SupplierBillDraftService>();
builder.Services.AddScoped<SupplierBillAttachmentService>();
builder.Services.AddScoped<PurchaseOrderService>();
builder.Services.AddScoped<PurchaseRequisitionService>();
builder.Services.AddScoped<PurchaseApprovalPolicyService>();
builder.Services.AddScoped<PurchaseOrderMatchService>();
builder.Services.AddScoped<RecurringSupplierBillService>();
builder.Services.AddScoped<SupplierCreditNoteService>();
builder.Services.AddScoped<BankReconciliationService>();
builder.Services.AddScoped<BankTransferService>();
builder.Services.AddScoped<BudgetService>();
builder.Services.AddScoped<BankStatementImportService>();
builder.Services.AddScoped<AccountingPeriodService>();
builder.Services.AddScoped<SalesCreditNoteService>();
builder.Services.AddScoped<SalesQuoteService>();
builder.Services.AddScoped<ChartOfAccountsService>();
builder.Services.AddScoped<FixedAssetService>();
builder.Services.AddScoped<BankRuleService>();
builder.Services.AddScoped<BankTransactionCodingService>();
builder.Services.AddScoped<ProductCatalogService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<FinancialReportService>();
builder.Services.AddScoped<GroupFinancialReportService>();
builder.Services.AddScoped<GroupExchangeRateService>();
builder.Services.AddScoped<BankReconciliationService>();
builder.Services.AddScoped<BankReconciliationSessionService>();
builder.Services.AddScoped<BankAccountService>();
builder.Services.AddScoped<DemoDataService>();
builder.Services.AddScoped<PlatformAdminAccessService>();
builder.Services.AddScoped<PlatformAdministrationService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

// Supplier bill attachment endpoint (single registration only).
app.MapSupplierBillAttachmentEndpoints();
app.MapBusinessPartyDocumentEndpoints();
app.MapBankStatementDocumentEndpoints();

if (app.Environment.IsDevelopment())
{
    await using var migrationScope = app.Services.CreateAsyncScope();
    var database = migrationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await database.Database.MigrateAsync();
}

await DevelopmentAccountSeeder.SeedAsync(app);
await PlatformAdminSeeder.SeedAsync(app);

if (builder.Configuration.GetValue<bool>("DevSeed:SeedOnly"))
{
    return;
}

app.Run();
