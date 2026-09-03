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
    int DefaultSupplierBillDueDays,
    string? SupplierAccountNumber = null,
    string? VatRegistrationNumber = null,
    string? DefaultSalesCurrency = null,
    string? DefaultPurchaseCurrency = null,
    string? AccountsEmail = null,
    string? Address = null);

public sealed record UpdateBusinessPartyEmailsRequest(
    Guid OrganisationId,
    Guid BusinessPartyId,
    string? Email,
    string? AccountsEmail);

public sealed record UpdateBusinessPartyContactDetailsRequest(
    Guid OrganisationId,
    Guid BusinessPartyId,
    string? Email,
    string? AccountsEmail,
    string? Address);

public sealed record UpdateCustomerDefaultsRequest(
    Guid OrganisationId,
    Guid BusinessPartyId,
    Guid? SalesAccountId,
    VatTreatment? VatTreatment,
    PaymentTermType PaymentTermType,
    int DueDays,
    string? Currency = null);

public sealed record UpdateSupplierDefaultsRequest(
    Guid OrganisationId,
    Guid BusinessPartyId,
    Guid? PurchaseAccountId,
    VatTreatment? VatTreatment,
    PaymentTermType PaymentTermType,
    int DueDays,
    string? VatRegistrationNumber,
    string? Currency = null);

public sealed record SupplierAccountProfileRequest(
    Guid OrganisationId,
    Guid SupplierId,
    string Label,
    string AccountNumber,
    bool MakeDefault);

public sealed record SupplierBankAccountRequest(
    Guid OrganisationId,
    Guid SupplierId,
    string AccountName,
    string? BankName,
    string AccountNumber);

public sealed record LearnCustomerSalesDefaultsRequest(
    Guid OrganisationId,
    Guid BusinessPartyId,
    Guid SalesAccountId,
    VatTreatment VatTreatment);

public sealed record LearnSupplierPurchaseDefaultsRequest(
    Guid OrganisationId,
    Guid BusinessPartyId,
    Guid PurchaseAccountId,
    VatTreatment VatTreatment);

