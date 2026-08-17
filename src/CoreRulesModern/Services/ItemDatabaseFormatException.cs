namespace CoreRulesModern.Services;

public sealed class ItemDatabaseFormatException : Exception
{
    public ItemDatabaseFormatException(string message) : base(message) { }
    public ItemDatabaseFormatException(string message, Exception innerException) : base(message, innerException) { }
}
