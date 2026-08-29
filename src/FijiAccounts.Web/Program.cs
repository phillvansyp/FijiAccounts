using FijiAccounts.Web.Components;
using FijiAccounts.Domain.Fiscalisation;
using FijiAccounts.Web.Components.Account;
using FijiAccounts.Web.Api.Mobile.V1;
using FijiAccounts.Web.Authentication;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Console logging works for local development and is collected by the
// production container. The Windows Event Log provider can terminate requests
// when the current user does not have permission to write to the event log.
builder.Logging.ClearProviders();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var mobileAuthenticationEnabled = builder.Configuration.GetValue<bool>(
    $"{MobileAuthenticationOptions.SectionName}:Enabled");

var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}

// Static web assets are required when the app is launched directly as well as
// through the Development launch profile.
builder.WebHost.UseStaticWebAssets();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddOpenApi(options =>
    options.AddOperationTransformer<MobileClientOpenApiTransformer>());
builder.Services.AddOptions<MobileApiOptions>()
    .Bind(builder.Configuration.GetSection(MobileApiOptions.SectionName))
    .Validate(options => Version.TryParse(options.MinimumIosVersion, out _),
        "MobileApi:MinimumIosVersion must be a valid version.")
    .Validate(options => Version.TryParse(options.MinimumAndroidVersion, out _),
        "MobileApi:MinimumAndroidVersion must be a valid version.")
    .Validate(options => options.RateLimitPermitLimit > 0,
        "MobileApi:RateLimitPermitLimit must be positive.")
    .Validate(options => options.RateLimitWindowSeconds > 0,
        "MobileApi:RateLimitWindowSeconds must be positive.")
    .ValidateOnStart();
builder.Services.AddOptions<ImmutableDocumentStorageOptions>()
    .Bind(builder.Configuration.GetSection(ImmutableDocumentStorageOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.Provider),
        "DocumentStorage:Provider is required.")
    .Validate(options => options.RequiredRetentionYears is >= 7 and <= 100,
        "DocumentStorage:RequiredRetentionYears must be between 7 and 100.")
    .Validate(options => options.RequireNativeRetentionLock,
        "DocumentStorage:RequireNativeRetentionLock must remain enabled for production readiness.")
    .ValidateOnStart();
var mobileApiConfiguration = builder.Configuration
    .GetSection(MobileApiOptions.SectionName)
    .Get<MobileApiOptions>() ?? new MobileApiOptions();
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy(MobileApiV1Endpoints.RateLimitPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            httpContext.Connection.RemoteIpAddress?.ToString() ??
            "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = mobileApiConfiguration.RateLimitPermitLimit,
                Window = TimeSpan.FromSeconds(
                    mobileApiConfiguration.RateLimitWindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
        await Results.Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            title: "Mobile API rate limit exceeded",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "rate_limit_exceeded"
            }).ExecuteAsync(context.HttpContext);
});

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
    options.UseSqlite(connectionString).UseOpenIddict());

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = AccountLockoutPolicy.MaxFailedAccessAttempts;
        options.Lockout.DefaultLockoutTimeSpan = AccountLockoutPolicy.Duration;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.AddMobileAuthentication(mobileAuthenticationEnabled);

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(
        PlatformAdminAccessService.PolicyName,
        policy => policy.RequireRole(PlatformAdminAccessService.RoleName));

builder.Services.AddHttpClient();
builder.Services.AddSingleton<SmtpEmailDeliveryService>();
builder.Services.AddSingleton<MicrosoftGraphEmailDeliveryService>();
builder.Services.AddSingleton<IEmailDeliveryService>(services =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    return configuration["Email:Provider"]?.Trim().ToUpperInvariant() switch
    {
        "MICROSOFTGRAPH" => services.GetRequiredService<MicrosoftGraphEmailDeliveryService>(),
        "SMTP" => services.GetRequiredService<SmtpEmailDeliveryService>(),
        null or "" when services.GetRequiredService<MicrosoftGraphEmailDeliveryService>().IsConfigured =>
            services.GetRequiredService<MicrosoftGraphEmailDeliveryService>(),
        null or "" => services.GetRequiredService<SmtpEmailDeliveryService>(),
        var provider => throw new InvalidOperationException(
            $"Unsupported email delivery provider '{provider}'.")
    };
});
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<PasswordResetRequestThrottle>();
var graphEmailConfigured =
    !string.IsNullOrWhiteSpace(builder.Configuration["Email:FromAddress"]) &&
    !string.IsNullOrWhiteSpace(builder.Configuration["Email:MicrosoftGraph:TenantId"]) &&
    !string.IsNullOrWhiteSpace(builder.Configuration["Email:MicrosoftGraph:ClientId"]) &&
    !string.IsNullOrWhiteSpace(builder.Configuration["Email:MicrosoftGraph:ClientSecret"]);
