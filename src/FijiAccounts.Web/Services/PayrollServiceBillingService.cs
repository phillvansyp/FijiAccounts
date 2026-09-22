using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record PayrollServiceCharge(Guid BillId, Guid CustomerId, string CustomerName,
    string Email, DateOnly IssueDate, DateOnly DueDate, DateOnly PeriodStart, DateOnly PeriodEnd,
    string Currency, decimal Subtotal, decimal Tax, decimal Total, string Reference, string Detail, string? Address = null, string? Tin = null);
public sealed record PayrollServiceInvoiceResult(Guid InvoiceId, string InvoiceNumber);

public sealed class PayrollServiceBillingService(ApplicationDbContext db, SalesInvoiceService invoices)
{
    public async Task<PayrollServiceInvoiceResult> ImportAsync(Guid organisationId, PayrollServiceCharge charge, CancellationToken ct)
    {
        if (charge.BillId == Guid.Empty || charge.CustomerId == Guid.Empty ||
            string.IsNullOrWhiteSpace(charge.CustomerName) || charge.CustomerName.Length > 160 ||
            (charge.Address?.Length ?? 0) > 500 || (charge.Tin?.Length ?? 0) > 32 ||
            string.IsNullOrWhiteSpace(charge.Email) || charge.Email.Length > 320 || string.IsNullOrWhiteSpace(charge.Reference) || charge.Reference.Length > 100 || charge.Detail is null || charge.Detail.Length > 16000 ||
            charge.Subtotal <= 0 || charge.Tax < 0 || charge.Total != charge.Subtotal + charge.Tax ||
            charge.DueDate < charge.IssueDate || charge.PeriodEnd < charge.PeriodStart)
            throw new InvalidOperationException("The Payroll Island billing snapshot is invalid.");
        var json = JsonSerializer.Serialize(charge);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var previous = await db.Set<PayrollServiceBillingImport>().Include(x => x.SalesInvoice).ThenInclude(x => x.Lines)
            .SingleOrDefaultAsync(x => x.SourceBillId == charge.BillId, ct);
        if (previous is not null)
        {
            if (previous.OrganisationId != organisationId || previous.PayloadHash != hash)
                throw new InvalidOperationException("This source bill was already imported with different details. Review the existing invoice.");
            var line = previous.SalesInvoice.Lines.SingleOrDefault();
            if (line is not null && line.Description == BaseDescription(charge) && Description(charge) != line.Description)
            {
                line.Description = Description(charge);
                db.AuditEvents.Add(new AuditEvent { OrganisationId = organisationId, UserId = "payroll-island",
                    EventType = "PayrollInvoiceEmployeeDetailAdded", EntityType = nameof(SalesInvoice),
                    EntityId = previous.SalesInvoiceId.ToString(), JsonData = JsonSerializer.Serialize(new { line.Description }) });
                db.InvoicesBeingCorrected.Add(previous.SalesInvoiceId);
                try { await db.SaveChangesAsync(ct); }
                finally { db.InvoicesBeingCorrected.Remove(previous.SalesInvoiceId); }
            }
            await transaction.CommitAsync(ct);
            return new(previous.SalesInvoiceId, previous.SalesInvoice.InvoiceNumber);
        }
        var org = await db.Organisations.SingleAsync(x => x.Id == organisationId, ct);
        if (org.BaseCurrency != charge.Currency)
            throw new InvalidOperationException("Billing currency differs from the issuing organisation. Configure an exchange rate before importing.");
        if (await db.FiscalisationConfigurations.AnyAsync(x => x.OrganisationId == organisationId && x.IsEnabled, ct))
            throw new InvalidOperationException("Automatic service billing requires the fiscalisation workflow for this organisation.");
        var revenue = await db.LedgerAccounts.SingleAsync(x => x.OrganisationId == organisationId && x.Code == "4000" && x.IsActive, ct);
        var customerId = await db.Set<PayrollServiceBillingImport>()
            .Where(x => x.OrganisationId == organisationId && x.SourceCustomerId == charge.CustomerId)
            .Select(x => (Guid?)x.SalesInvoice.CustomerId).FirstOrDefaultAsync(ct);
        // Source IDs, rather than similar names, keep unrelated customer accounts separate.
        if (customerId is null)
        {
            var customer = new BusinessParty { OrganisationId = organisationId, Name = charge.CustomerName,
                Email = charge.Email, AccountsEmail = charge.Email, Address = charge.Address, Tin = charge.Tin, Type = PartyType.Customer };
            db.BusinessParties.Add(customer);
            await db.SaveChangesAsync(ct);
            customerId = customer.Id;
        }
        var invoice = await invoices.CreateAndPostAutomaticallyAsync(organisationId,
            new(organisationId, customerId.Value, charge.IssueDate, charge.DueDate,
                [new(Description(charge),
                    1, charge.Subtotal, VatTreatment.Standard, revenue.Id, CustomerPurchaseOrderNumber: charge.Reference)],
                Currency: charge.Currency), ct);
        if (invoice.TransactionSubtotal != charge.Subtotal || invoice.TransactionVatTotal != charge.Tax || invoice.TransactionTotal != charge.Total)
            throw new InvalidOperationException("Account Island tax calculation differs from the original bill. Nothing was imported; review the billing tax settings.");
        db.Set<PayrollServiceBillingImport>().Add(new() { OrganisationId = organisationId, SourceBillId = charge.BillId,
            SourceCustomerId = charge.CustomerId, PeriodStart = charge.PeriodStart, SalesInvoiceId = invoice.Id,
            PayloadHash = hash, SourceJson = json });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(invoice.Id, invoice.InvoiceNumber);
    }

