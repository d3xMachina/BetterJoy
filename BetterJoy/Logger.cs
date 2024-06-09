using BetterJoy.Exceptions;
using System;
using System.IO;

namespace BetterJoy;

public class Logger : IDisposable
{
    private const int _logLevelPadding = 9; // length of the longest LogLevel + 2

    private readonly StreamWriter _logWriter;
    private bool _disposed = false;

    public enum LogLevel
    {
        Info,
        Warning,
        Error,
        Debug
    }

    public Logger(string path)
    {
        _logWriter = new StreamWriter(path, append : false);
    }

    public void Log(string message, LogLevel level = LogLevel.Info)
    {
        string levelPadded = $"[{level}]".ToUpper().PadRight(_logLevelPadding);
        string log = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {levelPadded} {message}";
        _logWriter.WriteLine(log);
        _logWriter.Flush();
    }

    public void Log(string message, Exception e, LogLevel level = LogLevel.Info)
    {
        Log($"{message} {e.Display(true)}", level);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _logWriter.Close();
        _logWriter.Dispose();

        _disposed = true;
    }
}
