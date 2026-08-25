using System.ComponentModel.DataAnnotations;

namespace FijiAccounts.Web.Data;

public enum CashflowScenarioEventKind
{
    PlannedReceipt,
    PlannedPayment,
    CustomerReceiptDelay
}

public enum CashflowScenarioFrequency
{
    OneOff,
    Monthly
}

public sealed class CashflowScenario
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    [MaxLength(120)] public required string Name { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
    public bool IsArchived { get; set; }
    [MaxLength(450)] public required string CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<CashflowScenarioEvent> Events { get; set; } = [];
}

public sealed class CashflowScenarioEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CashflowScenarioId { get; set; }
    public CashflowScenario CashflowScenario { get; set; } = null!;
    public CashflowScenarioEventKind Kind { get; set; }
    public CashflowScenarioFrequency Frequency { get; set; }
    [MaxLength(160)] public required string Title { get; set; }
    public decimal Amount { get; set; }
    public DateOnly EventDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public Guid? SalesInvoiceId { get; set; }
    public SalesInvoice? SalesInvoice { get; set; }
    [MaxLength(80)] public string? SourceReference { get; set; }
    public DateOnly? OriginalDate { get; set; }
    [MaxLength(450)] public required string CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
