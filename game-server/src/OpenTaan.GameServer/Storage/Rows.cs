using Npgsql;

namespace OpenTaan.GameServer.Storage;

/// <summary>Reading and writing the values of rows in the shape this server holds them.</summary>
internal static class Rows
{
    /// <summary>A parameter for the next placeholder, <c>$1</c>, <c>$2</c> and on, in order.</summary>
    public static NpgsqlCommand With(this NpgsqlCommand command, object value)
    {
        command.Parameters.Add(new NpgsqlParameter { Value = value });
        return command;
    }

    /// <summary>
    /// A 32-bit unsigned value, as the client is sent it, from a <c>BIGINT</c> column.
    /// The schema checks every such column fits, so a value that does not is a row no
    /// migration allowed.
    /// </summary>
    public static uint Unsigned(this NpgsqlDataReader reader, int column)
    {
        var value = reader.GetInt64(column);
        return value is >= 0 and <= uint.MaxValue
            ? (uint)value
            : throw new InvalidDataException($"Column {reader.GetName(column)} holds {value}, which no field of the client can carry");
    }
}
