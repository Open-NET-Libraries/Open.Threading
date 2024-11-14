using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Open.Threading.Tasks;

public class AsyncQuery<TResult>(Func<Progress, TResult> query, TaskScheduler? scheduler = null)
	: AsyncProcess(scheduler)
{
#if NETSTANDARD2_0
#else
	[AllowNull]
#endif
	TResult _latest = default!;

	protected new Func<Progress, TResult>? Closure { get; private set; } = query ?? throw new ArgumentNullException(nameof(query));

	protected Task<TResult>? InternalTaskValued { get; private set; }

	protected Task<TResult> EnsureProcessValued(bool once, TimeSpan? timeAllowedBeforeRefresh = null)
	{
		Task<TResult>? task = null;

		SyncLock!.ReadWriteConditional(
			_ =>
			{
				task = InternalTaskValued;
				return (task is null || !once && !task.IsActive()) // No action, or completed?
					&& (!timeAllowedBeforeRefresh.HasValue // Now?
						|| timeAllowedBeforeRefresh.Value < DateTime.Now - LatestCompleted); // Or later?
			},
			() =>
			{
				task = new Task<TResult>(Process!, new Progress());
				task.Start(Scheduler);
				InternalTask = InternalTaskValued = task;
				Count++;
			}
		);

		// action could be null in some cases where timeAllowedBeforeRefresh condition is still met.
		return task!;
	}

	protected override Task EnsureProcess(bool once, TimeSpan? timeAllowedBeforeRefresh = null)
		=> EnsureProcessValued(once, timeAllowedBeforeRefresh);

#if NETSTANDARD2_0
#else
	[return: MaybeNull]
#endif
	protected new TResult Process(object progress)
	{
		var p = (Progress)progress;
		try
		{
			var result = Closure!(p);
			Latest = result;
			return result;
		}
		catch (Exception ex)
		{
			SyncLock!.Write(() => LatestCompleted = DateTime.Now);
			p.Failed(ex.ToString());
		}
		return default!;
	}

	public bool IsCurrentDataReady => InternalTask?.IsActive() == false;

	public bool IsCurrentDataStale(TimeSpan timeAllowedBeforeStale) => LatestCompleted.Add(timeAllowedBeforeStale) < DateTime.Now;

	public override Progress Progress
	{
		get
		{
			var t = InternalTask;
			if (t is not null) return (Progress)t.AsyncState;
			var result = new Progress();
			if (IsLatestAvailable)
				result.Finish();
			return result;
		}
	}

	public virtual bool IsLatestAvailable
	{
		get;
		protected set;
	}

	protected virtual TResult GetLatest() => _latest;

	public virtual void OverrideLatest(TResult value, DateTime? completed = null)
		=> SyncLock!.Write(() =>
		{
			_latest = value;
			LatestCompleted = completed ?? DateTime.Now;
			IsLatestAvailable = true;
		});

	public virtual void OverrideLatest(TResult value, Func<TResult, TResult, bool> useNewValueEvaluator, DateTime? completed = null)
		=> SyncLock!.ReadWriteConditional(
		(_) => useNewValueEvaluator(_latest, value),
		() =>
		{
			_latest = value;
			LatestCompleted = completed ?? DateTime.Now;
			IsLatestAvailable = true;
		});

	public TResult Latest
	{
		get => GetLatest();
		protected set => OverrideLatest(value);
	}

	public TResult LatestEnsured
		=> GetLatestOrRunning(out _);

	public Task<TResult> LatestEnsuredAsync
		=> TryGetLatest(out var result, out _)
			? Task.FromResult(result)
			: RunningValueAsync;

	public bool WaitForRunningToComplete(TimeSpan? waitForCurrentTimeout = null)
	{
		var task = SyncLock!.Read(() => InternalTaskValued);
		if (task is null) return false;
		if (waitForCurrentTimeout.HasValue)
			task.Wait(waitForCurrentTimeout.Value);
		else
			task.Wait();
		return true;
	}

	public TResult RunningValue
	{
		get
		{
			var task = SyncLock!.Read(() => InternalTaskValued);
			return task is null ? GetRunningValue() : task.Result;
		}
	}

	public Task<TResult> RunningValueAsync
	{
		get
		{
			var task = SyncLock!.Read(() => InternalTaskValued);
			return task ?? EnsureProcessValued(false);
		}
	}

	public TResult ActiveRunningValueOrLatestPossible
	{
		get
		{
			WaitForRunningToComplete();

			return HasBeenRun // This is in the case where possibly the app-pool has been reset.
				? LatestEnsured
				: GetRunningValue();
		}
	}

	public virtual bool TryGetLatest(
#if NETSTANDARD2_0
#else
		[NotNullWhen(true)]
#endif
		out TResult latest,
		out DateTime completed)
	{
		var result = default(TResult);
		var resultComplete = DateTime.MinValue;
		var isReady = SyncLock!.Read(() =>
		{
			result = _latest;
			resultComplete = LatestCompleted;
			return IsLatestAvailable;
		});
		latest = result!;
		completed = resultComplete;
		return isReady;
	}

	public virtual bool TryGetLatest(out TResult latest)
		=> TryGetLatest(out latest, out _);

	public virtual bool TryGetLatestOrStart(out TResult latest, out DateTime completed)
	{
		var result = TryGetLatest(out latest, out completed);
		if (!result) EnsureProcessValued(true);
		return result;
	}

	public virtual bool TryGetLatestOrStart(out TResult latest)
		=> TryGetLatestOrStart(out latest, out _);

	public virtual bool TryGetLatestOrStart()
		=> TryGetLatestOrStart(out _, out _);

	public TResult Refresh(TimeSpan? timeAllowedBeforeRefresh = null, TimeSpan? waitForCurrentTimeout = null)
	{
		EnsureProcessValued(false, timeAllowedBeforeRefresh);
		if (waitForCurrentTimeout.HasValue)
			WaitForRunningToComplete(waitForCurrentTimeout);
		return Latest;
	}

	public TResult RefreshNow(TimeSpan? waitForCurrentTimeout = null)
		=> Refresh(null, waitForCurrentTimeout);

	// Will hold the requesting thread until the action is available.
	public TResult GetRunningValue()
		=> EnsureProcessValued(false).Result;

	public TResult GetLatestOrRunning(out DateTime completed)
	{
		if (TryGetLatest(out var result, out completed)) return result;
		result = RunningValue;
		completed = DateTime.Now;
		return result;
	}

	protected override void OnDispose()
	{
		base.OnDispose();
		_latest = default!;
		Closure = null;
	}
}
