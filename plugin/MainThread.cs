// Runs work on Unity's main thread, for the few requests that need Unity
// itself (the server answers on its own thread). MenuButton.Update drains it.
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace FM27LiveEditor;

public static class MainThread
{
    static readonly ConcurrentQueue<Action> work = new();

    public static T Run<T>(Func<T> f, int ms = 5000)
    {
        T result = default;
        Exception error = null;
        var done = new ManualResetEventSlim();
        work.Enqueue(() => { try { result = f(); } catch (Exception e) { error = e; } finally { done.Set(); } });
        if (!done.Wait(ms)) throw new EditorError("The game is busy. Try again.", 503);
        if (error != null) throw error;
        return result;
    }

    internal static void Drain() { while (work.TryDequeue(out var a)) a(); }
}
