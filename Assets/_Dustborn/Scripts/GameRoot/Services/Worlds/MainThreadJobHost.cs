using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Unity.Jobs;

public sealed class MainThreadJobHost : IGenerationJobHost, IDisposable
{
    private const int WAKE_MILLISECONDS = 1;
    private const int ABORT_POLL_MILLISECONDS = 50;

    private sealed class Request
    {
        public Func<JobHandle> Schedule;
        public JobHandle Handle;
        public Exception Error;
        public readonly ManualResetEventSlim Done = new();
    }

    private readonly ConcurrentQueue<Request> _incoming = new();
    private readonly List<Request> _waiting = new();
    private readonly List<JobHandle> _outstanding = new();
    private readonly SemaphoreSlim _arrived = new(0);
    private readonly CancellationToken _token;
    private volatile bool _stopped;

    public MainThreadJobHost(CancellationToken token)
    {
        _token = token;
    }

    public JobHandle Schedule(Func<JobHandle> schedule)
    {
        var request = new Request { Schedule = schedule };
        Post(request);
        return request.Handle;
    }

    public void Complete(JobHandle handle)
    {
        Post(new Request { Handle = handle });
    }

    public void Pump(double budgetMilliseconds)
    {
        Stopwatch clock = Stopwatch.StartNew();

        do
        {
            while (_incoming.TryDequeue(out Request request))
                Start(request);

            Finish();

            double left = budgetMilliseconds - clock.Elapsed.TotalMilliseconds;
            if (left > 0 && _incoming.IsEmpty)
                _arrived.Wait(Math.Min(WAKE_MILLISECONDS, (int)Math.Ceiling(left)));
        }
        while (clock.Elapsed.TotalMilliseconds < budgetMilliseconds);
    }

    public void Stop()
    {
        CompleteAll();
        _stopped = true;

        while (_incoming.TryDequeue(out Request request))
            Release(request, new OperationCanceledException());

        foreach (Request request in _waiting)
            Release(request, new OperationCanceledException());

        _waiting.Clear();
    }

    public void Dispose() => Stop();

    private void Post(Request request)
    {
        if (_stopped)
            throw new OperationCanceledException();

        _incoming.Enqueue(request);
        _arrived.Release();

        while (!request.Done.Wait(ABORT_POLL_MILLISECONDS))
        {
            if (_stopped && !request.Done.IsSet)
                throw new OperationCanceledException();
        }

        if (request.Error != null)
            throw request.Error is OperationCanceledException ? new OperationCanceledException() : new InvalidOperationException("A generation job failed on the main thread", request.Error);
    }

    private void Start(Request request)
    {
        if (_token.IsCancellationRequested)
        {
            CompleteAll();

            if (request.Schedule == null)
                CompleteOne(request.Handle);

            Release(request, new OperationCanceledException());
            return;
        }

        if (request.Schedule == null)
        {
            _waiting.Add(request);
            return;
        }

        try
        {
            request.Handle = request.Schedule();
            _outstanding.Add(request.Handle);
            JobHandle.ScheduleBatchedJobs();
            Release(request, null);
        }
        catch (Exception exception)
        {
            Release(request, exception);
        }
    }

    private void Finish()
    {
        for (int i = _waiting.Count - 1; i >= 0; i--)
        {
            Request request = _waiting[i];
            if (!request.Handle.IsCompleted)
                continue;

            _waiting.RemoveAt(i);

            try
            {
                CompleteOne(request.Handle);
                Release(request, null);
            }
            catch (Exception exception)
            {
                Release(request, exception);
            }
        }
    }

    private void CompleteOne(JobHandle handle)
    {
        handle.Complete();
        _outstanding.Remove(handle);
    }

    private void CompleteAll()
    {
        foreach (JobHandle handle in _outstanding)
            handle.Complete();

        _outstanding.Clear();
    }

    private static void Release(Request request, Exception error)
    {
        request.Error = error;
        request.Done.Set();
    }
}
