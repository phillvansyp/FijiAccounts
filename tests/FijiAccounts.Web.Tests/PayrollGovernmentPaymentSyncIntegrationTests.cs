using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FijiAccounts.Web.Tests;

public sealed class PayrollGovernmentPaymentSyncIntegrationTests
{
    [Theory]
    [InlineData("matched", true, true)]
    [InlineData("unreconciled", false, true)]
    [InlineData("wrong-amount", false, true)]
    [InlineData("ambiguous", false, true)]
    [InlineData("unposted", false, false)]
    public async Task SyncLoadsBankEvidenceAndReportsOnlyVerifiedPayments(
        string scenario, bool payeMatched, bool fnpfMatched)
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var now = DateOnly.FromDateTime(DateTime.UtcNow);
        var month = new DateOnly(now.Year, now.Month, 1).AddMonths(-1);
        var paidOn = month.AddMonths(1).AddDays(4);
        var protection = new EphemeralDataProtectionProvider();
        var liability = test.Account("2200").Id;
        var bank = test.Account("1000").Id;
        var connection = new PayrollIslandConnection
        {
            OrganisationId = test.Organisation.Id, BaseUrl = "https://payroll.example.test",
            PayrollOrganisationId = "test-company",
            ProtectedAccessToken = protection.CreateProtector("AccountIsland.PayrollIsland.AccessToken.v1").Protect("test-token"),
            WagesExpenseAccountId = test.Account("6000").Id,
            EmployerContributionsExpenseAccountId = test.Account("6000").Id,
            NetWagesPayableAccountId = liability, PayePayableAccountId = liability,
            FnpfPayableAccountId = liability, OtherDeductionsPayableAccountId = liability,
            CreatedByUserId = test.UserId, UpdatedByUserId = test.UserId
        };
        test.Db.PayrollIslandConnections.Add(connection);
        var accrual = await test.Posting.PostAsync(test.UserId, new JournalPostRequest(
            test.Organisation.Id, month, "PAYROLL-TEST", "Payroll accrual",
            [new(test.Account("6000").Id,"Wages",280m,0m),new(liability,"Liability",0m,280m)]));
        var import = new PayrollIslandPayRunImport
        {
            OrganisationId = test.Organisation.Id, ConnectionId = connection.Id,
            ExternalPayRunId = "test-run", PayRunNumber = "RUN-1", Revision = 1,
            PeriodStart = month, PeriodEnd = month.AddDays(6), PaymentDate = month.AddDays(7),
            Currency = "FJD", PayloadSha256 = new string('A',64), ImportedByUserId = test.UserId,
            Status = scenario == "unposted" ? PayrollIslandImportStatus.ReadyToPost : PayrollIslandImportStatus.Posted,
            PostedJournalId = scenario == "unposted" ? null : accrual.Id,
            Payments =
            [
                new() { ExternalPaymentId = "paye", Kind = PayrollPaymentKind.Paye, Amount = 80m, DueDate = paidOn },
                new() { ExternalPaymentId = "fnpf", Kind = PayrollPaymentKind.Fnpf, Amount = 200m, DueDate = paidOn }
            ]
        };
        test.Db.PayrollIslandPayRunImports.Add(import);

        async Task<BankStatementLine> AddPayment(string agency, decimal amount, bool reconciled = true)
        {
            var journal = await test.Posting.PostAsync(test.UserId, new JournalPostRequest(
                test.Organisation.Id, paidOn, $"{agency} {month:yyyy-MM}", $"{agency} payment",
                [new(liability,agency,amount,0m),new(bank,agency,0m,amount)]));
            var bankLine = await test.Db.PostedJournalLines.SingleAsync(x => x.PostedJournalId == journal.Id && x.LedgerAccountId == bank);
            var statement = new BankStatementLine
            {
                OrganisationId = test.Organisation.Id, BankAccountId = bank, TransactionDate = paidOn,
                Description = $"{agency} {month:yyyy-MM}", Amount = -amount,
                MatchedPostedJournalLineId = reconciled ? bankLine.Id : null,
                ReconciledAt = reconciled ? DateTimeOffset.UtcNow : null,
                ReconciledByUserId = reconciled ? test.UserId : null
            };
            test.Db.BankStatementLines.Add(statement);
            await test.Db.SaveChangesAsync();
            return statement;
        }

