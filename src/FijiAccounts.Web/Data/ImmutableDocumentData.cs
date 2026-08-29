using System.ComponentModel.DataAnnotations;

namespace FijiAccounts.Web.Data;

public sealed class ImmutableDocumentObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    [MaxLength(40)] public required string Provider { get; set; }
    [MaxLength(500)] public required string ObjectKey { get; set; }
    [MaxLength(64)] public required string Sha256 { get; set; }
    public long ContentLength { get; set; }
    public required byte[] Content { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public required string CreatedByUserId { get; set; }
}
