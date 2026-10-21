using System.ComponentModel.DataAnnotations;
using FijiAccounts.Domain.Tax;

namespace FijiAccounts.Web.Data;

public sealed class BankCodingMemory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;

    [MaxLength(300)]
    public required string PayeeKey { get; set; }

    public decimal Amount { get; set; }
    public Guid TargetAccountId { get; set; }
    public LedgerAccount TargetAccount { get; set; } = null!;
    public Guid DivisionId { get; set; }
    public Division Division { get; set; } = null!;

    [MaxLength(300)]
    public required string Description { get; set; }

    public VatTreatment VatTreatment { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public required string UpdatedByUserId { get; set; }
}
