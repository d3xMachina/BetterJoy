#nullable disable
using BetterJoy.Exceptions;
using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace BetterJoy;

public sealed class Logger : IDisposable
{
    private const int _logLevelPadding = 9; // length of the longest LogLevel + 2

    private readonly StreamWriter _logWriter;
    private bool _disposed = false;

    public event Action<string, LogLevel, Exception?>? OnMessageLogged;

    private readonly Task _logWriterTask;
    private readonly CancellationTokenSource _ctsLogs;
    private readonly Channel<LogEntry> _logChannel;
    private readonly bool _isRunning = false;

    public enum LogLevel
    {
        Info,
        Warning,
        Error,
        Debug
    }

    private record LogEntry(string Message, LogLevel Level);

    public Logger(string path)
    {
        _logWriter = new StreamWriter(path, append: false);

        _logChannel = Channel.CreateUnbounded<LogEntry>(
            new UnboundedChannelOptions
            {
                SingleWriter = false,
                SingleReader = true,
                AllowSynchronousContinuations = false
            }
        );

        _ctsLogs = new CancellationTokenSource();
        _logWriterTask = Task.Run(
            async () =>
            {
                try
                {
                    await ProcessLogs(_ctsLogs.Token);
                }
                catch (OperationCanceledException) when (_ctsLogs.IsCancellationRequested)
                {
                    // Nothing to do
                }
            }
        );

        //Log("Task log writer started.", LogLevel.Debug);

        _isRunning = true;
    }

    private async Task ProcessLogs(CancellationToken token)
    {
        await foreach (var entry in _logChannel.Reader.ReadAllAsync(token))
        {
            var levelPadded = $"[{entry.Level}]".ToUpper().PadRight(_logLevelPadding);
            var log = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {levelPadded} {entry.Message}";
            await _logWriter.WriteLineAsync(log);
            await _logWriter.FlushAsync(CancellationToken.None);
        }
    }

    public void Log(string message, LogLevel level = LogLevel.Info)
    {
        OnMessageLogged?.Invoke(message, level, null);
        LogImpl(message, level);
    }

    public void Log(string message, Exception e, LogLevel level = LogLevel.Error)
    {
        OnMessageLogged?.Invoke(message, level, e);
        LogImpl($"{message} {e.Display(true)}", level);
    }

    private void LogImpl(string message, LogLevel level)
    {
        var log = new LogEntry(Message: message, Level: level);

        while (!_logChannel.Writer.TryWrite(log)) { }
    }

    public async Task Close()
    {
        if (!_isRunning)
        {
            return;
        }

        _ctsLogs.Cancel();
        await _logWriterTask;

        _logWriter.Close();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _logWriter.Dispose();
        _disposed = true;
    }
}
