using Open.Disposable;
using System.Diagnostics;
using System.Diagnostics.Contracts;

namespace Open.Threading;

public class AsyncProcess<T> : DisposableBase
		where T : new()
{
	protected AsyncProcess(TaskScheduler? scheduler)
		=> Scheduler = scheduler ?? TaskScheduler.Default;

	public AsyncProcess(Action<T> closure, TaskScheduler? scheduler = null)
		: this(scheduler)
		=> Closure = closure ?? throw new ArgumentNullException(nameof(closure));

	// Used to help prevent recusion.
	protected ReaderWriterLockSlim? SyncLock = new();

	protected TaskScheduler? Scheduler { get; private set; }

	protected Action<T>? Closure { get; private set; }

	protected Task? InternalTask { get; set; }

	public virtual DateTime LatestCompleted { get; protected set; }

	public Exception? LastFault { get; protected set; }

	public int Count { get; protected set; }

	public bool HasBeenRun => Count != 0;

	protected virtual void Process(object? progress)
	{
		Debug.Assert(progress is not null);
		var p = (T)progress;
		try
		{
			Closure!(p);
			SyncLock!.Write(() => LatestCompleted = DateTime.Now);
		}
		catch (Exception ex)
		{
			SyncLock!.Write(() => LastFault = ex);
		}
	}

	protected virtual Task EnsureProcess(bool once, TimeSpan? timeAllowedBeforeRefresh = null)
	{
		Task? task = null;
		SyncLock!.ReadWriteConditional(
			_ =>
			{
				task = InternalTask;
				return (task is null || !once && !task.IsActive()) // No action, or completed?
					&& (!timeAllowedBeforeRefresh.HasValue // Now?
						|| timeAllowedBeforeRefresh.Value < DateTime.Now - LatestCompleted); // Or later?
			}, () =>
			{
				task = new Task(Process, new T());

				Debug.Assert(Scheduler is not null);
				task.Start(Scheduler);
				InternalTask = task;
				Count++;
			}
		);

		return task!;
	}

	// ReSharper disable once MemberCanBeProtected.Global
	public bool IsRunning
		=> InternalTask?.IsActive() ?? false;

	public void Wait(bool once = true)
	{
		EnsureActive(once);
		InternalTask?.Wait();
	}

	public Task WaitAsync(bool once = true)
	{
		EnsureActive(once);
		return InternalTask ?? Task.CompletedTask;
	}

	public bool EnsureActive(bool once = false) => EnsureProcess(once).IsActive();

	// ReSharper disable once MemberCanBeProtected.Global
	public virtual T Progress
	{
		get
		{
			var t = InternalTask;
			if (t is null)
				return new T();

			var state = t.AsyncState;
			Debug.Assert(state is not null);
			return (T)state;
		}
	}

	protected override void OnDispose()
	{
		var syncLock = Interlocked.Exchange(ref SyncLock, null)!;
		syncLock.Write(() => { }); // get exclusivity first.
		syncLock.Dispose();
		Scheduler = null;
		InternalTask = null;
		Closure = null;
	}
}

public class AsyncProcess : AsyncProcess<Progress>
{
	protected AsyncProcess(TaskScheduler? scheduler) : base(scheduler)
	{
	}

	// ReSharper disable once MemberCanBeProtected.Global
	public AsyncProcess(Action<Progress> closure, TaskScheduler? scheduler = null)
		: base(closure, scheduler)
	{
	}

	public string TimeStatistics
	{
		get
		{
			var result = string.Empty;
			if (LatestCompleted != default)
				result += "\n" + (DateTime.Now - LatestCompleted).ToString() + " ago";

			if (!IsRunning) return result;
			var p = Progress;
			result += $"\n{p.EstimatedTimeLeftString} remaining";

			return result;
		}
	}

	protected override void Process(object? progress)
	{
		Debug.Assert(progress is not null);
		var p = (Progress)progress;
		try
		{
			Closure!(p);
		}
		catch (Exception ex)
		{
			SyncLock!.Write(() => LatestCompleted = DateTime.Now);
			p.Failed(ex.ToString());
		}
	}
}
