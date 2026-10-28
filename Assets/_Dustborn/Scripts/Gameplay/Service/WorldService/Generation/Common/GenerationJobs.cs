using System;
using System.Threading;
using Unity.Collections;
using Unity.Jobs;

public interface IGenerationJobHost
{
    JobHandle Schedule(Func<JobHandle> schedule);
    void Complete(JobHandle handle);
}

public static class GenerationJobs
{
    public const Allocator ALLOCATOR = Allocator.Persistent;

    private static IGenerationJobHost _host;
    private static int _hostThread;

    private static bool Remote
    {
        get
        {
            IGenerationJobHost host = Volatile.Read(ref _host);
            return host != null && Thread.CurrentThread.ManagedThreadId != _hostThread;
        }
    }

    public static void Attach(IGenerationJobHost host)
    {
        if (Interlocked.CompareExchange(ref _host, host, null) != null)
            throw new InvalidOperationException("Another world is already being generated in the background");

        _hostThread = Thread.CurrentThread.ManagedThreadId;
    }

    public static void Detach(IGenerationJobHost host)
    {
        Interlocked.CompareExchange(ref _host, null, host);
    }

    public static void Run<T>(T job, int length, int batch) where T : struct, IJobParallelFor
    {
        if (!Remote)
        {
            job.Schedule(length, batch).Complete();
            return;
        }

        Complete(Schedule(() => job.Schedule(length, batch)));
    }

    public static void Run<T>(T job) where T : struct, IJob
    {
        if (!Remote)
        {
            job.Schedule().Complete();
            return;
        }

        Complete(Schedule(() => job.Schedule()));
    }

    public static JobHandle Schedule<T>(T job) where T : struct, IJob
    {
        return Remote ? Schedule(() => job.Schedule()) : job.Schedule();
    }

    public static void Complete(JobHandle handle)
    {
        if (Remote)
            _host.Complete(handle);
        else
            handle.Complete();
    }

    public static void Flush()
    {
        if (!Remote)
            JobHandle.ScheduleBatchedJobs();
    }

    private static JobHandle Schedule(Func<JobHandle> schedule) => _host.Schedule(schedule);
}
