namespace FijiAccounts.Web.Data;

public static class OrganisationIndustries
{
    public static IReadOnlyList<(string Code, string Label)> All { get; } = Array.AsReadOnly(new[]
    {
        ("Technology", "Technology & IT services"),
        ("Construction", "Construction & trades"),
        ("Retail", "Retail & shops"),
        ("Wholesale", "Wholesale & distribution"),
        ("Hospitality", "Hospitality, restaurants & cafes"),
        ("Tourism", "Tourism & accommodation"),
        ("ProfessionalServices", "Professional & consulting services"),
        ("Accounting", "Accounting & bookkeeping services"),
        ("Recruitment", "Recruitment & staffing"),
        ("Property", "Property & real estate"),
        ("Manufacturing", "Manufacturing"),
        ("Agriculture", "Agriculture & farming"),
        ("Fishing", "Fishing & aquaculture"),
        ("Transport", "Transport & logistics"),
        ("Automotive", "Automotive & vehicle services"),
        ("Health", "Health & medical services"),
        ("Education", "Education & training"),
        ("FinancialServices", "Financial & insurance services"),
        ("PersonalServices", "Personal care & beauty"),
        ("Cleaning", "Cleaning & maintenance"),
        ("Media", "Media, marketing & creative services"),
        ("Community", "Community, charity & non-profit services"),
        ("Government", "Government & public services"),
        ("Other", "Other business activity")
    });

    public static string? Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (!All.Any(x => x.Code == value))
            throw new InvalidOperationException("Select a valid industry or business activity.");
        return value;
    }
    public static string? Label(string? value) => All.FirstOrDefault(x => x.Code == value).Label;
}
