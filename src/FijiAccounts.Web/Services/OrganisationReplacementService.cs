using System.Data.Common;
using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FijiAccounts.Web.Services;

public sealed record ReplaceOrganisationPlan(
    string WorkspaceId,
    string ExpectedLegalName,
    string ExpectedCountryCode,
    string PreservedWorkspaceId,
    string PreservedExpectedLegalName,
    string PreservedExpectedCountryCode);

public sealed record ReplaceOrganisationResult(
    bool Applied,
    string RemovedWorkspaceId,
    string ReplacementWorkspaceId,
    string PreservedWorkspaceId,
    int MembershipsRestored,
    IReadOnlyDictionary<string, long> RemovedRows)
{
    public override string ToString() => JsonSerializer.Serialize(this);
}

/// <summary>
/// Replaces one company workspace with a clean standalone workspace while retaining
/// its users and immutable company identity. The operation is deliberately guarded
/// by both workspace IDs, legal names and jurisdictions so same-named companies in
/// different countries cannot be confused.
/// </summary>
public sealed class OrganisationReplacementService(ApplicationDbContext db)
{
    private sealed record MemberSnapshot(string UserId, OrganisationRole Role);
    private sealed record ColumnInfo(string Name, bool NotNull, int PrimaryKeyOrder);
    private sealed record ForeignKeyColumn(int Id, int Sequence, string ParentTable, string ChildColumn, string? ParentColumn, string OnDelete);

