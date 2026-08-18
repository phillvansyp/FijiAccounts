using System.ComponentModel.DataAnnotations;
using FijiAccounts.Domain.Tax;

namespace FijiAccounts.Web.Data;

public enum QuoteStatus { Draft, Sent, Accepted, Declined, Expired, Invoiced }

public sealed class SalesQuote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    public Guid CustomerId { get; set; }
    public BusinessParty Customer { get; set; } = null!;
    public long SequenceNumber { get; set; }
    [MaxLength(40)] public required string QuoteNumber { get; set; }
    public DateOnly QuoteDate { get; set; }
    public DateOnly ExpiryDate { get; set; }
    [MaxLength(3)] public string Currency { get; set; } = "FJD";
    public QuoteStatus Status { get; set; }
    public decimal Subtotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal Total { get; set; }
    public Guid? ConvertedInvoiceId { get; set; }
    public SalesInvoice? ConvertedInvoice { get; set; }
    public List<SalesQuoteLine> Lines { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public required string CreatedByUserId { get; set; }
}

public sealed class SalesQuoteLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SalesQuoteId { get; set; }
    public SalesQuote SalesQuote { get; set; } = null!;
    [MaxLength(300)] public required string Description { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public VatTreatment VatTreatment { get; set; }
    public decimal VatRate { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal GrossAmount { get; set; }
    public Guid RevenueAccountId { get; set; }
    public LedgerAccount RevenueAccount { get; set; } = null!;
}