var smtpEmailConfigured =
    !string.IsNullOrWhiteSpace(builder.Configuration["Email:FromAddress"]) &&
    !string.IsNullOrWhiteSpace(builder.Configuration["Email:Smtp:Host"]);
if (builder.Environment.IsDevelopment() &&
    !graphEmailConfigured &&
    !smtpEmailConfigured)
{
    builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
}
else
{
    builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityEmailSender>();
}
builder.Services.AddScoped<ILoginCodeEmailSender, LoginCodeEmailSender>();
builder.Services.AddScoped<IOrganisationInvitationEmailSender, OrganisationInvitationEmailSender>();
builder.Services.AddSingleton<SalesInvoicePdfRenderer>();
builder.Services.AddScoped<SalesInvoiceEmailSender>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments(MobileApiV1Endpoints.RoutePrefix))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.Request.Path.StartsWithSegments(MobileApiV1Endpoints.RoutePrefix))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});

builder.Services.AddScoped<TenantAccessService>();
builder.Services.AddScoped<OrganisationInvitationService>();
builder.Services.AddScoped<OrganisationPermissionProfileService>();
builder.Services.AddScoped<EnterpriseStructureService>();
builder.Services.AddScoped<OrganisationSettingsService>();
builder.Services.AddScoped<OrganisationBrandingService>();
builder.Services.AddScoped<BusinessPartyService>();
builder.Services.AddScoped<JournalPostingService>();
builder.Services.AddScoped<SalesInvoiceService>();
builder.Services.AddScoped<FiscalisationWorkflowService>();
builder.Services.AddScoped<FiscalisationOrchestratorService>();
builder.Services.AddSingleton<FiscalisationSubmissionFactory>();
builder.Services.AddSingleton<FiscalCreditNoteSubmissionFactory>();
builder.Services.AddScoped<FiscalisationConfigurationService>();
builder.Services.AddScoped<FiscalisedSalesInvoicePostingService>();
builder.Services.AddScoped<FiscalisedSalesCreditNotePostingService>();
builder.Services.AddScoped<FiscalCreditNoteReversalSubmissionFactory>();
builder.Services.AddScoped<FiscalisedSalesCreditNoteReversalPostingService>();
builder.Services.AddScoped<FiscalSalesInvoiceVoidSubmissionFactory>();
builder.Services.AddScoped<FiscalisedSalesInvoiceVoidPostingService>();
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddScoped<IFiscalisationGateway, DevelopmentFiscalisationGateway>();
}
else
{
    builder.Services.AddScoped<IFiscalisationGateway, UnconfiguredFiscalisationGateway>();
}
builder.Services.AddScoped<RecurringSalesInvoiceService>();
builder.Services.AddScoped<RecurringInvoiceAutomationSettingsService>();
builder.Services.AddHostedService<RecurringInvoiceGenerationWorker>();
builder.Services.AddHostedService<DocumentExpiryWorker>();
builder.Services.AddHostedService<OverdueInvoiceWorker>();
builder.Services.AddHostedService<OverdueSupplierBillWorker>();
builder.Services.AddHostedService<UpcomingInvoiceWorker>();
builder.Services.AddHostedService<UpcomingSupplierBillWorker>();
builder.Services.AddHostedService<VatTurnoverMonitorWorker>();
builder.Services.AddHostedService<ImmutableDocumentIntegrityWorker>();
builder.Services.AddScoped<CustomerReceiptService>();
builder.Services.AddScoped<BusinessPartyDocumentService>();
builder.Services.AddScoped<DatabaseImmutableDocumentStore>();
builder.Services.AddScoped<IImmutableDocumentStore>(services =>
    services.GetRequiredService<DatabaseImmutableDocumentStore>());
builder.Services.AddScoped<IImmutableDocumentProviderDiagnostics>(services =>
    services.GetRequiredService<DatabaseImmutableDocumentStore>());
