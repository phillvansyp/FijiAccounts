using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed record DefaultEnterpriseStructure(
    OrganisationGroup Group,
    Branch Branch,
    Division Division);

public sealed class EnterpriseStructureService(
    ApplicationDbContext db)
{
    public DefaultEnterpriseStructure AddDefaultFor(
        Organisation company)
    {
        if (company.OrganisationGroupId is not null ||
            company.OrganisationGroup is not null)
        {
            throw new InvalidOperationException(
                "The company already belongs to an organisation group.");
        }

        var group =
            new OrganisationGroup
            {
                Name = $"{company.LegalName.Trim()} Group"
            };

        var branch =
            new Branch
            {
                Organisation = company,
                Code = "MAIN",
                Name = "Main Branch",
                IsDefault = true
            };

        var division =
            new Division
            {
                Branch = branch,
                Code = "GENERAL",
                Name = "General",
                IsDefault = true
            };

        company.OrganisationGroup = group;

        db.OrganisationGroups.Add(group);
        db.Branches.Add(branch);
        db.Divisions.Add(division);

        return new(
            group,
            branch,
            division);
    }
}
