using Microsoft.Extensions.Logging;

namespace YukariConnect.Logging;

/// <summary>
/// Custom logger provider that broadcasts logs to WebSocket clients.
/// </summary>
public class WebSocketLoggerProvider : ILoggerProvider
{
    private readonly ILogBroadcaster _broadcaster;

    public WebSocketLoggerProvider(ILogBroadcaster broadcaster)
    {
        _broadcaster = broadcaster;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new WebSocketLogger(categoryName, _broadcaster);
    }

    public void Dispose()
    {
    }
}

/// <summary>
/// Custom logger that forwards log entries to WebSocket clients.
/// </summary>
public class WebSocketLogger : ILogger
{
    private readonly string _categoryName;
    private readonly ILogBroadcaster _broadcaster;

    public WebSocketLogger(string categoryName, ILogBroadcaster broadcaster)
    {
        _categoryName = categoryName;
        _broadcaster = broadcaster;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        var levelStr = logLevel.ToString();
        var timestamp = DateTimeOffset.UtcNow;

        // Broadcast to all WebSocket clients
        _broadcaster.Broadcast(timestamp, levelStr, _categoryName, message);
    }
}
