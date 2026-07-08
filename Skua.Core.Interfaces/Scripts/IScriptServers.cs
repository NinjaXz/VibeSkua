using Skua.Core.Models.Servers;
using System.ComponentModel;

namespace Skua.Core.Interfaces;

/// <summary>
/// Defines the contract for managing game server connections, authentication, and re-login operations within a
/// scriptable environment.
/// </summary>
/// <remarks>
/// This interface provides methods and properties for handling server lists, connection state, login
/// credentials, and automated re-login scenarios. Implementations are expected to support property change notifications
/// via <see cref="INotifyPropertyChanged"/>. Thread safety and specific error handling behaviors depend on the concrete
/// implementation.
/// </remarks>
public interface IScriptServers : INotifyPropertyChanged
{
    /// <summary>
    /// Sets the login credentials to be used for authentication.
    /// </summary>
    /// <param name="username">The username to use for authentication. Cannot be null or empty.</param>
    /// <param name="password">The password associated with the specified username. Cannot be null.</param>
    void SetLoginInfo(string username, string password);

    /// <summary>
    /// The IP of the last server the player was connected to.
    /// </summary>
    string LastIP { get; set; }

    /// <summary>
    /// The name of the last server the player was connected to.
    /// </summary>
    string LastName { get; set; }

    /// <summary>
    /// Gets the cached username of the player across disconnects and re-logins.
    /// </summary>
    string? CachedUsername { get; }

    /// <summary>
    /// The list of available game servers.
    /// </summary>
    List<Server> ServerList { get; }

    /// <summary>
    /// A list of servers cached from the game api.
    /// </summary>
    List<Server> CachedServers { get; }

    /// <summary>
    /// Gets a list of servers from the game api.
    /// </summary>
    /// <param name="forceUpdate">Whether to force an update of the list.</param>
    /// <returns>A <see cref="Server"/> object list.</returns>
    ValueTask<List<Server>> GetServers(bool forceUpdate = false);

    /// <summary>
    /// Connects to the specified game <paramref name="server"/>.
    /// </summary>
    /// <param name="server"><see cref="Server"/> to connect to.</param>
    bool Connect(Server server)
    {
        return ConnectIP(server.IP);
    }

    /// <summary>
    /// Connects to the game server with the specified <paramref name="serverName"/>.
    /// </summary>
    /// <param name="serverName">Name of the server to connect to (e.g. Artix)</param>
    bool Connect(string serverName)
    {
        Server? s = ServerList.Find(x => x.Name.Contains(serverName));
        return s is not null && Connect(s.IP);
    }

    /// <summary>
    /// Connects to the game server with the specified <paramref name="serverIp"/> address.
    /// </summary>
    /// <param name="serverIp">IP address of the server to connect to.</param>
    bool ConnectIP(string serverIp);

    /// <summary>
    /// Connects to the game server with the specified <paramref name="serverIp"/> address and <paramref name="port"/>.
    /// </summary>
    /// <param name="serverIp"></param>
    /// <param name="port"></param>
    /// <returns></returns>
    bool ConnectIP(string serverIp, int port);

    /// <summary>
    /// Checks if the player is currently connected to a game server.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Logs into the game with the specified username and password.
    /// </summary>
    /// <param name="username">Username to log in with.</param>
    /// <param name="password">Password to log in with.</param>
    void Login(string username, string password);

    /// <summary>
    /// Initiates the user login process. <see cref="Login(string, string)"/>
    /// </summary>
    void Login();

    /// <summary>
    /// Logs out of the game.
    /// </summary>
    void Logout();

    /// <summary>
    /// Logs in and connects to the server with specified <paramref name="name"/>.
    /// </summary>
    /// <param name="serverName">Name of the server to connect to.</param>
    /// <param name="loginDelay">Delay before trying to connect to the <paramref name="server"/></param>
    bool Reconnect(string serverName, int loginDelay = 2000);

    /// <summary>
    /// Logs in and connects to the specified <paramref name="server"/>.
    /// </summary>
    /// <param name="server">Server to connect to.</param>
    /// <param name="loginDelay">Delay before trying to connect to the <paramref name="server"/></param>
    bool Reconnect(Server server, int loginDelay = 2000);

    /// <summary>
    /// Logs out and then tries to log in back.
    /// </summary>
    /// <param name="server">Server to connect to.</param>
    /// <returns><see langword="true"/> if the player has successfully connected.</returns>
    /// <remarks>This will disable <see cref="IScriptOption.AutoRelogin"/>, try to log in and then enable it again.</remarks>
    bool Relogin(Server server = null!);

    /// <summary>
    /// Logs out and then tries to log in back.
    /// </summary>
    /// <param name="serverName">Name of the server to connect to.</param>
    /// <returns><see langword="true"/> if the player has successfully connected.</returns>
    /// <remarks>This will disable <see cref="IScriptOption.AutoRelogin"/>, try to log in and then enable it again.</remarks>
    bool Relogin(string serverName);

    /// <summary>
    /// Logs out and then tries to log in back.
    /// </summary>
    /// <param name="serverName">IP of the server to connect to.</param>
    /// <returns><see langword="true"/> if the player has successfully connected.</returns>
    /// <remarks>This will disable <see cref="IScriptOption.AutoRelogin"/>, try to log in and then enable it again.</remarks>
    bool ReloginIP(string ip);

    /// <summary>
    /// Tries to re-login for the number of <see cref="IScriptOption.ReloginTries"/>.
    /// </summary>
    /// <param name="serverName">Name of the server to connect to.</param>
    /// <returns><see langword="true"/> if the re-login was successful</returns>
    /// <remarks>This will disable <see cref="IScriptOption.AutoRelogin"/>, try to log in and then enable it again.</remarks>
    bool EnsureRelogin(string serverName);

    /// <summary>
    /// Tries to re-login for the number of <see cref="IScriptOption.ReloginTries"/>.
    /// </summary>
    /// <returns><see langword="true"/> if the re-login was successful</returns>
    /// <remarks>This is mainly used for auto re-login. If you plan in using this, do a <see cref="Logout"/> first.</remarks>
    Task<bool> EnsureRelogin(CancellationToken token);

    /// <summary>
    /// Tries to relogin for the number of <see cref="IScriptOption.ReloginTries"/>.
    /// </summary>
    /// <param name="serverName">Name of the server to connect to.</param>
    /// <returns><see langword="true"/> if the re-login was successful</returns>
    /// <remarks>This is mainly used for auto re-login. If you plan in using this, do a <see cref="Logout"/> first.</remarks>
    Task<bool> EnsureRelogin(string serverName, CancellationToken token);
}