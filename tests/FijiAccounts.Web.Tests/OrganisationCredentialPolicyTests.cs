using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class OrganisationCredentialPolicyTests
{
    [Theory]
    [InlineData(OrganisationRole.Owner, OrganisationRole.Administrator, true)]
    [InlineData(OrganisationRole.Administrator, OrganisationRole.Approver, true)]
    [InlineData(OrganisationRole.Administrator, OrganisationRole.Administrator, false)]
    [InlineData(OrganisationRole.Administrator, OrganisationRole.Owner, false)]
    [InlineData(OrganisationRole.Bookkeeper, OrganisationRole.Approver, false)]
    public void EnforcesRoleBoundary(
        OrganisationRole actorRole,
        OrganisationRole targetRole,
        bool expected)
    {
        Assert.Equal(expected, OrganisationCredentialPolicy.CanSetTemporaryPassword(
            "actor", actorRole, "target", targetRole));
    }

    [Fact]
    public void PreventsSelfReset()
    {
        Assert.False(OrganisationCredentialPolicy.CanSetTemporaryPassword(
            "same", OrganisationRole.Owner, "same", OrganisationRole.Administrator));
    }
}