builder.Services.AddScoped<ImmutableDocumentStorageReadinessService>();
builder.Services.AddScoped<ImmutableDocumentBackfillService>();
builder.Services.AddScoped<ImmutableDocumentIntegrityService>();
builder.Services.AddSingleton<OrganisationUpdateBroker>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<CashflowForecastService>();
builder.Services.AddScoped<CashflowScenarioService>();
builder.Services.AddScoped<FinancialRiskService>();
builder.Services.AddScoped<FinancialControlService>();
builder.Services.AddScoped<FinancialIntelligenceService>();
builder.Services.AddScoped<CashRunwayService>();
builder.Services.AddScoped<VatWorkpaperService>();
builder.Services.AddScoped<VatTurnoverMonitorService>();
builder.Services.AddScoped<PurchasingService>();
builder.Services.AddScoped<SupplierBillDraftService>();
builder.Services.AddScoped<SupplierBillAttachmentService>();
builder.Services.AddScoped<PurchaseOrderService>();
builder.Services.AddScoped<PurchaseRequisitionService>();
builder.Services.AddScoped<PurchaseApprovalPolicyService>();
builder.Services.AddScoped<PurchaseOrderMatchService>();
builder.Services.AddScoped<RecurringSupplierBillService>();
builder.Services.AddHostedService<RecurringSupplierBillDraftGenerationWorker>();
builder.Services.AddScoped<SupplierCreditNoteService>();
builder.Services.AddScoped<BankReconciliationService>();
builder.Services.AddScoped<BankTransferService>();
builder.Services.AddScoped<BudgetService>();
builder.Services.AddScoped<BudgetReportingService>();
builder.Services.AddScoped<BudgetScopeService>();
builder.Services.AddScoped<BankStatementImportService>();
builder.Services.AddScoped<AccountingPeriodService>();
builder.Services.AddScoped<SalesCreditNoteService>();
builder.Services.AddScoped<SalesQuoteService>();
builder.Services.AddScoped<ChartOfAccountsService>();
builder.Services.AddScoped<FixedAssetService>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<ProjectProfitabilityService>();
builder.Services.AddScoped<ProjectProgressClaimService>();
builder.Services.AddScoped<ProjectRevenueRecognitionService>();
builder.Services.AddScoped<ProjectWipPostingService>();
builder.Services.AddScoped<BankRuleService>();
builder.Services.AddScoped<BankTransactionCodingService>();
builder.Services.AddScoped<ProductCatalogService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<FinancialReportService>();
builder.Services.AddScoped<GroupFinancialReportService>();
builder.Services.AddScoped<GroupExchangeRateService>();
builder.Services.AddScoped<TransactionCurrencyService>();
builder.Services.AddScoped<GroupEliminationService>();
builder.Services.AddScoped<BankReconciliationService>();
builder.Services.AddScoped<BankReconciliationSessionService>();
builder.Services.AddScoped<BankAccountService>();
builder.Services.AddScoped<DemoDataService>();
builder.Services.AddScoped<PlatformAdminAccessService>();
builder.Services.AddScoped<PlatformAdministrationService>();
builder.Services.AddScoped<MobileApiV1Service>();
builder.Services.AddScoped<MobileIdempotencyService>();
builder.Services.AddScoped<MobileDeviceSessionService>();
builder.Services.AddScoped<MobileClientEndpointFilter>();

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
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.UseRateLimiter();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapMobileApiV1();

if (mobileAuthenticationEnabled)
{
    app.MapMobileAuthenticationEndpoints();
}

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

// Supplier bill attachment endpoint (single registration only).
app.MapSupplierBillAttachmentEndpoints();
app.MapBusinessPartyDocumentEndpoints();
app.MapBankStatementDocumentEndpoints();

app.MapGet(
        "/health",
        async (ApplicationDbContext database, CancellationToken cancellationToken) =>
            await database.Database.CanConnectAsync(cancellationToken)
                ? Results.Ok(new { status = "healthy" })
                : Results.StatusCode(StatusCodes.Status503ServiceUnavailable))
    .AllowAnonymous();

if (app.Environment.IsDevelopment() ||
    builder.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var migrationScope = app.Services.CreateAsyncScope();
    var database = migrationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await database.Database.MigrateAsync();
    var backfill = migrationScope.ServiceProvider
        .GetRequiredService<ImmutableDocumentBackfillService>();
    var backfillResult = await backfill.BackfillAsync();
    if (backfillResult.Total > 0)
    {
        app.Logger.LogInformation(
            "Backfilled {Total} retained documents into immutable storage ({BusinessPartyDocuments} contact, {SupplierBillAttachments} supplier bill, {BankStatementDocuments} bank statement).",
            backfillResult.Total,
            backfillResult.BusinessPartyDocuments,
            backfillResult.SupplierBillAttachments,
            backfillResult.BankStatementDocuments);
    }
}

if (await AccountMaintenanceCommand.TryRunAsync(app, args))
{
    return;
}

await DevelopmentAccountSeeder.SeedAsync(app);
await PlatformAdminSeeder.SeedAsync(app);
await MobileAuthenticationSeeder.SeedAsync(app);

if (builder.Configuration.GetValue<bool>("DevSeed:SeedOnly"))
{
    return;
}

app.Run();

public partial class Program;
