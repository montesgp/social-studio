using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Incoders.Hive.ApiService.Logging;

internal sealed class LocalFileLoggerProvider : ILoggerProvider
{
    private readonly object _sync = new();
    private readonly string _filePath;
    private readonly LogLevel _minimumLevel;
    private readonly string _categoryPrefix;
    private readonly bool _enabled;

    public LocalFileLoggerProvider(IConfiguration configuration, string contentRootPath)
    {
        _enabled = configuration.GetValue("Enabled", false);
        _minimumLevel = configuration.GetValue("MinimumLevel", LogLevel.Information);
        _categoryPrefix = configuration.GetValue("CategoryPrefix", "Incoders.Hive");

        var configuredPath = configuration.GetValue<string>("Path");
        _filePath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.GetFullPath(Path.Combine(contentRootPath, "..", "logs", "hive-api.log"))
            : Path.GetFullPath(Path.Combine(contentRootPath, configuredPath));
    }

    public ILogger CreateLogger(string categoryName) =>
        new LocalFileLogger(categoryName, _filePath, _minimumLevel, _categoryPrefix, _enabled, _sync);

    public void Dispose()
    {
    }

    private sealed class LocalFileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly string _filePath;
        private readonly LogLevel _minimumLevel;
        private readonly string _categoryPrefix;
        private readonly bool _enabled;
        private readonly object _sync;

        public LocalFileLogger(
            string categoryName,
            string filePath,
            LogLevel minimumLevel,
            string categoryPrefix,
            bool enabled,
            object sync)
        {
            _categoryName = categoryName;
            _filePath = filePath;
            _minimumLevel = minimumLevel;
            _categoryPrefix = categoryPrefix;
            _enabled = enabled;
            _sync = sync;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) =>
            _enabled
            && logLevel != LogLevel.None
            && logLevel >= _minimumLevel
            && _categoryName.StartsWith(_categoryPrefix, StringComparison.OrdinalIgnoreCase);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception is null)
            {
                return;
            }

            var builder = new StringBuilder()
                .Append(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture))
                .Append(" [")
                .Append(logLevel)
                .Append("] ")
                .Append(_categoryName);

            if (eventId.Id != 0 || !string.IsNullOrWhiteSpace(eventId.Name))
            {
                builder.Append(" [").Append(eventId.Id).Append(':').Append(eventId.Name).Append(']');
            }

            builder.Append(" - ").AppendLine(message);

            if (exception is not null)
            {
                builder.AppendLine(exception.ToString());
            }

            lock (_sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                File.AppendAllText(_filePath, builder.ToString(), Encoding.UTF8);
            }
        }
    }
}
