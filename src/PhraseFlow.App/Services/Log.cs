using System.Text;

namespace PhraseFlow.App;

/// <summary>Tiny append-only log file in the data folder (rotated at 1 MB).</summary>
internal static class Log
{
    private static readonly Lock Gate = new();
    private static string? _path;

    public static void Initialize(string path)
    {
        _path = path;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
            {
                File.Move(path, path + ".old", overwrite: true);
            }
        }
        catch (IOException)
        {
        }
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message) => Write("WARN", message, null);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        if (_path is null)
        {
            return;
        }

        var line = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(' ').Append(level).Append(' ').Append(message);
        if (exception is not null)
        {
            line.AppendLine().Append(exception);
        }

        line.AppendLine();
        lock (Gate)
        {
            try
            {
                File.AppendAllText(_path, line.ToString());
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
