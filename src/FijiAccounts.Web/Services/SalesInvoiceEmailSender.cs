using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text;
using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed class SalesInvoiceEmailSender(
    ApplicationDbContext db,
    TenantAccessService access,
    IEmailDeliveryService delivery)
{
    public async Task SendAsync(
        string userId,
        Guid organisationId,
        Guid invoiceId,
        string recipient,
        CancellationToken cancellationToken = default)
    {
        if (await access.FindAsync(userId, organisationId) is null ||
            !await access.CanPostJournalsAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException("You do not have permission to send sales invoices.");
        }

        recipient = recipient.Trim();
        if (!new EmailAddressAttribute().IsValid(recipient))
        {
            throw new InvalidOperationException("Enter a valid invoice email address.");
        }

        var invoice = await db.SalesInvoices
            .AsNoTracking()
            .Include(x => x.Organisation)
            .Include(x => x.Customer)
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(
                x => x.Id == invoiceId && x.OrganisationId == organisationId,
                cancellationToken)
            ?? throw new InvalidOperationException("Invoice not found.");

        if (invoice.Status is InvoiceStatus.Draft or InvoiceStatus.Voided)
        {
            throw new InvalidOperationException("Only active posted invoices can be emailed.");
        }

        await delivery.SendAsync(BuildMessage(invoice, recipient), cancellationToken);

        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = organisationId,
            UserId = userId,
            EventType = "SalesInvoiceEmailed",
            EntityType = nameof(SalesInvoice),
            EntityId = invoice.Id.ToString(),
            JsonData = JsonSerializer.Serialize(new
            {
                invoice.InvoiceNumber,
                Recipient = recipient,
                SentAt = DateTimeOffset.UtcNow
            })
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    internal static TransactionalEmail BuildMessage(SalesInvoice invoice, string recipient)
    {
        var supplier = invoice.SupplierNameSnapshot ?? invoice.Organisation.LegalName;
        var customer = invoice.RecipientNameSnapshot ?? invoice.Customer.Name;
        var subject = $"Invoice {invoice.InvoiceNumber} from {supplier}";
        var text = new StringBuilder()
            .AppendLine($"Hello {customer},")
            .AppendLine()
            .AppendLine($"Please find invoice {invoice.InvoiceNumber} from {supplier} below.")
            .AppendLine($"Issue date: {invoice.IssueDate:dd MMM yyyy}")
            .AppendLine($"Due date: {invoice.DueDate:dd MMM yyyy}")
            .AppendLine($"Amount due: {invoice.Currency} {invoice.TransactionTotal:N2}")
            .AppendLine()
            .AppendLine("Invoice lines:");
        foreach (var line in invoice.Lines)
        {
            text.AppendLine($"- {line.Description}: {invoice.Currency} {line.TransactionNetAmount:N2}");
        }
        text.AppendLine().AppendLine($"Regards,").AppendLine(supplier);

        static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
        var rows = string.Join(string.Empty, invoice.Lines.Select(line =>
            $"<tr><td style=\"padding:8px;border-bottom:1px solid #ddd\">{H(line.Description)}</td><td style=\"padding:8px;border-bottom:1px solid #ddd;text-align:right\">{H(invoice.Currency)} {line.TransactionNetAmount:N2}</td></tr>"));
        var html = $"""
            <p>Hello {H(customer)},</p>
            <p>Please find invoice <strong>{H(invoice.InvoiceNumber)}</strong> from {H(supplier)} below.</p>
            <table style="border-collapse:collapse;width:100%;max-width:640px">
              <tr><td style="padding:4px 0">Issue date</td><td style="text-align:right">{invoice.IssueDate:dd MMM yyyy}</td></tr>
              <tr><td style="padding:4px 0">Due date</td><td style="text-align:right">{invoice.DueDate:dd MMM yyyy}</td></tr>
              {rows}
              <tr><td style="padding:10px 8px"><strong>Amount due</strong></td><td style="padding:10px 8px;text-align:right"><strong>{H(invoice.Currency)} {invoice.TransactionTotal:N2}</strong></td></tr>
            </table>
            <p>Regards,<br>{H(supplier)}</p>
            """;
        return new TransactionalEmail(recipient, subject, text.ToString(), html);
    }
}
