using System.Text.Json;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed record PayrollEmployeeDetail(string PaymentId, string EmployeeId, string Name, DateOnly PaymentDate,
    decimal Gross, decimal Paye, decimal EmployeeFnpf, decimal EmployerFnpf, decimal OtherDeductions, decimal NetPay)
{
    public static IReadOnlyList<PayrollEmployeeDetail> Read(PayrollIslandPayRunImport run) =>
        run.EmployeesJson is null ? [] : JsonSerializer.Deserialize<PayrollEmployeeDetail[]>(run.EmployeesJson) ?? [];

    public static void Validate(PayrollIslandPayRunPayload run)
    {
        if (run.Employees is not { } rows) return; // Older exports remain readable, but cannot auto-match.
        if (rows.Count == 0 || rows.Count > 5000 || rows.Select(x => x.PaymentId).Distinct().Count() != rows.Count ||
            rows.Any(x => string.IsNullOrWhiteSpace(x.PaymentId) || x.PaymentId.Length > 120 ||
                string.IsNullOrWhiteSpace(x.EmployeeId) || x.EmployeeId.Length > 120 ||
                string.IsNullOrWhiteSpace(x.Name) || x.Name.Length > 240 ||
                x.PaymentDate < run.PeriodStart ||
                new[] { x.Gross, x.Paye, x.EmployeeFnpf, x.EmployerFnpf, x.OtherDeductions, x.NetPay }
                    .Any(a => a < 0 || decimal.Round(a, 2) != a) ||
                x.Gross != x.NetPay + x.Paye + x.EmployeeFnpf + x.OtherDeductions) ||
            rows.Select(x => x.EmployeeId).Distinct().Count() != run.EmployeeCount ||
            rows.Sum(x => x.Gross) != run.GrossEarnings || rows.Sum(x => x.NetPay) != run.NetPay ||
            rows.Sum(x => x.Paye) != run.EmployeePaye || rows.Sum(x => x.EmployeeFnpf) != run.EmployeeFnpf ||
            rows.Sum(x => x.EmployerFnpf) != run.EmployerFnpf || rows.Sum(x => x.OtherDeductions) != run.OtherDeductions)
            throw new InvalidOperationException($"Employee breakdown for {run.PayRunNumber} does not agree with its payroll totals.");
    }
}
