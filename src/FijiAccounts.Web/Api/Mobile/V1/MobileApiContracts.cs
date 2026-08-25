namespace FijiAccounts.Web.Api.Mobile.V1;

public sealed record MobileSessionResponse(
    string UserId,
    string? Email,
    string ApiVersion,
    string MinimumIosVersion,
    string MinimumAndroidVersion,
    bool DeviceRegistered,
    bool DeviceRevoked);

public sealed record MobileDeviceState(
    bool Registered,
    bool Revoked);

public sealed record MobileDeviceRegistrationRequest(
    string? DisplayName);

public sealed record MobileDeviceSessionSummary(
    Guid Id,
    Guid InstallationId,
    string Platform,
    string AppVersion,
    string? DisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? RevokedAt,
    bool IsCurrent);

public sealed record MobileOrganisationSummary(
    Guid Id,
    string LegalName,
    string? TradingName,
    string CountryCode,
    string BaseCurrency,
    string Kind,
    string Access,
    bool IsAccountantClient);

public sealed record MobileOrganisationCapabilities(
    Guid OrganisationId,
    string Access,
    bool IsAccountantClient,
    bool CanRead,
    bool CanPostJournals,
    bool CanManageContacts,
    bool CanManageTeam,
    IReadOnlyList<MobileBranchAccess> Branches);

public sealed record MobileBranchAccess(
    Guid Id,
    string Code,
    string Name,
    bool IsDefault,
    IReadOnlyList<MobileDivisionAccess> Divisions);

public sealed record MobileDivisionAccess(
    Guid Id,
    string Code,
    string Name,
    bool IsDefault);

public sealed record MobileDashboardResponse(
    Guid OrganisationId,
    DateOnly AsAt,
    string Currency,
    decimal CashPosition,
    decimal Receivables,
    decimal Payables,
    int OverdueSalesInvoices,
    int OverdueSupplierBills,
    int UnreadNotifications);

public sealed record MobileNotificationSummary(
    Guid Id,
    string Title,
    string Message,
    string Type,
    string Severity,
    string Status,
    string? RelatedEntityType,
    string? RelatedEntityId,
    decimal? Amount,
    string? Currency,
    DateTimeOffset CreatedAt);

public sealed record MobilePage<T>(
    IReadOnlyList<T> Items,
    string? NextCursor);
