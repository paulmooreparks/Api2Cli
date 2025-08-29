using System.Collections.Generic;

namespace ParksComputing.Api2Cli.Workspace.Services;

/// <summary>
/// Provides layered environment variable management with a root snapshot and an optional active overlay.
/// </summary>
public interface IEnvService {
    IReadOnlyDictionary<string,string> Root { get; }
    IReadOnlyDictionary<string,string> ActiveOverlay { get; }
    /// <summary>Reload the immutable root snapshot from a .env file (does not modify current process env until ApplyRoot is called).</summary>
    void LoadRoot(string? path);
    /// <summary>Apply the root snapshot to the current process (idempotent).</summary>
    void ApplyRoot();
    /// <summary>Load an overlay .env file and apply its values on top of root; previous overlay values are reverted first.</summary>
    void ApplyOverlay(string? path);
    /// <summary>Revert any previously applied overlay restoring root values (or clearing keys introduced by overlay).</summary>
    void RevertOverlay();
}
