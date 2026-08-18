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
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

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
builder.Services.AddScoped<JournalPostingService>();
builder.Services.AddScoped<SalesInvoiceService>();
builder.Services.AddScoped<CustomerReceiptService>();
builder.Services.AddScoped<PurchasingService>();
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
builder.Services.AddScoped<BankReconciliationService>();
builder.Services.AddScoped<BankReconciliationSessionService>();

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

if (app.Environment.IsDevelopment())
{
    await using var migrationScope = app.Services.CreateAsyncScope();
    var database = migrationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await database.Database.MigrateAsync();
}

await DevelopmentAccountSeeder.SeedAsync(app);

if (builder.Configuration.GetValue<bool>("DevSeed:SeedOnly"))
{
    return;
}

app.Run();