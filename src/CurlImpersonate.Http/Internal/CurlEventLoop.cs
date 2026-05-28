using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using CurlImpersonate.Enums;
using CurlImpersonate.Native;
using CurlImpersonate.Structs;

namespace CurlImpersonate.Http.Internal;

/// <summary>
/// Dedicated thread + curl_multi event loop for driving concurrent transfers.
/// Uses simple poll-based approach (MultiPoll + MultiPerform).
/// </summary>
internal sealed class CurlEventLoop : IDisposable
{
    private readonly nint _multi;
    private readonly BlockingCollection<WorkItem> _workQueue = new();
    private readonly ConcurrentDictionary<nint, CurlEasyWrapper> _pending = new();
    private readonly Thread _thread;
    private volatile bool _running = true;
    private bool _disposed;
    
    /// <summary>
    /// Creates a new event loop.
    /// </summary>
    public CurlEventLoop()
    {
        CurlGlobal.Initialize();
        _multi = NativeMethods.MultiInit();
        if (_multi == 0)
            throw new InvalidOperationException("Failed to initialize curl multi handle");

        _thread = new(RunLoop)
        {
            IsBackground = true,
            Name = "CurlEventLoop"
        };
        _thread.Start();
    }

    /// <summary>
    /// Queues a wrapper for execution and returns a task for the result.
    /// </summary>
    public Task<CurlResponse> ExecuteAsync(CurlEasyWrapper wrapper, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<CurlResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        wrapper.CompletionSource = tcs;
        wrapper.SetEventLoop(this);

        // Register cancellation
        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(
                static state => ((CurlEasyWrapper)state!).Abort(),
                wrapper,
                useSynchronizationContext: false);
        }

        _workQueue.Add(WorkItem.Add(wrapper));
        NativeMethods.MultiWakeup(_multi);
        return tcs.Task;
    }

    /// <summary>
    /// Queues removal of a wrapper (for cancellation/abort).
    /// </summary>
    public void QueueRemoval(CurlEasyWrapper wrapper)
    {
        try
        {
            _workQueue.Add(WorkItem.Remove(wrapper));
            NativeMethods.MultiWakeup(_multi);
        }
        catch (InvalidOperationException)
        {
            // Work queue is already completed, ignore
        }
    }

    private void RunLoop()
    {
        while (_running)
        {
            try
            {
                // Process queued work items (non-blocking drain)
                while (_workQueue.TryTake(out var item, TimeSpan.Zero))
                {
                    ProcessWorkItem(item);
                }

                // If no pending transfers, block until work arrives (0% CPU when idle)
                if (_pending.IsEmpty)
                {
                    try
                    {
                        var item = _workQueue.Take();
                        ProcessWorkItem(item);
                    }
                    catch (InvalidOperationException)
                    {
                        break; // CompleteAdding was called — shut down
                    }
                    continue;
                }

                // Drive transfers first (process any immediate work)
                NativeMethods.MultiPerform(_multi, out _);

                // Check for completed transfers
                ProcessCompletedTransfers();

                // Poll for I/O events (100ms timeout allows work queue processing)
                NativeMethods.MultiPoll(_multi, 0, 0, 100, out _);
            }
            catch (ObjectDisposedException)
            {
                // Work queue was disposed, exit gracefully
                break;
            }
            catch (Exception ex)
            {
                // Log unexpected errors but keep the loop running
                Console.Error.WriteLine($"[CurlEventLoop] Unexpected error: {ex}");
            }
        }
    }

    private void ProcessWorkItem(WorkItem item)
    {
        switch (item.Type)
        {
            case WorkType.Add:
                if (item.Wrapper != null)
                {
                    _pending[item.Wrapper.Handle] = item.Wrapper;
                    NativeMethods.MultiAddHandle(_multi, item.Wrapper.Handle);
                }
                break;

            case WorkType.Remove:
                if (item.Wrapper != null && _pending.TryRemove(item.Wrapper.Handle, out var wrapper))
                {
                    NativeMethods.MultiRemoveHandle(_multi, wrapper.Handle);
                    wrapper.CompletionSource?.TrySetCanceled();
                }
                break;
        }
    }

    private void ProcessCompletedTransfers()
    {
        while (true)
        {
            var msgPtr = NativeMethods.MultiInfoRead(_multi, out _);
            if (msgPtr == 0)
                break;

            var msg = Marshal.PtrToStructure<CurlMsg>(msgPtr);
            if (msg.Msg != CurlMsgType.Done)
                continue;

            // TryRemove is the synchronization gate: both cancellation
            // (ProcessWorkItem) and completion (here) use it, ensuring
            // only one path processes a given handle.
            if (!_pending.TryRemove(msg.EasyHandle, out var wrapper)) continue;

            if (msg.Data.Result == CurlCode.Ok)
            {
                var response = wrapper.BuildResponse();
                NativeMethods.MultiRemoveHandle(_multi, wrapper.Handle);
                wrapper.CompletionSource?.TrySetResult(response);
            }
            else
            {
                var errorMessage = wrapper.GetErrorMessage();
                NativeMethods.MultiRemoveHandle(_multi, wrapper.Handle);
                wrapper.CompletionSource?.TrySetException(
                    new CurlException(msg.Data.Result, errorMessage));
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _running = false;

        // Signal queue closed and wake up the event loop to exit
        _workQueue.CompleteAdding();

        // Wake up thread from MultiPoll so it can check _running and exit
        if (_multi != 0)
            NativeMethods.MultiWakeup(_multi);

        // Wait for thread to exit (with longer timeout to ensure clean shutdown)
        if (_thread.IsAlive)
            _thread.Join(TimeSpan.FromSeconds(10));

        // Cancel all pending transfers
        foreach (var kvp in _pending)
        {
            NativeMethods.MultiRemoveHandle(_multi, kvp.Key);
            kvp.Value.CompletionSource?.TrySetCanceled();
        }
        _pending.Clear();

        if (_multi != 0)
            NativeMethods.MultiCleanup(_multi);

        _workQueue.Dispose();
    }
}

/// <summary>
/// Work item types for the event loop.
/// </summary>
internal enum WorkType
{
    Add,
    Remove
}

/// <summary>
/// Work item for the event loop queue.
/// </summary>
internal readonly record struct WorkItem(WorkType Type, CurlEasyWrapper? Wrapper)
{
    public static WorkItem Add(CurlEasyWrapper w) => new(WorkType.Add, w);
    public static WorkItem Remove(CurlEasyWrapper w) => new(WorkType.Remove, w);
}
