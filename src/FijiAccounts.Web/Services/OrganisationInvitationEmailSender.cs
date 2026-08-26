using System.Net;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public interface IOrganisationInvitationEmailSender
{
    bool IsConfigured { get; }

    Task SendAsync(
        string email,
        string organisationName,
        OrganisationRole role,
        string invitationLink,
        CancellationToken cancellationToken = default);
}

public sealed class OrganisationInvitationEmailSender(IEmailDeliveryService delivery)
    : IOrganisationInvitationEmailSender
{
    public bool IsConfigured => delivery.IsConfigured;

    public Task SendAsync(
        string email,
        string organisationName,
        OrganisationRole role,
        string invitationLink,
        CancellationToken cancellationToken = default)
    {
        var encodedOrganisation = WebUtility.HtmlEncode(organisationName);
        var encodedRole = WebUtility.HtmlEncode(role.ToString());
        var encodedInvitationLink = WebUtility.HtmlEncode(invitationLink);

        return delivery.SendAsync(
            new TransactionalEmail(
                email,
                $"Invitation to {organisationName} on Account Island",
                $"You have been invited to {organisationName} as {role}. Create your password or sign in to accept the invitation: {invitationLink}",
                $"<p>You have been invited to <strong>{encodedOrganisation}</strong> as {encodedRole}.</p>" +
                $"<p><a href=\"{encodedInvitationLink}\">Create your password and accept the invitation</a></p>"),
            cancellationToken);
    }
}
