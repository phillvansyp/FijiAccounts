using System.ComponentModel.DataAnnotations;
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
        party.DefaultSalesInvoicePaymentTermType = request.PaymentTermType;
        party.DefaultSalesInvoiceDueDays = request.DueDays;
        party.DefaultSalesAccountId = request.SalesAccountId;
        party.DefaultSalesVatTreatment = request.VatTreatment;

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
        party.DefaultPurchaseAccountId = request.PurchaseAccountId;
        party.DefaultPurchaseVatTreatment = request.VatTreatment;
        party.DefaultSupplierBillPaymentTermType = request.PaymentTermType;
        party.DefaultSupplierBillDueDays = request.DueDays;

        await db.SaveChangesAsync(cancellationToken);
    }

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
