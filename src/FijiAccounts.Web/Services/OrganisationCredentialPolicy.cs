using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public static class OrganisationCredentialPolicy
{
    public static bool CanSetTemporaryPassword(
        string actorUserId,
        OrganisationRole actorRole,
        string targetUserId,
        OrganisationRole targetRole) =>
        actorUserId != targetUserId &&
        targetRole != OrganisationRole.Owner &&
        (actorRole == OrganisationRole.Owner ||
         actorRole == OrganisationRole.Administrator &&
         targetRole != OrganisationRole.Administrator);
}
