using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed record SalesQuoteRequest(Guid OrganisationId, Guid CustomerId, DateOnly QuoteDate, DateOnly ExpiryDate, IReadOnlyList<SalesInvoiceLineRequest> Lines);

public sealed class SalesQuoteService(ApplicationDbContext db, TenantAccessService access, SalesInvoiceService invoices)
{
    public async Task<SalesQuote> CreateAsync(string userId, SalesQuoteRequest request, CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, request.OrganisationId)) throw new UnauthorizedAccessException("You cannot create quotes for this organisation.");
        if (request.ExpiryDate < request.QuoteDate || request.Lines.Count == 0) throw new InvalidOperationException("Enter valid quote dates and at least one line.");
        var organisation = await db.Organisations.SingleAsync(x => x.Id == request.OrganisationId, ct); var jurisdiction = IslandJurisdictions.Get(organisation.CountryCode); if (!jurisdiction.TaxPackEnabled) throw new InvalidOperationException($"The {jurisdiction.CountryName} tax pack must be verified before tax-inclusive quotes are enabled.");
        if (!await db.BusinessParties.AnyAsync(x => x.Id == request.CustomerId && x.OrganisationId == request.OrganisationId && x.IsActive && (x.Type & PartyType.Customer) != 0, ct)) throw new InvalidOperationException("Select an active customer.");
        var accountIds = request.Lines.Select(x => x.RevenueAccountId).Distinct().ToArray(); if (await db.LedgerAccounts.CountAsync(x => x.OrganisationId == request.OrganisationId && x.IsActive && x.Type == AccountType.Revenue && accountIds.Contains(x.Id), ct) != accountIds.Length) throw new InvalidOperationException("Select valid revenue accounts.");
        var schedule = IndirectTaxSchedules.For(organisation.CountryCode); var lines = request.Lines.Select(x => { if (string.IsNullOrWhiteSpace(x.Description) || x.Quantity <= 0 || x.UnitPrice < 0) throw new InvalidOperationException("Enter valid quote lines."); var tax = schedule.CalculateFromExclusive(new Money(x.Quantity * x.UnitPrice, organisation.BaseCurrency).Round(), request.QuoteDate, x.VatTreatment); return new SalesQuoteLine { Description = x.Description.Trim(), Quantity = x.Quantity, UnitPrice = x.UnitPrice, VatTreatment = x.VatTreatment, VatRate = tax.Rate, NetAmount = tax.Exclusive.Amount, VatAmount = tax.Vat.Amount, GrossAmount = tax.Inclusive.Amount, RevenueAccountId = x.RevenueAccountId }; }).ToList();
        var sequence = (await db.SalesQuotes.Where(x => x.OrganisationId == request.OrganisationId).MaxAsync(x => (long?)x.SequenceNumber, ct) ?? 0) + 1; var quote = new SalesQuote { OrganisationId = request.OrganisationId, CustomerId = request.CustomerId, SequenceNumber = sequence, QuoteNumber = $"QU-{sequence:D6}", QuoteDate = request.QuoteDate, ExpiryDate = request.ExpiryDate, Currency = organisation.BaseCurrency, Status = QuoteStatus.Draft, Subtotal = lines.Sum(x => x.NetAmount), VatTotal = lines.Sum(x => x.VatAmount), Total = lines.Sum(x => x.GrossAmount), CreatedByUserId = userId, Lines = lines };
        db.SalesQuotes.Add(quote); db.AuditEvents.Add(Audit(request.OrganisationId, userId, "SalesQuoteCreated", quote.Id, new { quote.QuoteNumber, quote.Total })); await db.SaveChangesAsync(ct); return quote;
    }

    public async Task<SalesInvoice> ConvertToDraftInvoiceAsync(string userId, Guid organisationId, Guid quoteId, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var quote = await db.SalesQuotes.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == quoteId && x.OrganisationId == organisationId, ct) ?? throw new InvalidOperationException("Quote not found.");
        if (quote.Status != QuoteStatus.Accepted) throw new InvalidOperationException("Only an accepted quote can be converted to an invoice.");
        if (quote.ExpiryDate < DateOnly.FromDateTime(DateTime.Today)) throw new InvalidOperationException("This quote has expired. Create a replacement quote before invoicing.");
        var request = new SalesInvoiceRequest(organisationId, quote.CustomerId, DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today.AddDays(30)), quote.Lines.Select(x => new SalesInvoiceLineRequest(x.Description, x.Quantity, x.UnitPrice, x.VatTreatment, x.RevenueAccountId)).ToList()); var invoice = await invoices.CreateDraftAsync(userId, request, ct);
        quote.Status = QuoteStatus.Invoiced; quote.ConvertedInvoiceId = invoice.Id; db.AuditEvents.Add(Audit(organisationId, userId, "SalesQuoteConverted", quote.Id, new { quote.QuoteNumber, invoice.InvoiceNumber })); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return invoice;
    }

    public async Task SetStatusAsync(string userId, Guid organisationId, Guid quoteId, QuoteStatus status, CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId)) throw new UnauthorizedAccessException("You cannot update quotes for this organisation.");
        var quote = await db.SalesQuotes.SingleOrDefaultAsync(x => x.Id == quoteId && x.OrganisationId == organisationId, ct) ?? throw new InvalidOperationException("Quote not found.");
        if (quote.Status == QuoteStatus.Invoiced) throw new InvalidOperationException("An invoiced quote cannot be changed.");
        if (status == QuoteStatus.Invoiced) throw new InvalidOperationException("Use Convert to invoice to complete this quote.");
        var allowed = (quote.Status, status) switch { (QuoteStatus.Draft, QuoteStatus.Sent) => true, (QuoteStatus.Sent, QuoteStatus.Accepted) => true, (QuoteStatus.Sent, QuoteStatus.Declined) => true, (QuoteStatus.Draft, QuoteStatus.Expired) => true, (QuoteStatus.Sent, QuoteStatus.Expired) => true, _ => false };
        if (!allowed) throw new InvalidOperationException($"A {quote.Status} quote cannot be changed to {status}.");
        quote.Status = status;
        db.AuditEvents.Add(Audit(organisationId, userId, "SalesQuoteStatusChanged", quote.Id, new { quote.QuoteNumber, Status = status.ToString() }));
        await db.SaveChangesAsync(ct);
    }

    private static AuditEvent Audit(Guid organisationId, string userId, string eventType, Guid id, object data) => new() { OrganisationId = organisationId, UserId = userId, EventType = eventType, EntityType = nameof(SalesQuote), EntityId = id.ToString(), JsonData = JsonSerializer.Serialize(data) };
}
