namespace GNAS.Core;

/// <summary>
/// File access permission flags.
/// </summary>
[Flags]
public enum FilePermission
{
    /// <summary>No permissions.</summary>
    None = 0,
    /// <summary>Read permission.</summary>
    Read = 1,
    /// <summary>Write permission.</summary>
    Write = 2,
    /// <summary>Read and write permissions.</summary>
    ReadWrite = 3,
    /// <summary>Full control permission.</summary>
    FullControl = 7
}
