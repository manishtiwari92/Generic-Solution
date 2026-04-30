namespace IPS.AutoPost.Core.Exceptions;

/// <summary>
/// Thrown by <see cref="Engine.PluginRegistry.Resolve"/> when no plugin is registered
/// for the requested <c>client_type</c>.
/// </summary>
public class PluginNotFoundException : Exception
{
    /// <summary>
    /// Initialises a new instance with a message that includes the unrecognised client type.
    /// </summary>
    /// <param name="message">
    /// Should include the unrecognised <c>client_type</c> value so the caller can diagnose
    /// the missing registration.
    /// Example: "No plugin registered for client_type: 'NEWCLIENT'"
    /// </param>
    public PluginNotFoundException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initialises a new instance with a message and an inner exception.
    /// </summary>
    public PluginNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
