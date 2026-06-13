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
    private readonly ConcurrentDictionary<nint, CurlTransferState> _pending = new();
    private readonly Thread _thread;
    private volatile bool _running = true;
    private bool _disposed;
    private int _wakeupsInFlight;
    private int _wakeupsClosed;
    
    /// <summary>
    /// Creates a new event loop.
    /// </summary>
    public CurlEventLoop(CurlHandlerOptions? options = null)
    {
        CurlGlobal.EnsureInitialized();
        _multi = NativeMethods.MultiInit();
        if (_multi == 0)
            throw new InvalidOperationException("Failed to initialize curl multi handle");

        try
        {
            if (options is not null)
                ApplyMultiOptions(options);
        }
        catch (Exception ex)
        {
            var cleanupCode = NativeMethods.MultiCleanup(_multi);
            if (cleanupCode != CurlMultiCode.Ok)
            {
                throw new AggregateException(
                    "Failed to configure and clean up curl multi handle.",
                    ex,
                    CurlNativeErrors.CreateMultiException(cleanupCode, "curl_multi_cleanup"));
            }

            throw;
        }

        _thread = new(RunLoop)
        {
            IsBackground = true,
            Name = "CurlEventLoop"
        };
        _thread.Start();
    }

    private void ApplyMultiOptions(CurlHandlerOptions options)
    {
        SetMultiLongIfPresent(CurlMultiOption.MaxTotalConnections, options.MaxTotalConnections);
        SetMultiLongIfPresent(CurlMultiOption.MaxHostConnections, options.MaxConnectionsPerHost);
        SetMultiLongIfPresent(CurlMultiOption.MaxConnects, options.MaxConnects);
    }

    private void SetMultiLongIfPresent(CurlMultiOption option, int? value)
    {
        if (!value.HasValue)
            return;

        CurlNativeErrors.ThrowIfMultiError(
            NativeMethods.MultiSetOptLong(_multi, option, value.Value),
            $"curl_multi_setopt({option})");
    }

    /// <summary>
    /// Queues a wrapper for execution and returns a task for the result.
    /// </summary>
    public Task<CurlResponse> ExecuteAsync(CurlEasyWrapper wrapper, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var tcs = new TaskCompletionSource<CurlResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transfer = new CurlTransferState(this, wrapper, tcs, cancellationToken);
        wrapper.BeginTransfer(cancellationToken);
        transfer.RegisterCancellation();

        try
        {
            _workQueue.Add(WorkItem.Add(transfer));
            WakeEventLoop();
        }
        catch (InvalidOperationException)
        {
            // The handler was disposed after the guard above and the work queue
            // is completed; surface one disposal error instead of the queue's
            // internal exception. ObjectDisposedException derives from
            // InvalidOperationException, so this also covers a disposed queue.
            transfer.Dispose();
            throw new ObjectDisposedException(GetType().FullName);
        }
        catch
        {
            transfer.Dispose();
            throw;
        }

        return tcs.Task;
    }

    /// <summary>
    /// Queues a wrapper for streaming execution and returns a task that completes when response headers arrive.
    /// </summary>
    public Task<CurlResponseHeaders> ExecuteStreamingAsync(
        CurlEasyWrapper wrapper,
        CurlStreamingResponseState streamingResponse,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var transfer = new CurlTransferState(this, wrapper, completion: null, cancellationToken, streamingResponse);
        streamingResponse.SetCancelTransfer(transfer.RequestCancellation);
        streamingResponse.SetResumeTransfer(transfer.RequestResume);
        wrapper.BeginTransfer(cancellationToken);
        transfer.RegisterCancellation();

        try
        {
            _workQueue.Add(WorkItem.Add(transfer));
            WakeEventLoop();
        }
        catch (InvalidOperationException)
        {
            // The handler was disposed after the guard above and the work queue
            // is completed; surface one disposal error instead of the queue's
            // internal exception. ObjectDisposedException derives from
            // InvalidOperationException, so this also covers a disposed queue.
            transfer.Dispose();
            throw new ObjectDisposedException(GetType().FullName);
        }
        catch
        {
            transfer.Dispose();
            throw;
        }

        return streamingResponse.HeadersTask;
    }

    /// <summary>
    /// Queues removal of a transfer (for cancellation/abort).
    /// </summary>
    public void QueueRemoval(CurlTransferState transfer)
    {
        try
        {
            _workQueue.Add(WorkItem.Remove(transfer));
            WakeEventLoop();
        }
        catch (InvalidOperationException)
        {
            // Work queue is already completed, ignore
        }
    }

    /// <summary>
    /// Queues unpausing of a transfer whose write callback paused it.
    /// </summary>
    public void QueueResume(CurlTransferState transfer)
    {
        try
        {
            _workQueue.Add(WorkItem.Resume(transfer));
            WakeEventLoop();
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
                        break; // CompleteAdding was called, shut down
                    }
                    continue;
                }

                CurlNativeErrors.ThrowIfMultiError(
                    NativeMethods.MultiPerform(_multi, out _),
                    "curl_multi_perform");

                ProcessCompletedTransfers();

                // The 100 ms poll timeout bounds how long the loop blocks so it
                // keeps draining the work queue between I/O events.
                CurlNativeErrors.ThrowIfMultiError(
                    NativeMethods.MultiPoll(_multi, 0, 0, 100, out _),
                    "curl_multi_poll");
            }
            catch (CurlMultiException ex)
            {
                FailPendingTransfers(ex);
            }
            catch (ObjectDisposedException)
            {
                // Work queue was disposed, exit gracefully
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CurlEventLoop] Unexpected error: {ex}");
                FailPendingTransfers(ex);
            }
        }
    }

    private void ProcessWorkItem(WorkItem item)
    {
        switch (item.Type)
        {
            case WorkType.Add:
                if (item.Transfer != null)
                {
                    var transfer = item.Transfer;
                    if (transfer.IsCancellationRequested)
                    {
                        CompleteCanceled(transfer);
                        break;
                    }

                    if (!_pending.TryAdd(transfer.Handle, transfer))
                    {
                        CompleteException(
                            transfer,
                            new InvalidOperationException("A transfer is already pending for this curl easy handle."));
                        break;
                    }

                    var addCode = NativeMethods.MultiAddHandle(_multi, transfer.Handle);
                    if (addCode != CurlMultiCode.Ok)
                    {
                        TryRemovePending(transfer);
                        CompleteException(
                            transfer,
                            CurlNativeErrors.CreateMultiException(addCode, "curl_multi_add_handle"));
                    }
                }
                break;

            case WorkType.Remove:
                if (item.Transfer != null)
                {
                    var transfer = item.Transfer;

                    // Check pending first: a transfer that already completed
                    // must not abort the wrapper, which may be reused by now.
                    if (!TryRemovePending(transfer))
                        break;

                    transfer.MarkCancellationRequested();

                    var removeError = RemoveFromMulti(transfer, "curl_multi_remove_handle");
                    if (removeError is null)
                        CompleteCanceled(transfer);
                    else
                        CompleteException(transfer, removeError);
                }
                break;

            case WorkType.Resume:
                if (item.Transfer is { } resumeTarget &&
                    _pending.TryGetValue(resumeTarget.Handle, out var pendingTransfer) &&
                    ReferenceEquals(pendingTransfer, resumeTarget))
                {
                    var pauseCode = NativeMethods.EasyPause(resumeTarget.Handle, 0);
                    if (pauseCode != CurlCode.Ok && TryRemovePending(resumeTarget))
                    {
                        var resumeRemoveError = RemoveFromMulti(resumeTarget, "curl_multi_remove_handle");
                        CompleteException(
                            resumeTarget,
                            resumeRemoveError ?? CurlNativeErrors.CreateException(
                                pauseCode,
                                "curl_easy_pause",
                                resumeTarget.Wrapper.GetErrorMessage(),
                                resumeTarget.Wrapper.CallbackException));
                    }
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
            if (!_pending.TryRemove(msg.EasyHandle, out var transfer)) continue;

            if (msg.Data.Result == CurlCode.Ok)
            {
                CurlResponse response;
                try
                {
                    response = transfer.Wrapper.BuildResponse();
                }
                catch (Exception ex)
                {
                    var buildRemoveError = RemoveFromMulti(transfer, "curl_multi_remove_handle");
                    CompleteException(transfer, buildRemoveError ?? ex);
                    continue;
                }

                var removeError = RemoveFromMulti(transfer, "curl_multi_remove_handle");
                if (removeError is null)
                    CompleteSuccess(transfer, response);
                else
                    CompleteException(transfer, removeError);
            }
            else if (msg.Data.Result == CurlCode.AbortedByCallback && transfer.IsCancellationRequested)
            {
                var removeError = RemoveFromMulti(transfer, "curl_multi_remove_handle");
                if (removeError is null)
                    CompleteCanceled(transfer);
                else
                    CompleteException(transfer, removeError);
            }
            else
            {
                var callbackException = transfer.Wrapper.CallbackException;
                var exception = CurlNativeErrors.CreateException(
                    msg.Data.Result,
                    "curl transfer",
                    transfer.Wrapper.GetErrorMessage(),
                    callbackException);

                var removeError = RemoveFromMulti(transfer, "curl_multi_remove_handle");
                CompleteException(transfer, removeError ?? exception);
            }
        }
    }

    private bool TryRemovePending(CurlTransferState transfer)
    {
        var pair = new KeyValuePair<nint, CurlTransferState>(transfer.Handle, transfer);
        return ((ICollection<KeyValuePair<nint, CurlTransferState>>)_pending).Remove(pair);
    }

    private Exception? RemoveFromMulti(CurlTransferState transfer, string operation)
    {
        var code = NativeMethods.MultiRemoveHandle(_multi, transfer.Handle);
        return code == CurlMultiCode.Ok
            ? null
            : CurlNativeErrors.CreateMultiException(code, operation);
    }

    private void FailPendingTransfers(Exception exception)
    {
        foreach (var transfer in _pending.Values)
        {
            if (!TryRemovePending(transfer))
                continue;

            var removeError = RemoveFromMulti(transfer, "curl_multi_remove_handle");
            CompleteException(transfer, removeError ?? exception);
        }
    }

    private void WakeEventLoop()
    {
        // The in-flight counter pairs with Dispose, which closes wakeups and
        // waits for the counter to drain before freeing the multi handle, so
        // this can never call into a freed handle. Skipping the wakeup once
        // shutdown starts is safe: the loop also wakes from the 100 ms poll
        // timeout, and CompleteAdding wakes the idle queue path.
        Interlocked.Increment(ref _wakeupsInFlight);
        try
        {
            if (Volatile.Read(ref _wakeupsClosed) == 1)
                return;

            var code = NativeMethods.MultiWakeup(_multi);
            if (code == CurlMultiCode.Ok)
                return;

            if (code == CurlMultiCode.WakeupFailure)
            {
                // Non-fatal: the loop also wakes from the 100 ms poll timeout, and
                // CompleteAdding wakes the idle queue path during disposal.
                return;
            }

            Console.Error.WriteLine(
                $"[CurlEventLoop] curl_multi_wakeup failed: {CurlMultiException.GetErrorMessage(code)}");
        }
        finally
        {
            Interlocked.Decrement(ref _wakeupsInFlight);
        }
    }

    private static void CompleteSuccess(CurlTransferState transfer, CurlResponse response)
    {
        transfer.Dispose();
        transfer.StreamingResponse?.CompleteSuccess(response);
        transfer.Completion?.TrySetResult(response);
    }

    private static void CompleteCanceled(CurlTransferState transfer)
    {
        transfer.Wrapper.MarkCancellationRequested();
        transfer.Dispose();
        transfer.StreamingResponse?.CompleteCanceled(transfer.CancellationToken);

        if (transfer.CancellationToken.IsCancellationRequested)
            transfer.Completion?.TrySetCanceled(transfer.CancellationToken);
        else
            transfer.Completion?.TrySetCanceled();
    }

    private static void CompleteException(CurlTransferState transfer, Exception exception)
    {
        transfer.Dispose();
        transfer.StreamingResponse?.CompleteException(exception);
        transfer.Completion?.TrySetException(exception);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _running = false;

        _workQueue.CompleteAdding();

        // Wake the loop out of MultiPoll so it observes _running and exits.
        if (_multi != 0)
            WakeEventLoop();

        if (_thread.IsAlive && !_thread.Join(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException(
                "Timed out waiting for the curl event loop thread to exit. The native multi handle was not cleaned up to avoid racing a running event loop.");
        }

        // The loop thread exits without draining the queue, so complete any
        // submission that raced CompleteAdding; it was never added to the
        // multi handle and nothing else will finish its task.
        while (_workQueue.TryTake(out var item))
        {
            if (item.Type == WorkType.Add && item.Transfer is { } queuedTransfer)
                CompleteCanceled(queuedTransfer);
        }

        foreach (var transfer in _pending.Values)
        {
            if (!TryRemovePending(transfer))
                continue;

            var removeError = RemoveFromMulti(transfer, "curl_multi_remove_handle");
            if (removeError is null)
                CompleteCanceled(transfer);
            else
                CompleteException(transfer, removeError);
        }

        // A caller whose queue add won the race against CompleteAdding may
        // still be inside WakeEventLoop; close wakeups (full fence, so a waker
        // that increments afterwards observes the flag) and wait the last call
        // out before freeing the multi handle.
        Interlocked.Exchange(ref _wakeupsClosed, 1);
        var spinner = new SpinWait();
        while (Volatile.Read(ref _wakeupsInFlight) != 0)
            spinner.SpinOnce();

        if (_multi != 0)
            CurlNativeErrors.ThrowIfMultiError(NativeMethods.MultiCleanup(_multi), "curl_multi_cleanup");

        _workQueue.Dispose();
    }
}

/// <summary>
/// Work item types for the event loop.
/// </summary>
internal enum WorkType
{
    Add,
    Remove,
    Resume
}

/// <summary>
/// Work item for the event loop queue.
/// </summary>
internal readonly record struct WorkItem(WorkType Type, CurlTransferState? Transfer)
{
    public static WorkItem Add(CurlTransferState transfer) => new(WorkType.Add, transfer);
    public static WorkItem Remove(CurlTransferState transfer) => new(WorkType.Remove, transfer);
    public static WorkItem Resume(CurlTransferState transfer) => new(WorkType.Resume, transfer);
}

internal sealed class CurlTransferState : IDisposable
{
    private readonly CurlEventLoop _eventLoop;
    private readonly object _wrapperLock = new();
    private CancellationTokenRegistration _cancellationRegistration;
    private int _cancellationRequested;
    private bool _completed;
    private int _disposed;

    public CurlTransferState(
        CurlEventLoop eventLoop,
        CurlEasyWrapper wrapper,
        TaskCompletionSource<CurlResponse>? completion,
        CancellationToken cancellationToken,
        CurlStreamingResponseState? streamingResponse = null)
    {
        _eventLoop = eventLoop;
        Wrapper = wrapper;
        Handle = wrapper.Handle;
        Completion = completion;
        CancellationToken = cancellationToken;
        StreamingResponse = streamingResponse;
    }

    public CurlEasyWrapper Wrapper { get; }

    public nint Handle { get; }

    public TaskCompletionSource<CurlResponse>? Completion { get; }

    public CancellationToken CancellationToken { get; }

    public CurlStreamingResponseState? StreamingResponse { get; }

    public bool IsCancellationRequested =>
        Volatile.Read(ref _cancellationRequested) == 1 || CancellationToken.IsCancellationRequested || Wrapper.IsAborted;

    public void RegisterCancellation()
    {
        if (!CancellationToken.CanBeCanceled)
            return;

        _cancellationRegistration = CancellationToken.Register(
            static state => ((CurlTransferState)state!).RequestCancellation(),
            this,
            useSynchronizationContext: false);

        if (CancellationToken.IsCancellationRequested)
            RequestCancellation();
    }

    public void RequestCancellation()
    {
        // The wrapper may already be back in the pool serving an unrelated
        // request once this transfer completed; the lock pairs with Dispose
        // so a stale cancel can never abort it after completion.
        lock (_wrapperLock)
        {
            if (_completed)
                return;

            MarkCancellationRequested();
        }

        _eventLoop.QueueRemoval(this);
    }

    public void RequestResume()
    {
        _eventLoop.QueueResume(this);
    }

    public void MarkCancellationRequested()
    {
        if (Interlocked.Exchange(ref _cancellationRequested, 1) == 0)
            Wrapper.MarkCancellationRequested();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        lock (_wrapperLock)
        {
            _completed = true;
        }

        _cancellationRegistration.Dispose();
        Wrapper.EndTransfer();
    }
}
