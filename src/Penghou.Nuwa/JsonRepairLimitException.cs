namespace Penghou.Nuwa;

/// <summary>Thrown when a repair exceeds a configured resource limit.</summary>
public sealed class JsonRepairLimitException : Exception
{
    /// <summary>Initializes the exception.</summary>
    public JsonRepairLimitException(string message) : base(message)
    {
    }
}
