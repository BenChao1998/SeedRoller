using System;

namespace SeedRollerCli;

public enum RollerLogLevel
{
    Info,
    Warning,
    Error
}

public interface IRollerLogSink
{
    void Log(RollerLogLevel level, string message);
}

public static class RollerLog
{
    private sealed class Scope(IRollerLogSink previous) : IDisposable
    {
        public void Dispose()
        {
            lock (SyncRoot)
            {
                _current = previous;
            }
        }
    }

    private sealed class ConsoleLogSink : IRollerLogSink
    {
        public void Log(RollerLogLevel level, string message)
        {
            var prefix = level switch
            {
                RollerLogLevel.Info => "[信息]",
                RollerLogLevel.Warning => "[警告]",
                RollerLogLevel.Error => "[错误]",
                _ => "[日志]"
            };

            if (level == RollerLogLevel.Error)
            {
                Console.Error.WriteLine($"{prefix} {message}");
            }
            else
            {
                Console.WriteLine($"{prefix} {message}");
            }
        }
    }

    private static readonly object SyncRoot = new();
    private static IRollerLogSink _current = new ConsoleLogSink();

    public static IRollerLogSink Current
    {
        get
        {
            lock (SyncRoot)
            {
                return _current;
            }
        }
    }

    public static IDisposable? PushLogger(IRollerLogSink? sink)
    {
        if (sink == null)
        {
            return null;
        }

        lock (SyncRoot)
        {
            var previous = _current;
            _current = sink;
            return new Scope(previous);
        }
    }

    public static void Info(string message) => Current.Log(RollerLogLevel.Info, message);
    public static void Warning(string message) => Current.Log(RollerLogLevel.Warning, message);
    public static void Error(string message) => Current.Log(RollerLogLevel.Error, message);
}
