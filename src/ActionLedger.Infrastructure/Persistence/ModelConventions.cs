using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ActionLedger.Infrastructure.Persistence;

/// <summary>
/// The model-wide rules from AD-10 and the Consistency Conventions, applied once over the whole
/// model rather than repeated in every entity configuration.
/// </summary>
/// <remarks>
/// <para>
/// Applying these as a sweep is deliberate: a configuration added later for a new aggregate
/// cannot forget them, and there is no package to keep in step with EF Core — the spine calls for
/// "a naming convention in <c>AppDbContext</c>", not a naming-convention dependency.
/// </para>
/// <para>
/// The sweep runs <em>after</em> <c>ApplyConfigurationsFromAssembly</c>, so an explicit
/// <c>HasColumnType</c> or <c>HasColumnName</c> in a configuration is not overwritten: the sweep
/// only fills in what a configuration left at its default.
/// </para>
/// </remarks>
internal static class ModelConventions
{
    /// <summary>The PostgreSQL type every instant maps to (AD-10).</summary>
    public const string InstantColumnType = "timestamptz";

    /// <summary>The PostgreSQL type every calendar date maps to (AD-10).</summary>
    public const string DateColumnType = "date";

    /// <summary>
    /// Applies snake_case naming, string enums, <c>ValueGeneratedNever</c> on every
    /// <see cref="Guid"/> key, and the instant and date column types.
    /// </summary>
    public static void Apply(ModelBuilder modelBuilder)
    {
        foreach (IMutableEntityType entity in modelBuilder.Model.GetEntityTypes())
        {
            ApplyTableName(entity);

            foreach (IMutableProperty property in entity.GetProperties())
            {
                ApplyColumnName(entity, property);
                ApplyStringEnum(property);
                ApplyNeverGeneratedGuid(property);
                ApplyTemporalColumnType(property);
            }

            foreach (IMutableKey key in entity.GetKeys())
            {
                key.SetName(SnakeCaseOrNull(key.GetName()));
            }

            foreach (IMutableForeignKey foreignKey in entity.GetForeignKeys())
            {
                foreignKey.SetConstraintName(SnakeCaseOrNull(foreignKey.GetConstraintName()));
            }

            foreach (IMutableIndex index in entity.GetIndexes())
            {
                index.SetDatabaseName(SnakeCaseOrNull(index.GetDatabaseName()));
            }
        }
    }

    /// <summary>
    /// Converts a PascalCase or camelCase identifier to snake_case: <c>MeetingNotes</c> becomes
    /// <c>meeting_notes</c>, <c>SHA256</c> becomes <c>sha256</c>.
    /// </summary>
    internal static string SnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        StringBuilder snake = new(name.Length + 8);

        for (int index = 0; index < name.Length; index++)
        {
            char current = name[index];

            if (current == '_')
            {
                snake.Append('_');
                continue;
            }

            bool boundary = index > 0
                && char.IsUpper(current)
                && (!char.IsUpper(name[index - 1])
                    || (index + 1 < name.Length && char.IsLower(name[index + 1])));

            if (boundary && snake.Length > 0 && snake[^1] != '_')
            {
                snake.Append('_');
            }

            snake.Append(char.ToLowerInvariant(current));
        }

        return snake.ToString();
    }

    private static string? SnakeCaseOrNull(string? name) => name is null ? null : SnakeCase(name);

    /// <summary>
    /// Tables are snake_case plural. EF's default table name is the <c>DbSet</c> property name,
    /// which <c>AppDbContext</c> declares in the plural, so the convention is the case change:
    /// <c>Users</c> becomes <c>users</c>, and a later <c>MeetingNotes</c> becomes
    /// <c>meeting_notes</c>. Entities stay singular PascalCase.
    /// </summary>
    private static void ApplyTableName(IMutableEntityType entity)
    {
        if (entity.GetTableName() is { } table)
        {
            entity.SetTableName(SnakeCase(table));
        }
    }

    private static void ApplyColumnName(IMutableEntityType entity, IMutableProperty property)
    {
        StoreObjectIdentifier? store = StoreObjectIdentifier.Create(entity, StoreObjectType.Table);

        // Only rename a column the configuration left at its default. An explicit HasColumnName
        // wins, so a column that has to carry a specific name still can.
        string? current = store is { } table
            ? property.GetColumnName(table)
            : property.GetDefaultColumnName();

        if (string.Equals(current, property.Name, StringComparison.Ordinal))
        {
            property.SetColumnName(SnakeCase(property.Name));
        }
    }

    /// <summary>Enums are stored as strings, each behind a value converter (AD-10, Enums row).</summary>
    private static void ApplyStringEnum(IMutableProperty property)
    {
        Type clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

        if (!clrType.IsEnum || property.GetValueConverter() is not null)
        {
            return;
        }

        Type converterType = typeof(EnumToStringConverter<>).MakeGenericType(clrType);

        property.SetValueConverter((ValueConverter)Activator.CreateInstance(converterType)!);
    }

    /// <summary>
    /// AD-10 — ids are UUIDv7 from the Domain constructor. EF generates nothing, or the id the
    /// aggregate already put in its domain events would not be the id in the row.
    /// </summary>
    private static void ApplyNeverGeneratedGuid(IMutableProperty property)
    {
        if ((Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) == typeof(Guid))
        {
            property.ValueGenerated = ValueGenerated.Never;
        }
    }

    private static void ApplyTemporalColumnType(IMutableProperty property)
    {
        if (property.GetColumnType() is not null)
        {
            return;
        }

        Type clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

        if (clrType == typeof(DateTimeOffset))
        {
            property.SetColumnType(InstantColumnType);
        }
        else if (clrType == typeof(DateOnly))
        {
            property.SetColumnType(DateColumnType);
        }
    }
}
