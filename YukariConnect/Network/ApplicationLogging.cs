using Microsoft.Extensions.Logging;

namespace YukariConnect.Network;

/// <summary>
/// Helper for creating loggers in static contexts.
/// </summary>
internal static class ApplicationLogging
{
    private static ILoggerFactory? _loggerFactory;

    public static void Configure(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public static ILogger CreateLogger(string categoryName)
    {
        return _loggerFactory?.CreateLogger(categoryName)
            ?? new NoOpLogger(categoryName);
    }

    public static ILogger<T> CreateLogger<T>()
    {
        return _loggerFactory?.CreateLogger<T>()
            ?? new NoOpLogger<T>();
    }

    private class NoOpLogger : ILogger
    {
        public string Name { get; }

        public NoOpLogger(string name)
        {
            Name = name;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null!;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }

    private class NoOpLogger<T> : ILogger<T>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null!;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }
}
