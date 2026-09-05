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

    private Mutex? _mutex;

    public SingleInstanceLifetime()
        : this(MutexName, ShowWindowEventName)
    {
    }

    public SingleInstanceLifetime(string mutexName, string eventName)
    {
        ShowWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
        _mutex = new Mutex(true, mutexName, out bool createdNew);
        IsPrimary = createdNew;
    }

    public EventWaitHandle ShowWindowEvent { get; }

    public bool IsPrimary { get; }

    /// <summary>
    ///     Drops mutex ownership and closes the handle so another process can create
    ///     the named mutex as primary before this process shuts down.
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

    public void Dispose()
    {
        ShowWindowEvent.Dispose();
        ReleaseOwnership();
    }
}
