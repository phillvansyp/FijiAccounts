namespace FijiAccounts.Web.Services;

public sealed class OrganisationUpdateBroker(
    ILogger<OrganisationUpdateBroker> logger)
{
    private readonly Lock sync = new();
    private readonly List<Action<Guid>> subscribers = [];

    public IDisposable Subscribe(Action<Guid> subscriber)
    {
        lock (sync)
        {
            subscribers.Add(subscriber);
        }

        return new Subscription(this, subscriber);
    }

    public void Publish(Guid organisationId)
    {
        Action<Guid>[] snapshot;

        lock (sync)
        {
            snapshot = [.. subscribers];
        }

        foreach (var subscriber in snapshot)
        {
            try
            {
                subscriber(organisationId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "A live-update subscriber failed for organisation {OrganisationId}.",
                    organisationId);
            }
        }
    }

    private void Unsubscribe(Action<Guid> subscriber)
    {
        lock (sync)
        {
            subscribers.Remove(subscriber);
        }
    }

    private sealed class Subscription(
        OrganisationUpdateBroker broker,
        Action<Guid> subscriber) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            broker.Unsubscribe(subscriber);
        }
    }
}
