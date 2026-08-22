using FijiAccounts.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace FijiAccounts.Web.Tests;

public sealed class OrganisationUpdateBrokerTests
{
    [Fact]
    public void Publish_IsolatesFailuresAndHonoursUnsubscribe()
    {
        var broker =
            new OrganisationUpdateBroker(
                NullLogger<OrganisationUpdateBroker>.Instance);
        var organisationId = Guid.NewGuid();
        var received = new List<Guid>();

        using var failing =
            broker.Subscribe(
                _ => throw new InvalidOperationException("Subscriber failed."));
        var working =
            broker.Subscribe(received.Add);

        broker.Publish(organisationId);

        Assert.Equal([organisationId], received);

        working.Dispose();
        broker.Publish(organisationId);

        Assert.Equal([organisationId], received);
    }
}
