using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ServerPilot.UnitTests.Infrastructure;

internal sealed class TestLogProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<TestLogEntry> entries = new();
    private IExternalScopeProvider scopeProvider = new LoggerExternalScopeProvider();

    public IReadOnlyCollection<TestLogEntry> Entries => entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new TestLogger(this, categoryName, entries);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) =>
        this.scopeProvider = scopeProvider;

    public void Dispose()
    {
    }

    private sealed class TestLogger(
        TestLogProvider provider,
        string categoryName,
        ConcurrentQueue<TestLogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => provider.scopeProvider.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            string? correlationId = null;
            provider.scopeProvider.ForEachScope(
                static (scope, state) => state.Visit(scope),
                new ScopeState(value => correlationId = value));
            entries.Enqueue(new TestLogEntry(categoryName, logLevel, formatter(state, exception), correlationId));
        }
    }

    private sealed class ScopeState(Action<string?> setCorrelationId)
    {
        public void Visit(object? scope)
        {
            if (scope is not IEnumerable<KeyValuePair<string, object?>> values)
            {
                return;
            }

            foreach (KeyValuePair<string, object?> value in values)
            {
                if (value.Key == "CorrelationId")
                {
                    setCorrelationId(value.Value?.ToString());
                }
            }
        }
    }
}

internal sealed record TestLogEntry(
    string CategoryName,
    LogLevel Level,
    string Message,
    string? CorrelationId);
