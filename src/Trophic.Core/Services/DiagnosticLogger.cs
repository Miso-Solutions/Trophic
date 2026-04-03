namespace Trophic.Core.Services;

/// <summary>
/// Simple file-based diagnostic logger. Writes to a log file next to the executable.
/// Rotates automatically when the file exceeds 5 MB.
/// </summary>
public sealed class DiagnosticLogger
{
    private const int MaxLogSizeBytes = 5 * 1024 * 1024; // 5 MB
    private const string LogFileName = "trophic.log";

    private readonly string _logPath;
    private readonly object _lock = new();

    public DiagnosticLogger(string basePath)
    {
        _logPath = Path.Combine(basePath, LogFileName);
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);
    public void Error(string message, Exception ex) => Write("ERROR", $"{message}: {ex.Message}");

    private void Write(string level, string message)
    {
        try
        {
            lock (_lock)
            {
                RotateIfNeeded();
                var line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(_logPath, line);
            }
        }
        catch
        {
            // Logging should never crash the app
        }
    }

    private void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(_logPath)) return;
            var info = new FileInfo(_logPath);
            if (info.Length <= MaxLogSizeBytes) return;

            var backupPath = _logPath + ".old";
            if (File.Exists(backupPath)) File.Delete(backupPath);
            File.Move(_logPath, backupPath);
        }
        catch
        {
            // Best effort rotation
        }
    }

    /// <summary>
    /// Returns the log file path for diagnostic export.
    /// </summary>
    public string LogFilePath => _logPath;
}
