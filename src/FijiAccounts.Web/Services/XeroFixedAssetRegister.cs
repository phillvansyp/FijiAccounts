using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.VisualBasic.FileIO;

namespace FijiAccounts.Web.Services;

internal static class XeroFixedAssetRegister
{
    internal const string Endpoint = "https://api.xero.com/assets.xro/1.0/Assets";
    private const string AssetTypesEndpoint = "https://api.xero.com/assets.xro/1.0/AssetTypes";
    private static readonly string[] Statuses = ["DRAFT", "REGISTERED", "DISPOSED"];
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    internal static bool HasReadScope(string scopes) =>
        scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(scope => scope is "assets.read" or "assets");

    internal static async Task<byte[]> ReadSourceAsync(
        Func<string, Task<byte[]>> read, CancellationToken cancellationToken)
    {
        var assets = new List<JsonElement>();
        var identities = new HashSet<Guid>();
        foreach (var status in Statuses)
        {
            var count = 0;
            int? expected = null;
            int? expectedPages = null;
            for (var page = 1; page <= Math.Max(1, expectedPages ?? 1); page++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var endpoint = $"{Endpoint}?status={status}&page={page}&pageSize=100&orderBy=AssetNumber&sortDirection=asc";
                var response = JsonSerializer.Deserialize<AssetPage>(await read(endpoint), Options);
                if (response?.Items is null ||
                    response.Pagination is not { PageCount: int pageCount, ItemCount: int itemCount } pagination ||
                    pageCount is < 0 or > 2_000 || itemCount < 0 || pagination.Page != page ||
                    expected is not null && expected != itemCount ||
                    expectedPages is not null && expectedPages != pageCount)
                    throw new InvalidOperationException("The Xero asset register changed during the refresh. Retry to get a complete copy.");

                expected = itemCount;
                expectedPages = pageCount;
                foreach (var item in response.Items)
                {
                    if (item.ValueKind != JsonValueKind.Object ||
                        !item.TryGetProperty("assetId", out var assetId) ||
                        !assetId.TryGetGuid(out var id) || id == Guid.Empty ||
                        !identities.Add(id) ||
                        !item.TryGetProperty("assetStatus", out var assetStatus) ||
                        !string.Equals(assetStatus.GetString(), status, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Xero returned an asset with a duplicate identity or unexpected status.");
                    assets.Add(item);
                    count++;
                }
            }

            if (count != expected)
                throw new InvalidOperationException("Xero returned an incomplete fixed-asset register.");
        }

        var assetTypes = assets.Any(asset => asset.TryGetProperty("assetTypeId", out var value) &&
            value.ValueKind == JsonValueKind.String && value.TryGetGuid(out var id) && id != Guid.Empty)
            ? ReadAssetTypes(await read(AssetTypesEndpoint))
            : [];
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            Registers = new[] { new { Id = "register", Name = "Xero fixed-asset register", Assets = assets, AssetTypes = assetTypes } }
        }, Options);
    }

    internal static IReadOnlyList<Asset> ReadSnapshot(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var assets = root.GetProperty("assets").Deserialize<List<Asset>>(Options) ?? [];
        if (!root.TryGetProperty("assetTypes", out var types))
            return assets;
        var names = (types.Deserialize<AssetType[]>(Options) ?? [])
            .ToDictionary(type => type.AssetTypeId, type => type.AssetTypeName);
        return assets.Select(asset => string.IsNullOrWhiteSpace(asset.AssetTypeName) &&
            asset.AssetTypeId is { } id && names.TryGetValue(id, out var name)
                ? asset with { AssetTypeName = name }
                : asset).ToArray();
    }

    private static AssetType[] ReadAssetTypes(byte[] response)
    {
        var types = JsonSerializer.Deserialize<AssetType[]>(response, Options);
        if (types is null || types.Any(type => type is null || type.AssetTypeId == Guid.Empty ||
                string.IsNullOrWhiteSpace(type.AssetTypeName)) ||
            types.Select(type => type.AssetTypeId).Distinct().Count() != types.Length)
            throw new InvalidOperationException("Xero did not return a valid list of fixed-asset types.");
        return types;
    }

