using System.Text.Json;

namespace FijiAccounts.Web.Services;

internal sealed record XeroBankRuleSnapshot(string ShortCode, IReadOnlyList<XeroBankRuleSnapshot.Rule> Rules)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    internal static XeroBankRuleSnapshot Parse(string json)
    {
        if (json.Length > 1_000_000)
            throw new InvalidOperationException("The Xero rule list exceeds the supported size.");
        var snapshot = JsonSerializer.Deserialize<XeroBankRuleSnapshot>(json, Options);
        if (snapshot?.ShortCode is not { Length: >= 2 and <= 20 } code || code[0] != '!' ||
            code.Skip(1).Any(x => !char.IsAsciiLetterOrDigit(x) && x is not '-' and not '_') ||
            snapshot.Rules is not { Count: > 0 })
            throw new InvalidOperationException("Enter the complete rule list from the connected Xero organisation.");

        var identifiers = new HashSet<Guid>();
        var positions = new HashSet<(string Direction, int Order)>();
        foreach (var rule in snapshot.Rules)
        {
            if (rule is null || rule.Id == Guid.Empty || !identifiers.Add(rule.Id) ||
                string.IsNullOrWhiteSpace(rule.Name) ||
                rule.Direction is not ("Spend" or "Receive" or "Transfer") ||
                rule.Order < 1 || !positions.Add((rule.Direction, rule.Order)) ||
                rule.Match is not ("All" or "Any" or "Single") ||
                rule.Conditions is not { Count: > 0 } || rule.Match == "Single" && rule.Conditions.Count != 1 ||
                rule.Conditions.Any(x => x is null || string.IsNullOrWhiteSpace(x.Field) ||
                    string.IsNullOrWhiteSpace(x.Condition) || x.Value is null) ||
                rule.Allocations is null || rule.Direction != "Transfer" && rule.Allocations.Count == 0 ||
                rule.Allocations.Any(x => x is null || x.Kind is not ("Fixed amount" or "Percentage") ||
                    string.IsNullOrWhiteSpace(x.Account) || string.IsNullOrWhiteSpace(x.Tax) ||
                    string.IsNullOrWhiteSpace(x.Amount)) ||
                string.IsNullOrWhiteSpace(rule.AccountScope) || string.IsNullOrWhiteSpace(rule.Contact) ||
                string.IsNullOrWhiteSpace(rule.Reference))
                throw new InvalidOperationException("Each Xero rule needs a unique ID and priority, its conditions and its allocation settings.");
        }
        return snapshot;
    }

    internal string Serialise() => JsonSerializer.Serialize(this, Options);

    internal Uri SourceLink(Rule rule) => new($"https://go.xero.com/app/{ShortCode}/bank-rules/edit/{rule.Id:D}");

    internal sealed record Rule(
        Guid Id, string Name, string Direction, int Order, string Match,
        IReadOnlyList<MatchCondition> Conditions, IReadOnlyList<Allocation> Allocations,
        string Contact, string Reference, string AccountScope);

    internal sealed record MatchCondition(string Field, string Condition, string Value);

    internal sealed record Allocation(
        string Kind, string Description, string Account, string Tax, string Tracking, string Amount);
}
