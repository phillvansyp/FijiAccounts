using System.Net;
using System.Text;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class PayrollIslandHttpClientTests
{
    [Fact]
    public async Task GetFinalisedPayRunsAsync_UsesVersionedAuthenticatedContract()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {"payRuns":[],"nextCursor":"cursor:42"}
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });
        var client = new PayrollIslandHttpClient(new HttpClient(handler));

        var result = await client.GetFinalisedPayRunsAsync(
            "https://payroll.example.test",
            "org:123",
            "secret-token",
            "cursor:41");

        Assert.Empty(result.PayRuns);
        Assert.Equal("cursor:42", result.NextCursor);
        Assert.Equal(
            "https://payroll.example.test/api/account-island/v1/organisations/org%3A123/pay-runs?after=cursor%3A41",
            captured!.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", captured.Headers.Authorization!.Scheme);
        Assert.Equal("secret-token", captured.Headers.Authorization.Parameter);
        Assert.Equal(
            "2026-09-01",
            Assert.Single(captured.Headers.GetValues("X-Account-Island-Contract")));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
