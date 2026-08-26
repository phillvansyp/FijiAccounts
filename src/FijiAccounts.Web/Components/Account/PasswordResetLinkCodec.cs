using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace FijiAccounts.Web.Components.Account;

internal static class PasswordResetLinkCodec
{
    public static string Encode(string userId, string token)
    {
        var payload = JsonSerializer.Serialize(new ResetPayload(userId, token));
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
    }

    public static bool TryDecode(string code, out string userId, out string token)
    {
        userId = string.Empty;
        token = string.Empty;

        try
        {
            var json = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
            var payload = JsonSerializer.Deserialize<ResetPayload>(json);
            if (payload is null ||
                string.IsNullOrWhiteSpace(payload.UserId) ||
                string.IsNullOrWhiteSpace(payload.Token))
            {
                return false;
            }

            userId = payload.UserId;
            token = payload.Token;
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

    private sealed record ResetPayload(string UserId, string Token);
}
