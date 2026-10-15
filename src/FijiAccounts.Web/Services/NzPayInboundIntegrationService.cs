using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record NzPayIssuedCredential(string Token, string Prefix, DateTimeOffset CreatedAt);
public sealed record NzPayJournalLinePayload(string AccountCode, string Description, decimal Debit, decimal Credit);
public sealed record NzPayAssetReferencePayload(
    string? BenefitId,
    string AssetId,
    string? AssetCode,
    string? AssetName,
    string? Registration);
public sealed record NzPayFbtJournalPayload(
    string ExternalJournalId,
    DateOnly Date,
    string Reference,
    string? Description,
    string Currency,
    decimal Amount,
    IReadOnlyList<NzPayJournalLinePayload> Lines,
    IReadOnlyList<NzPayAssetReferencePayload> AssetReferences);
public sealed record NzPayJournalReceipt(Guid JournalId, string Status, DateTimeOffset ReceivedAt);

public sealed class NzPayIntegrationConflictException(string message) : InvalidOperationException(message);

public sealed class NzPayInboundIntegrationService(
    ApplicationDbContext db,
    TenantAccessService access,
    JournalPostingService posting)
{
    public const string ContractVersion = "2026-10-16";
    public const string FixedAssetsReadScope = "fixed-assets.read";
    public const string FbtJournalsCreateScope = "fbt-journals.create";
    private static readonly Regex ExternalIdPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._:-]{0,119}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<NzPayApiCredential?> GetActiveCredentialAsync(
        string userId,
        Guid organisationId,
        CancellationToken ct = default)
    {
        await EnsureOrganisationAccessAsync(userId, organisationId, ct);
        return await db.NzPayApiCredentials.AsNoTracking()
            .Where(x => x.OrganisationId == organisationId && x.IsActive)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<NzPayIssuedCredential> RotateCredentialAsync(
        string userId,
        Guid organisationId,
        CancellationToken ct = default)
    {
        if (!await access.CanManageTeamAsync(userId, organisationId))
            throw new UnauthorizedAccessException("You cannot manage integrations for this organisation.");
        await EnsureNewZealandOrganisationAsync(organisationId, ct);

        var now = DateTimeOffset.UtcNow;
        var active = await db.NzPayApiCredentials
            .Where(x => x.OrganisationId == organisationId && x.IsActive)
            .ToListAsync(ct);
        foreach (var item in active)
        {
            item.IsActive = false;
            item.RevokedAt = now;
            item.RevokedByUserId = userId;
        }

        var token = $"aizp_{Base64Url(RandomNumberGenerator.GetBytes(32))}";
        var credential = new NzPayApiCredential
        {
            OrganisationId = organisationId,
            TokenSha256 = Sha256(token),
            TokenPrefix = token[..Math.Min(14, token.Length)],
            CreatedAt = now,
            CreatedByUserId = userId
        };
        db.NzPayApiCredentials.Add(credential);
        db.AuditEvents.Add(Audit(organisationId, userId, "NzPayCredentialRotated", nameof(NzPayApiCredential),
            credential.Id, new { credential.TokenPrefix, credential.Scopes, Replaced = active.Count }));
        await db.SaveChangesAsync(ct);
        return new(token, credential.TokenPrefix, credential.CreatedAt);
    }

    public async Task<NzPayApiCredential?> AuthenticateAsync(
        string? authorization,
        Guid organisationId,
        string requiredScope,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(authorization) ||
            !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        var token = authorization[7..].Trim();
        if (token.Length is < 32 or > 200 || !token.StartsWith("aizp_", StringComparison.Ordinal)) return null;
        var hash = Sha256(token);
        var credential = await db.NzPayApiCredentials.SingleOrDefaultAsync(x =>
            x.OrganisationId == organisationId && x.TokenSha256 == hash && x.IsActive, ct);
        if (credential is null || !credential.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains(requiredScope, StringComparer.Ordinal)) return null;
        if (credential.LastUsedAt is null || credential.LastUsedAt < DateTimeOffset.UtcNow.AddMinutes(-15))
        {
            credential.LastUsedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return credential;
    }

    public async Task<IReadOnlyList<object>> SearchAssetsAsync(
        Guid organisationId,
        string? query,
        CancellationToken ct = default)
    {
        var organisation = await EnsureNewZealandOrganisationAsync(organisationId, ct);
        var cleaned = query?.Trim();
        if (cleaned?.Length > 120) throw new InvalidOperationException("Search using no more than 120 characters.");
        var assets = await db.FixedAssets.AsNoTracking()
            .Include(x => x.DepreciationEntries)
            .Include(x => x.Disposal)
            .Where(x => x.OrganisationId == organisationId && x.IsActive &&
                (string.IsNullOrEmpty(cleaned) || x.AssetNumber.Contains(cleaned) || x.Name.Contains(cleaned) ||
                 (x.VehicleRegistration != null && x.VehicleRegistration.Contains(cleaned))))
            .OrderBy(x => x.AssetNumber)
            .Take(500)
            .ToListAsync(ct);
        return assets.Select(x => AssetDto(x, organisation.BaseCurrency)).ToList();
    }

    public async Task<object?> GetAssetAsync(
        Guid organisationId,
        Guid assetId,
        CancellationToken ct = default)
    {
        var organisation = await EnsureNewZealandOrganisationAsync(organisationId, ct);
        var asset = await db.FixedAssets.AsNoTracking()
            .Include(x => x.DepreciationEntries)
            .Include(x => x.Disposal)
            .SingleOrDefaultAsync(x => x.OrganisationId == organisationId && x.Id == assetId, ct);
        return asset is null ? null : AssetDto(asset, organisation.BaseCurrency);
    }

    public async Task<NzPayJournalReceipt> CreateDraftAsync(
        NzPayApiCredential credential,
        string idempotencyKey,
        NzPayFbtJournalPayload request,
        CancellationToken ct = default)
    {
        idempotencyKey = idempotencyKey.Trim();
        ValidateDraftRequest(idempotencyKey, request);
        var organisation = await EnsureNewZealandOrganisationAsync(credential.OrganisationId, ct);
        if (!string.Equals(request.Currency, organisation.BaseCurrency, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The journal currency must be {organisation.BaseCurrency}.");
        var externalJournalId = request.ExternalJournalId.Trim();
        var payloadHash = Sha256(JsonSerializer.Serialize(request));
        var existing = await db.NzPayFbtJournalDrafts.AsNoTracking().FirstOrDefaultAsync(x =>
            x.OrganisationId == credential.OrganisationId &&
            (x.IdempotencyKey == idempotencyKey || x.ExternalJournalId == externalJournalId), ct);
        if (existing is not null)
        {
            if (!string.Equals(existing.PayloadSha256, payloadHash, StringComparison.Ordinal))
                throw new NzPayIntegrationConflictException(
                    "This idempotency key or external journal ID was already used for different journal content.");
            return new(existing.Id, StatusText(existing.Status), existing.ReceivedAt);
        }

        var codes = request.Lines.Select(x => x.AccountCode.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var accounts = (await db.LedgerAccounts.AsNoTracking()
            .Where(x => x.OrganisationId == credential.OrganisationId && x.IsActive)
            .ToListAsync(ct))
            .Where(x => codes.Contains(x.Code, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        if (accounts.Count != codes.Length)
            throw new InvalidOperationException("Every journal account code must be active in this Account Island organisation.");

        var assetIds = request.AssetReferences.Select(x =>
            Guid.TryParse(x.AssetId, out var id) ? id : Guid.Empty).Distinct().ToArray();
        if (assetIds.Contains(Guid.Empty)) throw new InvalidOperationException("An FBT asset reference is invalid.");
        if (assetIds.Length > 0)
        {
            var activeAssets = await db.FixedAssets.AsNoTracking().CountAsync(x =>
                x.OrganisationId == credential.OrganisationId && x.IsActive && assetIds.Contains(x.Id), ct);
            if (activeAssets != assetIds.Length)
                throw new InvalidOperationException("Every FBT asset must be active in this Account Island organisation.");
        }

        var draft = new NzPayFbtJournalDraft
        {
            OrganisationId = credential.OrganisationId,
            CredentialId = credential.Id,
            ExternalJournalId = externalJournalId,
            IdempotencyKey = idempotencyKey,
            PayloadSha256 = payloadHash,
            EntryDate = request.Date,
            Reference = request.Reference.Trim(),
            Description = request.Description?.Trim(),
            Currency = request.Currency.Trim().ToUpperInvariant(),
            Amount = request.Amount,
            AssetReferencesJson = JsonSerializer.Serialize(request.AssetReferences),
            Lines = request.Lines.Select(x => new NzPayFbtJournalDraftLine
            {
                LedgerAccountId = accounts[x.AccountCode.Trim()].Id,
                Description = x.Description.Trim(),
                Debit = x.Debit,
                Credit = x.Credit
            }).ToList()
        };
        db.NzPayFbtJournalDrafts.Add(draft);
        db.AuditEvents.Add(Audit(credential.OrganisationId, $"integration:nz-pay:{credential.Id:N}",
            "NzPayFbtJournalReceived", nameof(NzPayFbtJournalDraft), draft.Id,
            new { draft.ExternalJournalId, draft.Reference, draft.EntryDate, draft.Amount, draft.Currency }));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.Entry(draft).State = EntityState.Detached;
            var duplicate = await db.NzPayFbtJournalDrafts.AsNoTracking().FirstOrDefaultAsync(x =>
                x.OrganisationId == credential.OrganisationId &&
                (x.IdempotencyKey == idempotencyKey || x.ExternalJournalId == externalJournalId), ct);
            if (duplicate is not null && duplicate.PayloadSha256 == payloadHash)
                return new(duplicate.Id, StatusText(duplicate.Status), duplicate.ReceivedAt);
            throw;
        }
        return new(draft.Id, "draft", draft.ReceivedAt);
    }

    public async Task<IReadOnlyList<NzPayFbtJournalDraft>> ListDraftsAsync(
        string userId,
        Guid organisationId,
        CancellationToken ct = default)
    {
        await EnsureOrganisationAccessAsync(userId, organisationId, ct);
        return await db.NzPayFbtJournalDrafts.AsNoTracking()
            .Include(x => x.Lines).ThenInclude(x => x.LedgerAccount)
            .Include(x => x.PostedJournal)
            .Where(x => x.OrganisationId == organisationId)
            .OrderByDescending(x => x.ReceivedAt)
            .Take(100)
            .ToListAsync(ct);
    }

    public async Task<PostedJournal> ApproveDraftAsync(
        string userId,
        Guid organisationId,
        Guid draftId,
        CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId))
            throw new UnauthorizedAccessException("You cannot approve journals for this organisation.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var draft = await db.NzPayFbtJournalDrafts.Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.OrganisationId == organisationId && x.Id == draftId, ct)
            ?? throw new InvalidOperationException("NZ Pay journal draft not found.");
        if (draft.Status == NzPayFbtJournalStatus.Posted && draft.PostedJournalId is Guid postedId)
            return await db.PostedJournals.SingleAsync(x => x.Id == postedId, ct);
        if (draft.Status != NzPayFbtJournalStatus.AwaitingApproval)
            throw new InvalidOperationException("This NZ Pay journal is no longer awaiting approval.");

        var journal = await posting.PostApprovedWorkflowAsync(userId, new JournalPostRequest(
            organisationId,
            draft.EntryDate,
            draft.Reference,
            draft.Description,
            draft.Lines.Select(x => new JournalLineInput(
                x.LedgerAccountId, x.Description, x.Debit, x.Credit)).ToList(),
            Purpose: JournalPurpose.Standard,
            Currency: draft.Currency), ct);
        draft.Status = NzPayFbtJournalStatus.Posted;
        draft.PostedJournalId = journal.Id;
        draft.DecidedAt = DateTimeOffset.UtcNow;
        draft.DecidedByUserId = userId;
        db.AuditEvents.Add(Audit(organisationId, userId, "NzPayFbtJournalApproved",
            nameof(NzPayFbtJournalDraft), draft.Id, new { journal.Id, journal.SequenceNumber }));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return journal;
    }

    public async Task RejectDraftAsync(
        string userId,
        Guid organisationId,
        Guid draftId,
        CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId))
            throw new UnauthorizedAccessException("You cannot reject journals for this organisation.");
        var draft = await db.NzPayFbtJournalDrafts.SingleOrDefaultAsync(x =>
            x.OrganisationId == organisationId && x.Id == draftId, ct)
            ?? throw new InvalidOperationException("NZ Pay journal draft not found.");
        if (draft.Status == NzPayFbtJournalStatus.Rejected) return;
        if (draft.Status != NzPayFbtJournalStatus.AwaitingApproval)
            throw new InvalidOperationException("This NZ Pay journal is no longer awaiting approval.");

        draft.Status = NzPayFbtJournalStatus.Rejected;
        draft.DecidedAt = DateTimeOffset.UtcNow;
        draft.DecidedByUserId = userId;
        draft.DecisionReason = "Rejected in Account Island.";
        db.AuditEvents.Add(Audit(organisationId, userId, "NzPayFbtJournalRejected",
            nameof(NzPayFbtJournalDraft), draft.Id, new { draft.ExternalJournalId, draft.Reference }));
        await db.SaveChangesAsync(ct);
    }

    private async Task<Organisation> EnsureNewZealandOrganisationAsync(Guid organisationId, CancellationToken ct)
    {
        var organisation = await db.Organisations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == organisationId, ct)
            ?? throw new InvalidOperationException("Account Island organisation not found.");
        if (!string.Equals(organisation.CountryCode, "NZ", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This connection is available only for a New Zealand Account Island organisation.");
        return organisation;
    }

    private async Task EnsureOrganisationAccessAsync(string userId, Guid organisationId, CancellationToken ct)
    {
        if (await access.FindAsync(userId, organisationId) is null)
            throw new UnauthorizedAccessException("You cannot access this organisation.");
        await EnsureNewZealandOrganisationAsync(organisationId, ct);
    }

    private static void ValidateDraftRequest(string idempotencyKey, NzPayFbtJournalPayload request)
    {
        if (idempotencyKey.Length is < 16 or > 160) throw new InvalidOperationException("A valid idempotency key is required.");
        if (!ExternalIdPattern.IsMatch(request.ExternalJournalId?.Trim() ?? "")) throw new InvalidOperationException("The external journal ID is invalid.");
        if (string.IsNullOrWhiteSpace(request.Reference) || request.Reference.Trim().Length > 80) throw new InvalidOperationException("The journal reference is invalid.");
        if (request.Description?.Trim().Length > 500) throw new InvalidOperationException("The journal description is too long.");
        if (request.Currency?.Trim().Length != 3) throw new InvalidOperationException("The journal currency is invalid.");
        if (request.Amount <= 0 || request.Amount != Money(request.Amount)) throw new InvalidOperationException("The journal amount must be a positive cents value.");
        if (request.Lines is null || request.Lines.Count != 2) throw new InvalidOperationException("An FBT journal must contain exactly two balanced lines.");
        foreach (var line in request.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.AccountCode) || line.AccountCode.Trim().Length > 40 ||
                string.IsNullOrWhiteSpace(line.Description) || line.Description.Trim().Length > 300)
                throw new InvalidOperationException("Each journal line needs a valid account code and description.");
            if (line.Debit < 0 || line.Credit < 0 || line.Debit != Money(line.Debit) || line.Credit != Money(line.Credit) ||
                (line.Debit > 0) == (line.Credit > 0))
                throw new InvalidOperationException("Each journal line must contain either a cents debit or a cents credit.");
        }
        var debit = request.Lines.Sum(x => x.Debit);
        var credit = request.Lines.Sum(x => x.Credit);
        if (debit != credit || debit != request.Amount) throw new InvalidOperationException("The FBT journal must balance to its stated amount.");
        if (request.AssetReferences is null || request.AssetReferences.Count > 500)
            throw new InvalidOperationException("The FBT journal contains too many asset references.");
    }

    private static object AssetDto(FixedAsset asset, string currency) => new
    {
        id = asset.Id.ToString(),
        code = asset.AssetNumber,
        name = asset.Name,
        registration = asset.VehicleRegistration,
        acquisitionDate = asset.AcquisitionDate,
        cost = Money(asset.Cost),
        bookValue = Money(asset.Disposal?.BookValue ?? Math.Max(asset.ResidualValue,
            asset.Cost - asset.DepreciationEntries.Sum(x => x.Amount))),
        currency,
        status = asset.IsActive ? "active" : "disposed"
    };

    private static string StatusText(NzPayFbtJournalStatus status) => status switch
    {
        NzPayFbtJournalStatus.AwaitingApproval => "draft",
        NzPayFbtJournalStatus.Posted => "posted",
        _ => "rejected"
    };
    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static AuditEvent Audit(Guid organisationId, string userId, string eventType, string entityType, Guid entityId, object data) => new()
    {
        OrganisationId = organisationId,
        UserId = userId,
        EventType = eventType,
        EntityType = entityType,
        EntityId = entityId.ToString(),
        JsonData = JsonSerializer.Serialize(data)
    };
}
