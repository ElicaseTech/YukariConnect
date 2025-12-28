using Serilog;
using Serilog.Events;

namespace YukariConnect.Logging
{
    public interface ILogService
    {
        void Log(LogEventLevel level, string type, string component, string logMessage);
    }

    public class LogService : ILogService
    {
        private readonly Serilog.ILogger _logger;

        public LogService(Serilog.ILogger logger)
        {
            _logger = logger;
        }

        public void Log(LogEventLevel level, string type, string component, string logMessage)
        {
            var ctx = _logger
                .ForContext("Type", type)
                .ForContext("Component", component);

            ctx.Write(level, "{LogMessage}", logMessage);
        }
    }
}
