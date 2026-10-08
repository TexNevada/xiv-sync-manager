using System;
using System.Threading.Tasks;

namespace XivSyncManager;

// Snapshots must be independent of live configuration. All writes use one gate so an older
// background snapshot cannot overwrite a newer saved pause-restoration record.
internal sealed class ConfigurationSaveQueue<T>(Action<T> write, Action<Exception> reportError)
{
    private readonly object stateGate = new();
    private readonly object writeGate = new();
    private long revision;
    private long writtenRevision;
    private (long Revision, T Snapshot)? pending;
    private Task worker = Task.CompletedTask;
    private bool running;

    internal void SaveNow(T snapshot)
    {
        long ticket;
        lock (stateGate)
        {
            ticket = ++revision;
            pending = null;
        }
        Write(ticket, snapshot, background: false);
    }

    internal void SaveBackground(T snapshot)
    {
        lock (stateGate)
        {
            pending = (++revision, snapshot);
            if (running) return;
            running = true;
            worker = Task.Run(Drain);
        }
    }

    internal Task FlushAsync()
    {
        lock (stateGate) return worker;
    }

    private void Drain()
    {
        while (true)
        {
            (long Revision, T Snapshot) request;
            lock (stateGate)
            {
                if (pending == null)
                {
                    running = false;
                    return;
                }
                request = pending.Value;
                pending = null;
            }
            try { Write(request.Revision, request.Snapshot, background: true); }
            catch (Exception exception) { reportError(exception); }
        }
    }

    private void Write(long ticket, T snapshot, bool background)
    {
        lock (writeGate)
        {
            lock (stateGate)
                if (ticket <= writtenRevision || background && ticket != revision) return;
            write(snapshot);
            lock (stateGate) writtenRevision = ticket;
        }
    }
}
