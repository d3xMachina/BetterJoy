using System;

namespace BetterJoy.Logging;

public interface ILogger : IDisposable
{
    void Log(string message, LogLevel level = LogLevel.Info);
    void Log(string message, Exception exception, LogLevel level = LogLevel.Error);
    
    event Action<string, LogLevel, Exception?>? OnMessageLogged;
    
}
