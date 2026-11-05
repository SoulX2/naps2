namespace NAPS2.ExternalApplications;

public static class ExternalApplicationValidator
{
    /// <summary>
    /// Gets a user-readable description of what's wrong with the application's configuration, or null if it's valid.
    /// </summary>
    public static string? GetError(ExternalApplicationDefinition app)
    {
        if (string.IsNullOrWhiteSpace(app.Name))
        {
            return UiStrings.ExternalAppNameRequired;
        }
        var path = app.ExecutablePath;
        // Windows runs .bat and .cmd files through cmd.exe even with UseShellExecute=false, which would re-interpret the
        // arguments. So we only allow programs that are started directly.
        if (!Path.IsPathFullyQualified(path) ||
            !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return string.Format(UiStrings.ExternalAppExecutableInvalid, app.Name, path);
        }
        if (!File.Exists(path))
        {
            return string.Format(UiStrings.ExternalAppExecutableNotFound, app.Name, path);
        }
        try
        {
            ExternalArgumentResolver.Validate(app.Arguments);
        }
        catch (ExternalArgumentException ex)
        {
            return string.Format(UiStrings.ExternalAppArgumentsInvalid, app.Name, ex.Message);
        }
        return null;
    }
}
