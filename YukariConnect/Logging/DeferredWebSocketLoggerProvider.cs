using Microsoft.Extensions.Logging;

namespace YukariConnect.Logging;

/// <summary>
/// Deferred logger provider for WebSocket logging that initializes after DI container is built.
/// This is only needed for WebSocket logger since it requires ILogBroadcaster from DI.
/// Console and Debug loggers are added directly via builder.Logging.AddConsole()/AddDebug().
/// </summary>
public class DeferredWebSocketLoggerProvider : ILoggerProvider
{
    private WebSocketLoggerProvider? _innerProvider;
    private readonly object _lock = new();

    public void Initialize(IServiceProvider serviceProvider)
    {
        lock (_lock)
        {
            if (_innerProvider != null)
                return;

            var broadcaster = serviceProvider.GetRequiredService<ILogBroadcaster>();
            _innerProvider = new WebSocketLoggerProvider(broadcaster);
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        if (_innerProvider == null)
        {
            // Return a null logger that does nothing until initialized
            return NullLogger.Instance;
        }

        return _innerProvider.CreateLogger(categoryName);
    }

    public void Dispose()
    {
        _innerProvider?.Dispose();
    }

    /// <summary>
    /// Null logger that suppresses all log output.
    /// </summary>
    private class NullLogger : ILogger
    {
        public static readonly NullLogger Instance = new();

        private NullLogger() { }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // Do nothing - suppress all logs
        }
    }
}
