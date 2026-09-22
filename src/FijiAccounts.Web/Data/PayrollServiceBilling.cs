using System.ComponentModel.DataAnnotations;

namespace FijiAccounts.Web.Data;

public sealed class PayrollServiceBillingImport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Guid SourceBillId { get; set; }
    public Guid SourceCustomerId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public Guid SalesInvoiceId { get; set; }
    public SalesInvoice SalesInvoice { get; set; } = null!;
    [MaxLength(64)] public required string PayloadHash { get; set; }
    public required string SourceJson { get; set; }
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;
}
