using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace FijiAccounts.Web.Api.Mobile.V1;

internal static class MobileApiCursor
{
    public static string Encode(long createdAtTicks, Guid id) =>
        WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(
            new CursorValue(createdAtTicks, id)));

    public static bool TryDecode(
        string? cursor,
        out long? createdAtTicks,
        out Guid id)
    {
        createdAtTicks = null;
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return true;
        }

        try
        {
            var value = JsonSerializer.Deserialize<CursorValue>(
                WebEncoders.Base64UrlDecode(cursor));
            if (value is null || value.Ticks <= 0 || value.Id == Guid.Empty)
            {
                return false;
            }

            createdAtTicks = value.Ticks;
            id = value.Id;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record CursorValue(long Ticks, Guid Id);
}
