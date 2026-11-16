using System.Text.Json;

namespace FijiAccounts.Web.Services;

internal static class XeroContactGroups
{
    internal const string Key = "contact-groups";
    internal const string Endpoint = "ContactGroups";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    internal static bool HasReadScope(string scopes) =>
        scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(scope => scope is "accounting.contacts.read" or "accounting.contacts");

    internal static async Task<byte[]> ReadSourceAsync(
        Func<string, Task<byte[]>> read, CancellationToken cancellationToken)
    {
        var listing = ReadCollection(await read(Endpoint));
        var groups = new List<Group>(listing.Length);
        var identities = new HashSet<Guid>();
        foreach (var entry in listing)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry is null || entry.ContactGroupId == Guid.Empty || !identities.Add(entry.ContactGroupId))
                throw new InvalidOperationException("Xero returned a contact group without a unique identity.");

            var details = ReadCollection(await read($"{Endpoint}/{entry.ContactGroupId:D}"));
            if (details.Length != 1 || details[0] is not { } group || group.ContactGroupId != entry.ContactGroupId ||
                string.IsNullOrWhiteSpace(group.Name) || !string.Equals(group.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase) ||
                group.Contacts is null)
                throw new InvalidOperationException("Xero did not return the complete contact group. The previous copy was kept.");

            var members = new HashSet<Guid>();
            foreach (var member in group.Contacts)
            {
                if (member is null || member.ContactId == Guid.Empty || !members.Add(member.ContactId) ||
                    string.IsNullOrWhiteSpace(member.Name))
                    throw new InvalidOperationException("Xero returned a contact group member without a unique identity or name.");
            }

            groups.Add(group with { Contacts = group.Contacts.OrderBy(contact => contact.ContactId).ToArray() });
        }

        return JsonSerializer.SerializeToUtf8Bytes(new Collection(groups.ToArray()));
    }

    internal static Group ReadSaved(string json) =>
        JsonSerializer.Deserialize<Group>(json, Options) ??
        throw new InvalidOperationException("The copied Xero group could not be read.");

    private static Group[] ReadCollection(byte[] response) =>
        JsonSerializer.Deserialize<Collection>(response, Options)?.ContactGroups ??
        throw new InvalidOperationException("Xero did not return the contact group collection.");

    private sealed record Collection(Group[] ContactGroups);
    internal sealed record Group(Guid ContactGroupId, string Name, string Status, Member[] Contacts);
    internal sealed record Member(Guid ContactId, string Name);
}
