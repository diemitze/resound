using Microsoft.Extensions.Logging;
using SPTarkov.Common.Models.Logging;

namespace ResoundServer.Tests;

/// <summary>Swallows service logs; they are for the console, not a contract.</summary>
public class TestLogger<T> : ISptLogger<T>
{
    public List<string> Lines { get; } = new();

    public void LogWithColor(string data, Spectre.Console.Color? textColor = null,
                             Spectre.Console.Color? backgroundColor = null, Exception? ex = null) => Lines.Add(data);

    public void Success(string data, Exception? ex = null) => Lines.Add(data);
    public void Error(string data, Exception? ex = null) => Lines.Add(data);
    public void Warning(string data, Exception? ex = null) => Lines.Add(data);
    public void Info(string data, Exception? ex = null) => Lines.Add(data);
    public void Debug(string data, Exception? ex = null) => Lines.Add(data);
    public void Critical(string data, Exception? ex = null) => Lines.Add(data);

    public void Log(LogLevel level, string data, Spectre.Console.Color? textColor = null,
                    Spectre.Console.Color? backgroundColor = null, Exception? ex = null) => Lines.Add(data);

    public bool IsLogEnabled(LogLevel level) => false;
}
