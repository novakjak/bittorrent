using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public sealed class Logger
{
    const string TIME_FORMAT = "HH:mm:ss.ffff";
    private readonly static string projectName = Assembly.GetCallingAssembly().GetName().Name!;
    private readonly static string logFilePath = $"{projectName}.log";

    private static Logger? _instance;
    private readonly FileStream _logFile;
    private readonly StreamWriter _writer;

    private Logger()
    {
        _logFile = File.Open(logFilePath, FileMode.Create);
        _writer = new StreamWriter(_logFile);
        _instance = this;
    }

    private static Logger Get()
    {
        _instance ??= new Logger();
        return _instance!;
    }

    private static string BuildMessage(string message, string member, int lineNumber)
    {
        var frames = new StackTrace().GetFrames();
        var frame = Array.Find(frames, fr => fr.GetMethod()?.DeclaringType != typeof(Logger))!;
        var method = frame.GetMethod()!;
        // For some reason the extension attribute is not defined on the Handle methods in torrent task,
        // so this does not work as it should. Mangled symbol names are still better than nothing so ¯\_(ツ)_/¯
        Type? classType = method.IsDefined(typeof(ExtensionAttribute))
            ? method.GetParameters()[0]?.ParameterType
            : method.DeclaringType;
        while (classType?.IsNested ?? false && classType?.DeclaringType is not null)
            classType = classType.DeclaringType;

        string className = classType?.FullName ?? "<unknown>";

        var now = DateTime.Now.ToString(TIME_FORMAT);
        var logMessage = $"[{now}] {className}.{member}:{lineNumber} - {message}";
        return logMessage;
    }

    public static void Log(LogLevel level, string message,
        [CallerMemberName] string member = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        var logger = Logger.Get();
        var logMessage = Logger.BuildMessage(message, member, lineNumber);

        Console.ForegroundColor = level.ToColor();
        Console.WriteLine(logMessage);
        Console.ResetColor();
        logger._writer.WriteLine(logMessage);
        logger._writer.Flush();
    }

    public static async Task LogAsync(LogLevel level, string message,
        CancellationTokenSource? tokenSource = null,
        [CallerMemberName] string member = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        tokenSource ??= new CancellationTokenSource();
        var logger = Logger.Get();
        var logMessage = Logger.BuildMessage(message, member, lineNumber);

        Console.ForegroundColor = level.ToColor();
        Console.Error.WriteLine(logMessage);
        Console.ResetColor();

        await logger._writer.WriteLineAsync(logMessage.AsMemory(), tokenSource!.Token);
        await logger._writer.FlushAsync(tokenSource!.Token);
    }

    public static void Debug(string message, [CallerMemberName] string member = "", [CallerLineNumber] int ln = 0)
        => Logger.Log(LogLevel.Info, message, member, ln);
    public static void DebugIter<T>(IEnumerable<T> arr, [CallerMemberName] string member = "", [CallerLineNumber] int ln = 0)
    {
        StringBuilder msg = new();
        msg.Append("[");
        var first = true;
        foreach (var obj in arr)
        {
            if (!first)
                msg.Append(", ");
            first = false;
            msg.Append(obj.ToString());
        }
        Logger.Debug(msg.ToString(), member, ln);
    }
    public static void Info(string message, [CallerMemberName] string member = "", [CallerLineNumber] int ln = 0)
        => Logger.Log(LogLevel.Info, message, member, ln);
    public static void Warn(string message, [CallerMemberName] string member = "", [CallerLineNumber] int ln = 0)
        => Logger.Log(LogLevel.Warning, message, member, ln);
    public static void Error(string message, [CallerMemberName] string member = "", [CallerLineNumber] int ln = 0)
        => Logger.Log(LogLevel.Error, message, member, ln);

    public static async Task DebugAsync(string message, CancellationTokenSource? tokenSource = null,
        [CallerMemberName] string member = "", [CallerLineNumber] int ln = 0)
        => await Logger.LogAsync(LogLevel.Info, message, tokenSource, member, ln);
    public static async Task InfoAsync(string message, CancellationTokenSource? tokenSource = null,
        [CallerMemberName] string member = "", [CallerLineNumber] int ln = 0)
        => await Logger.LogAsync(LogLevel.Info, message, tokenSource, member, ln);
    public static async Task WarnAsync(string message, CancellationTokenSource? tokenSource = null,
        [CallerMemberName] string member = "", [CallerLineNumber] int ln = 0)
        => await Logger.LogAsync(LogLevel.Warning, message, tokenSource, member, ln);
    public static async Task ErrorAsync(string message, CancellationTokenSource? tokenSource = null,
        [CallerMemberName] string member = "", [CallerLineNumber] int ln = 0)
        => await Logger.LogAsync(LogLevel.Error, message, tokenSource, member, ln);

    ~Logger()
    {
        _writer.Close();
        _logFile.Close();
    }
}

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

public static class LogLevelExtensions
{
    public static ConsoleColor ToColor(this LogLevel level) => level switch
    {
        LogLevel.Debug | LogLevel.Info => ConsoleColor.White,
        LogLevel.Warning => ConsoleColor.Yellow,
        LogLevel.Error => ConsoleColor.Red,
        _ => ConsoleColor.White,
    };
}
