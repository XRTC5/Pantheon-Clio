using System.IO;
using System.Text;

namespace Clio.Core;

/// <summary>One capped file, no framework, no background flusher.</summary>
internal static class Log
{
    private static readonly object Gate = new();
    private static readonly string Path = System.IO.Path.Combine(Store.Directory, "clio.log");

    private const long MaxBytes = 256 * 1024;

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERR ", ex is null ? message : $"{message} :: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Store.Directory);
                if (File.Exists(Path) && new FileInfo(Path).Length > MaxBytes)
                    File.WriteAllText(Path, $"[log truncated {DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n");

                File.AppendAllText(Path,
                    $"{DateTime.Now:HH:mm:ss} {level} {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // A logger that can throw is worse than no logger.
        }
    }
}
