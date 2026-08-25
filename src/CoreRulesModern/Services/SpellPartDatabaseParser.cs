using System.IO;
using System.Text;
using CoreRulesModern.Models;

namespace CoreRulesModern.Services;

/// <summary>
/// Reads the spell collections embedded in the Core Rules PartsU.dat database.
/// CPart records are heterogeneous; spell collections are identified by their
/// embedded CSpellsOb archive rather than treating every part as a school.
/// </summary>
public sealed class SpellPartDatabaseParser
{
    private const ushort PartClassReference = 0x8001;
    private const ushort NewClassTag = 0xffff;
    private const int MaximumEmbeddedSpells = 10_000;
    private static readonly Encoding Windows1252;

    static SpellPartDatabaseParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Windows1252 = Encoding.GetEncoding(1252);
    }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Parse(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bytes = File.ReadAllBytes(Path.GetFullPath(path));
        var parts = FindPartRecords(bytes);
        var collections = new Dictionary<string, IReadOnlyList<string>>(StringComparer.CurrentCultureIgnoreCase);
        ushort? spellClassReference = null;

        for (var partIndex = 0; partIndex < parts.Count; partIndex++)
        {
            var part = parts[partIndex];
            var recordEnd = partIndex + 1 < parts.Count ? parts[partIndex + 1].Start : bytes.Length;
            var array = FindSpellArray(bytes, part.ContentStart, recordEnd, spellClassReference);
            if (array is null) continue;

            spellClassReference ??= array.Value.ClassReference;
            collections[part.Name] = array.Value.Spells
                .Select(spell => spell.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }

        return collections;
    }

    private static EmbeddedArray? FindSpellArray(
        byte[] bytes,
        int start,
        int end,
        ushort? knownClassReference)
    {
        for (var offset = start; offset + 4 < end; offset++)
        {
            var count = BitConverter.ToUInt16(bytes, offset);
            if (count is 0 or > MaximumEmbeddedSpells) continue;

            var tag = BitConverter.ToUInt16(bytes, offset + sizeof(ushort));
            if (tag != NewClassTag &&
                ((tag & 0x8000) == 0 || (knownClassReference is not null && tag != knownClassReference))) continue;

            try
            {
                using var stream = new MemoryStream(bytes, writable: false);
                stream.Position = offset;
                using var reader = new BinaryReader(stream, Windows1252, leaveOpen: true);
                var candidateReference = tag == NewClassTag ? knownClassReference : tag;
                var spells = SpellDatabaseParser.ParseEmbeddedArray(reader, SpellDatabaseKind.User, candidateReference);
                if (stream.Position > end) continue;

                return new EmbeddedArray(spells, tag == NewClassTag ? knownClassReference : tag);
            }
            catch (Exception exception) when (exception is SpellDatabaseFormatException or EndOfStreamException)
            {
                // Continue looking: most CPart fields are not object arrays.
            }
        }

        return null;
    }

    private static List<PartRecord> FindPartRecords(byte[] bytes)
    {
        var records = new List<PartRecord>();
        for (var offset = 0; offset + 4 < bytes.Length; offset++)
        {
            if (BitConverter.ToUInt16(bytes, offset) != PartClassReference) continue;
            var length = bytes[offset + sizeof(ushort)];
            if (length is < 2 or byte.MaxValue || offset + 3 + length >= bytes.Length) continue;

            var nameBytes = bytes.AsSpan(offset + 3, length);
            if (!IsDisplayText(nameBytes)) continue;

            var duplicate = FindDuplicate(bytes, nameBytes, offset + 3 + length, Math.Min(bytes.Length, offset + 4096));
            if (duplicate < 0) continue;

            records.Add(new PartRecord(offset, Windows1252.GetString(nameBytes).Trim(), duplicate + 1 + length));
            offset = duplicate + length;
        }

        return records;
    }

    private static int FindDuplicate(byte[] bytes, ReadOnlySpan<byte> name, int start, int end)
    {
        for (var offset = start; offset + 1 + name.Length <= end; offset++)
        {
            if (bytes[offset] == name.Length && bytes.AsSpan(offset + 1, name.Length).SequenceEqual(name)) return offset;
        }

        return -1;
    }

    private static bool IsDisplayText(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            if (value is < 32 or 127) return false;
        }

        return true;
    }

    private readonly record struct PartRecord(int Start, string Name, int ContentStart);
    private readonly record struct EmbeddedArray(IReadOnlyList<SpellRecord> Spells, ushort? ClassReference);
}