    public async Task<ReplaceOrganisationResult> RunAsync(
        ReplaceOrganisationPlan plan,
        bool apply,
        CancellationToken ct = default)
    {
        ValidatePlan(plan);

        var organisations = await db.Organisations.AsNoTracking().ToListAsync(ct);
        var target = organisations.SingleOrDefault(x =>
            OrganisationDisplay.WorkspaceId(x).Equals(plan.WorkspaceId, StringComparison.OrdinalIgnoreCase));
        var preserved = organisations.SingleOrDefault(x =>
            OrganisationDisplay.WorkspaceId(x).Equals(plan.PreservedWorkspaceId, StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            throw new InvalidOperationException($"Workspace {plan.WorkspaceId} was not found.");
        }
        if (preserved is null)
        {
            throw new InvalidOperationException($"Protected workspace {plan.PreservedWorkspaceId} was not found.");
        }
        RequireIdentity(target, plan.ExpectedLegalName, plan.ExpectedCountryCode, "target");
        RequireIdentity(preserved, plan.PreservedExpectedLegalName, plan.PreservedExpectedCountryCode, "protected");

        if (target.Id == preserved.Id ||
            target.CountryCode.Equals(preserved.CountryCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The target and protected workspaces must be different companies in different countries.");
        }

        var members = await db.OrganisationMemberships.AsNoTracking()
            .Where(x => x.OrganisationId == target.Id)
            .OrderBy(x => x.UserId)
            .Select(x => new MemberSnapshot(x.UserId, x.Role))
            .ToListAsync(ct);
        var primaryOwner = members.FirstOrDefault(x => x.Role == OrganisationRole.Owner)
            ?? throw new InvalidOperationException("The target workspace has no owner to receive the clean replacement.");

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = db.Database.GetDbConnection();
            await RequireCleanForeignKeysAsync(connection, null, ct);
            var tables = await ReadTablesAsync(connection, null, ct);
            var removedRows = await CountDirectOrganisationRowsAsync(connection, null, tables, target.Id, ct);

            if (!apply)
            {
                return new ReplaceOrganisationResult(
                    false,
                    OrganisationDisplay.WorkspaceId(target),
                    "preview-only",
                    OrganisationDisplay.WorkspaceId(preserved),
                    members.Count,
                    removedRows);
            }

            var protectedCounts = await CountOrganisationReferencesAsync(connection, null, tables, preserved.Id, ct);
            var protectedIdentity = IdentityFingerprint(preserved);

            await ExecuteAsync(connection, null, "PRAGMA foreign_keys = OFF;", ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var nativeTransaction = transaction.GetDbTransaction();
                await DeleteDirectOrganisationRowsAsync(connection, nativeTransaction, tables, target.Id, ct);
                await ExecuteAsync(
                    connection,
                    nativeTransaction,
                    "DELETE FROM \"Organisations\" WHERE lower(\"Id\") = lower($id);",
                    ct,
                    ("$id", target.Id.ToString()));

                if (target.OrganisationGroupId is Guid groupId)
                {
                    await ExecuteAsync(
                        connection,
                        nativeTransaction,
                        "DELETE FROM \"OrganisationGroups\" WHERE lower(\"Id\") = lower($id) " +
                        "AND NOT EXISTS (SELECT 1 FROM \"Organisations\" WHERE lower(\"OrganisationGroupId\") = lower($id));",
                        ct,
                        ("$id", groupId.ToString()));
                }

                await RemoveForeignKeyOrphansAsync(connection, nativeTransaction, tables, ct);
                db.ChangeTracker.Clear();

                var jurisdiction = IslandJurisdictions.Get(target.CountryCode);
                var replacement = new Organisation
                {
                    LegalName = target.LegalName,
                    TradingName = target.TradingName,
                    Tin = target.Tin,
                    Nzbn = target.Nzbn,
                    NzCompanyNumber = target.NzCompanyNumber,
                    Kind = target.Kind,
                    Industry = target.Industry,
                    CountryCode = jurisdiction.CountryCode,
                    BaseCurrency = jurisdiction.CurrencyCode,
                    TimeZoneId = jurisdiction.TimeZoneId,
                    TaxLabel = jurisdiction.TaxLabel,
                    FinancialYearEndMonth = jurisdiction.FinancialYearEndMonth,
                    FinancialYearEndDay = jurisdiction.FinancialYearEndDay
                };

                db.Organisations.Add(replacement);
                var structure = new EnterpriseStructureService(db).AddDefaultFor(replacement, primaryOwner.UserId);
                foreach (var member in members)
                {
                    db.OrganisationMemberships.Add(new OrganisationMembership
                    {
                        Organisation = replacement,
                        UserId = member.UserId,
                        Role = member.Role
                    });
                }
                db.LedgerAccounts.AddRange(StarterCharts.For(replacement.Id, replacement.CountryCode));
                db.AuditEvents.Add(new AuditEvent
                {
                    OrganisationId = replacement.Id,
                    UserId = primaryOwner.UserId,
                    EventType = "OrganisationRecreated",
                    EntityType = nameof(Organisation),
                    EntityId = replacement.Id.ToString(),
                    JsonData = JsonSerializer.Serialize(new
                    {
                        RemovedWorkspaceId = plan.WorkspaceId,
                        ReplacementWorkspaceId = OrganisationDisplay.WorkspaceId(replacement),
                        replacement.LegalName,
                        replacement.CountryCode,
                        replacement.BaseCurrency,
                        OrganisationGroupId = structure.Group.Id,
                        DefaultBranchId = structure.Branch.Id,
                        DefaultDivisionId = structure.Division.Id,
                        XeroConnectionReset = true
                    })
                });
                await db.SaveChangesAsync(ct);

                await ExecuteAsync(
                    connection,
                    nativeTransaction,
                    "UPDATE \"AspNetUsers\" SET \"LastSelectedOrganisationId\" = $replacement " +
                    "WHERE lower(\"LastSelectedOrganisationId\") = lower($removed);",
                    ct,
                    ("$replacement", replacement.Id.ToString()),
                    ("$removed", target.Id.ToString()));

                await RequireCleanForeignKeysAsync(connection, nativeTransaction, ct);
                var afterProtectedCounts = await CountOrganisationReferencesAsync(connection, nativeTransaction, tables, preserved.Id, ct);
                if (!protectedCounts.OrderBy(x => x.Key).SequenceEqual(afterProtectedCounts.OrderBy(x => x.Key)))
                {
                    throw new InvalidOperationException("Protected Fiji workspace references changed; replacement was rolled back.");
                }

                var preservedAfter = await db.Organisations.AsNoTracking().SingleAsync(x => x.Id == preserved.Id, ct);
                if (IdentityFingerprint(preservedAfter) != protectedIdentity)
                {
                    throw new InvalidOperationException("Protected Fiji workspace identity changed; replacement was rolled back.");
                }
                if (await db.Organisations.AnyAsync(x => x.Id == target.Id, ct))
                {
                    throw new InvalidOperationException("The old NZ workspace still exists; replacement was rolled back.");
                }
                if (replacement.CountryCode != "NZ" || replacement.BaseCurrency != "NZD")
                {
                    throw new InvalidOperationException("The replacement was not provisioned as a New Zealand company.");
                }

                await transaction.CommitAsync(ct);
                return new ReplaceOrganisationResult(
                    true,
                    OrganisationDisplay.WorkspaceId(target),
                    OrganisationDisplay.WorkspaceId(replacement),
                    OrganisationDisplay.WorkspaceId(preserved),
                    members.Count,
                    removedRows);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
            finally
            {
                await ExecuteAsync(connection, null, "PRAGMA foreign_keys = ON;", CancellationToken.None);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static void ValidatePlan(ReplaceOrganisationPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.WorkspaceId) ||
            string.IsNullOrWhiteSpace(plan.ExpectedLegalName) ||
            string.IsNullOrWhiteSpace(plan.ExpectedCountryCode) ||
            string.IsNullOrWhiteSpace(plan.PreservedWorkspaceId) ||
            string.IsNullOrWhiteSpace(plan.PreservedExpectedLegalName) ||
            string.IsNullOrWhiteSpace(plan.PreservedExpectedCountryCode))
        {
            throw new InvalidOperationException("Every replacement identity guard is required.");
        }
    }

    private static void RequireIdentity(Organisation organisation, string legalName, string countryCode, string label)
    {
        if (!organisation.LegalName.Equals(legalName.Trim(), StringComparison.Ordinal) ||
            !organisation.CountryCode.Equals(countryCode.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The {label} workspace identity does not match the reviewed plan.");
        }
    }

    private static string IdentityFingerprint(Organisation organisation) => JsonSerializer.Serialize(new
    {
        organisation.Id,
        organisation.OrganisationGroupId,
        organisation.LegalName,
        organisation.TradingName,
        organisation.Tin,
        organisation.Nzbn,
        organisation.NzCompanyNumber,
        organisation.CountryCode,
        organisation.BaseCurrency,
        organisation.TimeZoneId,
        organisation.Kind,
        organisation.Industry,
        organisation.CreatedAt
    });

    private static async Task<List<string>> ReadTablesAsync(DbConnection connection, DbTransaction? transaction, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) tables.Add(reader.GetString(0));
        return tables;
    }

    private static async Task<List<ColumnInfo>> ReadColumnsAsync(DbConnection connection, DbTransaction? transaction, string table, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info({Quote(table)});";
        var columns = new List<ColumnInfo>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            columns.Add(new ColumnInfo(reader.GetString(1), reader.GetInt64(3) != 0, reader.GetInt32(5)));
        }
        return columns;
    }

    private static async Task<List<ForeignKeyColumn>> ReadForeignKeysAsync(DbConnection connection, DbTransaction? transaction, string table, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA foreign_key_list({Quote(table)});";
        var foreignKeys = new List<ForeignKeyColumn>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            foreignKeys.Add(new ForeignKeyColumn(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(6)));
        }
        return foreignKeys;
    }

    private static async Task<Dictionary<string, long>> CountDirectOrganisationRowsAsync(
        DbConnection connection, DbTransaction? transaction, IEnumerable<string> tables, Guid organisationId, CancellationToken ct)
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            var columns = await ReadColumnsAsync(connection, transaction, table, ct);
            if (!columns.Any(x => x.Name.Equals("OrganisationId", StringComparison.OrdinalIgnoreCase))) continue;
            counts[table] = await ScalarLongAsync(connection, transaction,
                $"SELECT COUNT(*) FROM {Quote(table)} WHERE lower(\"OrganisationId\") = lower($id);", ct,
                ("$id", organisationId.ToString()));
        }
        return counts;
    }

    private static async Task<Dictionary<string, long>> CountOrganisationReferencesAsync(
        DbConnection connection, DbTransaction? transaction, IEnumerable<string> tables, Guid organisationId, CancellationToken ct)
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            var columns = await ReadColumnsAsync(connection, transaction, table, ct);
            foreach (var column in columns.Where(x => x.Name.EndsWith("OrganisationId", StringComparison.OrdinalIgnoreCase)))
            {
                counts[$"{table}.{column.Name}"] = await ScalarLongAsync(connection, transaction,
                    $"SELECT COUNT(*) FROM {Quote(table)} WHERE lower({Quote(column.Name)}) = lower($id);", ct,
                    ("$id", organisationId.ToString()));
            }
        }
        return counts;
    }

    private static async Task DeleteDirectOrganisationRowsAsync(
        DbConnection connection, DbTransaction transaction, IEnumerable<string> tables, Guid organisationId, CancellationToken ct)
    {
        foreach (var table in tables.Where(x => !x.Equals("Organisations", StringComparison.OrdinalIgnoreCase)))
        {
            var columns = await ReadColumnsAsync(connection, transaction, table, ct);
            if (!columns.Any(x => x.Name.Equals("OrganisationId", StringComparison.OrdinalIgnoreCase))) continue;
            await ExecuteAsync(connection, transaction,
                $"DELETE FROM {Quote(table)} WHERE lower(\"OrganisationId\") = lower($id);", ct,
                ("$id", organisationId.ToString()));
        }
    }

    private static async Task RemoveForeignKeyOrphansAsync(
        DbConnection connection, DbTransaction transaction, IReadOnlyList<string> tables, CancellationToken ct)
    {
        for (var pass = 0; pass < tables.Count + 2; pass++)
        {
            long changed = 0;
            foreach (var table in tables.Where(x => !x.Equals("AspNetUsers", StringComparison.OrdinalIgnoreCase)))
            {
                var childColumns = await ReadColumnsAsync(connection, transaction, table, ct);
                var foreignKeys = await ReadForeignKeysAsync(connection, transaction, table, ct);
                foreach (var group in foreignKeys.GroupBy(x => x.Id))
                {
                    var parts = group.OrderBy(x => x.Sequence).ToArray();
                    var parentColumns = await ReadColumnsAsync(connection, transaction, parts[0].ParentTable, ct);
                    var pairs = parts.Select(part => new
                    {
                        Child = part.ChildColumn,
                        Parent = part.ParentColumn ?? parentColumns.Single(x => x.PrimaryKeyOrder > 0).Name
                    }).ToArray();
                    var present = string.Join(" AND ", pairs.Select(x => $"child.{Quote(x.Child)} IS NOT NULL"));
                    var match = string.Join(" AND ", pairs.Select(x => $"parent.{Quote(x.Parent)} = child.{Quote(x.Child)}"));
                    var orphan = $"{present} AND NOT EXISTS (SELECT 1 FROM {Quote(parts[0].ParentTable)} parent WHERE {match})";
                    var canSetNull = parts[0].OnDelete.Equals("SET NULL", StringComparison.OrdinalIgnoreCase) &&
                        pairs.All(pair => !childColumns.Single(x => x.Name == pair.Child).NotNull);
                    var sql = canSetNull
                        ? $"UPDATE {Quote(table)} AS child SET {string.Join(", ", pairs.Select(x => $"{Quote(x.Child)} = NULL"))} WHERE {orphan};"
                        : $"DELETE FROM {Quote(table)} AS child WHERE {orphan};";
                    changed += await ExecuteAsync(connection, transaction, sql, ct);
                }
            }
            if (changed == 0) break;
        }
    }

    private static async Task RequireCleanForeignKeysAsync(DbConnection connection, DbTransaction? transaction, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA foreign_key_check;";
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            throw new InvalidOperationException(
                $"Database foreign-key verification failed for table {reader.GetString(0)}.");
        }
    }

    private static async Task<long> ScalarLongAsync(
        DbConnection connection, DbTransaction? transaction, string sql, CancellationToken ct,
        params (string Name, object Value)[] parameters)
    {
        await using var command = CreateCommand(connection, transaction, sql, parameters);
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    }

    private static async Task<int> ExecuteAsync(
        DbConnection connection, DbTransaction? transaction, string sql, CancellationToken ct,
        params (string Name, object Value)[] parameters)
    {
        await using var command = CreateCommand(connection, transaction, sql, parameters);
        return await command.ExecuteNonQueryAsync(ct);
    }

    private static DbCommand CreateCommand(
        DbConnection connection, DbTransaction? transaction, string sql,
        IEnumerable<(string Name, object Value)> parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
        return command;
    }

    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";
}
