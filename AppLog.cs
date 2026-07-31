namespace Snapshot;

internal static class AppLog
{
    private static readonly object Sync = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Snapshot");

    internal static string LogPath { get; } = Path.Combine(LogDirectory, "Snapshot.log");

    internal static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(
                    LogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Diagnostics must never prevent screenshots.
        }
    }
}
