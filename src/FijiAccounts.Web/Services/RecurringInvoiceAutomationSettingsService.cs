using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record UpdateRecurringInvoiceAutomationSettingsRequest(
    Guid OrganisationId,
    bool Enabled,
    TimeOnly RunTime);

public sealed class RecurringInvoiceAutomationSettingsService(
    ApplicationDbContext db,
    TenantAccessService access)
{
    public async Task<Organisation> UpdateAsync(
        string userId,
        UpdateRecurringInvoiceAutomationSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await access.CanPostJournalsAsync(
                userId,
                request.OrganisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot change recurring invoice automation settings for this organisation.");
        }

        if (request.RunTime.Ticks % TimeSpan.TicksPerMinute != 0)
        {
            throw new InvalidOperationException(
                "Select an automation run time using whole minutes.");
        }

        var organisation = await db.Organisations.SingleOrDefaultAsync(
            x => x.Id == request.OrganisationId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The organisation could not be updated.");

        var previous = new
        {
            Enabled = organisation.RecurringInvoiceAutomationEnabled,
            RunTime = organisation.RecurringInvoiceAutomationTime.ToString("HH:mm")
        };
        var updated = new
        {
            request.Enabled,
            RunTime = request.RunTime.ToString("HH:mm")
        };
        if (previous.Equals(updated))
        {
            return organisation;
        }

        organisation.RecurringInvoiceAutomationEnabled = request.Enabled;
        organisation.RecurringInvoiceAutomationTime = request.RunTime;
        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = request.OrganisationId,
            UserId = userId,
            EventType = "RecurringInvoiceAutomationSettingsUpdated",
            EntityType = nameof(Organisation),
            EntityId = organisation.Id.ToString(),
            JsonData = JsonSerializer.Serialize(
                new { Old = previous, New = updated })
        });
        await db.SaveChangesAsync(cancellationToken);

        return organisation;
    }
}
