using System.Diagnostics.CodeAnalysis;

namespace Nefarius.DsHidMini.ControlApp.Models;

/// <summary>
///     Named mutex + activation event for single-instance ControlApp.
///     The event is opened first so a secondary process cannot Set+Dispose it
///     before the primary has a handle.
/// </summary>
internal sealed class SingleInstanceLifetime : IDisposable
{
    public const string MutexName = "Nefarius.DsHidMini.ControlApp.SingleInstance";
    public const string ShowWindowEventName = "Nefarius.DsHidMini.ControlApp.ShowWindow";
    public const string HandoffArgumentPrefix = "--instance-handoff=";

    private readonly string _mutexName;
    private Mutex? _mutex;

    public SingleInstanceLifetime()
        : this(MutexName, ShowWindowEventName)
    {
    }

    public SingleInstanceLifetime(string mutexName, string eventName)
    {
        _mutexName = mutexName;
        ShowWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
        _mutex = new Mutex(true, mutexName, out bool createdNew);
        IsPrimary = createdNew;
    }

    private SingleInstanceLifetime(string mutexName, EventWaitHandle showWindowEvent, Mutex ownedMutex)
    {
        _mutexName = mutexName;
        ShowWindowEvent = showWindowEvent;
        _mutex = ownedMutex;
        IsPrimary = true;
    }

    public EventWaitHandle ShowWindowEvent { get; }

    public bool IsPrimary { get; }

    public static string GetHandoffReadyEventName(string token)
    {
        return "Nefarius.DsHidMini.ControlApp.HandoffReady." + token;
    }

    public static EventWaitHandle CreateHandoffReadyEvent(string token)
    {
        return new EventWaitHandle(false, EventResetMode.ManualReset, GetHandoffReadyEventName(token));
    }

    public static bool TryParseHandoffToken(IEnumerable<string> args, [NotNullWhen(true)] out string? token)
    {
        foreach (string arg in args)
        {
            if (!arg.StartsWith(HandoffArgumentPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            string value = arg[HandoffArgumentPrefix.Length..];
            if (Guid.TryParseExact(value, "N", out _))
            {
                token = value;
                return true;
            }
        }

        token = null;
        return false;
    }

    /// <summary>
    ///     Successor path: open the existing mutex (parent still owns it), signal
    ///     ready, then wait to inherit ownership so a competing startup cannot
    ///     become primary during the handoff.
    /// </summary>
    public static SingleInstanceLifetime? TryAdoptAfterHandoff(
        string mutexName,
        string eventName,
        string token,
        TimeSpan mutexWait)
    {
        EventWaitHandle showWindowEvent = new(false, EventResetMode.AutoReset, eventName);
        Mutex mutex = new(false, mutexName);
        try
        {
            using EventWaitHandle ready = EventWaitHandle.OpenExisting(GetHandoffReadyEventName(token));
            ready.Set();

            try
            {
                if (!mutex.WaitOne(mutexWait))
                {
                    mutex.Dispose();
                    showWindowEvent.Dispose();
                    return null;
                }
            }
            catch (AbandonedMutexException)
            {
                // Parent exited without releasing; this process now owns the mutex.
            }

            return new SingleInstanceLifetime(mutexName, showWindowEvent, mutex);
        }
        catch
        {
            mutex.Dispose();
            showWindowEvent.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     Drops mutex ownership and closes the handle so the waiting successor
    ///     can acquire the same kernel object.
    /// </summary>
    public void ReleaseOwnership()
    {
        if (_mutex is null)
        {
            return;
        }

        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not owned (secondary instance, or already released).
        }

        _mutex.Dispose();
        _mutex = null;
    }

    /// <summary>
    ///     Recreates the named mutex if this instance no longer holds a handle.
    ///     No-op when ownership was never released.
    /// </summary>
    public void ReacquireOwnership()
    {
        if (_mutex is not null)
        {
            return;
        }

        _mutex = new Mutex(true, _mutexName, out _);
    }

    public void Dispose()
    {
        ReleaseOwnership();
        ShowWindowEvent.Dispose();
    }
}
