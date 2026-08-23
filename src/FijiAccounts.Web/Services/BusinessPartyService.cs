using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record CreateBusinessPartyRequest(
    Guid OrganisationId,
    string Name,
    string? Email,
    string? Tin,
    PartyType Type,
    Guid? DefaultSalesAccountId,
    VatTreatment? DefaultSalesVatTreatment,
    Guid? DefaultPurchaseAccountId,
    VatTreatment? DefaultPurchaseVatTreatment,
    PaymentTermType DefaultSalesInvoicePaymentTermType,
    int DefaultSalesInvoiceDueDays,
    PaymentTermType DefaultSupplierBillPaymentTermType,
    int DefaultSupplierBillDueDays);

public sealed record UpdateCustomerDefaultsRequest(
    Guid OrganisationId,
    Guid BusinessPartyId,
    Guid? SalesAccountId,
    VatTreatment? VatTreatment,
    PaymentTermType PaymentTermType,
    int DueDays);

public sealed record UpdateSupplierDefaultsRequest(
    Guid OrganisationId,
    Guid BusinessPartyId,
    Guid? PurchaseAccountId,
    VatTreatment? VatTreatment,
    PaymentTermType PaymentTermType,
    int DueDays);

public sealed record LearnCustomerSalesDefaultsRequest(
    Guid OrganisationId,
    Guid BusinessPartyId,
    Guid SalesAccountId,
    VatTreatment VatTreatment);