    private static string BaseDescription(PayrollServiceCharge charge) =>
        $"Payroll Island services {charge.PeriodStart:dd MMM yyyy}–{charge.PeriodEnd:dd MMM yyyy} · {charge.Reference}";

    public static string Description(PayrollServiceCharge charge)
    {
        var match = Regex.Match(charge.Detail, @"(?m)^Distinct employees active during [^:\r\n]+: ([0-9]+) (\([^\r\n]+\) = [A-Z]{3} [0-9,.]+)\r?$");
        return match.Success ? $"Payroll services {charge.PeriodStart:dd MMM yyyy}–{charge.PeriodEnd:dd MMM yyyy}\n{match.Groups[1].Value} employees {match.Groups[2].Value}" : BaseDescription(charge);
    }

    public static bool Authenticate(string? configured, string? supplied) =>
        configured is { Length: >= 32 } && supplied is not null &&
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(configured)),
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied)));

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/integrations/payroll-island/service-bills/{sourceBillId:guid}/invoice.pdf",
            async (Guid sourceBillId, HttpContext context, IConfiguration config, ApplicationDbContext db,
                SalesInvoicePdfRenderer renderer, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!Authenticate(config["PayrollServiceBilling:Token"], context.Request.Headers["X-Billing-Key"]))
                return Results.Unauthorized();
            if (!Guid.TryParse(config["PayrollServiceBilling:OrganisationId"], out var org)) return Results.NotFound();
            var invoiceId = await db.Set<PayrollServiceBillingImport>().Where(x => x.SourceBillId == sourceBillId && x.OrganisationId == org)
                .Select(x => (Guid?)x.SalesInvoiceId).SingleOrDefaultAsync(ct);
            if (invoiceId is null) return Results.NotFound();
            var invoice = await db.SalesInvoices.AsNoTracking().Include(x => x.Organisation).Include(x => x.Customer)
                .Include(x => x.Branch).Include(x => x.Division).Include(x => x.Lines).ThenInclude(x => x.Project)
                .Include(x => x.Lines).ThenInclude(x => x.ProjectCostCode)
                .SingleAsync(x => x.Id == invoiceId && x.OrganisationId == org, ct);
            if (invoice.Status is InvoiceStatus.Draft or InvoiceStatus.Voided) return Results.Conflict();
            var branding = await db.OrganisationBrandings.AsNoTracking().SingleOrDefaultAsync(x => x.OrganisationId == org, ct);
            return Results.File(renderer.Render(invoice, branding), "application/pdf", invoice.InvoiceNumber + ".pdf");
        }).AllowAnonymous();
        app.MapPost("/api/integrations/payroll-island/service-bills", async (HttpContext context,
            IConfiguration config, PayrollServiceBillingService service, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!Authenticate(config["PayrollServiceBilling:Token"], context.Request.Headers["X-Billing-Key"]))
                return Results.Unauthorized();
            if (!Guid.TryParse(config["PayrollServiceBilling:OrganisationId"], out var org))
                return Results.Problem("The service billing organisation is not configured.", statusCode: 503);
            if (context.Request.ContentLength is > 32768) return Results.StatusCode(413);
            try
            {
                var charge = await context.Request.ReadFromJsonAsync<PayrollServiceCharge>(ct);
                if (charge is null) return Results.BadRequest();
                return Results.Ok(await service.ImportAsync(org, charge, ct));
            }
            catch (InvalidOperationException ex) { return Results.Conflict(new { message = ex.Message }); }
            catch (DbUpdateException) { return Results.Conflict(new { message = "Billing import conflicted. Retry the same source bill." }); }
        }).AllowAnonymous();
    }
}
