using FijiAccounts.Web.Components.Account;

namespace FijiAccounts.Web.Tests;

public sealed class PasswordResetLinkCodecTests
{
    [Fact]
    public void RoundTrip_PreservesUserAndIdentityToken()
    {
        var code = PasswordResetLinkCodec.Encode(
            "user-123",
            "CfDJ8+/token== with symbols");

        var decoded = PasswordResetLinkCodec.TryDecode(
            code,
            out var userId,
            out var token);

        Assert.True(decoded);
        Assert.Equal("user-123", userId);
        Assert.Equal("CfDJ8+/token== with symbols", token);
        Assert.DoesNotContain('&', code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64!")]
    [InlineData("e30")]
    public void TryDecode_RejectsMalformedPayload(string code)
    {
        Assert.False(PasswordResetLinkCodec.TryDecode(code, out _, out _));
    }
}