public sealed class BusinessPartyService(
    ApplicationDbContext db,
    TenantAccessService access)
{
    public async Task<BusinessParty> CreateAsync(
        string userId,
        CreateBusinessPartyRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireAccessAsync(
            userId,
            request.OrganisationId,
            cancellationToken);
        ValidatePartyType(request.Type);
        ValidatePaymentTerm(
            request.DefaultSalesInvoicePaymentTermType,
            request.DefaultSalesInvoiceDueDays);
        ValidatePaymentTerm(
            request.DefaultSupplierBillPaymentTermType,
            request.DefaultSupplierBillDueDays);
        if ((request.Type & PartyType.Customer) != 0)
        {
            await ValidateSalesDefaultsAsync(
                request.OrganisationId,
                request.DefaultSalesAccountId,
                request.DefaultSalesVatTreatment,
                cancellationToken);
        }

        if ((request.Type & PartyType.Supplier) != 0)
        {
            await ValidatePurchaseDefaultsAsync(
                request.OrganisationId,
                request.DefaultPurchaseAccountId,
                request.DefaultPurchaseVatTreatment,
                cancellationToken);
        }

        var email = OptionalText(request.Email, 320, "email address");
        if (email is not null && !new EmailAddressAttribute().IsValid(email))
        {
            throw new InvalidOperationException(
                "Enter a valid email address.");
        }

        var party =
            new BusinessParty
            {
                OrganisationId = request.OrganisationId,
                Name = RequiredText(request.Name, 160, "contact name"),
                Email = email,
                Tin = OptionalText(
                    request.Tin,
                    32,
                    "tax identification number"),
                Type = request.Type,
                DefaultSalesAccountId = (request.Type & PartyType.Customer) != 0
                    ? request.DefaultSalesAccountId
                    : null,
                DefaultSalesVatTreatment = (request.Type & PartyType.Customer) != 0
                    ? request.DefaultSalesVatTreatment
                    : null,
                DefaultPurchaseAccountId = (request.Type & PartyType.Supplier) != 0
                    ? request.DefaultPurchaseAccountId
                    : null,
                DefaultPurchaseVatTreatment =
                    (request.Type & PartyType.Supplier) != 0
                        ? request.DefaultPurchaseVatTreatment
                        : null,
                DefaultSalesInvoicePaymentTermType =
                    request.DefaultSalesInvoicePaymentTermType,
                DefaultSalesInvoiceDueDays =
                    request.DefaultSalesInvoiceDueDays,
                DefaultSupplierBillPaymentTermType =
                    request.DefaultSupplierBillPaymentTermType,
                DefaultSupplierBillDueDays =
                    request.DefaultSupplierBillDueDays
            };

        db.BusinessParties.Add(party);
        db.AuditEvents.Add(CreateAuditEvent(
            request.OrganisationId,
            userId,
            "BusinessPartyCreated",
            party.Id,
            new
            {
                party.Name,
                party.Email,
                party.Tin,
                Type = party.Type.ToString(),
                CustomerDefaults = new
                {
                    party.DefaultSalesAccountId,
                    VatTreatment = party.DefaultSalesVatTreatment?.ToString(),
                    PaymentTermType = party.DefaultSalesInvoicePaymentTermType.ToString(),
                    DueDays = party.DefaultSalesInvoiceDueDays
                },
                SupplierDefaults = new
                {
                    party.DefaultPurchaseAccountId,
                    VatTreatment = party.DefaultPurchaseVatTreatment?.ToString(),
                    PaymentTermType = party.DefaultSupplierBillPaymentTermType.ToString(),
                    DueDays = party.DefaultSupplierBillDueDays
                }
            }));
        await db.SaveChangesAsync(cancellationToken);

        return party;
    }

    public async Task UpdateCustomerDefaultsAsync(
        string userId,
        UpdateCustomerDefaultsRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireAccessAsync(
            userId,
            request.OrganisationId,
            cancellationToken);
        ValidatePaymentTerm(request.PaymentTermType, request.DueDays);
        await ValidateSalesDefaultsAsync(
            request.OrganisationId,
            request.SalesAccountId,
            request.VatTreatment,
            cancellationToken);

        var party = await GetPartyAsync(
            request.OrganisationId,
            request.BusinessPartyId,
            PartyType.Customer,
            cancellationToken);
        var previous = new
        {
            SalesAccountId = party.DefaultSalesAccountId,
            VatTreatment = party.DefaultSalesVatTreatment?.ToString(),
            PaymentTermType = party.DefaultSalesInvoicePaymentTermType.ToString(),
            DueDays = party.DefaultSalesInvoiceDueDays
        };
        var updated = new
        {
            SalesAccountId = request.SalesAccountId,
            VatTreatment = request.VatTreatment?.ToString(),
            PaymentTermType = request.PaymentTermType.ToString(),
            DueDays = request.DueDays
        };
        if (previous.Equals(updated))
        {
            return;
        }

        party.DefaultSalesInvoicePaymentTermType = request.PaymentTermType;
        party.DefaultSalesInvoiceDueDays = request.DueDays;
        party.DefaultSalesAccountId = request.SalesAccountId;
        party.DefaultSalesVatTreatment = request.VatTreatment;

        db.AuditEvents.Add(CreateAuditEvent(
            request.OrganisationId,
            userId,
            "CustomerDefaultsUpdated",
            party.Id,
            new { party.Name, Old = previous, New = updated }));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateSupplierDefaultsAsync(
        string userId,
        UpdateSupplierDefaultsRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireAccessAsync(
            userId,
            request.OrganisationId,
            cancellationToken);
        ValidatePaymentTerm(request.PaymentTermType, request.DueDays);
        await ValidatePurchaseDefaultsAsync(
            request.OrganisationId,
            request.PurchaseAccountId,
            request.VatTreatment,
            cancellationToken);

        var party = await GetPartyAsync(
            request.OrganisationId,
            request.BusinessPartyId,
            PartyType.Supplier,
            cancellationToken);
        var previous = new
        {
            PurchaseAccountId = party.DefaultPurchaseAccountId,
            VatTreatment = party.DefaultPurchaseVatTreatment?.ToString(),
            PaymentTermType = party.DefaultSupplierBillPaymentTermType.ToString(),
            DueDays = party.DefaultSupplierBillDueDays
        };
        var updated = new
        {
            PurchaseAccountId = request.PurchaseAccountId,
            VatTreatment = request.VatTreatment?.ToString(),
            PaymentTermType = request.PaymentTermType.ToString(),
            DueDays = request.DueDays
        };
        if (previous.Equals(updated))
        {
            return;
        }

        party.DefaultPurchaseAccountId = request.PurchaseAccountId;
        party.DefaultPurchaseVatTreatment = request.VatTreatment;
        party.DefaultSupplierBillPaymentTermType = request.PaymentTermType;
        party.DefaultSupplierBillDueDays = request.DueDays;

        db.AuditEvents.Add(CreateAuditEvent(
            request.OrganisationId,
            userId,
            "SupplierDefaultsUpdated",
            party.Id,
            new { party.Name, Old = previous, New = updated }));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task LearnCustomerSalesDefaultsAsync(
        string userId,
        LearnCustomerSalesDefaultsRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireAccessAsync(
            userId,
            request.OrganisationId,
            cancellationToken);
        await ValidateSalesDefaultsAsync(
            request.OrganisationId,
            request.SalesAccountId,
            request.VatTreatment,
            cancellationToken);

        var party = await GetPartyAsync(
            request.OrganisationId,
            request.BusinessPartyId,
            PartyType.Customer,
            cancellationToken);
        if (party.DefaultSalesAccountId is not null &&
            party.DefaultSalesVatTreatment is not null)
        {
            return;
        }

        var previous = new
        {
            SalesAccountId = party.DefaultSalesAccountId,
            VatTreatment = party.DefaultSalesVatTreatment?.ToString(),
            PaymentTermType = party.DefaultSalesInvoicePaymentTermType.ToString(),
            DueDays = party.DefaultSalesInvoiceDueDays
        };
        party.DefaultSalesAccountId ??= request.SalesAccountId;
        party.DefaultSalesVatTreatment ??= request.VatTreatment;
        var updated = new
        {
            SalesAccountId = party.DefaultSalesAccountId,
            VatTreatment = party.DefaultSalesVatTreatment?.ToString(),
            PaymentTermType = party.DefaultSalesInvoicePaymentTermType.ToString(),
            DueDays = party.DefaultSalesInvoiceDueDays
        };

        db.AuditEvents.Add(CreateAuditEvent(
            request.OrganisationId,
            userId,
            "CustomerDefaultsLearnedFromInvoice",
            party.Id,
            new { party.Name, Old = previous, New = updated }));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static AuditEvent CreateAuditEvent(
        Guid organisationId,
        string userId,
        string eventType,
        Guid businessPartyId,
        object evidence) =>
        new()
        {
            OrganisationId = organisationId,
            UserId = userId,
            EventType = eventType,
            EntityType = nameof(BusinessParty),
            EntityId = businessPartyId.ToString(),
            JsonData = JsonSerializer.Serialize(evidence)
        };

    private async Task RequireAccessAsync(
        string userId,
        Guid organisationId,
        CancellationToken cancellationToken)
    {
        if (!await access.CanManageContactsAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException(
                "You do not have permission to manage contacts for this organisation.");
        }
    }

    private async Task<BusinessParty> GetPartyAsync(
        Guid organisationId,
        Guid businessPartyId,
        PartyType requiredType,
        CancellationToken cancellationToken) =>
        await db.BusinessParties.SingleOrDefaultAsync(
            x =>
                x.Id == businessPartyId &&
                x.OrganisationId == organisationId &&
                (x.Type & requiredType) != 0,
            cancellationToken)
        ?? throw new InvalidOperationException(
            "The selected contact was not found for this organisation.");

    private async Task ValidatePurchaseDefaultsAsync(
        Guid organisationId,
        Guid? purchaseAccountId,
        VatTreatment? vatTreatment,
        CancellationToken cancellationToken)
    {
        if (vatTreatment is not null && !Enum.IsDefined(vatTreatment.Value))
        {
            throw new InvalidOperationException(
                "Select a valid default VAT treatment.");
        }

        if (purchaseAccountId is not Guid accountId)
        {
            return;
        }

        var validAccount = await db.LedgerAccounts
            .AsNoTracking()
            .AnyAsync(x =>
                x.Id == accountId &&
                x.OrganisationId == organisationId &&
                x.IsActive &&
                (x.Type == FijiAccounts.Domain.Accounting.AccountType.Expense ||
                 x.Code == "1200" ||
                 x.Code == "1500"),
                cancellationToken);

        if (!validAccount)
        {
            throw new InvalidOperationException(
                "Select an active purchase account from this organisation.");
        }
    }

    private async Task ValidateSalesDefaultsAsync(
        Guid organisationId,
        Guid? salesAccountId,
        VatTreatment? vatTreatment,
        CancellationToken cancellationToken)
    {
        if (vatTreatment is not null && !Enum.IsDefined(vatTreatment.Value))
        {
            throw new InvalidOperationException(
                "Select a valid default VAT treatment.");
        }

        if (salesAccountId is not Guid accountId)
        {
            return;
        }

        var validAccount = await db.LedgerAccounts
            .AsNoTracking()
            .AnyAsync(x =>
                x.Id == accountId &&
                x.OrganisationId == organisationId &&
                x.IsActive &&
                x.Type == FijiAccounts.Domain.Accounting.AccountType.Revenue,
                cancellationToken);

        if (!validAccount)
        {
            throw new InvalidOperationException(
                "Select an active sales account from this organisation.");
        }
    }

    private static void ValidatePartyType(PartyType type)
    {
        var validTypes = PartyType.Customer | PartyType.Supplier;
        if (type == 0 || (type & ~validTypes) != 0)
        {
            throw new InvalidOperationException(
                "Select a valid contact type.");
        }
    }

    private static void ValidatePaymentTerm(
        PaymentTermType type,
        int dueDays)
    {
        if (!Enum.IsDefined(type) || dueDays is < 0 or > 365)
        {
            throw new InvalidOperationException(
                "Enter valid payment terms.");
        }
    }

    private static string RequiredText(
        string value,
        int maximumLength,
        string fieldName)
    {
        var result = value.Trim();
        if (result.Length is < 1 || result.Length > maximumLength)
        {
            throw new InvalidOperationException(
                $"Enter a {fieldName} between 1 and {maximumLength} characters.");
        }

        return result;
    }

    private static string? OptionalText(
        string? value,
        int maximumLength,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var result = value.Trim();
        if (result.Length > maximumLength)
        {
            throw new InvalidOperationException(
                $"Enter a {fieldName} no longer than {maximumLength} characters.");
        }

        return result;
    }
}
