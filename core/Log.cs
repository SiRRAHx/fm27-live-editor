// Where the editing code's messages go: the engine writes them to its log file.
using System;

namespace FM27LiveEditor;

public static class Log
{
    public static Action<string, string> Sink = (level, text) => Console.WriteLine($"[{level}] {text}");

    public static void Info(string text) => Sink("Info", text);
    public static void Warn(string text) => Sink("Warning", text);
    public static void Error(string text) => Sink("Error", text);
}
