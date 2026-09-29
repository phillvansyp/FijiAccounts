using Microsoft.AspNetCore.Identity;

namespace FijiAccounts.Web.Data;

// Add profile data for application users by adding properties to the ApplicationUser class
public class ApplicationUser : IdentityUser
{
    public Guid? LastSelectedOrganisationId { get; set; }
}

