using Dapper;
using Microsoft.Data.Sqlite;
using Strider.Core.Abstractions;
using Strider.Core.Domain;

namespace Strider.Infrastructure.Persistence;

/// <summary>SQLite implementation of <see cref="IFlagRuleStore"/> (table <c>flag_rules</c>).</summary>
public class SqliteFlagRuleStore : IFlagRuleStore
{
    private readonly string _connectionString;

    public SqliteFlagRuleStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    private const string SelectColumns =
        "id, name, field, operator, pattern, color, priority, is_enabled, created_at";

    public async Task<IReadOnlyList<FlagRule>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(ct);

        var rows = await db.QueryAsync(new CommandDefinition(
            $"SELECT {SelectColumns} FROM flag_rules ORDER BY is_enabled DESC, priority, created_at",
            cancellationToken: ct));

        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<FlagRule>> GetEnabledAsync(CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(ct);

        var rows = await db.QueryAsync(new CommandDefinition(
            $"SELECT {SelectColumns} FROM flag_rules WHERE is_enabled = 1 ORDER BY priority, created_at",
            cancellationToken: ct));

        return rows.Select(Map).ToList();
    }

    public async Task SaveAsync(FlagRule rule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);

        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(ct);

        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO flag_rules (id, name, field, operator, pattern, color, priority, is_enabled, created_at)
            VALUES (@Id, @Name, @Field, @Operator, @Pattern, @Color, @Priority, @IsEnabled, @CreatedAt)
            ON CONFLICT(id) DO UPDATE SET
                name=@Name, field=@Field, operator=@Operator, pattern=@Pattern,
                color=@Color, priority=@Priority, is_enabled=@IsEnabled
            """,
            new
            {
                Id = rule.Id.ToString(),
                Name = rule.Name,
                Field = rule.Field.ToString().ToLowerInvariant(),
                Operator = rule.Operator.ToString().ToLowerInvariant(),
                Pattern = rule.Pattern,
                Color = rule.Color.ToString().ToLowerInvariant(),
                Priority = rule.Priority,
                IsEnabled = rule.IsEnabled ? 1 : 0,
                CreatedAt = new DateTimeOffset(rule.CreatedAt.ToUniversalTime()).ToUnixTimeSeconds(),
            },
            cancellationToken: ct));
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(ct);

        await db.ExecuteAsync(new CommandDefinition(
            "DELETE FROM flag_rules WHERE id = @Id", new { Id = id.ToString() }, cancellationToken: ct));
    }

    public async Task ReorderAsync(IReadOnlyList<Guid> orderedIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(orderedIds);

        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await db.BeginTransactionAsync(ct);

        try
        {
            for (var i = 0; i < orderedIds.Count; i++)
            {
                await db.ExecuteAsync(new CommandDefinition(
                    "UPDATE flag_rules SET priority = @Priority WHERE id = @Id",
                    new { Priority = i, Id = orderedIds[i].ToString() },
                    transaction: tx, cancellationToken: ct));
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private static FlagRule Map(dynamic row) => new()
    {
        Id = Guid.Parse((string)row.id),
        Name = (string)row.name,
        Field = Enum.TryParse<FlagRuleField>((string)row.field, true, out var field) ? field : FlagRuleField.Sender,
        Operator = Enum.TryParse<FlagRuleOperator>((string)row.@operator, true, out var op)
            ? op
            : FlagRuleOperator.Contains,
        Pattern = (string)row.pattern,
        Color = FlagColors.Parse((string?)row.color),
        Priority = (int)(long)row.priority,
        IsEnabled = (long)row.is_enabled == 1,
        CreatedAt = DateTimeOffset.FromUnixTimeSeconds((long)row.created_at).UtcDateTime,
    };
}
