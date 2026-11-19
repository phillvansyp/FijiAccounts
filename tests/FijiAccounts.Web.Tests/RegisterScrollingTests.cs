using FijiAccounts.Web.Components;
using FijiAccounts.Web.Components.Pages;

namespace FijiAccounts.Web.Tests;

public sealed class RegisterScrollingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(101)]
    [InlineData(80669)]
    public void ConsecutiveWindowsReturnEachRowExactlyOnce(int total)
    {
        var windows = Enumerable.Range(0, total / 37 + 2)
            .SelectMany(page => RegisterRows.Window(total, page * 37, 37));
        Assert.Equal(Enumerable.Range(0, total), windows);
    }

    [Fact]
    public async Task ContactWindowPreservesOrderAndOrganisationScope()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        List<Guid> ids = [test.Supplier.Id, Guid.NewGuid(), test.Customer.Id];
        var rows = await ContactWindow.ReadRowsAsync(test.Db, test.Organisation.Id, ids);
        Assert.Equal(new[] { test.Supplier.Id, test.Customer.Id }, rows.Select(row => row.Id));
        Assert.Empty(await ContactWindow.ReadRowsAsync(test.Db, Guid.NewGuid(), ids));
    }
}
