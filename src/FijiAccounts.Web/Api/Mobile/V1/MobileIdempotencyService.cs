using System.Data;
using System.Security.Cryptography;
using System.Text;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Api.Mobile.V1;

public sealed record MobileStoredCommandResult(int StatusCode, string? ResultCode = null);

public sealed record MobileIdempotentCommandResult(
    int StatusCode,
    string? ResultCode,
    bool Replayed,
    bool KeyConflict = false);

public sealed class MobileIdempotencyService(ApplicationDbContext db)
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    public static bool IsValidKey(string? key) =>
        !string.IsNullOrWhiteSpace(key) &&
        key.Length <= 128 &&
        key.All(character => character is >= '!' and <= '~');

    public async Task<MobileIdempotentCommandResult> ExecuteAsync(
        string userId,
        Guid organisationId,
        string key,
        string operation,
        string requestFingerprint,
        Func<Task<MobileStoredCommandResult>> execute,
        CancellationToken cancellationToken = default)
    {
        var requestHash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(requestFingerprint)));

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var existing = await db.MobileIdempotencyRecords
            .SingleOrDefaultAsync(record =>
                record.OrganisationId == organisationId &&
                record.UserId == userId &&
                record.Key == key,
                cancellationToken);
        if (existing is not null && existing.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            db.MobileIdempotencyRecords.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
            existing = null;
        }

        if (existing is not null)
        {
            return Replay(existing, operation, requestHash);
        }

        var result = await execute();
        var now = DateTimeOffset.UtcNow;
        db.MobileIdempotencyRecords.Add(new MobileIdempotencyRecord
        {
            OrganisationId = organisationId,
            UserId = userId,
            Key = key,
            Operation = operation,
            RequestHash = requestHash,
            StatusCode = result.StatusCode,
            ResultCode = result.ResultCode,
            CreatedAt = now,
            ExpiresAt = now.Add(Retention)
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            var racedRecord = await db.MobileIdempotencyRecords
                .AsNoTracking()
                .SingleOrDefaultAsync(record =>
                    record.OrganisationId == organisationId &&
                    record.UserId == userId &&
                    record.Key == key,
                    cancellationToken);
            if (racedRecord is not null)
            {
                return Replay(racedRecord, operation, requestHash);
            }

            throw;
        }

        return new MobileIdempotentCommandResult(
            result.StatusCode,
            result.ResultCode,
            Replayed: false);
    }

    private static MobileIdempotentCommandResult Replay(
        MobileIdempotencyRecord record,
        string operation,
        string requestHash) =>
        record.Operation != operation || record.RequestHash != requestHash
            ? new MobileIdempotentCommandResult(
                StatusCodes.Status409Conflict,
                "idempotency_key_reused",
                Replayed: true,
                KeyConflict: true)
            : new MobileIdempotentCommandResult(
                record.StatusCode,
                record.ResultCode,
                Replayed: true);
}
