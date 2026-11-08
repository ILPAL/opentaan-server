using System.Buffers.Binary;
using System.Collections;
using System.Globalization;
using System.Text;

namespace OpenTaan.Tests.Conformance;

/// <summary>
/// Reads a body by the layout the contract declares for it, without any codec, and
/// compares each field with what a codec decoded from the same body.
/// </summary>
/// <remarks>
/// <para>
/// Fields are paired with the decoded record's constructor parameters by position:
/// a codec's record lists its fields in wire order. Pairing by position rather than by
/// name lets a codec name a field for what it is, and still catches a field read and
/// written in the same wrong place, because the value found there is then another
/// field's.
/// </para>
/// <para>
/// Objects in a list are read by the layout declared for the class their id names,
/// so nested layouts are checked as well. A declared type this does not know fails the
/// test rather than being skipped, so a new layout cannot go unchecked.
/// </para>
/// <para>
/// A few fields are declared only as an object, with no layout of their own: a value
/// written inline, with no id. Their layouts are given in <see cref="InlineObjects"/>,
/// read from the client's own writer, and each of their parts is paired with the next
/// constructor parameter.
/// </para>
/// </remarks>
internal static class DeclaredLayout
{
    // The parts of each inline object, by the wire name of the class that holds it and
    // the field's name.
    private static readonly Dictionary<(string Class, string Field), string[]> InlineObjects = new()
    {
        [("PcGamePlay_SetGhostPos", "position")] = ["i32", "i32"],
    };

    /// <summary>Every place where <paramref name="decoded"/> disagrees with the declared layout of <paramref name="body"/>.</summary>
    public static IReadOnlyList<string> Mismatches(
        byte[] body,
        object decoded,
        IReadOnlyDictionary<uint, ClassSpec> declared)
    {
        var failures = new List<string>();
        var cursor = new Cursor(body);
        CompareObject(cursor, decoded, declared, decoded.GetType().Name, failures);

        if (cursor.Offset != body.Length)
        {
            failures.Add($"{decoded.GetType().Name}: the declared layout ends {body.Length - cursor.Offset} bytes before the body");
        }

        return failures;
    }

    private static void CompareObject(
        Cursor cursor,
        object decoded,
        IReadOnlyDictionary<uint, ClassSpec> declared,
        string path,
        List<string> failures)
    {
        var start = cursor.Offset;
        var id = cursor.UInt32();
        var spec = declared.GetValueOrDefault(id)
            ?? throw new InvalidOperationException($"{path}: no layout is declared for the object at byte {start}");

        var parameters = Positional(decoded);
        var position = 0;
        uint? computedCount = null;

        foreach (var field in spec.Fields.Where(field => field.Name != "registry_id"))
        {
            if (field.Source == "computed")
            {
                computedCount = (uint)cursor.Number(field.Type);
                continue;
            }

            if (position == parameters.Count)
            {
                // A codec that supports a list only while it is empty has no parameter for it.
                if (IsList(field.Type) && cursor.Count(field.Type, computedCount) == 0)
                {
                    continue;
                }

                throw new InvalidOperationException($"{path}: {decoded.GetType().Name} has no parameter for '{field.Name}'");
            }

            var (name, value) = parameters[position++];
            var where = $"{path}.{name}";

            switch (field.Type)
            {
                case "object":
                    var parts = InlineObjects.GetValueOrDefault((spec.WireName, field.Name!))
                        ?? throw new InvalidOperationException($"{path}: '{field.Name}' is an object with no declared layout");
                    Expect(where, cursor.Number(parts[0]), AsNumber(value), failures);
                    foreach (var part in parts.Skip(1))
                    {
                        if (position == parameters.Count)
                        {
                            throw new InvalidOperationException($"{path}: {decoded.GetType().Name} has no parameter for part of '{field.Name}'");
                        }

                        var (partName, partValue) = parameters[position++];
                        Expect($"{path}.{partName}", cursor.Number(part), AsNumber(partValue), failures);
                    }

                    break;

                case "str_u32":
                    Expect(where, cursor.Utf16String(), value as string, failures);
                    break;

                case "u8_list_u32" or "u16_list_u32" or "u32_list_u32":
                    var elementType = field.Type[..field.Type.IndexOf('_', StringComparison.Ordinal)];
                    var numbers = Enumerable.Range(0, (int)cursor.UInt32()).Select(_ => cursor.Number(elementType)).ToArray();
                    Expect(where, numbers, Elements(value).Select(AsNumber).ToArray(), failures);
                    break;

                case "object_list_u32" or "object_list":
                    var count = cursor.Count(field.Type, computedCount);
                    var elements = Elements(value).ToArray();
                    if (elements.Length != count)
                    {
                        failures.Add($"{where}: declared {count} objects, decoded {elements.Length}");
                        return;
                    }

                    for (var index = 0; index < elements.Length; index++)
                    {
                        CompareObject(cursor, elements[index]!, declared, $"{where}[{index}]", failures);
                    }

                    break;

                default:
                    Expect(where, cursor.Number(field.Type), AsNumber(value), failures);
                    break;
            }
        }

        if (position != parameters.Count)
        {
            failures.Add($"{path}: the declared layout has no field for {parameters[position].Name}");
        }
    }

