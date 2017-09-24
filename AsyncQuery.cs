using System;
using System.Threading.Tasks;

namespace Open.Threading
{
    public class AsyncQuery<TResult> : AsyncProcess
	{
		TResult _latest;

		protected new Func<Progress, TResult> Closure
		{
			get;
			private set;
		}

		protected Task<TResult> InternalTaskValued
		{
			get;
			private set;
		}

		public AsyncQuery(Func<Progress, TResult> query, TaskScheduler scheduler = null)
			: base(scheduler)
		{
			Closure = query;
		}

		protected Task<TResult> EnsureProcessValued(bool once, TimeSpan? timeAllowedBeforeRefresh)
		{

			Task<TResult> task = null;

			SyncLock.ReadWriteConditionalOptimized(
				write =>
				{
					task = InternalTaskValued;
					return (task == null || !once && !task.IsActive()) // No action, or completed?
						&& (!timeAllowedBeforeRefresh.HasValue // Now?
							|| timeAllowedBeforeRefresh.Value < DateTime.Now - LatestCompleted); // Or later?
				}, () =>
				{

					task = new Task<TResult>((Func<object, TResult>)Process, new Progress());
					task.Start(Scheduler);
					InternalTask = InternalTaskValued = task;
					Count++;

				}
			);

			// action could be null in some cases where timeAllowedBeforeRefresh condition is still met.
			return task;
		}

		protected Task<TResult> EnsureProcessValued(bool once)
		{
			return EnsureProcessValued(once, null);
		}

		protected override Task EnsureProcess(bool once, TimeSpan? timeAllowedBeforeRefresh)
		{
			return EnsureProcessValued(once, timeAllowedBeforeRefresh);
		}

		//long _processCount = 0;
		protected new TResult Process(object progress)
		{

			var p = (Progress)progress;
			try
			{
				//Contract.Assert(Interlocked.Increment(ref _processCount) == 1);
				var result = Closure(p);
				Latest = result;
				return result;
			}
			catch (Exception ex)
			{
				SyncLock.Write(() => LatestCompleted = DateTime.Now);
				p.Failed(ex.ToString());
			}
			finally
			{
				//Interlocked.Decrement(ref _processCount);
			}
			return default(TResult);
		}

		public bool IsCurrentDataReady
		{
			get
			{
				var t = InternalTask;
				if (t == null)
					return false;
				return !t.IsActive();
			}
		}

		public bool IsCurrentDataStale(TimeSpan timeAllowedBeforeStale)
		{
			return LatestCompleted.Add(timeAllowedBeforeStale) < DateTime.Now;
		}

		public override Progress Progress
		{
			get
			{
				var t = InternalTask;
				if (t == null)
				{
					var result = new Progress();
					if (IsLatestAvailable)
						result.Finish();
					return result;
				}

				return (Progress)(t.AsyncState);
			}
		}

		public virtual bool IsLatestAvailable
		{
			get;
			protected set;
		}

		protected virtual TResult GetLatest()
		{
			return _latest;
		}

		public virtual void OverrideLatest(TResult value, DateTime? completed = null)
		{
			SyncLock.Write(() =>
			{
				_latest = value;
				LatestCompleted = completed ?? DateTime.Now;
				IsLatestAvailable = true;
			});
		}

		public virtual void OverrideLatest(TResult value, Func<TResult, TResult, bool> useNewValueEvaluator, DateTime? completed = null)
		{
			SyncLock.ReadWriteConditionalOptimized(
				(write) => useNewValueEvaluator(_latest, value),
				() =>
				{
					_latest = value;
					LatestCompleted = completed ?? DateTime.Now;
					IsLatestAvailable = true;
				});
		}

		public TResult Latest
		{
			get
			{
				return GetLatest();
			}
			protected set
			{
				OverrideLatest(value);
			}
		}


		public TResult LatestEnsured
		{
			get
			{
				DateTime completed;
				return GetLatestOrRunning(out completed);
			}
		}

		public bool WaitForRunningToComplete(TimeSpan? waitForCurrentTimeout = null)
		{
			var task = SyncLock.ReadValue(() => InternalTaskValued);
			if (task != null)
			{
				if (waitForCurrentTimeout.HasValue)
					task.Wait(waitForCurrentTimeout.Value);
				else
					task.Wait();
				return true;
			}
			return false;
		}


		public TResult RunningValue
		{
			get
			{
				var task = SyncLock.ReadValue(() => InternalTaskValued);
				return task == null ? GetRunningValue() : task.Result;
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

		public virtual bool TryGetLatest(out TResult latest, out DateTime completed)
		{
			TResult result = default(TResult);
			DateTime resultComplete = DateTime.MinValue;
			bool isReady = SyncLock.ReadValue(() =>
			{
				result = _latest;
				resultComplete = LatestCompleted;
				return IsLatestAvailable;
			});
			latest = result;
			completed = resultComplete;
			return isReady;
		}

		public virtual bool TryGetLatest(out TResult latest)
		{
			DateTime completed;
			return TryGetLatest(out latest, out completed);
		}

		public virtual bool TryGetLatestOrStart(out TResult latest, out DateTime completed)
		{
			var result = TryGetLatest(out latest, out completed);
			if (!result)
				EnsureProcessValued(true);
			return result;
		}

		public virtual bool TryGetLatestOrStart(out TResult latest)
		{
			DateTime completed;
			return TryGetLatestOrStart(out latest, out completed);
		}

		public virtual bool TryGetLatestOrStart()
		{
			TResult latest;
			DateTime completed;
			return TryGetLatestOrStart(out latest, out completed);
		}


		public TResult Refresh(TimeSpan? timeAllowedBeforeRefresh = null, TimeSpan? waitForCurrentTimeout = null)
		{
			EnsureProcessValued(false, timeAllowedBeforeRefresh);
			if (waitForCurrentTimeout.HasValue)
				WaitForRunningToComplete(waitForCurrentTimeout);
			return Latest;
		}

		public TResult RefreshNow(TimeSpan? waitForCurrentTimeout = null)
		{
			return Refresh(null, waitForCurrentTimeout);
		}

		// Will hold the requesting thread until the action is available.
		public TResult GetRunningValue()
		{
			return EnsureProcessValued(false).Result;
		}

		public TResult GetLatestOrRunning(out DateTime completed)
		{
			TResult result;
			if (!TryGetLatest(out result, out completed))
			{
				result = RunningValue;
				completed = DateTime.Now;
			}
			return result;
		}



		protected override void OnDispose(bool calledExplicitly)
		{
			base.OnDispose(calledExplicitly);
			_latest = default(TResult);
		}
	}
}
