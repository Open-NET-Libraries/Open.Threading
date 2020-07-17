using System;
using System.Threading.Tasks;

namespace Open.Threading
{
	internal static class IsActiveExtension
	{

		/// <summary>
		/// Returns true if the target Task has not yet run, is waiting, or is running, else returns false.
		/// </summary>
		public static bool IsActive(this Task target)
		{
			if (target is null)
				throw new NullReferenceException();

			switch (target.Status)
			{
				case TaskStatus.Created:
				case TaskStatus.Running:
				case TaskStatus.WaitingForActivation:
				case TaskStatus.WaitingForChildrenToComplete:
				case TaskStatus.WaitingToRun:
					return true;
					//case TaskStatus.Canceled:
					//case TaskStatus.Faulted:
					//case TaskStatus.RanToCompletion:
					//	return false;
			}

			return false;
		}
	}
}
