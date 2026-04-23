using EveStatsCollector.Diagnostics;
using EveStatsCollector.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace EveStatsCollector.Tests.Diagnostics;

/// <summary>
/// Tests for the internal static <see cref="ReportDebugLogger"/>. Relies on
/// <c>InternalsVisibleTo</c> in EveStatsCollector.csproj.
///
/// We use a lightweight recording ILogger instead of Moq because ILogger.Log
/// is a generic method and awkward to mock; recording lets us inspect what
/// was actually emitted and — importantly — verify the resolver Func was invoked.
/// </summary>
public class ReportDebugLoggerTests
{
    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }

    [Fact]
    public void LogKillsReport_EmitsHeaderAndOneLinePerEntry()
    {
        var logger = new RecordingLogger();
        var entries = new[]
        {
            new SystemKills(30000142, 1, 2, 3),
            new SystemKills(30000144, 4, 5, 6),
        };
        var report = new KillsReport(42, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, entries);

        int calls = 0;
        ReportDebugLogger.LogKillsReport(logger, report, id =>
        {
            calls++;
            return id == 30000142 ? "Jita" : null; // second entry falls back to id.ToString()
        });

        calls.Should().Be(2);
        logger.Entries.Should().HaveCount(1 + entries.Length);
        logger.Entries[0].Message.Should().Contain("Kills report #42");
        logger.Entries[1].Message.Should().Contain("Jita");
        logger.Entries[2].Message.Should().Contain("30000144"); // resolver returned null — fallback to id
    }

    [Fact]
    public void LogJumpsReport_EmitsHeaderAndOneLinePerEntry()
    {
        var logger = new RecordingLogger();
        var entries = new[]
        {
            new SystemJumps(30000142, 420),
            new SystemJumps(30000144, 69),
        };
        var report = new JumpsReport(7, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, entries);

        ReportDebugLogger.LogJumpsReport(logger, report, id => id == 30000142 ? "Jita" : null);

        logger.Entries.Should().HaveCount(1 + entries.Length);
        logger.Entries[0].Message.Should().Contain("Jumps report #7");
        logger.Entries[1].Message.Should().Contain("Jita");
        logger.Entries[1].Message.Should().Contain("420");
        logger.Entries[2].Message.Should().Contain("30000144");
    }

    [Fact]
    public void LogKillsReport_EmptyEntries_OnlyEmitsHeader()
    {
        var logger = new RecordingLogger();
        var report = new KillsReport(1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Array.Empty<SystemKills>());

        ReportDebugLogger.LogKillsReport(logger, report, _ => null);

        logger.Entries.Should().ContainSingle();
    }

    [Fact]
    public void LogJumpsReport_EmptyEntries_OnlyEmitsHeader()
    {
        var logger = new RecordingLogger();
        var report = new JumpsReport(1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Array.Empty<SystemJumps>());

        ReportDebugLogger.LogJumpsReport(logger, report, _ => null);

        logger.Entries.Should().ContainSingle();
    }
}
