using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record PayrollIslandConnectionRequest(
    string BaseUrl,
    string PayrollOrganisationId,
    string? AccessToken,
    Guid WagesExpenseAccountId,
    Guid EmployerContributionsExpenseAccountId,
    Guid NetWagesPayableAccountId,
    Guid PayePayableAccountId,
    Guid FnpfPayableAccountId,
    Guid OtherDeductionsPayableAccountId);

public sealed record PayrollIslandSyncResult(int Imported, int Skipped, string? NextCursor);

public sealed class PayrollIslandIntegrationService(
    ApplicationDbContext db,
    TenantAccessService access,
    JournalPostingService posting,
    IPayrollIslandClient client,
    IDataProtectionProvider dataProtection)
{
    private const string ProtectorPurpose = "AccountIsland.PayrollIsland.AccessToken.v1";
    private static readonly Regex ExternalIdPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._:-]{0,119}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly IDataProtector tokenProtector =
        dataProtection.CreateProtector(ProtectorPurpose);

    public async Task<PayrollIslandConnection?> GetConnectionAsync(
        string userId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureAccessAsync(userId, organisationId);
        return await db.PayrollIslandConnections
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganisationId == organisationId, cancellationToken);
    }

    public async Task<IReadOnlyList<PayrollIslandPayRunImport>> ListImportsAsync(
        string userId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureAccessAsync(userId, organisationId);
        return await db.PayrollIslandPayRunImports
            .AsNoTracking()
            .Include(x => x.Payments)
            .Include(x => x.PostedJournal)
            .Where(x => x.OrganisationId == organisationId)
            .OrderByDescending(x => x.PaymentDate)
            .ThenByDescending(x => x.Revision)
            .Take(100)
            .ToListAsync(cancellationToken);
    }

    public async Task<PayrollIslandConnection> SaveConnectionAsync(
        string userId,
        Guid organisationId,
        PayrollIslandConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await access.CanManageTeamAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot manage integrations for this organisation.");
        }

        var baseUrl = ValidateBaseUrl(request.BaseUrl);
        var payrollOrganisationId = request.PayrollOrganisationId.Trim();
        if (!ExternalIdPattern.IsMatch(payrollOrganisationId))
        {
            throw new InvalidOperationException(
                "Enter a valid Payroll Island organisation ID.");
        }

        var accountIds = new[]
        {
            request.WagesExpenseAccountId,
            request.EmployerContributionsExpenseAccountId,
            request.NetWagesPayableAccountId,
            request.PayePayableAccountId,
            request.FnpfPayableAccountId,
            request.OtherDeductionsPayableAccountId
        };
        var accounts = await db.LedgerAccounts
            .Where(x => x.OrganisationId == organisationId &&
                        x.IsActive && accountIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        if (accounts.Count != accountIds.Distinct().Count())
        {
            throw new InvalidOperationException(
                "Every payroll mapping must use an active account in this organisation.");
        }
        EnsureAccountType(accounts, request.WagesExpenseAccountId, AccountType.Expense, "Wages expense");
        EnsureAccountType(accounts, request.EmployerContributionsExpenseAccountId, AccountType.Expense, "Employer contributions");
        EnsureAccountType(accounts, request.NetWagesPayableAccountId, AccountType.Liability, "Net wages payable");
        EnsureAccountType(accounts, request.PayePayableAccountId, AccountType.Liability, "PAYE payable");
        EnsureAccountType(accounts, request.FnpfPayableAccountId, AccountType.Liability, "FNPF payable");
        EnsureAccountType(accounts, request.OtherDeductionsPayableAccountId, AccountType.Liability, "Other deductions payable");

        var connection = await db.PayrollIslandConnections
            .SingleOrDefaultAsync(x => x.OrganisationId == organisationId, cancellationToken);
        var trimmedToken = request.AccessToken?.Trim();
        if (connection is null && string.IsNullOrWhiteSpace(trimmedToken))
        {
            throw new InvalidOperationException("Enter a Payroll Island access token.");
        }
        if (!string.IsNullOrWhiteSpace(trimmedToken) && trimmedToken.Length < 32)
        {
            throw new InvalidOperationException(
                "The Payroll Island access token must contain at least 32 characters.");
        }
        if (trimmedToken?.Length > 4000)
        {
            throw new InvalidOperationException("The Payroll Island access token is too long.");
        }

        if (connection is null)
        {
            connection = new PayrollIslandConnection
            {
                OrganisationId = organisationId,
                BaseUrl = baseUrl,
                PayrollOrganisationId = payrollOrganisationId,
                ProtectedAccessToken = tokenProtector.Protect(trimmedToken!),
                WagesExpenseAccountId = request.WagesExpenseAccountId,
                EmployerContributionsExpenseAccountId = request.EmployerContributionsExpenseAccountId,
                NetWagesPayableAccountId = request.NetWagesPayableAccountId,
                PayePayableAccountId = request.PayePayableAccountId,
                FnpfPayableAccountId = request.FnpfPayableAccountId,
                OtherDeductionsPayableAccountId = request.OtherDeductionsPayableAccountId,
                CreatedByUserId = userId,
                UpdatedByUserId = userId
            };
            db.PayrollIslandConnections.Add(connection);
        }
        else
        {
            var sourceChanged =
                !string.Equals(connection.BaseUrl, baseUrl, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(connection.PayrollOrganisationId, payrollOrganisationId, StringComparison.Ordinal);
            connection.BaseUrl = baseUrl;
            connection.PayrollOrganisationId = payrollOrganisationId;
            connection.WagesExpenseAccountId = request.WagesExpenseAccountId;
            connection.EmployerContributionsExpenseAccountId = request.EmployerContributionsExpenseAccountId;
            connection.NetWagesPayableAccountId = request.NetWagesPayableAccountId;
            connection.PayePayableAccountId = request.PayePayableAccountId;
            connection.FnpfPayableAccountId = request.FnpfPayableAccountId;
            connection.OtherDeductionsPayableAccountId = request.OtherDeductionsPayableAccountId;
            connection.IsActive = true;
            connection.UpdatedAt = DateTimeOffset.UtcNow;
            connection.UpdatedByUserId = userId;
            if (sourceChanged)
            {
                connection.LastSyncCursor = null;
                connection.LastSyncedAt = null;
                connection.LastSyncError = null;
            }
            if (!string.IsNullOrWhiteSpace(trimmedToken))
            {
                connection.ProtectedAccessToken = tokenProtector.Protect(trimmedToken);
            }
        }

        db.AuditEvents.Add(Audit(
            organisationId,
            userId,
            "PayrollIslandConnectionSaved",
            nameof(PayrollIslandConnection),
            connection.Id,
            new { connection.BaseUrl, connection.PayrollOrganisationId, TokenReplaced = !string.IsNullOrWhiteSpace(trimmedToken) }));
        await db.SaveChangesAsync(cancellationToken);
        return connection;
    }

    public async Task<PayrollIslandSyncResult> SyncAsync(
        string userId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot import payroll for this organisation.");
        }

        var connection = await db.PayrollIslandConnections
            .SingleOrDefaultAsync(
                x => x.OrganisationId == organisationId && x.IsActive,
                cancellationToken)
            ?? throw new InvalidOperationException("Connect Payroll Island first.");
        try
        {
            var page = await client.GetFinalisedPayRunsAsync(
                connection.BaseUrl,
                connection.PayrollOrganisationId,
                tokenProtector.Unprotect(connection.ProtectedAccessToken),
                connection.LastSyncCursor,
                cancellationToken);
            if (page.NextCursor?.Length > 500)
            {
                throw new InvalidOperationException(
                    "Payroll Island returned an invalid sync cursor.");
            }
            var result = await ImportAsync(userId, organisationId, connection, page, cancellationToken);
            if (!string.IsNullOrWhiteSpace(page.NextCursor))
            {
                connection.LastSyncCursor = page.NextCursor.Trim();
            }
            connection.LastSyncedAt = DateTimeOffset.UtcNow;
            connection.LastSyncError = null;
            await db.SaveChangesAsync(cancellationToken);
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or CryptographicException)
        {
            connection.LastSyncError = ex.Message[..Math.Min(1000, ex.Message.Length)];
            connection.LastSyncedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException(
                "Payroll Island could not be synchronised. Check the connection and try again.",
                ex);
        }
    }

    internal async Task<PayrollIslandSyncResult> ImportAsync(
        string userId,
        Guid organisationId,
        PayrollIslandConnection connection,
        PayrollIslandPayRunPage page,
        CancellationToken cancellationToken = default)
    {
        if (page.PayRuns.Count > 500)
        {
            throw new InvalidOperationException("A payroll import cannot contain more than 500 pay runs.");
        }

        var organisation = await db.Organisations
            .AsNoTracking()
            .SingleAsync(x => x.Id == organisationId, cancellationToken);
        foreach (var payload in page.PayRuns)
        {
            ValidatePayload(payload, organisation.BaseCurrency);
        }
        if (page.PayRuns
            .GroupBy(x => new { ExternalPayRunId = x.ExternalPayRunId.Trim(), x.Revision })
            .Any(x => x.Count() > 1))
        {
            throw new InvalidOperationException(
                "Payroll Island returned the same pay-run revision more than once.");
        }

        var externalIds = page.PayRuns
            .Select(x => x.ExternalPayRunId.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var knownImports = await db.PayrollIslandPayRunImports
            .Where(x => x.ConnectionId == connection.Id &&
                        externalIds.Contains(x.ExternalPayRunId))
            .ToListAsync(cancellationToken);
        foreach (var payload in page.PayRuns)
        {
            var existing = knownImports.SingleOrDefault(x =>
                x.ExternalPayRunId == payload.ExternalPayRunId.Trim() &&
                x.Revision == payload.Revision);
            if (existing is not null && !CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(existing.PayloadSha256),
                    Convert.FromHexString(PayloadHash(payload))))
            {
                throw new InvalidOperationException(
                    $"Payroll run {payload.PayRunNumber} revision {payload.Revision} changed after it was imported.");
            }
        }

        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var imported = 0;
        var skipped = 0;
        foreach (var payload in page.PayRuns)
        {
            var hash = PayloadHash(payload);
            var existing = knownImports.SingleOrDefault(x =>
                x.ExternalPayRunId == payload.ExternalPayRunId.Trim() &&
                x.Revision == payload.Revision);
            if (existing is not null)
            {
                skipped++;
                continue;
            }

            var prior = knownImports
                .Where(x => x.ExternalPayRunId == payload.ExternalPayRunId.Trim())
                .OrderByDescending(x => x.Revision)
                .ToList();
            var latest = prior.FirstOrDefault();
            var postedPrior = prior.FirstOrDefault(x =>
                x.Status == PayrollIslandImportStatus.Posted &&
                x.PostedJournalId is not null);
            var paymentOnlyRevision = postedPrior is not null &&
                                      HasSameAccounting(postedPrior, payload);
            var importStatus = latest is not null && payload.Revision < latest.Revision
                ? PayrollIslandImportStatus.Superseded
                : paymentOnlyRevision
                    ? PayrollIslandImportStatus.Posted
                    : postedPrior is not null
                    ? PayrollIslandImportStatus.CorrectionRequired
                    : PayrollIslandImportStatus.ReadyToPost;
            if (importStatus == PayrollIslandImportStatus.ReadyToPost)
            {
                foreach (var draft in prior.Where(x => x.Status == PayrollIslandImportStatus.ReadyToPost))
                {
                    draft.Status = PayrollIslandImportStatus.Superseded;
                }
            }
            else if (importStatus == PayrollIslandImportStatus.Posted && paymentOnlyRevision)
            {
                postedPrior!.Status = PayrollIslandImportStatus.Superseded;
            }

            var payRun = new PayrollIslandPayRunImport
            {
                OrganisationId = organisationId,
                ConnectionId = connection.Id,
                ExternalPayRunId = payload.ExternalPayRunId.Trim(),
                Revision = payload.Revision,
                PayRunNumber = payload.PayRunNumber.Trim(),
                PeriodStart = payload.PeriodStart,
                PeriodEnd = payload.PeriodEnd,
                PaymentDate = payload.PaymentDate,
                Currency = payload.Currency.Trim().ToUpperInvariant(),
                EmployeeCount = payload.EmployeeCount,
                GrossEarnings = payload.GrossEarnings,
                EmployeePaye = payload.EmployeePaye,
                EmployeeFnpf = payload.EmployeeFnpf,
                EmployerFnpf = payload.EmployerFnpf,
                OtherDeductions = payload.OtherDeductions,
                NetPay = payload.NetPay,
                PayloadSha256 = hash,
                Status = importStatus,
                PostedJournalId = importStatus == PayrollIslandImportStatus.Posted
                    ? postedPrior!.PostedJournalId
                    : null,
                ImportedByUserId = userId,
                Payments = payload.Payments.Select(payment => new PayrollIslandPaymentRecord
                {
                    ExternalPaymentId = payment.ExternalPaymentId.Trim(),
                    Kind = ParsePaymentKind(payment.Kind),
                    Status = ParsePaymentStatus(payment.Status),
                    DueDate = payment.DueDate,
                    PaidDate = payment.PaidDate,
                    Amount = payment.Amount,
                    Reference = NullIfWhiteSpace(payment.Reference)
                }).ToList()
            };
            db.PayrollIslandPayRunImports.Add(payRun);
            knownImports.Add(payRun);
            db.AuditEvents.Add(Audit(
                organisationId,
                userId,
                "PayrollIslandPayRunImported",
                nameof(PayrollIslandPayRunImport),
                payRun.Id,
                new
                {
                    payRun.ExternalPayRunId,
                    payRun.Revision,
                    payRun.PayRunNumber,
                    payRun.Status,
                    payRun.PayloadSha256
                }));
            imported++;
        }

        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return new PayrollIslandSyncResult(imported, skipped, page.NextCursor);
    }

    public async Task<PostedJournal> PostPayRunAsync(
        string userId,
        Guid organisationId,
        Guid importId,
        CancellationToken cancellationToken = default)
    {
        var payRun = await db.PayrollIslandPayRunImports
            .SingleOrDefaultAsync(
                x => x.Id == importId && x.OrganisationId == organisationId,
                cancellationToken)
            ?? throw new InvalidOperationException("Payroll import not found.");
        if (payRun.Status != PayrollIslandImportStatus.ReadyToPost)
        {
            throw new InvalidOperationException(
                "Only a current, unposted payroll import can be posted.");
        }

        var connection = await db.PayrollIslandConnections
            .SingleAsync(x => x.Id == payRun.ConnectionId, cancellationToken);
        var lines = new List<JournalLineInput>
        {
            new(connection.WagesExpenseAccountId, $"Gross wages · {payRun.PayRunNumber}", payRun.GrossEarnings, 0),
            new(connection.EmployerContributionsExpenseAccountId, $"Employer FNPF · {payRun.PayRunNumber}", payRun.EmployerFnpf, 0),
            new(connection.NetWagesPayableAccountId, $"Net wages payable · {payRun.PayRunNumber}", 0, payRun.NetPay),
            new(connection.PayePayableAccountId, $"PAYE payable · {payRun.PayRunNumber}", 0, payRun.EmployeePaye),
            new(connection.FnpfPayableAccountId, $"FNPF payable · {payRun.PayRunNumber}", 0, payRun.EmployeeFnpf + payRun.EmployerFnpf),
            new(connection.OtherDeductionsPayableAccountId, $"Other payroll deductions · {payRun.PayRunNumber}", 0, payRun.OtherDeductions)
        };
        lines = lines.Where(x => x.Debit != 0 || x.Credit != 0).ToList();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var journal = await posting.PostAsync(
            userId,
            new JournalPostRequest(
                organisationId,
                payRun.PaymentDate,
                $"PAYROLL-{payRun.PayRunNumber}",
                $"Payroll Island pay run {payRun.PayRunNumber}, revision {payRun.Revision}",
                lines,
                Purpose: JournalPurpose.Payroll,
                Currency: payRun.Currency),
            cancellationToken);
        payRun.Status = PayrollIslandImportStatus.Posted;
        payRun.PostedJournalId = journal.Id;
        db.AuditEvents.Add(Audit(
            organisationId,
            userId,
            "PayrollIslandPayRunPosted",
            nameof(PayrollIslandPayRunImport),
            payRun.Id,
            new { payRun.ExternalPayRunId, payRun.Revision, JournalId = journal.Id }));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return journal;
    }

    private async Task EnsureAccessAsync(
        string userId,
        Guid organisationId)
    {
        if (await access.FindAsync(userId, organisationId) is null)
        {
            throw new UnauthorizedAccessException(
                "You cannot access payroll imports for this organisation.");
        }
    }

    private static string ValidateBaseUrl(string value)
    {
        var trimmed = value.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException(
                "Payroll Island must use an HTTPS base URL without credentials, a query, or a fragment.");
        }
        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static void EnsureAccountType(
        IReadOnlyDictionary<Guid, LedgerAccount> accounts,
        Guid accountId,
        AccountType expected,
        string label)
    {
        if (accounts[accountId].Type != expected)
        {
            throw new InvalidOperationException($"{label} must use an {expected.ToString().ToLowerInvariant()} account.");
        }
    }

    private static void ValidatePayload(PayrollIslandPayRunPayload payload, string baseCurrency)
    {
        if (payload.Payments is null)
        {
            throw new InvalidOperationException("Payroll Island returned a pay run without payment records.");
        }
        if (!ExternalIdPattern.IsMatch(payload.ExternalPayRunId?.Trim() ?? "") ||
            payload.Revision < 1 ||
            string.IsNullOrWhiteSpace(payload.PayRunNumber) ||
            payload.PayRunNumber.Trim().Length > 72)
        {
            throw new InvalidOperationException("Payroll Island returned an invalid pay-run identity.");
        }
        if (payload.PeriodEnd < payload.PeriodStart ||
            payload.PaymentDate < payload.PeriodStart ||
            payload.EmployeeCount < 1)
        {
            throw new InvalidOperationException(
                $"Payroll run {payload.PayRunNumber} has invalid dates or employee count.");
        }
        if (!string.Equals(payload.Currency?.Trim(), baseCurrency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Payroll run {payload.PayRunNumber} must use the organisation base currency {baseCurrency}.");
        }
        var amounts = new[]
        {
            payload.GrossEarnings,
            payload.EmployeePaye,
            payload.EmployeeFnpf,
            payload.EmployerFnpf,
            payload.OtherDeductions,
            payload.NetPay
        };
        if (amounts.Any(x => x < 0 || decimal.Round(x, 2) != x))
        {
            throw new InvalidOperationException(
                $"Payroll run {payload.PayRunNumber} contains an invalid amount.");
        }
        if (payload.GrossEarnings == 0)
        {
            throw new InvalidOperationException(
                $"Payroll run {payload.PayRunNumber} must contain gross earnings.");
        }
        var debits = payload.GrossEarnings + payload.EmployerFnpf;
        var credits = payload.NetPay + payload.EmployeePaye + payload.EmployeeFnpf +
                      payload.EmployerFnpf + payload.OtherDeductions;
        if (debits != credits)
        {
            throw new InvalidOperationException(
                $"Payroll run {payload.PayRunNumber} does not balance.");
        }
        if (payload.Payments.Count > 1000 ||
            payload.Payments.Select(x => x.ExternalPaymentId?.Trim()).Distinct(StringComparer.Ordinal).Count() != payload.Payments.Count)
        {
            throw new InvalidOperationException(
                $"Payroll run {payload.PayRunNumber} contains invalid payment records.");
        }
        foreach (var payment in payload.Payments)
        {
            if (!ExternalIdPattern.IsMatch(payment.ExternalPaymentId?.Trim() ?? "") ||
                payment.Amount <= 0 || decimal.Round(payment.Amount, 2) != payment.Amount ||
                payment.Reference?.Trim().Length > 160 ||
                (ParsePaymentStatus(payment.Status) == PayrollPaymentStatus.Paid && payment.PaidDate is null))
            {
                throw new InvalidOperationException(
                    $"Payroll run {payload.PayRunNumber} contains an invalid payment record.");
            }
            _ = ParsePaymentKind(payment.Kind);
        }
        var activePayments = payload.Payments
            .Where(x => ParsePaymentStatus(x.Status) != PayrollPaymentStatus.Cancelled)
            .ToList();
        EnsurePaymentTotal(activePayments, PayrollPaymentKind.NetWages, payload.NetPay, payload.PayRunNumber);
        EnsurePaymentTotal(activePayments, PayrollPaymentKind.Paye, payload.EmployeePaye, payload.PayRunNumber);
        EnsurePaymentTotal(activePayments, PayrollPaymentKind.Fnpf, payload.EmployeeFnpf + payload.EmployerFnpf, payload.PayRunNumber);
        EnsurePaymentTotal(activePayments, PayrollPaymentKind.OtherDeduction, payload.OtherDeductions, payload.PayRunNumber);
    }

    private static PayrollPaymentKind ParsePaymentKind(string value) =>
        Enum.TryParse<PayrollPaymentKind>(value, true, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Unsupported payroll payment kind '{value}'.");

    private static PayrollPaymentStatus ParsePaymentStatus(string value) =>
        Enum.TryParse<PayrollPaymentStatus>(value, true, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Unsupported payroll payment status '{value}'.");

    private static void EnsurePaymentTotal(
        IReadOnlyList<PayrollIslandPaymentPayload> payments,
        PayrollPaymentKind kind,
        decimal expected,
        string payRunNumber)
    {
        var actual = payments
            .Where(x => ParsePaymentKind(x.Kind) == kind)
            .Sum(x => x.Amount);
        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"Payroll run {payRunNumber} {kind} payment records total {actual:N2}, but {expected:N2} is required.");
        }
    }

    private static string PayloadHash(PayrollIslandPayRunPayload payload)
    {
        var canonical = new
        {
            ExternalPayRunId = payload.ExternalPayRunId.Trim(),
            payload.Revision,
            PayRunNumber = payload.PayRunNumber.Trim(),
            payload.PeriodStart,
            payload.PeriodEnd,
            payload.PaymentDate,
            Currency = payload.Currency.Trim().ToUpperInvariant(),
            payload.EmployeeCount,
            payload.GrossEarnings,
            payload.EmployeePaye,
            payload.EmployeeFnpf,
            payload.EmployerFnpf,
            payload.OtherDeductions,
            payload.NetPay,
            Payments = payload.Payments
                .OrderBy(x => x.ExternalPaymentId, StringComparer.Ordinal)
                .Select(x => new
                {
                    ExternalPaymentId = x.ExternalPaymentId.Trim(),
                    Kind = ParsePaymentKind(x.Kind).ToString(),
                    Status = ParsePaymentStatus(x.Status).ToString(),
                    x.DueDate,
                    x.PaidDate,
                    x.Amount,
                    Reference = NullIfWhiteSpace(x.Reference)
                })
        };
        return Convert.ToHexString(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(canonical)));
    }

    private static bool HasSameAccounting(
        PayrollIslandPayRunImport imported,
        PayrollIslandPayRunPayload payload) =>
        imported.PayRunNumber == payload.PayRunNumber.Trim() &&
        imported.PeriodStart == payload.PeriodStart &&
        imported.PeriodEnd == payload.PeriodEnd &&
        imported.PaymentDate == payload.PaymentDate &&
        imported.Currency == payload.Currency.Trim().ToUpperInvariant() &&
        imported.GrossEarnings == payload.GrossEarnings &&
        imported.EmployeePaye == payload.EmployeePaye &&
        imported.EmployeeFnpf == payload.EmployeeFnpf &&
        imported.EmployerFnpf == payload.EmployerFnpf &&
        imported.OtherDeductions == payload.OtherDeductions &&
        imported.NetPay == payload.NetPay;

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AuditEvent Audit(
        Guid organisationId,
        string userId,
        string eventType,
        string entityType,
        Guid entityId,
        object data) => new()
        {
            OrganisationId = organisationId,
            UserId = userId,
            EventType = eventType,
            EntityType = entityType,
            EntityId = entityId.ToString(),
            JsonData = JsonSerializer.Serialize(data)
        };
}