    // The constructor's parameters, in order, with the values of the properties they set.
    private static List<(string Name, object? Value)> Positional(object decoded)
    {
        var type = decoded.GetType();
        var constructor = type.GetConstructors().MaxBy(candidate => candidate.GetParameters().Length)
            ?? throw new InvalidOperationException($"{type.Name} has no public constructor");

        return
        [
            .. constructor.GetParameters().Select(parameter =>
            {
                var property = type.GetProperty(parameter.Name!)
                    ?? throw new InvalidOperationException($"{type.Name} has no property for parameter '{parameter.Name}'");
                return (property.Name, property.GetValue(decoded));
            }),
        ];
    }

    private static bool IsList(string type) => type.Contains("list", StringComparison.Ordinal);

    private static IEnumerable<object?> Elements(object? value) =>
        value is IEnumerable elements ? elements.Cast<object?>() : throw new InvalidOperationException("expected a list");

    private static ulong AsNumber(object? value) => value switch
    {
        bool flag => flag ? 1ul : 0ul,
        int signed => unchecked((uint)signed),
        Enum member => Convert.ToUInt64(member, CultureInfo.InvariantCulture),
        _ => Convert.ToUInt64(value, CultureInfo.InvariantCulture),
    };

    private static void Expect<T>(string where, T declared, T decoded, List<string> failures)
    {
        if (!EqualityComparer<T>.Default.Equals(declared, decoded))
        {
            failures.Add($"{where} is not what the layout puts there");
        }
    }

    private static void Expect(string where, ulong[] declared, ulong[] decoded, List<string> failures)
    {
        if (!declared.SequenceEqual(decoded))
        {
            failures.Add($"{where} is not what the layout puts there");
        }
    }

    private sealed class Cursor(byte[] body)
    {
        public int Offset { get; private set; }

        public uint UInt32() => (uint)Number("u32");

        public ulong Number(string type)
        {
            var width = type switch
            {
                "u8" => 1,
                "u16" => 2,
                "u32" or "i32" => 4,
                "u64" => 8,
                _ => throw new InvalidOperationException($"'{type}' is not read here yet"),
            };

            var bytes = Take(width);
            return width switch
            {
                1 => bytes[0],
                2 => BinaryPrimitives.ReadUInt16LittleEndian(bytes),
                4 => BinaryPrimitives.ReadUInt32LittleEndian(bytes),
                _ => BinaryPrimitives.ReadUInt64LittleEndian(bytes),
            };
        }

        public string Utf16String() => Encoding.Unicode.GetString(Take((int)UInt32() * sizeof(char)));

        // A list's count: its own prefix, or the computed field before it.
        public uint Count(string type, uint? computed) =>
            type.EndsWith("_u32", StringComparison.Ordinal)
                ? UInt32()
                : computed ?? throw new InvalidOperationException($"a '{type}' needs a computed count before it");

        private ReadOnlySpan<byte> Take(int length)
        {
            var bytes = body.AsSpan(Offset, length);
            Offset += length;
            return bytes;
        }
    }
}
