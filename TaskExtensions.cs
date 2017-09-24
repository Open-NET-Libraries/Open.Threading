using System;
using System.Threading.Tasks;

namespace Open.Threading
{
	public static class TaskExtensions
	{
		public static bool IsActive(this Task target)
		{
			if (target == null)
				throw new NullReferenceException();

			switch (target.Status)
			{
				case TaskStatus.Created:
				case TaskStatus.Running:
				case TaskStatus.WaitingForActivation:
				case TaskStatus.WaitingForChildrenToComplete:
				case TaskStatus.WaitingToRun:
					return true;
				case TaskStatus.Canceled:
				case TaskStatus.Faulted:
				case TaskStatus.RanToCompletion:
					return false;
			}

			return false;
		}



		public static Task OnFullfilled(this Task target, Action action)
		{
			return target.ContinueWith(task =>
			{
				if (task.IsCompleted) action();
			});
		}


		public static Task<T> OnFullfilled<T>(this Task<T> target, Action<T> action)
		{
			return target.ContinueWith(task =>
			{
				if (task.IsCompleted) action(task.Result);
				return task.Result;
			});
		}

		// Tasks don't behave like promises so even though this seems like we should call this "Catch", it's not doing that and a real catch statment needs to be wrapped around a wait call.
		public static Task OnFaulted(this Task target, Action<Exception> action)
		{
			return target.ContinueWith(task =>
			{
				if (task.IsFaulted) action(task.Exception);
			});
		}

		public static Task<T> OnFaulted<T>(this Task<T> target, Action<Exception> action)
		{
			return target.ContinueWith(task =>
			{
				if (task.IsFaulted) action(task.Exception);
				return task.Result;
			});
		}

		public static Task OnCancelled(this Task target, Action action)
		{
			return target.ContinueWith(task =>
			{
				if (task.IsCanceled) action();
			});
		}

		public static Task<T> OnCancelled<T>(this Task<T> target, Action action)
		{
			return target.ContinueWith(task =>
			{
				if (task.IsCanceled) action();
				return task.Result;
			});
		}


	}
}