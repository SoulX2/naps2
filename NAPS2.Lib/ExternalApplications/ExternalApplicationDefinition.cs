namespace NAPS2.ExternalApplications;

/// <summary>
/// A user-configured program that scanned documents can be passed to.
/// </summary>
public record ExternalApplicationDefinition
{
    public Guid Id { get; init; }

    public string Name { get; init; } = "";

    /// <summary>
    /// The full path to the program's .exe file.
    /// </summary>
    public string ExecutablePath { get; init; } = "";

    /// <summary>
    /// The command-line template. See ExternalArgumentResolver for the syntax.
    /// </summary>
    public string Arguments { get; init; } = "\"{file}\"";

    public ExternalPageSelection PageSelection { get; init; } = ExternalPageSelection.All;

    public bool IncludeOcr { get; init; }

    /// <summary>
    /// Whether to remove the passed pages from the image list once the program has started. This is separate from the
    /// "clear after saving" setting as a started program doesn't mean the document was successfully processed.
    /// </summary>
    public bool ClearAfterPassing { get; init; }

    public bool Enabled { get; init; } = true;
}

public enum ExternalPageSelection
{
    All,
    Selected
}
