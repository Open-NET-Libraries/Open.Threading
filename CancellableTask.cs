using System;
using System.Threading;
using System.Threading.Tasks;

namespace Open.Threading
{
	/// <summary>
	/// A Task sub-class that simplifies cancelling.
	/// </summary>
	public class CancellableTask : Task, ICancellable
	{
		protected CancellationTokenSource TokenSource;

		public bool Cancel(bool onlyIfNotRunning)
		{
			var ts = Interlocked.Exchange(ref TokenSource, null); // Cancel can only be called once.

			if (ts == null || ts.IsCancellationRequested || IsCanceled || IsFaulted || IsCompleted)
				return false;

			var isRunning = Status == TaskStatus.Running;
			if (!onlyIfNotRunning || !isRunning)
				ts.Cancel();

			return !isRunning; 
		}

		public bool Cancel()
		{
			return Cancel(false);
		}

		protected static void Blank() { }

		public void Dispose()
		{
			Cancel();
		}

		protected CancellableTask(Action action)
			: base(action ?? Blank)
		{
		}

		protected CancellableTask(Action action, CancellationToken token)
			: base(action ?? Blank, token)
		{
		}

		protected CancellableTask()
			: this(Blank)
		{
		}

		protected CancellableTask(CancellationToken token)
			: this(Blank, token)
		{
		}

		public static CancellableTask Prepare(Action action)
		{
			var ts = new CancellationTokenSource();
			var token = ts.Token;
			return new CancellableTask(action, token)
			{
				TokenSource = ts // Could potentially call cancel before run actually happens.
			};
		}

		public static CancellableTask Start(TimeSpan delay, Action action = null, TaskScheduler scheduler = null)
		{
			CancellableTask cancellable;
			scheduler = scheduler ?? TaskScheduler.Default;

			if (delay < TimeSpan.Zero)
			{
				cancellable = new CancellableTask(action);
				cancellable.RunSynchronously();
			}
			else
			{
				var ts = new CancellationTokenSource();
				var token = ts.Token;
				cancellable = new CancellableTask(action, token)
				{
					TokenSource = ts // Could potentially call cancel before run actually happens.
				};

				if (delay == TimeSpan.Zero)
				{
					cancellable.Start(scheduler); 
				}
				else
				{
					cancellable.ContinueWith(t => cancellable.Cancel()); // If this is arbitrarily run before the delay, then cancel the delay.
					Delay(delay, token)
						.OnFullfilled(() => cancellable.Start(scheduler));
				}
			}

			return cancellable;
		}

		public static CancellableTask Start(int millisecondsDelay, Action action = null)
		{
			return Start(TimeSpan.FromMilliseconds(millisecondsDelay), action);
		}

		public static CancellableTask Start(Action action, TimeSpan? delay = null, TaskScheduler scheduler = null)
		{
			return Start(delay ?? TimeSpan.Zero, action, scheduler);
		}
	}

	/// <summary>
	/// A Task sub-class that simplifies cancelling.
	/// </summary>
	public class CancellableTask<T> : Task<T>, ICancellable
	{
		protected CancellationTokenSource TokenSource;

		public bool Cancel()
		{
			var ts = Interlocked.Exchange(ref TokenSource, null); // Cancel can only be called once.

			if (ts == null || ts.IsCancellationRequested || IsCanceled || IsFaulted || IsCompleted)
				return false;

			ts.Cancel();

			return Status != TaskStatus.Running; // should never be running, but just to be correct...
		}

		public void Dispose()
		{
			Cancel();
		}

		protected CancellableTask(Func<T> action)
			: base(action)
		{
		}

		protected CancellableTask(Func<T> action, CancellationToken token)
			: base(action, token)
		{
		}

		public static CancellableTask<T> Prepare(Func<T> action)
		{
			var ts = new CancellationTokenSource();
			var token = ts.Token;
			return new CancellableTask<T>(action, token)
			{
				TokenSource = ts // Could potentially call cancel before run actually happens.
			};
		}


		public static CancellableTask<T> Start(TimeSpan delay, Func<T> action = null)
		{
			CancellableTask<T> cancellable;

			if (delay < TimeSpan.Zero)
			{
				cancellable = new CancellableTask<T>(action);
				cancellable.RunSynchronously();
			}
			else
			{
				var ts = new CancellationTokenSource();
				var token = ts.Token;
				cancellable = new CancellableTask<T>(action, token)
				{
					TokenSource = ts // Could potentially call cancel before run actually happens.
				};

				if (delay == TimeSpan.Zero)
				{
					cancellable.Start();
				}
				else
				{
					cancellable.ContinueWith(t => cancellable.Cancel()); // If this is arbitrarily run before the delay, then cancel the delay.
					Delay(delay, token)
						.OnFullfilled(() => cancellable.Start());
				}
			}

			return cancellable;
		}

		public static CancellableTask<T> Start(int millisecondsDelay, Func<T> action)
		{
			return Start(TimeSpan.FromMilliseconds(millisecondsDelay), action);
		}

		public static CancellableTask<T> Start(Func<T> action)
		{
			return Start(TimeSpan.Zero, action);
		}
	}

}
