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
/// Objects are read by the layout declared for the class their id names, whether they
/// sit in a list or in a field of their own, so nested layouts are checked as well. A
/// declared type this does not know fails the test rather than being skipped, so a new
/// layout cannot go unchecked.
/// </para>
/// <para>
/// A few fields are declared only as an object, with no layout of their own: a value
/// written inline, with no id. Their layouts are given in <see cref="InlineObjects"/>,
/// read from the client's own writer, and each of their parts is paired with the next
/// constructor parameter. Computed fields, which the contract gives only as an
/// expression, are read by the layouts in <see cref="ComputedParts"/>, from the client's
/// own writer too.
/// </para>
/// <para>
/// Some classes have no usable declaration at all: the contract lists none of their
/// fields, or lists them only as bytes it computes, because their layout depends on
/// their own values. Those are read by <see cref="ClientLayouts"/>, written from the
/// client's own reader and apart from the codecs, and each part read is compared with
/// the decoded object's property of the name it is read for, since their records do not
/// follow the wire field for field.
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

    // The parts some computed fields are written as, by the expression the contract
    // gives them; each is paired with the parts of the next constructor parameter.
    private static readonly Dictionary<string, string[]> ComputedParts = new(StringComparer.Ordinal)
    {
        // The client's report on its own code, at the end of a room request.
        ["encode(server_connection_info)"] = ["u32", "u32", "u8", "u32", "u32", "u32"],
    };

    // The computed fields that are an object with its own id, read by that object's layout.
    private static readonly HashSet<string> ComputedObjects = new(StringComparer.Ordinal)
    {
        "encode(game_session_info)",
    };

    // The classes with no usable declaration, by wire name, read as the client reads them.
    private static readonly Dictionary<string, Action<Walk>> ClientLayouts = new(StringComparer.Ordinal)
    {
        ["GameSessionInfo"] = RoomRecord,
        ["GameSessionInfoPack"] = RoomListEntry,
        ["GameSlotInfo"] = RoomSlot,
        ["CharacterAppear"] = Appearance,
    };

    /// <summary>Every place where <paramref name="decoded"/> disagrees with the declared layout of <paramref name="body"/>.</summary>
    /// <param name="body">The body, its packet's id first.</param>
    /// <param name="decoded">What a codec decoded from it.</param>
    /// <param name="classes">Every class the contract names, by registry id, whether or not it declares its fields.</param>
    public static IReadOnlyList<string> Mismatches(
        byte[] body,
        object decoded,
        IReadOnlyDictionary<uint, ClassSpec> classes)
    {
        var failures = new List<string>();
        var cursor = new Cursor(body);
        CompareObject(cursor, decoded, classes, decoded.GetType().Name, failures);

        if (cursor.Offset != body.Length)
        {
            failures.Add($"{decoded.GetType().Name}: the declared layout ends {body.Length - cursor.Offset} bytes before the body");
        }

        return failures;
    }

    private static void CompareObject(
        Cursor cursor,
        object decoded,
        IReadOnlyDictionary<uint, ClassSpec> classes,
        string path,
        List<string> failures)
    {
        var start = cursor.Offset;
        var id = cursor.UInt32();
        var spec = classes.GetValueOrDefault(id)
            ?? throw new InvalidOperationException($"{path}: the object at byte {start} names a class the contract does not know");

        if (ClientLayouts.TryGetValue(spec.WireName, out var layout))
        {
            layout(new Walk(cursor, decoded, path, classes, failures));
            return;
        }

        if (spec.Fields.Count == 0)
        {
            throw new InvalidOperationException($"{path}: no layout is declared for the object at byte {start}");
        }

        var parameters = Positional(decoded);
        var position = 0;
        uint? computedCount = null;

        foreach (var field in spec.Fields.Where(field => field.Name != "registry_id"))
        {
            if (field.Source == "computed" && field.Type != "bytes")
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

                throw new InvalidOperationException($"{path}: {decoded.GetType().Name} has no parameter for '{field.Name ?? field.Expr}'");
            }

            var (name, value) = parameters[position++];
            var where = $"{path}.{name}";

            switch (field.Type)
            {
                case "bytes" when field.Expr is { } expr && ComputedObjects.Contains(expr):
                    CompareObject(cursor, value ?? throw new InvalidOperationException($"{where} is null"), classes, where, failures);
                    break;

                case "bytes":
                    var computed = ComputedParts.GetValueOrDefault(field.Expr ?? string.Empty)
                        ?? throw new InvalidOperationException($"{path}: no layout is known for the computed '{field.Expr}'");
                    var computedParts = Positional(value ?? throw new InvalidOperationException($"{where} is null"));
                    if (computedParts.Count != computed.Length)
                    {
                        failures.Add($"{where}: the layout has {computed.Length} parts, the codec {computedParts.Count}");
                        return;
                    }

                    for (var index = 0; index < computed.Length; index++)
                    {
                        Expect($"{where}.{computedParts[index].Name}", cursor.Number(computed[index]), AsNumber(computedParts[index].Value), failures);
                    }

                    break;

                case "object" when !InlineObjects.ContainsKey((spec.WireName, field.Name!)):
                    // An object with an id of its own, read by its own class's layout.
                    CompareObject(cursor, value ?? throw new InvalidOperationException($"{where} is null"), classes, where, failures);
                    break;

                case "object":
                    var parts = InlineObjects[(spec.WireName, field.Name!)];
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
                        CompareObject(cursor, elements[index]!, classes, $"{where}[{index}]", failures);
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

    // A room record. A status of 0 means no room, and nothing follows it.
    private static void RoomRecord(Walk room)
    {
        room.Number("u16", "RoomNumber");
        if (room.Number("u8", "Status") == 0)
        {
            return;
        }

        room.Number("u8", "Grade");
        room.Text("Name");
        room.Number("u8", "GameType");
        room.Number("u16", "GameMode");
        room.Number("u8", "ChanceMode");
        room.Number("u8", "CurrentPlayers");
        room.Number("u8", "MaxPlayers");
        Password(room);
        room.Number("u16", "MapId");
    }

    // One entry of the lobby's room list: a mask, the room number, then the fields the
    // mask names, in the list's own order, which is not the record's.
    private static void RoomListEntry(Walk entry)
    {
        var mask = entry.Unpaired("u16");
        entry.Number("u16", "RoomNumber");
        if ((mask & 0x8000) != 0)
        {
            entry.Absent("Room");
            return;
        }

        entry.Into("Room", room =>
        {
            if ((mask & 0x001) != 0)
            {
                room.Number("u8", "Status");
            }

            if ((mask & 0x200) != 0)
            {
                room.Number("u8", "Grade");
            }

            if ((mask & 0x002) != 0)
            {
                room.Text("Name");
            }

            if ((mask & 0x100) != 0)
            {
                room.Number("u8", "GameType");
            }

            if ((mask & 0x004) != 0)
            {
                room.Number("u16", "GameMode");
            }

            if ((mask & 0x080) != 0)
            {
                room.Number("u8", "ChanceMode");
            }

            if ((mask & 0x008) != 0)
            {
                room.Number("u8", "CurrentPlayers");
            }

            if ((mask & 0x010) != 0)
            {
                room.Number("u8", "MaxPlayers");
            }

            if ((mask & 0x040) != 0)
            {
                room.Number("u16", "MapId");
            }

            if ((mask & 0x020) != 0)
            {
                Password(room);
            }
        });
    }

    // A flag, and the password only when it is set.
    private static void Password(Walk room)
    {
        if (room.Unpaired("u8") != 0)
        {
            room.Text("Password");
        }
        else
        {
            room.Absent("Password");
        }
    }

    // A room's slot. Only an occupied one carries more than its index and status: the
    // character, then its appearance and its item options, each behind a presence flag.
    private static void RoomSlot(Walk slot)
    {
        slot.Number("u8", "SlotIndex");
        if (slot.Number("u8", "Status") <= 1)
        {
            slot.Absent("Occupant");
            return;
        }

        slot.Into("Occupant", occupant =>
        {
            occupant.Number("u32", "PlayerId");
            occupant.Number("u32", "CharacterId");
            occupant.Text("Name");
            occupant.Text("GuildName");
            occupant.Number("u16", "Level");
            occupant.Number("u8", "Team");
            if (occupant.Unpaired("u8") != 0)
            {
                occupant.Object("Appearance");
            }
            else
            {
                occupant.Absent("Appearance");
            }

            occupant.Number("u16", "WeaponId");
            if (occupant.Unpaired("u8") != 0)
            {
                // The codecs keep only empty option lists, so there is nothing to pair.
                occupant.EmptyObjectList("OptionList");
            }
        });
    }

    // A character's appearance: three fields, sixteen parts, and a presence flag for an
    // object the codecs never carry.
    private static void Appearance(Walk appearance)
    {
        appearance.Number("u8", "Gender");
        appearance.Number("u32", "Field08");
        appearance.Number("u16", "Field0C");
        appearance.Each("Slots", 16, part =>
        {
            part.Number("u32", "ItemId");
            part.Number("u16", "Variant1");
            part.Number("u16", "Variant2");
            part.Number("u16", "Variant3");
        });

        if (appearance.Unpaired("u8") != 0)
        {
            appearance.Fail("carries an object after its parts, which no codec reads");
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
        float single => BitConverter.SingleToUInt32Bits(single),
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

    /// <summary>
    /// Reads an object by a client layout, part by part, comparing each part with the
    /// decoded object's property of the name it is read for.
    /// </summary>
    private sealed class Walk(
        Cursor cursor,
        object decoded,
        string path,
        IReadOnlyDictionary<uint, ClassSpec> classes,
        List<string> failures)
    {
        // Reads a number and compares it with the property; gives back what was read.
        public ulong Number(string type, string property)
        {
            var read = cursor.Number(type);
            Expect($"{path}.{property}", read, AsNumber(Property(property)), failures);
            return read;
        }

        // Reads a number the decoded object keeps no property for, such as a mask or a flag.
        public ulong Unpaired(string type) => cursor.Number(type);

        public void Text(string property) => Expect($"{path}.{property}", cursor.Utf16String(), Property(property) as string, failures);

        // An object with an id of its own, read by its own class's layout.
        public void Object(string property) =>
            CompareObject(cursor, Property(property) ?? Missing(property), classes, $"{path}.{property}", failures);

        // A part the layout says is not on the wire, so the decoded object must not have it.
        public void Absent(string property)
        {
            if (Property(property) is not null)
            {
                failures.Add($"{path}.{property} is decoded where the layout puts nothing");
            }
        }

        // Parts that belong to an object the decoded record holds as a property.
        public void Into(string property, Action<Walk> parts) =>
            parts(new Walk(cursor, Property(property) ?? Missing(property), $"{path}.{property}", classes, failures));

        // A fixed number of elements, each paired with the property's elements in order.
        public void Each(string property, int count, Action<Walk> parts)
        {
            var elements = Elements(Property(property)).ToArray();
            if (elements.Length != count)
            {
                failures.Add($"{path}.{property}: the layout has {count} elements, the codec {elements.Length}");
                return;
            }

            for (var index = 0; index < count; index++)
            {
                parts(new Walk(cursor, elements[index]!, $"{path}.{property}[{index}]", classes, failures));
            }
        }

        // An object of the named class whose only list is empty.
        public void EmptyObjectList(string wireName)
        {
            var id = cursor.UInt32();
            if (classes.GetValueOrDefault(id)?.WireName != wireName)
            {
                failures.Add($"{path}: expected {wireName} here");
                return;
            }

            if (cursor.UInt32() != 0)
            {
                failures.Add($"{path}: {wireName} holds elements no codec keeps");
            }
        }

        public void Fail(string what) => failures.Add($"{path} {what}");

        private object? Property(string name) =>
            (decoded.GetType().GetProperty(name)
             ?? throw new InvalidOperationException($"{path}: {decoded.GetType().Name} has no property '{name}'")).GetValue(decoded);

        private object Missing(string property) =>
            throw new InvalidOperationException($"{path}.{property} is null where the layout puts an object");
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
                "u32" or "i32" or "f32" => 4,
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
