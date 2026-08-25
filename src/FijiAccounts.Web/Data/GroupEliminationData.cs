using System.ComponentModel.DataAnnotations;
using FijiAccounts.Domain.Accounting;

namespace FijiAccounts.Web.Data;

public sealed class GroupEliminationJournal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationGroupId { get; set; }
    public OrganisationGroup OrganisationGroup { get; set; } = null!;
    public DateOnly EntryDate { get; set; }
    [MaxLength(80)] public required string Reference { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
    [MaxLength(3)] public required string Currency { get; set; }
    [MaxLength(450)] public required string PostedByUserId { get; set; }
    public DateTimeOffset PostedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<GroupEliminationJournalLine> Lines { get; set; } = [];
}

public sealed class GroupEliminationJournalLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupEliminationJournalId { get; set; }
    public GroupEliminationJournal GroupEliminationJournal { get; set; } = null!;
    [MaxLength(32)] public required string AccountCode { get; set; }
    [MaxLength(160)] public required string AccountName { get; set; }
    public AccountType AccountType { get; set; }
    [MaxLength(300)] public required string Description { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
}