    internal static IReadOnlyList<Asset> ReadExport(string csv)
    {
        using var reader = new TextFieldParser(new StringReader(csv));
        reader.SetDelimiters(",");
        reader.HasFieldsEnclosedInQuotes = true;
        var header = reader.ReadFields() ?? throw new InvalidOperationException("Choose the CSV exported from Xero's Fixed assets page.");
        var fields = header.Select((name, index) => (Name: name.TrimStart('\uFEFF', '*'), Index: index))
            .ToLookup(x => x.Name, x => x.Index, StringComparer.OrdinalIgnoreCase);
        string[] required = ["AssetName", "AssetNumber", "AssetStatus", "PurchaseDate", "PurchasePrice",
            "AssetType", "Book_BookValue", "AccumulatedDepreciation", "DisposalDate"];
        if (required.Any(name => fields[name].Count() != 1))
            throw new InvalidOperationException("The file must contain the columns from Xero's Fixed assets export.");

        var assets = new List<Asset>();
        var numbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (!reader.EndOfData)
        {
            var row = reader.ReadFields();
            if (row is null || row.Length != header.Length)
                throw new InvalidOperationException("A row in the Xero asset export has missing columns.");
            string Field(string key) => row[fields[key].Single()].Trim();
            var number = Field("AssetNumber");
            var name = Field("AssetName");
            var status = Field("AssetStatus");
            if (number.Length == 0 || name.Length == 0 || !numbers.Add(number) ||
                !Statuses.Contains(status, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Each asset must have one unique number, a name and a valid Xero status.");

            assets.Add(new(new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(number))[..16]),
                number, name, status, ExportDate(Field("PurchaseDate")), ExportAmount(Field("PurchasePrice")),
                ExportAmount(Field("Book_BookValue")), ExportDate(Field("DisposalDate")), null,
                new(ExportAmount(Field("AccumulatedDepreciation")), null), Field("AssetType")));
        }

        if (assets.Count == 0)
            throw new InvalidOperationException("The Xero asset export has no records.");
        return assets;
    }

    internal static string CreateExportSnapshot(IReadOnlyList<Asset> assets) =>
        JsonSerializer.Serialize(new { Source = "Xero CSV export", Assets = assets }, Options);

    internal static string SnapshotSource(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("source", out var source) ? source.GetString() ?? "Xero API" : "Xero API";
    }

    private static decimal? ExportAmount(string value)
    {
        if (value.Length == 0) return null;
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : throw new InvalidOperationException("The Xero asset export contains an invalid amount.");
    }

    private static string? ExportDate(string value)
    {
        if (value.Length == 0) return null;
        string[] formats = ["d/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd"];
        return DateOnly.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : throw new InvalidOperationException("The Xero asset export contains an invalid date.");
    }

    internal sealed record Asset(
        Guid AssetId,
        string AssetNumber,
        string AssetName,
        string AssetStatus,
        string? PurchaseDate,
        decimal? PurchasePrice,
        decimal? AccountingBookValue,
        string? DisposalDate,
        decimal? DisposalPrice,
        Depreciation? BookDepreciationDetail,
        string? AssetTypeName,
        Guid? AssetTypeId = null);

    internal sealed record Depreciation(
        decimal? CurrentAccumDepreciationAmount, decimal? CurrentGainLoss, decimal? PriorAccumDepreciationAmount = null)
    {
        [JsonIgnore]
        internal decimal? AccumulatedDepreciationAmount =>
            CurrentAccumDepreciationAmount is null && PriorAccumDepreciationAmount is null
                ? null
                : CurrentAccumDepreciationAmount.GetValueOrDefault() + PriorAccumDepreciationAmount.GetValueOrDefault();
    }

    private sealed record AssetType(Guid AssetTypeId, string AssetTypeName);
    private sealed record AssetPage(Pagination? Pagination, List<JsonElement>? Items);
    private sealed record Pagination(int? Page, int? PageCount, int? ItemCount);
}
