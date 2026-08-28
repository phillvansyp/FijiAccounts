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
    IEmailDeliveryService delivery,
    SalesInvoicePdfRenderer pdfRenderer)
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
            .Include(x => x.Branch)
            .Include(x => x.Division)
            .Include(x => x.Lines)
                .ThenInclude(x => x.Project)
            .Include(x => x.Lines)
                .ThenInclude(x => x.ProjectCostCode)
            .SingleOrDefaultAsync(
                x => x.Id == invoiceId && x.OrganisationId == organisationId,
                cancellationToken)
            ?? throw new InvalidOperationException("Invoice not found.");

        if (invoice.Status is InvoiceStatus.Draft or InvoiceStatus.Voided)
        {
            throw new InvalidOperationException("Only active posted invoices can be emailed.");
        }

        var branding = await db.OrganisationBrandings
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganisationId == organisationId, cancellationToken);
        var pdf = pdfRenderer.Render(invoice, branding);

        await delivery.SendAsync(BuildMessage(invoice, recipient, pdf), cancellationToken);

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

    internal static TransactionalEmail BuildMessage(
        SalesInvoice invoice,
        string recipient,
        byte[] pdf)
    {
        var supplier = invoice.SupplierNameSnapshot ?? invoice.Organisation.LegalName;
        var customer = invoice.RecipientNameSnapshot ?? invoice.Customer.Name;
        var subject = $"Invoice {invoice.InvoiceNumber} from {supplier}";
        var text = new StringBuilder()
            .AppendLine($"Hello {customer},")
            .AppendLine()
            .AppendLine($"Please find invoice {invoice.InvoiceNumber} from {supplier} attached.")
            .AppendLine($"Issue date: {invoice.IssueDate:dd MMM yyyy}")
            .AppendLine($"Due date: {invoice.DueDate:dd MMM yyyy}")
            .AppendLine($"Amount due: {invoice.Currency} {invoice.TransactionTotal:N2}")
            .AppendLine()
            .AppendLine("Regards,")
            .AppendLine(supplier);

        static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
        var html = $"""
            <p>Hello {H(customer)},</p>
            <p>Please find invoice <strong>{H(invoice.InvoiceNumber)}</strong> from {H(supplier)} attached as a PDF.</p>
            <p>Amount due: <strong>{H(invoice.Currency)} {invoice.TransactionTotal:N2}</strong><br>
            Due date: {invoice.DueDate:dd MMM yyyy}</p>
            <p>Regards,<br>{H(supplier)}</p>
            """;
        return new TransactionalEmail(
            recipient,
            subject,
            text.ToString(),
            html,
            [new TransactionalEmailAttachment(
                $"{invoice.InvoiceNumber}.pdf",
                "application/pdf",
                pdf)]);
    }
}
