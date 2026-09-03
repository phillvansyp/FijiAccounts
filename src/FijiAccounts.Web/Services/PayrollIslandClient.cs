using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FijiAccounts.Web.Services;

public sealed record PayrollIslandPaymentPayload(
    string ExternalPaymentId,
    string Kind,
    string Status,
    DateOnly DueDate,
    DateOnly? PaidDate,
    decimal Amount,
    string? Reference);

public sealed record PayrollIslandPayRunPayload(
    string ExternalPayRunId,
    int Revision,
    string PayRunNumber,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly PaymentDate,
    string Currency,
    int EmployeeCount,
    decimal GrossEarnings,
    decimal EmployeePaye,
    decimal EmployeeFnpf,
    decimal EmployerFnpf,
    decimal OtherDeductions,
    decimal NetPay,
    IReadOnlyList<PayrollIslandPaymentPayload> Payments);

public sealed record PayrollIslandPayRunPage(
    IReadOnlyList<PayrollIslandPayRunPayload> PayRuns,
    string? NextCursor);

public interface IPayrollIslandClient
{
    Task<PayrollIslandPayRunPage> GetFinalisedPayRunsAsync(
        string baseUrl,
        string payrollOrganisationId,
        string accessToken,
        string? afterCursor,
        CancellationToken cancellationToken = default);
}

public sealed class PayrollIslandHttpClient(HttpClient http) : IPayrollIslandClient
{
    private const long MaximumResponseBytes = 5 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<PayrollIslandPayRunPage> GetFinalisedPayRunsAsync(
        string baseUrl,
        string payrollOrganisationId,
        string accessToken,
        string? afterCursor,
        CancellationToken cancellationToken = default)
    {
        var root = new Uri(baseUrl.EndsWith('/') ? baseUrl : $"{baseUrl}/");
        var relative =
            $"api/account-island/v1/organisations/{Uri.EscapeDataString(payrollOrganisationId)}/pay-runs";
        if (!string.IsNullOrWhiteSpace(afterCursor))
        {
            relative += $"?after={Uri.EscapeDataString(afterCursor)}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(root, relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("X-Account-Island-Contract", "2026-09-01");
        using var response = await http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
        {
            throw new InvalidOperationException(
                "Payroll Island returned more than the 5 MB import limit.");
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await responseStream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaximumResponseBytes)
            {
                throw new InvalidOperationException(
                    "Payroll Island returned more than the 5 MB import limit.");
            }
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        buffer.Position = 0;
        var page = await JsonSerializer.DeserializeAsync<PayrollIslandPayRunPage>(
            buffer,
            JsonOptions,
            cancellationToken);
        if (page?.PayRuns is null)
        {
            throw new InvalidOperationException(
                "Payroll Island returned an empty response.");
        }
        return page;
    }
}
