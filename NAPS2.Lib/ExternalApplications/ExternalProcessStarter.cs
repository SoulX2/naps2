namespace NAPS2.ExternalApplications;

public interface IExternalProcessStarter
{
    /// <summary>
    /// Starts the program and returns without waiting for it to exit. Success only means the process started, not that the
    /// program did anything with its arguments.
    /// </summary>
    void Start(string executablePath, IReadOnlyList<string> arguments);
}

public class ExternalProcessStarter : IExternalProcessStarter
{
    public void Start(string executablePath, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = Process.Start(startInfo);
    }
}
