using IPS.AutoPost.Core.Exceptions;
using IPS.AutoPost.Core.Interfaces;

namespace IPS.AutoPost.Core.Engine;

/// <summary>
/// Maps <c>client_type</c> strings to <see cref="IClientPlugin"/> implementations.
/// Populated at application startup by <c>PluginRegistration.RegisterAll()</c> in the
/// <c>IPS.AutoPost.Plugins</c> project.
/// </summary>
/// <remarks>
/// Lookup is case-insensitive so "INVITEDCLUB", "InvitedClub", and "invitedclub" all
/// resolve to the same plugin.
/// </remarks>
public class PluginRegistry
{
    private readonly Dictionary<string, IClientPlugin> _plugins
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a plugin. If a plugin with the same <c>ClientType</c> is already
    /// registered, it is replaced (last-write wins).
    /// </summary>
    /// <param name="plugin">The plugin instance to register.</param>
    public void Register(IClientPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        _plugins[plugin.ClientType] = plugin;
    }

    /// <summary>
    /// Resolves the plugin for the given <paramref name="clientType"/>.
    /// </summary>
    /// <param name="clientType">
    /// The <c>client_type</c> value from <c>generic_job_configuration</c>
    /// (e.g. "INVITEDCLUB", "SEVITA").
    /// </param>
    /// <returns>The registered <see cref="IClientPlugin"/> for this client type.</returns>
    /// <exception cref="PluginNotFoundException">
    /// Thrown when no plugin is registered for <paramref name="clientType"/>.
    /// </exception>
    public IClientPlugin Resolve(string clientType)
    {
        if (_plugins.TryGetValue(clientType, out var plugin))
            return plugin;

        throw new PluginNotFoundException(
            $"No plugin registered for client_type: '{clientType}'. " +
            $"Registered types: [{string.Join(", ", _plugins.Keys)}]");
    }

    /// <summary>
    /// Returns <c>true</c> when a plugin is registered for <paramref name="clientType"/>.
    /// </summary>
    public bool IsRegistered(string clientType)
        => _plugins.ContainsKey(clientType);

    /// <summary>
    /// Returns all registered client type strings (for diagnostics and logging).
    /// </summary>
    public IReadOnlyCollection<string> RegisteredClientTypes
        => _plugins.Keys.ToList().AsReadOnly();
}
