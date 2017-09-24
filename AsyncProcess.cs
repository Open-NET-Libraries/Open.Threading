using System;
using System.Threading.Tasks;
using System.Threading;
using Open.Disposable;

namespace Open.Threading
{
	public class AsyncProcess<T> : DisposableBase
		where T : new()
	{
		protected readonly ReaderWriterLockSlim SyncLock = new ReaderWriterLockSlim();

		protected TaskScheduler Scheduler
		{
			get;
			private set;
		}

		protected virtual Action<T> Closure
		{
			get;
			set;
		}

		protected Task InternalTask
		{
			get;
			set;
		}

		public virtual DateTime LatestCompleted
		{
			get;
			protected set;
		}

		protected AsyncProcess(TaskScheduler scheduler = null)
			: base()
		{
			Scheduler = scheduler ?? TaskScheduler.Default;
			Count = 0;
		}

		public AsyncProcess(Action<T> closure, TaskScheduler scheduler = null)
			: this(scheduler)
		{
			Closure = closure;
		}

		public int Count
		{
			get;
			protected set;
		}

		public bool HasBeenRun
		{
			get { return Count != 0; }
		}


		//long _processCount = 0;
		protected virtual void Process(object progress)
		{
			if(progress==null)
				throw new ArgumentNullException("progress");

			var p = (T)progress;
			try
			{
				//Contract.Assert(Interlocked.Increment(ref _processCount) == 1);
				Closure(p);
			}
			catch
			{
				SyncLock.Write(() => LatestCompleted = DateTime.Now);
			}
			finally
			{
				//Interlocked.Decrement(ref _processCount);
			}
		}

		protected virtual Task EnsureProcess(bool once, TimeSpan? timeAllowedBeforeRefresh)
		{
			Task task = null;
			SyncLock.ReadWriteConditionalOptimized(
				write=>{
					task = InternalTask;
					return (task == null || !once && !task.IsActive()) // No action, or completed?
						&& (!timeAllowedBeforeRefresh.HasValue // Now?
							|| timeAllowedBeforeRefresh.Value < DateTime.Now - LatestCompleted); // Or later?
				}, () => {

					task = new Task((Action<object>)Process, new T());
					task.Start(Scheduler);
					InternalTask = task;
					Count++;
					
				}
			);

			return task;
		}

		protected Task EnsureProcess(bool once)
		{
			return EnsureProcess(once, null);
		}

		public bool IsRunning
		{
			get
			{
				return InternalTask?.IsActive() ?? false;
			}
		}

		public void Wait()
		{
			EnsureActive(true);
            InternalTask?.Wait();
		}

		public bool EnsureActive(bool once = false)
		{
			return EnsureProcess(once).IsActive();
		}

		public virtual T Progress
		{
			get
			{
				var t = InternalTask;
				if (t == null)
					return new T();

				var state = t.AsyncState;
				T result = (T)state;
				return result;
			}
		}



		protected override void OnDispose(bool calledExplicitly)
		{
			SyncLock.Dispose();
			Scheduler = null;
			InternalTask = null;
			Closure = null;
		}

	}

	public class AsyncProcess : AsyncProcess<Progress>
	{
		protected AsyncProcess(TaskScheduler scheduler = null)
			: base(scheduler)
		{
		}

		public AsyncProcess(Action<Progress> closure, TaskScheduler scheduler = null)
			: this(scheduler)
		{
			Closure = closure;
		}

		public string TimeStatistics
		{
			get
			{
				var result = String.Empty;
				if (LatestCompleted != default(DateTime))
					result += "\n" + (DateTime.Now - LatestCompleted).ToString() + " ago";

				if (IsRunning)
				{
					var p = Progress;
					result += "\n" + p.EstimatedTimeLeftString + " remaining";
				}

				return result;
			}
		}

		protected override void Process(object progress)
		{
			var p = (Progress)progress;
			try
			{
				//Contract.Assert(Interlocked.Increment(ref _processCount) == 1);
				Closure(p);
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
		}

	}
}
