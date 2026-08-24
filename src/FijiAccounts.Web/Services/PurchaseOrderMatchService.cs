using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record PurchaseMatchResult(
    bool IsException,
    decimal QuantityVariance,
    decimal PriceVariance,
    decimal TotalVariance,
    string Summary,
    string Fingerprint);

public sealed record PurchaseMatchToleranceRequest(
    Guid OrganisationId,
    decimal QuantityTolerancePercent,
    decimal PriceTolerancePercent,
    decimal TotalToleranceAmount);

public sealed class PurchaseOrderMatchService(
    ApplicationDbContext db,
    TenantAccessService access)
{
    public async Task ConfigureTolerancesAsync(
        string userId,
        PurchaseMatchToleranceRequest request,
        CancellationToken ct = default)
    {
        if (!await access.CanManageTeamAsync(userId, request.OrganisationId))
        {
            throw new UnauthorizedAccessException(
                "Only an owner or administrator can configure purchase matching tolerances.");
        }

        if (request.QuantityTolerancePercent is < 0 or > 100 ||
            request.PriceTolerancePercent is < 0 or > 100 ||
            request.TotalToleranceAmount is < 0 or > 1_000_000)
        {
            throw new InvalidOperationException(
                "Enter percentage tolerances from 0 to 100 and a non-negative total tolerance.");
        }

        var organisation = await db.Organisations.SingleAsync(
            x => x.Id == request.OrganisationId,
            ct);
        var old = new
        {
            organisation.PurchaseQuantityTolerancePercent,
            organisation.PurchasePriceTolerancePercent,
            organisation.PurchaseTotalToleranceAmount
        };
        organisation.PurchaseQuantityTolerancePercent = request.QuantityTolerancePercent;
        organisation.PurchasePriceTolerancePercent = request.PriceTolerancePercent;
        organisation.PurchaseTotalToleranceAmount = request.TotalToleranceAmount;
        db.AuditEvents.Add(Audit(
            organisation.Id,
            userId,
            "PurchaseMatchTolerancesChanged",
            nameof(Organisation),
            organisation.Id,
            new
            {
                Old = old,
                New = new
                {
                    request.QuantityTolerancePercent,
                    request.PriceTolerancePercent,
                    request.TotalToleranceAmount
                }
            }));
        await db.SaveChangesAsync(ct);
    }

    public async Task ApproveExceptionAsync(
        string userId,
        Guid organisationId,
        Guid purchaseOrderId,
        string reason,
        CancellationToken ct = default)
    {
        var isOwner = await db.OrganisationMemberships.AnyAsync(x =>
            x.UserId == userId && x.OrganisationId == organisationId &&
            x.Role == OrganisationRole.Owner &&
            (x.Organisation.OrganisationGroupId == null ||
             x.Organisation.OrganisationGroup!.Status == TenantStatus.Active), ct);
        if (!isOwner)
        {
            throw new UnauthorizedAccessException(
                "Only an organisation owner can approve a purchase match exception.");
        }

        var normalizedReason = reason.Trim();
        if (normalizedReason.Length is 0 or > 500)
        {
            throw new InvalidOperationException(
                "Enter an approval reason of 500 characters or fewer.");
        }

        var order = await db.PurchaseOrders.SingleOrDefaultAsync(x =>
            x.Id == purchaseOrderId && x.OrganisationId == organisationId, ct)
            ?? throw new InvalidOperationException("Purchase order not found.");
        if (order.MatchStatus != PurchaseMatchStatus.Exception ||
            string.IsNullOrWhiteSpace(order.MatchFingerprint))
        {
            throw new InvalidOperationException(
                "This purchase order does not have a current match exception to approve.");
        }

        order.MatchStatus = PurchaseMatchStatus.ExceptionApproved;
        order.MatchApprovedAt = DateTimeOffset.UtcNow;
        order.MatchApprovedByUserId = userId;
        order.MatchApprovalReason = normalizedReason;
        order.UpdatedAt = DateTimeOffset.UtcNow;
        db.AuditEvents.Add(Audit(
            organisationId,
            userId,
            "PurchaseMatchExceptionApproved",
            nameof(PurchaseOrder),
            order.Id,
            new
            {
                order.PurchaseOrderNumber,
                order.MatchSummary,
                order.MatchQuantityVariance,
                order.MatchPriceVariance,
                order.MatchTotalVariance,
                Reason = normalizedReason,
                order.MatchFingerprint
            }));
        await db.SaveChangesAsync(ct);
    }

    public static PurchaseMatchResult Evaluate(
        PurchaseOrder order,
        Organisation organisation,
        Guid invoiceSupplierId,
        IReadOnlyList<SupplierBillLineRequest> invoiceLines)
    {
        var reasons = new List<string>();
        var quantityVariance = 0m;
        var priceVariance = 0m;

        if (invoiceSupplierId != order.SupplierId)
        {
            reasons.Add("invoice supplier differs from the purchase order");
        }

        if (invoiceLines.Count != order.Lines.Count)
        {
            reasons.Add($"line count differs ({order.Lines.Count} ordered, {invoiceLines.Count} invoiced)");
        }

        var comparedCount = Math.Min(order.Lines.Count, invoiceLines.Count);
        for (var index = 0; index < comparedCount; index++)
        {
            var ordered = order.Lines[index];
            var invoiced = invoiceLines[index];
            var lineNumber = index + 1;
            var quantityDifference = Math.Abs(invoiced.Quantity - ordered.QuantityReceived);
            var priceDifference = Math.Abs(invoiced.UnitPrice - ordered.UnitPrice);
            quantityVariance += quantityDifference;
            priceVariance += decimal.Round(
                priceDifference * invoiced.Quantity,
                2,
                MidpointRounding.AwayFromZero);

            var quantityPercent = Percent(quantityDifference, ordered.QuantityReceived);
            var pricePercent = Percent(priceDifference, ordered.UnitPrice);
            if (quantityPercent > organisation.PurchaseQuantityTolerancePercent)
            {
                reasons.Add($"line {lineNumber} quantity variance {quantityPercent:N2}% exceeds {organisation.PurchaseQuantityTolerancePercent:N2}%");
            }

            if (pricePercent > organisation.PurchasePriceTolerancePercent)
            {
                reasons.Add($"line {lineNumber} price variance {pricePercent:N2}% exceeds {organisation.PurchasePriceTolerancePercent:N2}%");
            }
        }

        var invoiceTotal = invoiceLines.Sum(x => x.Quantity * x.UnitPrice);
        var totalVariance = decimal.Round(
            Math.Abs(invoiceTotal - order.Subtotal),
            2,
            MidpointRounding.AwayFromZero);
        if (totalVariance > organisation.PurchaseTotalToleranceAmount)
        {
            reasons.Add($"net total variance ${totalVariance:N2} exceeds ${organisation.PurchaseTotalToleranceAmount:N2}");
        }

        var fingerprint = Fingerprint(invoiceSupplierId, invoiceLines);
        return new PurchaseMatchResult(
            reasons.Count > 0,
            quantityVariance,
            priceVariance,
            totalVariance,
            reasons.Count == 0
                ? "PO, received quantities and supplier invoice are within tolerance."
                : string.Join("; ", reasons),
            fingerprint);
    }

    private static decimal Percent(decimal difference, decimal basis) =>
        basis == 0
            ? difference == 0 ? 0 : 100m
            : decimal.Round(difference / Math.Abs(basis) * 100m, 4,
                MidpointRounding.AwayFromZero);

    private static string Fingerprint(
        Guid supplierId,
        IReadOnlyList<SupplierBillLineRequest> lines)
    {
        var value = string.Join("|",
            new[] { supplierId.ToString("N") }.Concat(lines.Select(x => string.Join(":",
                x.Description.Trim(),
                x.Quantity.ToString(CultureInfo.InvariantCulture),
                x.UnitPrice.ToString(CultureInfo.InvariantCulture),
                (int)x.VatTreatment,
                x.ExpenseAccountId.ToString("N"),
                x.ProductItemId?.ToString("N") ?? ""))));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    internal static AuditEvent Audit(
        Guid organisationId,
        string userId,
        string eventType,
        string entityType,
        Guid entityId,
        object evidence) => new()
        {
            OrganisationId = organisationId,
            UserId = userId,
            EventType = eventType,
            EntityType = entityType,
            EntityId = entityId.ToString(),
            JsonData = JsonSerializer.Serialize(evidence)
        };
}
