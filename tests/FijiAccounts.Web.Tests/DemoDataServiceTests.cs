using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

namespace FijiAccounts.Web.Tests;

public sealed class DemoDataServiceTests
{
    [Fact]
    public async Task ResetAndGenerateRejectsNonDevelopmentEnvironment()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var service = new DemoDataService(
            db,
            new DevelopmentEnvironment { EnvironmentName = "Production" },
            new PlatformAdminAccessService(db));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResetAndGenerateAsync("administrator", new DateOnly(2026, 8, 23)));

        Assert.Contains("Development", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResetAndGenerateRejectsUserWithoutPlatformRole()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var user = User("ordinary-user", "ordinary@example.com");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var service = new DemoDataService(
            db,
            new DevelopmentEnvironment(),
            new PlatformAdminAccessService(db));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ResetAndGenerateAsync(user.Id, new DateOnly(2026, 8, 23)));
    }

    [Fact]
    public async Task MissingDedicatedDemoTenantDoesNotModifyProductionData()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var administrator = User("platform-administrator", "admin@example.com");
        var productionCompany = new Organisation
        {
            LegalName = "Production Company",
            CountryCode = "FJ",
            BaseCurrency = "FJD",
            TaxLabel = "VAT",
            Kind = OrganisationKind.Business
        };
        var platformRole = new IdentityRole(PlatformAdminAccessService.RoleName)
        {
            Id = "platform-admin-role",
            NormalizedName = PlatformAdminAccessService.RoleName.ToUpperInvariant()
        };
        db.Users.Add(administrator);
        db.Roles.Add(platformRole);
        db.UserRoles.Add(new IdentityUserRole<string>
        {
            UserId = administrator.Id,
            RoleId = platformRole.Id
        });
        db.Organisations.Add(productionCompany);
        await db.SaveChangesAsync();
        var service = new DemoDataService(
            db,
            new DevelopmentEnvironment(),
            new PlatformAdminAccessService(db));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResetAndGenerateAsync(administrator.Id, new DateOnly(2026, 8, 23)));

        Assert.Contains("Demo tenant does not exist", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(await db.Organisations.AnyAsync(x => x.Id == productionCompany.Id));
    }

    [Fact]
    public async Task ResetAndGenerateCreatesRepeatableBalancedIsolatedDemo()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var administrator = new ApplicationUser
        {
            Id = "platform-administrator",
            UserName = "admin@example.com",
            NormalizedUserName = "ADMIN@EXAMPLE.COM",
            Email = "admin@example.com",
            NormalizedEmail = "ADMIN@EXAMPLE.COM",
            EmailConfirmed = true
        };
        var demoOwner = new ApplicationUser
        {
            Id = "demo-owner",
            UserName = "demo@accountisland.com",
            NormalizedUserName = "DEMO@ACCOUNTISLAND.COM",
            Email = "demo@accountisland.com",
            NormalizedEmail = "DEMO@ACCOUNTISLAND.COM",
            EmailConfirmed = true
        };
        var demoGroup = new OrganisationGroup
        {
            Name = DemoDataService.DemoGroupName,
            PresentationCurrency = "FJD",
            IsDemo = true
        };
        var demoCompany = new Organisation
        {
            OrganisationGroupId = demoGroup.Id,
            LegalName = "Demo",
            CountryCode = "FJ",
            BaseCurrency = "FJD",
            TaxLabel = "VAT",
            Kind = OrganisationKind.Business
        };
        var legacyDemoGroup = new OrganisationGroup
        {
            Id = Guid.Parse("8d13b614-47f4-50eb-a994-7e0ca5c49cc0"),
            Name = "Account Island Demo Group",
            PresentationCurrency = "FJD",
            IsDemo = true
        };
        var legacyDemoCompany = new Organisation
        {
            OrganisationGroupId = legacyDemoGroup.Id,
            LegalName = "Legacy Demo Company",
            CountryCode = "FJ",
            BaseCurrency = "FJD",
            TaxLabel = "VAT",
            Kind = OrganisationKind.Business
        };
        var unrelated = new Organisation
        {
            LegalName = "Unrelated Production Company",
            CountryCode = "FJ",
            BaseCurrency = "FJD",
            TaxLabel = "VAT",
            Kind = OrganisationKind.Business
        };
        db.Users.AddRange(administrator, demoOwner);
        db.OrganisationGroups.AddRange(demoGroup, legacyDemoGroup);
        db.Organisations.AddRange(demoCompany, legacyDemoCompany, unrelated);
        db.OrganisationGroupMemberships.Add(new OrganisationGroupMembership
        {
            OrganisationGroupId = demoGroup.Id,
            UserId = demoOwner.Id,
            Role = OrganisationGroupRole.Owner
        });
        db.OrganisationMemberships.Add(new OrganisationMembership
        {
            OrganisationId = demoCompany.Id,
            UserId = demoOwner.Id,
            Role = OrganisationRole.Owner
        });
        var platformRole = new IdentityRole(PlatformAdminAccessService.RoleName)
        {
            Id = "platform-admin-role",
            NormalizedName = PlatformAdminAccessService.RoleName.ToUpperInvariant()
        };
        db.Roles.Add(platformRole);
        db.UserRoles.Add(new IdentityUserRole<string>
        {
            UserId = administrator.Id,
            RoleId = platformRole.Id
        });
        await db.SaveChangesAsync();

        var service = new DemoDataService(
            db,
            new DevelopmentEnvironment(),
            new PlatformAdminAccessService(db));
        var asOf = new DateOnly(2026, 8, 23);
        var first = await service.ResetAndGenerateAsync(administrator.Id, asOf);

        Assert.Equal(asOf, first.AsOfDate);
        Assert.Equal(asOf.AddMonths(-3).AddDays(1), first.StartDate);
        Assert.Equal(2, first.CompanyCount);
        Assert.Equal(4, first.BranchCount);
        Assert.Equal(9, first.DivisionCount);
        Assert.Equal(40, first.CustomerCount);
        Assert.Equal(24, first.SupplierCount);
        Assert.Equal(180, first.SalesInvoiceCount);
        Assert.Equal(84, first.SupplierBillCount);
        Assert.True(first.CustomerReceiptCount > 0);
        Assert.True(first.SupplierPaymentCount > 0);
        Assert.True(first.CreditNoteCount > 0);
        Assert.InRange(first.AnnualisedNetSales, 4_800_000m, 5_100_000m);

        var demoOrganisationIds = await db.Organisations
            .Where(x => x.OrganisationGroupId == demoGroup.Id)
            .Select(x => x.Id)
            .ToArrayAsync();
        Assert.Contains(demoCompany.Id, demoOrganisationIds);
        Assert.Equal(
            2,
            await db.OrganisationMemberships.CountAsync(x =>
                demoOrganisationIds.Contains(x.OrganisationId) &&
                x.UserId == demoOwner.Id &&
                x.Role == OrganisationRole.Owner));
        Assert.False(await db.OrganisationMemberships.AnyAsync(x =>
            demoOrganisationIds.Contains(x.OrganisationId) &&
            x.UserId == administrator.Id));
        var journals = await db.PostedJournals
            .Where(x => demoOrganisationIds.Contains(x.OrganisationId))
            .Include(x => x.Lines)
            .ToListAsync();
        Assert.NotEmpty(journals);
        Assert.All(journals, journal =>
            Assert.Equal(journal.Lines.Sum(x => x.Debit), journal.Lines.Sum(x => x.Credit)));

        var fixedAssets = await db.FixedAssets
            .Where(x => demoOrganisationIds.Contains(x.OrganisationId))
            .Include(x => x.DepreciationEntries)
            .ToListAsync();
        Assert.Equal(4, fixedAssets.Count);
        Assert.All(fixedAssets, asset =>
        {
            Assert.True(asset.IsActive);
            Assert.NotNull(asset.AcquisitionJournalId);
            Assert.Single(asset.DepreciationEntries);
            Assert.True(asset.DepreciationEntries[0].Amount > 0);
        });

        var trackedItems = await db.ProductItems
            .Where(x =>
                demoOrganisationIds.Contains(x.OrganisationId) &&
                x.Kind == ProductKind.TrackedItem)
            .ToListAsync();
        Assert.Equal(6, trackedItems.Count);
        Assert.All(trackedItems, item =>
        {
            Assert.True(item.QuantityOnHand > 0);
            Assert.True(item.AverageCost > 0);
            Assert.NotNull(item.InventoryAccountId);
        });
        Assert.Equal(
            12,
            await db.InventoryMovements.CountAsync(x =>
                demoOrganisationIds.Contains(x.OrganisationId)));

        var accountingPeriods = await db.AccountingPeriods
            .Where(x => demoOrganisationIds.Contains(x.OrganisationId))
            .ToListAsync();
        Assert.Equal(8, accountingPeriods.Count);
        foreach (var organisationId in demoOrganisationIds)
        {
            var companyPeriods = accountingPeriods
                .Where(x => x.OrganisationId == organisationId)
                .OrderBy(x => x.StartsOn)
                .ToList();
            Assert.Equal(4, companyPeriods.Count);
            Assert.Equal(3, companyPeriods.Count(x => x.IsLocked));
            Assert.False(companyPeriods[^1].IsLocked);
            Assert.Equal(new DateOnly(asOf.Year, asOf.Month, 1), companyPeriods[^1].StartsOn);
            Assert.Null(companyPeriods[^1].LockedAt);
            Assert.All(companyPeriods[..^1], period =>
            {
                Assert.NotNull(period.LockedAt);
                Assert.Equal(administrator.Id, period.LockedByUserId);
            });
        }
        Assert.Equal(
            6,
            await db.AuditEvents.CountAsync(x =>
                demoOrganisationIds.Contains(x.OrganisationId) &&
                x.EventType == "AccountingPeriodLocked"));

        var notifications = await db.Notifications
            .Where(x => demoOrganisationIds.Contains(x.OrganisationId))
            .ToListAsync();
        Assert.NotEmpty(notifications);
        Assert.All(notifications, notification =>
        {
            Assert.Equal(NotificationType.PaymentDueSoon, notification.Type);
            Assert.Equal(NotificationStatus.Open, notification.Status);
            Assert.False(notification.IsRead);
            Assert.True(notification.Amount > 0);
            Assert.Contains(
                notification.RelatedEntityType,
                new[] { nameof(SalesInvoice), nameof(SupplierBill) });
        });
        Assert.All(
            demoOrganisationIds,
            organisationId => Assert.Contains(
                notifications,
                notification => notification.OrganisationId == organisationId));

        Assert.Equal(
            6,
            await db.TransactionExchangeRates.CountAsync(x =>
                demoOrganisationIds.Contains(x.OrganisationId)));
        Assert.Equal(
            6,
            await db.OrganisationCurrencies.CountAsync(x =>
                demoOrganisationIds.Contains(x.OrganisationId) && x.IsActive));
        var foreignInvoices = await db.SalesInvoices
            .Where(x =>
                demoOrganisationIds.Contains(x.OrganisationId) &&
                x.Currency != "FJD")
            .ToListAsync();
        var foreignBills = await db.SupplierBills
            .Where(x =>
                demoOrganisationIds.Contains(x.OrganisationId) &&
                x.Currency != "FJD")
            .ToListAsync();
        Assert.Equal(6, foreignInvoices.Count);
        Assert.Equal(6, foreignBills.Count);
        Assert.All(foreignInvoices, invoice =>
        {
            Assert.True(invoice.ExchangeRateToBase > 1m);
            Assert.True(invoice.TransactionTotal > 0m);
            Assert.Equal(0m, invoice.TransactionAmountPaid);
        });
        Assert.All(foreignBills, bill =>
        {
            Assert.True(bill.ExchangeRateToBase > 1m);
            Assert.True(bill.TransactionTotal > 0m);
            Assert.Equal(0m, bill.TransactionAmountPaid);
        });
        var specialistDivisionIds = await db.Divisions
            .Where(x =>
                demoOrganisationIds.Contains(x.Branch.OrganisationId) &&
                !x.IsDefault)
            .Select(x => x.Id)
            .ToListAsync();
        Assert.NotEmpty(specialistDivisionIds);
        Assert.All(
            specialistDivisionIds,
            divisionId => Assert.Contains(
                journals.SelectMany(x => x.Lines),
                line => line.DivisionId == divisionId));

        var elimination = await db.GroupEliminationJournals
            .Include(x => x.Lines)
            .SingleAsync(x => x.OrganisationGroupId == demoGroup.Id);
        Assert.Equal($"ELIM-{asOf.Year}-001", elimination.Reference);
        Assert.Equal("FJD", elimination.Currency);
        Assert.Equal(4, elimination.Lines.Count);
        Assert.Equal(
            elimination.Lines.Sum(x => x.Debit),
            elimination.Lines.Sum(x => x.Credit));
        Assert.Contains(elimination.Lines, x =>
            x.AccountCode == "4000" && x.Debit == 25_000m);
        Assert.Contains(elimination.Lines, x =>
            x.AccountCode == "5000" && x.Credit == 25_000m);
        Assert.Contains(elimination.Lines, x =>
            x.AccountCode == "2000" && x.Debit == 10_000m);
        Assert.Contains(elimination.Lines, x =>
            x.AccountCode == "1100" && x.Credit == 10_000m);

        var statementLines = await db.BankStatementLines
            .Where(x => demoOrganisationIds.Contains(x.OrganisationId))
            .ToListAsync();
        Assert.Equal(
            first.CustomerReceiptCount + first.SupplierPaymentCount,
            statementLines.Count);
        Assert.All(statementLines, line =>
        {
            Assert.NotEqual(0, line.Amount);
            Assert.Equal("Demo", line.Source);
            Assert.NotNull(line.ImportBatchId);
            Assert.NotNull(line.SourceHash);
        });

        var reconciliationSessions = await db.BankReconciliationSessions
            .Where(x => demoOrganisationIds.Contains(x.OrganisationId))
            .ToListAsync();
        Assert.Equal(4, reconciliationSessions.Count);
        Assert.Equal(2, reconciliationSessions.Count(x => x.IsCompleted));
        Assert.Equal(2, reconciliationSessions.Count(x => !x.IsCompleted));
        Assert.All(reconciliationSessions, session =>
        {
            Assert.Equal(0, session.Difference);
            Assert.Equal(session.ClosingStatementBalance, session.LedgerBalance);
        });
        foreach (var organisationId in demoOrganisationIds)
        {
            Assert.Equal(
                4,
                statementLines.Count(x =>
                    x.OrganisationId == organisationId &&
                    x.ReconciledAt == null));
            var completed = reconciliationSessions.Single(x =>
                x.OrganisationId == organisationId && x.IsCompleted);
            Assert.DoesNotContain(statementLines, x =>
                x.OrganisationId == organisationId &&
                x.TransactionDate >= completed.StatementStartDate &&
                x.TransactionDate <= completed.StatementEndDate &&
                x.ReconciledAt == null);
        }

        var budgets = await db.AccountBudgets
            .Where(x => demoOrganisationIds.Contains(x.OrganisationId))
            .ToListAsync();
        Assert.NotEmpty(budgets);
        Assert.All(budgets, budget => Assert.True(budget.Amount > 0));
        foreach (var organisationId in demoOrganisationIds)
        {
            var organisationBudgets = budgets
                .Where(x => x.OrganisationId == organisationId)
                .ToList();
            Assert.Contains(organisationBudgets, x => x.ScopeKey == "organisation");
            Assert.Contains(organisationBudgets, x => x.ScopeKey.StartsWith("branch:"));
            Assert.Contains(organisationBudgets, x => x.ScopeKey.StartsWith("division:"));
            Assert.All(
                organisationBudgets.Where(x => x.ScopeKey.StartsWith("branch:")),
                budget =>
                {
                    Assert.NotNull(budget.BranchId);
                    Assert.Null(budget.DivisionId);
                });
            Assert.All(
                organisationBudgets.Where(x => x.ScopeKey.StartsWith("division:")),
                budget =>
                {
                    Assert.NotNull(budget.BranchId);
                    Assert.NotNull(budget.DivisionId);
                });
        }

        var invoiceDates = await db.SalesInvoices
            .Where(x => demoOrganisationIds.Contains(x.OrganisationId))
            .Select(x => x.IssueDate)
            .ToListAsync();
        Assert.All(invoiceDates, date =>
            Assert.InRange(date, first.StartDate, first.AsOfDate));

        var approvalBill = await db.SupplierBills
            .FirstAsync(x => demoOrganisationIds.Contains(x.OrganisationId));
        var approvalBank = await db.LedgerAccounts
            .SingleAsync(x =>
                x.OrganisationId == approvalBill.OrganisationId &&
                x.Code == "1000");
        db.SupplierPaymentApprovals.Add(new SupplierPaymentApproval
        {
            OrganisationId = approvalBill.OrganisationId,
            BranchId = approvalBill.BranchId,
            DivisionId = approvalBill.DivisionId,
            SupplierId = approvalBill.SupplierId,
            SupplierBillId = approvalBill.Id,
            PaymentDate = asOf,
            Reference = "DEMO-APPROVAL",
            Amount = 1m,
            BankAccountId = approvalBank.Id,
            RequestedByUserId = demoOwner.Id
        });
        var projectBranch = await db.Branches
            .Include(x => x.Divisions)
            .FirstAsync(x => x.OrganisationId == approvalBill.OrganisationId);
        db.Projects.Add(new Project
        {
            OrganisationId = approvalBill.OrganisationId,
            BranchId = projectBranch.Id,
            DivisionId = projectBranch.Divisions.First().Id,
            ProjectNumber = "DEMO-PROJECT",
            Name = "Legacy demo project",
            StartDate = asOf.AddMonths(-1),
            CreatedByUserId = demoOwner.Id
        });
        await db.SaveChangesAsync();

        var second = await service.ResetAndGenerateAsync(administrator.Id, asOf);
        Assert.Equal(first, second);
        Assert.Equal(
            2,
            await db.PlatformAuditEvents.CountAsync(x =>
                x.OrganisationGroupId == demoGroup.Id &&
                x.EventType == "DemoDataReset"));
        Assert.True(await db.Organisations.AnyAsync(x => x.Id == unrelated.Id));
        Assert.Equal(1, await db.OrganisationGroups.CountAsync(x => x.Id == demoGroup.Id));
        Assert.False(await db.OrganisationGroups.AnyAsync(x => x.Id == legacyDemoGroup.Id));
        Assert.Equal(
            1,
            await db.GroupEliminationJournals.CountAsync(x =>
                x.OrganisationGroupId == demoGroup.Id));
    }

    private static ApplicationUser User(string id, string email) => new()
    {
        Id = id,
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        EmailConfirmed = true
    };

    private sealed class DevelopmentEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "FijiAccounts.Web.Tests";
        public string WebRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
