namespace Exodus.Mcp.Core.Auth;

/// <summary>
/// Login to Exodus failed. The message is user-facing (Turkish) and never contains a secret.
/// <see cref="Permanent"/> failures are latched by <see cref="TokenStore"/> for the life of the process.
/// </summary>
public sealed class ExodusAuthException(string message, bool permanent) : Exception(message)
{
    public bool Permanent { get; } = permanent;
}
