using System.IO;
using System.Text;
using CoreRulesModern.Models;

namespace CoreRulesModern.Services;

/// <summary>
/// Reads the MFC CArchive representation used by Core Rules Spells.dat and
/// SpellsU.dat. The parser never opens a database with write access.
/// </summary>
public sealed class SpellDatabaseParser
{
    private const ushort NewClassTag = 0xffff;
    private const ushort SpellClassReferenceTag = 0x8001;
    private const ushort SupportedSchema = 88;
    private const int MaximumRecords = 100_000;
    private const int MaximumStringBytes = 16 * 1024 * 1024;
    private const int MaximumCategories = 1_024;
    private static readonly Encoding Windows1252;

    static SpellDatabaseParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Windows1252 = Encoding.GetEncoding(1252);
    }

    public SpellDatabase Parse(string path, SpellDatabaseKind databaseKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        using var reader = new BinaryReader(stream, Windows1252, leaveOpen: false);

        try
        {
            return Parse(reader, fullPath, databaseKind);
        }
        catch (SpellDatabaseFormatException)
        {
            throw;
        }
        catch (EndOfStreamException exception)
        {
            throw FormatError(stream, "The spell database ended inside a record.", exception);
        }
        catch (IOException exception)
        {
            throw FormatError(stream, "The spell database could not be read.", exception);
        }
    }

    private static SpellDatabase Parse(
        BinaryReader reader,
        string path,
        SpellDatabaseKind databaseKind)
    {
        var stream = reader.BaseStream;
        var spells = new List<SpellRecord>();
        var sawClassHeader = false;
        ushort schema = 0;
        string runtimeClass = string.Empty;

        while (stream.Position < stream.Length)
        {
            var arrayCount = ReadCount(reader, "spell-array count");
            if (arrayCount == 0)
            {
                if (stream.Position != stream.Length)
                {
                    throw FormatError(stream, "An empty spell array was followed by unexpected data.");
                }

                break;
            }

            if (arrayCount > MaximumRecords - spells.Count)
            {
                throw FormatError(stream, $"The database declares too many spell records ({arrayCount:N0}).");
            }

            for (var index = 0; index < arrayCount; index++)
            {
                var objectTag = reader.ReadUInt16();
                if (!sawClassHeader)
                {
                    if (objectTag != NewClassTag)
                    {
                        throw FormatError(stream, $"Expected the CSpellsOb class tag, found 0x{objectTag:X4}.");
                    }

                    schema = reader.ReadUInt16();
                    runtimeClass = ReadClassName(reader);
                    if (schema != SupportedSchema || runtimeClass != "CSpellsOb")
                    {
                        throw FormatError(
                            stream,
                            $"Unsupported spell schema {schema} ({runtimeClass}). Expected CSpellsOb schema {SupportedSchema}.");
                    }

                    sawClassHeader = true;
                }
                else if (objectTag != SpellClassReferenceTag)
                {
                    throw FormatError(
                        stream,
                        $"Expected a CSpellsOb reference tag, found 0x{objectTag:X4} at record {spells.Count + 1:N0}.");
                }

                spells.Add(ReadSpell(reader, databaseKind));
            }
        }

        if (!sawClassHeader)
        {
            throw FormatError(stream, "The file does not contain a CSpellsOb spell array.");
        }

        return new SpellDatabase(path, schema, runtimeClass, spells);
    }

    private static SpellRecord ReadSpell(BinaryReader reader, SpellDatabaseKind databaseKind)
    {
        var stream = reader.BaseStream;
        var name = ReadArchiveString(reader, "spell name");
        if (string.IsNullOrWhiteSpace(name))
        {
            throw FormatError(stream, "A spell has no name.");
        }

        var neverBanCantrip = ReadBoolean(reader, "Never Ban Cantrip");
        var reversible = ReadBoolean(reader, "Reversible");
        var wizardSpell = ReadBoolean(reader, "Wizard Spell");
        var priestSpell = ReadBoolean(reader, "Priest Spell");
        var levelValue = reader.ReadUInt32();
        if (levelValue > 99)
        {
            throw FormatError(stream, $"Spell '{name}' has an invalid level ({levelValue}).");
        }

        var areaOfEffect = ReadArchiveString(reader, "area of effect");
        var castingTime = ReadArchiveString(reader, "casting time");
        var components = ReadArchiveString(reader, "components");
        var critical = ReadArchiveString(reader, "critical");
        var duration = ReadArchiveString(reader, "duration");
        var knockdown = ReadArchiveString(reader, "knockdown");
        var range = ReadArchiveString(reader, "range");
        var savingThrow = ReadArchiveString(reader, "saving throw");
        var sensory = ReadArchiveString(reader, "sensory");
        var subtlety = ReadArchiveString(reader, "subtlety");
        var helpTopicId = reader.ReadUInt32();
        var description = ReadArchiveString(reader, "description");
        var schools = ReadStringList(reader, "schools");
        var spheres = ReadStringList(reader, "spheres");

        return new SpellRecord(
            name,
            neverBanCantrip,
            reversible,
            wizardSpell,
            priestSpell,
            (int)levelValue,
            areaOfEffect,
            castingTime,
            components,
            critical,
            duration,
            knockdown,
            range,
            savingThrow,
            sensory,
            subtlety,
            helpTopicId,
            description,
            schools,
            spheres,
            databaseKind);
    }

    private static bool ReadBoolean(BinaryReader reader, string fieldName)
    {
        var value = reader.ReadUInt32();
        if (value > 1)
        {
            throw FormatError(reader.BaseStream, $"Field '{fieldName}' contains invalid Boolean value {value}.");
        }

        return value == 1;
    }

    private static IReadOnlyList<string> ReadStringList(BinaryReader reader, string fieldName)
    {
        var count = ReadCount(reader, fieldName);
        if (count > MaximumCategories)
        {
            throw FormatError(reader.BaseStream, $"The {fieldName} list is unreasonably large ({count:N0}).");
        }

        var values = new string[count];
        for (var index = 0; index < count; index++)
        {
            values[index] = ReadArchiveString(reader, fieldName);
        }

        return values;
    }

    private static uint ReadCount(BinaryReader reader, string fieldName)
    {
        var shortCount = reader.ReadUInt16();
        if (shortCount != ushort.MaxValue) return shortCount;

        var longCount = reader.ReadUInt32();
        if (longCount > int.MaxValue)
        {
            throw FormatError(reader.BaseStream, $"The {fieldName} is too large ({longCount:N0}).");
        }

        return longCount;
    }

    private static string ReadClassName(BinaryReader reader)
    {
        var length = reader.ReadUInt16();
        if (length is 0 or > 256)
        {
            throw FormatError(reader.BaseStream, $"Invalid runtime class-name length ({length}).");
        }

        return Windows1252.GetString(ReadExactly(reader, length, "runtime class name"));
    }

    private static string ReadArchiveString(BinaryReader reader, string fieldName)
    {
        uint length = reader.ReadByte();
        if (length == byte.MaxValue)
        {
            length = reader.ReadUInt16();
            if (length == ushort.MaxValue) length = reader.ReadUInt32();
        }

        if (length > MaximumStringBytes)
        {
            throw FormatError(reader.BaseStream, $"The {fieldName} is too large ({length:N0} bytes).");
        }

        if (length == 0) return string.Empty;
        return Windows1252.GetString(ReadExactly(reader, checked((int)length), fieldName));
    }

    private static byte[] ReadExactly(BinaryReader reader, int length, string fieldName)
    {
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
        {
            throw new EndOfStreamException($"The {fieldName} ended after {bytes.Length:N0} of {length:N0} bytes.");
        }

        return bytes;
    }

    private static SpellDatabaseFormatException FormatError(
        Stream stream,
        string message,
        Exception? innerException = null)
    {
        var qualifiedMessage = $"{message} Offset: 0x{stream.Position:X}.";
        return innerException is null
            ? new SpellDatabaseFormatException(qualifiedMessage)
            : new SpellDatabaseFormatException(qualifiedMessage, innerException);
    }
}
