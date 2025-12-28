using Microsoft.Extensions.Logging;
using Serilog.Events;

namespace YukariConnect.Logging
{
    public class LogServiceLoggerProvider : ILoggerProvider
    {
        private readonly ILogService _logService;

        public LogServiceLoggerProvider(ILogService logService)
        {
            _logService = logService;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new LogServiceLogger(categoryName, _logService);
        }

        public void Dispose()
        {
        }
    }

    internal sealed class LogServiceLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly ILogService _logService;

        public LogServiceLogger(string categoryName, ILogService logService)
        {
            _categoryName = categoryName;
            _logService = logService;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var level = MapLevel(logLevel);
            var message = formatter(state, exception);

            if (exception != null)
            {
                message = $"{message} | Exception: {exception}";
            }

            _logService.Log(level, "AspNetCore", _categoryName, message);
        }

        private static LogEventLevel MapLevel(LogLevel level) =>
            level switch
            {
                LogLevel.Trace => LogEventLevel.Verbose,
                LogLevel.Debug => LogEventLevel.Debug,
                LogLevel.Information => LogEventLevel.Information,
                LogLevel.Warning => LogEventLevel.Warning,
                LogLevel.Error => LogEventLevel.Error,
                LogLevel.Critical => LogEventLevel.Fatal,
                _ => LogEventLevel.Information
            };
    }

    public class DeferredLogServiceLoggerProvider : ILoggerProvider
    {
        private LogServiceLoggerProvider? _innerProvider;
        private readonly object _lock = new();

        public void Initialize(IServiceProvider serviceProvider)
        {
            lock (_lock)
            {
                if (_innerProvider != null)
                    return;

                var logService = serviceProvider.GetRequiredService<ILogService>();
                _innerProvider = new LogServiceLoggerProvider(logService);
            }
        }

        public ILogger CreateLogger(string categoryName)
        {
            if (_innerProvider == null)
            {
                return NoOpLogger.Instance;
            }

            return _innerProvider.CreateLogger(categoryName);
        }

        public void Dispose()
        {
            _innerProvider?.Dispose();
        }

        private class NoOpLogger : ILogger
        {
            public static readonly NoOpLogger Instance = new();

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => false;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
            }
        }
    }
}