        var paye = await AddPayment("PAYE", scenario == "wrong-amount" ? 79m : 80m, scenario != "unreconciled");
        var fnpf = await AddPayment("FNPF",200m);
        if (scenario == "ambiguous") await AddPayment("PAYE",80m);
        test.Db.ChangeTracker.Clear();
        using var handler = new CapturingHandler(month,paidOn);
        var service = new PayrollGovernmentPaymentSyncService(test.Db,new TestHttpFactory(handler),protection,
            NullLogger<PayrollGovernmentPaymentSyncService>.Instance);

        Assert.Equal(2,await service.SyncAsync(test.Organisation.Id));
        Assert.Equal(2,handler.Updates.Count);
        var payeUpdate = handler.Updates.Single(x => x.Path.EndsWith($"/paye-monthly/{month:yyyy-MM-dd}"));
        var fnpfUpdate = handler.Updates.Single(x => x.Path.EndsWith($"/fnpf-payment/{month:yyyy-MM-dd}"));
        AssertUpdate(payeUpdate.Body,payeMatched,80m,paye.Id,paidOn);
        AssertUpdate(fnpfUpdate.Body,fnpfMatched,200m,fnpf.Id,paidOn);
        var claim = await test.Db.PayrollGovernmentPaymentClaims.AsNoTracking().SingleAsync();
        Assert.Equal(payeMatched,claim.BankConfirmedAtUtc.HasValue);
        Assert.Empty(test.Db.ChangeTracker.Entries<PostedJournal>());
        Assert.Empty(test.Db.ChangeTracker.Entries<PostedJournalLine>());
    }

    private static void AssertUpdate(JsonElement body,bool matched,decimal amount,Guid statementId,DateOnly paidOn)
    {
        Assert.Equal(matched,body.GetProperty("matched").GetBoolean());
        Assert.Equal(amount,body.GetProperty("amount").GetDecimal());
        if (matched)
        {
            Assert.Equal(statementId,body.GetProperty("bankStatementLineId").GetGuid());
            Assert.Equal(paidOn.ToString("yyyy-MM-dd"),body.GetProperty("bankPaymentDate").GetString());
            Assert.NotEqual(JsonValueKind.Null,body.GetProperty("matchedAtUtc").ValueKind);
        }
        else
        {
            Assert.Equal(JsonValueKind.Null,body.GetProperty("bankStatementLineId").ValueKind);
            Assert.Equal(JsonValueKind.Null,body.GetProperty("bankPaymentDate").ValueKind);
            Assert.Equal(JsonValueKind.Null,body.GetProperty("matchedAtUtc").ValueKind);
        }
    }

    private sealed class TestHttpFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler,disposeHandler:false);
    }
    private sealed class CapturingHandler(DateOnly month,DateOnly paidOn) : HttpMessageHandler
    {
        public List<(string Path,JsonElement Body)> Updates { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            Assert.Equal("Bearer",request.Headers.Authorization?.Scheme);
            Assert.Equal("test-token",request.Headers.Authorization?.Parameter);
            Assert.Equal("2026-09-01",Assert.Single(request.Headers.GetValues("X-Account-Island-Contract")));
            if (request.Method == HttpMethod.Get)
            {
                Assert.EndsWith("/government-payments/manual",request.RequestUri!.AbsolutePath);
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new
                {
                    payments = new[] { new { deadlineKey = "paye-monthly",periodStart = month,paidOn,
                        reference = "PAYE claim",recordedBy = "tester",recordedAtUtc = DateTimeOffset.UtcNow } }
                }) };
            }
            Assert.Equal(HttpMethod.Put,request.Method);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Updates.Add((request.RequestUri!.AbsolutePath,body.RootElement.Clone()));
            return new(HttpStatusCode.OK);
        }
    }
}
