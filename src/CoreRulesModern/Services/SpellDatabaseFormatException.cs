using System.IO;

namespace CoreRulesModern.Services;

public sealed class SpellDatabaseFormatException : IOException
{
    public SpellDatabaseFormatException(string message) : base(message)
    {
    }

    public SpellDatabaseFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