public sealed class BusinessPartyService(
    ApplicationDbContext db,
    TenantAccessService access,
    TransactionCurrencyService? currencyService = null)
{
    private readonly TransactionCurrencyService currencies =
        currencyService ?? new TransactionCurrencyService(db, access);
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
            await ValidateCurrencyAsync(
                userId,
                request.OrganisationId,
                request.DefaultSalesCurrency,
                cancellationToken);
        }

        if ((request.Type & PartyType.Supplier) != 0)
        {
            await ValidatePurchaseDefaultsAsync(
                request.OrganisationId,
                request.DefaultPurchaseAccountId,
                request.DefaultPurchaseVatTreatment,
                cancellationToken);
            await ValidateCurrencyAsync(
                userId,
                request.OrganisationId,
                request.DefaultPurchaseCurrency,
                cancellationToken);
        }

        var email = ValidateEmail(request.Email, "email address");
        var accountsEmail = ValidateEmail(request.AccountsEmail, "accounts email address");

        var party =
            new BusinessParty
            {
                OrganisationId = request.OrganisationId,
                Name = RequiredText(request.Name, 160, "contact name"),
                Email = email,
                AccountsEmail = accountsEmail,
                Address = OptionalText(request.Address, 500, "contact address"),
                Tin = OptionalText(
                    request.Tin,
                    32,
                    "tax identification number"),
                VatRegistrationNumber = (request.Type & PartyType.Supplier) != 0
                    ? OptionalText(request.VatRegistrationNumber, 80, "VAT registration number")
                    : null,
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
                DefaultSalesCurrency = (request.Type & PartyType.Customer) != 0
                    ? NormalizeOptionalCurrency(request.DefaultSalesCurrency)
                    : null,
                DefaultPurchaseCurrency = (request.Type & PartyType.Supplier) != 0
                    ? NormalizeOptionalCurrency(request.DefaultPurchaseCurrency)
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

        var initialSupplierAccountNumber =
            (request.Type & PartyType.Supplier) != 0
                ? OptionalText(request.SupplierAccountNumber, 80, "supplier account number")
                : null;
        if (initialSupplierAccountNumber is not null)
        {
            party.SupplierAccounts.Add(new SupplierAccountProfile
            {
                OrganisationId = request.OrganisationId,
                SupplierId = party.Id,
                Label = "Primary",
                AccountNumber = initialSupplierAccountNumber,
                IsDefault = true
            });
        }

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
                party.AccountsEmail,
                party.Address,
                party.Tin,
                InitialSupplierAccountNumber = initialSupplierAccountNumber,
                party.VatRegistrationNumber,
                Type = party.Type.ToString(),
                CustomerDefaults = new
                {
                    party.DefaultSalesAccountId,
                    VatTreatment = party.DefaultSalesVatTreatment?.ToString(),
                    PaymentTermType = party.DefaultSalesInvoicePaymentTermType.ToString(),
                    DueDays = party.DefaultSalesInvoiceDueDays,
                    party.DefaultSalesCurrency
                },
                SupplierDefaults = new
                {
                    party.DefaultPurchaseAccountId,
                    VatTreatment = party.DefaultPurchaseVatTreatment?.ToString(),
                    PaymentTermType = party.DefaultSupplierBillPaymentTermType.ToString(),
                    DueDays = party.DefaultSupplierBillDueDays,
                    party.DefaultPurchaseCurrency
                }
            }));
        await db.SaveChangesAsync(cancellationToken);

        return party;
    }

    public async Task UpdateEmailsAsync(
        string userId,
        UpdateBusinessPartyEmailsRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireAccessAsync(userId, request.OrganisationId, cancellationToken);
        var party = await db.BusinessParties.SingleOrDefaultAsync(
            x => x.OrganisationId == request.OrganisationId &&
                 x.Id == request.BusinessPartyId &&
                 x.IsActive,
            cancellationToken)
            ?? throw new InvalidOperationException("Contact not found.");

        var email = ValidateEmail(request.Email, "email address");
        var accountsEmail = ValidateEmail(request.AccountsEmail, "accounts email address");
        if (party.Email == email && party.AccountsEmail == accountsEmail)
        {
            return;
        }

        var previous = new { party.Email, party.AccountsEmail };
        party.Email = email;
        party.AccountsEmail = accountsEmail;
        db.AuditEvents.Add(CreateAuditEvent(
            request.OrganisationId,
            userId,
            "BusinessPartyEmailsUpdated",
            party.Id,
            new { party.Name, Old = previous, New = new { party.Email, party.AccountsEmail } }));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateContactDetailsAsync(
        string userId,
        UpdateBusinessPartyContactDetailsRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireAccessAsync(userId, request.OrganisationId, cancellationToken);
        var party = await db.BusinessParties.SingleOrDefaultAsync(
            x => x.OrganisationId == request.OrganisationId &&
                 x.Id == request.BusinessPartyId &&
                 x.IsActive,
            cancellationToken)
            ?? throw new InvalidOperationException("Contact not found.");

        var email = ValidateEmail(request.Email, "email address");
        var accountsEmail = ValidateEmail(request.AccountsEmail, "accounts email address");
        var address = OptionalText(request.Address, 500, "contact address");
        if (party.Email == email &&
            party.AccountsEmail == accountsEmail &&
            party.Address == address)
        {
            return;
        }

        var previous = new { party.Email, party.AccountsEmail, party.Address };
        party.Email = email;
        party.AccountsEmail = accountsEmail;
        party.Address = address;
        db.AuditEvents.Add(CreateAuditEvent(
            request.OrganisationId,
            userId,
            "BusinessPartyContactDetailsUpdated",
            party.Id,
            new { party.Name, Old = previous, New = new { party.Email, party.AccountsEmail, party.Address } }));
        await db.SaveChangesAsync(cancellationToken);
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
        await ValidateCurrencyAsync(
            userId,
            request.OrganisationId,
            request.Currency,
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
            DueDays = party.DefaultSalesInvoiceDueDays,
            Currency = party.DefaultSalesCurrency
        };
        var updated = new
        {
            SalesAccountId = request.SalesAccountId,
            VatTreatment = request.VatTreatment?.ToString(),
            PaymentTermType = request.PaymentTermType.ToString(),
            DueDays = request.DueDays,
            Currency = NormalizeOptionalCurrency(request.Currency)
        };
        if (previous.Equals(updated))
        {
            return;
        }

        party.DefaultSalesInvoicePaymentTermType = request.PaymentTermType;
        party.DefaultSalesInvoiceDueDays = request.DueDays;
        party.DefaultSalesAccountId = request.SalesAccountId;
        party.DefaultSalesVatTreatment = request.VatTreatment;
        party.DefaultSalesCurrency = NormalizeOptionalCurrency(request.Currency);

        db.AuditEvents.Add(CreateAuditEvent(
            request.OrganisationId,
            userId,
            "CustomerDefaultsUpdated",
            party.Id,
            new { party.Name, Old = previous, New = updated }));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string? ValidateEmail(string? value, string label)
    {
        var email = OptionalText(value, 320, label);
        if (email is not null && !new EmailAddressAttribute().IsValid(email))
        {
            throw new InvalidOperationException($"Enter a valid {label}.");
        }

        return email;
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
        await ValidateCurrencyAsync(
            userId,
            request.OrganisationId,
            request.Currency,
            cancellationToken);

        var party = await GetPartyAsync(
            request.OrganisationId,
            request.BusinessPartyId,
            PartyType.Supplier,
            cancellationToken);
        var vatRegistrationNumber = OptionalText(
            request.VatRegistrationNumber,
            80,
            "VAT registration number");
        var previous = new
        {
            party.VatRegistrationNumber,
            PurchaseAccountId = party.DefaultPurchaseAccountId,
            VatTreatment = party.DefaultPurchaseVatTreatment?.ToString(),
            PaymentTermType = party.DefaultSupplierBillPaymentTermType.ToString(),
            DueDays = party.DefaultSupplierBillDueDays,
            Currency = party.DefaultPurchaseCurrency
        };
        var updated = new
        {
            VatRegistrationNumber = vatRegistrationNumber,
            PurchaseAccountId = request.PurchaseAccountId,
            VatTreatment = request.VatTreatment?.ToString(),
            PaymentTermType = request.PaymentTermType.ToString(),
            DueDays = request.DueDays,
            Currency = NormalizeOptionalCurrency(request.Currency)
        };
        if (previous.Equals(updated))
        {
            return;
        }

        party.VatRegistrationNumber = vatRegistrationNumber;
        party.DefaultPurchaseAccountId = request.PurchaseAccountId;
        party.DefaultPurchaseVatTreatment = request.VatTreatment;
        party.DefaultSupplierBillPaymentTermType = request.PaymentTermType;
        party.DefaultSupplierBillDueDays = request.DueDays;
        party.DefaultPurchaseCurrency = NormalizeOptionalCurrency(request.Currency);

        db.AuditEvents.Add(CreateAuditEvent(
            request.OrganisationId,
            userId,
            "SupplierDefaultsUpdated",
            party.Id,
            new { party.Name, Old = previous, New = updated }));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<SupplierAccountProfile> AddSupplierAccountAsync(
        string userId,
        SupplierAccountProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireAccessAsync(userId, request.OrganisationId, cancellationToken);
        var supplier = await GetPartyAsync(
            request.OrganisationId,
            request.SupplierId,
            PartyType.Supplier,
            cancellationToken);
        var label = RequiredText(request.Label, 80, "supplier account label");
        var accountNumber = RequiredText(request.AccountNumber, 80, "supplier account number");
        if (await db.SupplierAccountProfiles.AnyAsync(x =>
                x.OrganisationId == request.OrganisationId &&
                x.SupplierId == request.SupplierId &&
                x.AccountNumber == accountNumber,
                cancellationToken))
        {
            throw new InvalidOperationException("This supplier account number already exists.");
        }

        var existing = await db.SupplierAccountProfiles
            .Where(x => x.OrganisationId == request.OrganisationId &&
                        x.SupplierId == request.SupplierId &&
                        x.IsActive)
            .ToListAsync(cancellationToken);
        var makeDefault = request.MakeDefault || existing.Count == 0;
        if (makeDefault)
        {
            foreach (var item in existing)
            {
                item.IsDefault = false;
            }
        }

        var account = new SupplierAccountProfile
        {
            OrganisationId = request.OrganisationId,
            SupplierId = request.SupplierId,
            Label = label,
            AccountNumber = accountNumber,
            IsDefault = makeDefault
        };
        db.SupplierAccountProfiles.Add(account);
        db.AuditEvents.Add(CreateAuditEvent(
            request.OrganisationId,
            userId,
            "SupplierAccountAdded",
            supplier.Id,
            new { supplier.Name, account.Id, account.Label, account.AccountNumber, account.IsDefault }));
        await db.SaveChangesAsync(cancellationToken);
        return account;
    }

    public async Task SetDefaultSupplierAccountAsync(
        string userId,
        Guid organisationId,
        Guid supplierId,
        Guid supplierAccountId,
        CancellationToken cancellationToken = default)
    {
        await RequireAccessAsync(userId, organisationId, cancellationToken);
        var supplier = await GetPartyAsync(
            organisationId,
            supplierId,
            PartyType.Supplier,
            cancellationToken);
        var accounts = await db.SupplierAccountProfiles
            .Where(x => x.OrganisationId == organisationId &&
                        x.SupplierId == supplierId &&
                        x.IsActive)
            .ToListAsync(cancellationToken);
        var selected = accounts.SingleOrDefault(x => x.Id == supplierAccountId)
            ?? throw new InvalidOperationException("Supplier account was not found.");
        if (selected.IsDefault)
        {
            return;
        }

        foreach (var account in accounts)
        {
            account.IsDefault = account.Id == selected.Id;
        }
        db.AuditEvents.Add(CreateAuditEvent(
            organisationId,
            userId,
            "SupplierAccountDefaultChanged",
            supplier.Id,
            new { supplier.Name, selected.Id, selected.Label, selected.AccountNumber }));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteSupplierAccountAsync(
        string userId,
        Guid organisationId,
        Guid supplierId,
        Guid supplierAccountId,
        CancellationToken cancellationToken = default)
    {
        await RequireAccessAsync(userId, organisationId, cancellationToken);
        var supplier = await GetPartyAsync(
            organisationId,
            supplierId,
            PartyType.Supplier,
            cancellationToken);
        var account = await db.SupplierAccountProfiles.SingleOrDefaultAsync(x =>
                x.Id == supplierAccountId &&
                x.OrganisationId == organisationId &&
                x.SupplierId == supplierId,
                cancellationToken)
            ?? throw new InvalidOperationException("Supplier account was not found.");
        var wasDefault = account.IsDefault;
        db.SupplierAccountProfiles.Remove(account);
        if (wasDefault)
        {
            var replacement = await db.SupplierAccountProfiles
                .Where(x => x.OrganisationId == organisationId &&
                            x.SupplierId == supplierId &&
                            x.Id != supplierAccountId &&
                            x.IsActive)
                .OrderBy(x => x.Label)
                .FirstOrDefaultAsync(cancellationToken);
            if (replacement is not null)
            {
                replacement.IsDefault = true;
            }
        }
        db.AuditEvents.Add(CreateAuditEvent(
            organisationId,
            userId,
            "SupplierAccountDeleted",
            supplier.Id,
            new { supplier.Name, account.Id, account.Label, account.AccountNumber }));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<SupplierBankAccount> SubmitSupplierBankAccountAsync(
        string userId,
        SupplierBankAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireAccessAsync(userId, request.OrganisationId, cancellationToken);
        var supplier = await GetPartyAsync(
            request.OrganisationId,
            request.SupplierId,
            PartyType.Supplier,
            cancellationToken);
        var accountName = RequiredText(request.AccountName, 120, "bank account name");
        var bankName = OptionalText(request.BankName, 120, "bank name");
        var accountNumber = RequiredText(request.AccountNumber, 80, "bank account number");
        var normalisedAccountNumber = NormaliseBankAccountNumber(accountNumber);
        if (normalisedAccountNumber.Length < 4)
        {
            throw new InvalidOperationException("Enter a valid bank account number.");
        }

        var existingNumbers = await db.SupplierBankAccounts
            .Where(x => x.OrganisationId == request.OrganisationId &&
                        x.SupplierId == request.SupplierId)
            .Select(x => x.AccountNumber)
            .ToListAsync(cancellationToken);
        if (existingNumbers.Any(x =>
                NormaliseBankAccountNumber(x) == normalisedAccountNumber))
        {
            throw new InvalidOperationException(
                "This supplier bank account already exists.");
        }

        var account = new SupplierBankAccount
        {
            OrganisationId = request.OrganisationId,
            SupplierId = request.SupplierId,
            AccountName = accountName,
            BankName = bankName,
            AccountNumber = accountNumber,
            SubmittedByUserId = userId
        };
        db.SupplierBankAccounts.Add(account);
        db.AuditEvents.Add(CreateAuditEvent(
            request.OrganisationId,
            userId,
            "SupplierBankAccountSubmitted",
            supplier.Id,
            new
            {
                supplier.Name,
                account.Id,
                account.AccountName,
                account.BankName,
                AccountNumber = MaskBankAccountNumber(account.AccountNumber),
                Status = "Pending verification"
            }));
        await db.SaveChangesAsync(cancellationToken);
        return account;
    }

    public async Task VerifySupplierBankAccountAsync(
        string userId,
        Guid organisationId,
        Guid supplierId,
        Guid supplierBankAccountId,
        CancellationToken cancellationToken = default)
    {
        if (!await access.CanManageTeamAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException(
                "Only an organisation owner or administrator can verify supplier bank details.");
        }

        var supplier = await GetPartyAsync(
            organisationId,
            supplierId,
            PartyType.Supplier,
            cancellationToken);
        var accounts = await db.SupplierBankAccounts
            .Where(x => x.OrganisationId == organisationId &&
                        x.SupplierId == supplierId &&
                        x.IsActive)
            .ToListAsync(cancellationToken);
        var account = accounts.SingleOrDefault(x => x.Id == supplierBankAccountId)
            ?? throw new InvalidOperationException("Supplier bank account was not found.");
        if (account.IsVerified)
        {
            return;
        }
        if (account.SubmittedByUserId == userId)
        {
            throw new InvalidOperationException(
                "A different owner or administrator must verify this bank account change.");
        }

        var replacedDefaultAccountId = accounts
            .SingleOrDefault(x => x.Id != account.Id && x.IsDefault)?.Id;
        foreach (var item in accounts)
        {
            item.IsDefault = false;
        }
        account.VerifiedByUserId = userId;
        account.VerifiedAt = DateTimeOffset.UtcNow;
        account.IsDefault = true;
        db.AuditEvents.Add(CreateAuditEvent(
            organisationId,
            userId,
            "SupplierBankAccountVerified",
            supplier.Id,
            new
            {
                supplier.Name,
                account.Id,
                account.AccountName,
                account.BankName,
                AccountNumber = MaskBankAccountNumber(account.AccountNumber),
                ReplacedDefaultAccountId = replacedDefaultAccountId
            }));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeactivateSupplierBankAccountAsync(
        string userId,
        Guid organisationId,
        Guid supplierId,
        Guid supplierBankAccountId,
        CancellationToken cancellationToken = default)
    {
        if (!await access.CanManageTeamAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException(
                "Only an organisation owner or administrator can remove supplier bank details.");
        }

        var supplier = await GetPartyAsync(
            organisationId,
            supplierId,
            PartyType.Supplier,
            cancellationToken);
        var accounts = await db.SupplierBankAccounts
            .Where(x => x.OrganisationId == organisationId &&
                        x.SupplierId == supplierId &&
                        x.IsActive)
            .ToListAsync(cancellationToken);
        var account = accounts.SingleOrDefault(x => x.Id == supplierBankAccountId)
            ?? throw new InvalidOperationException("Supplier bank account was not found.");
        var wasDefault = account.IsDefault;
        account.IsActive = false;
        account.IsDefault = false;
        var replacement = wasDefault
            ? accounts
                .Where(x => x.Id != account.Id && x.IsVerified)
                .OrderByDescending(x => x.VerifiedAt)
                .FirstOrDefault()
            : null;
        if (replacement is not null)
        {
            replacement.IsDefault = true;
        }
        db.AuditEvents.Add(CreateAuditEvent(
            organisationId,
            userId,
            "SupplierBankAccountDeactivated",
            supplier.Id,
            new
            {
                supplier.Name,
                account.Id,
                account.AccountName,
                account.BankName,
                AccountNumber = MaskBankAccountNumber(account.AccountNumber),
                WasVerified = account.IsVerified,
                ReplacementDefaultAccountId = replacement?.Id
            }));
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

    public async Task LearnSupplierPurchaseDefaultsAsync(
        string userId,
        LearnSupplierPurchaseDefaultsRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireAccessAsync(
            userId,
            request.OrganisationId,
            cancellationToken);
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
        if (party.DefaultPurchaseAccountId is not null &&
            party.DefaultPurchaseVatTreatment is not null)
        {
            return;
        }

        var previous = new
        {
            PurchaseAccountId = party.DefaultPurchaseAccountId,
            VatTreatment = party.DefaultPurchaseVatTreatment?.ToString(),
            PaymentTermType = party.DefaultSupplierBillPaymentTermType.ToString(),
            DueDays = party.DefaultSupplierBillDueDays
        };
        party.DefaultPurchaseAccountId ??= request.PurchaseAccountId;
        party.DefaultPurchaseVatTreatment ??= request.VatTreatment;
        var updated = new
        {
            PurchaseAccountId = party.DefaultPurchaseAccountId,
            VatTreatment = party.DefaultPurchaseVatTreatment?.ToString(),
            PaymentTermType = party.DefaultSupplierBillPaymentTermType.ToString(),
            DueDays = party.DefaultSupplierBillDueDays
        };

        db.AuditEvents.Add(CreateAuditEvent(
            request.OrganisationId,
            userId,
            "SupplierDefaultsLearnedFromBill",
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

    public static string MaskBankAccountNumber(string value)
    {
        var normalised = NormaliseBankAccountNumber(value);
        return normalised.Length <= 4
            ? $"•••• {normalised}"
            : $"•••• {normalised[^4..]}";
    }

    private static string NormaliseBankAccountNumber(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();

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

    private async Task ValidateCurrencyAsync(
        string userId,
        Guid organisationId,
        string? currency,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            return;
        }

        await currencies.RequireEnabledAsync(
            userId,
            organisationId,
            currency,
            cancellationToken);
    }

    private static string? NormalizeOptionalCurrency(string? currency) =>
        string.IsNullOrWhiteSpace(currency)
            ? null
            : TransactionCurrencyService.NormalizeCode(currency);

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
