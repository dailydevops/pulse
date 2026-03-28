namespace NetEvolve.Pulse;

/// <summary>
/// Specifies the HTTP method to use when mapping a command to a Minimal API endpoint.
/// Only non-<c>GET</c> methods are supported, since commands are state-changing operations.
/// </summary>
public enum CommandHttpMethod
{
    /// <summary>HTTP <c>POST</c> — creates a new resource or submits data. This is the default.</summary>
    Post = 0,

    /// <summary>HTTP <c>PUT</c> — replaces an existing resource entirely.</summary>
    Put,

    /// <summary>HTTP <c>PATCH</c> — partially updates an existing resource.</summary>
    Patch,

    /// <summary>HTTP <c>DELETE</c> — removes a resource.</summary>
    Delete,
}
