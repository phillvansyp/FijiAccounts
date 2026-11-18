using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Data;

[PrimaryKey(nameof(ConnectionId), nameof(TenantId), nameof(Dataset))]
public sealed class XeroDatasetCheck
{
    public Guid ConnectionId { get; set; }
    public Guid TenantId { get; set; }
    public required string Dataset { get; set; }
    public DateTimeOffset CheckedAt { get; set; }
}

[PrimaryKey(nameof(ConnectionId), nameof(TenantId), nameof(Kind), nameof(PeriodFrom), nameof(PeriodTo))]
public sealed class XeroPeriodReport
{
    public Guid ConnectionId { get; set; }
    public Guid TenantId { get; set; }
    public required string Kind { get; set; }
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public DateTimeOffset RetrievedAt { get; set; }
    public required byte[] Payload { get; set; }
}
