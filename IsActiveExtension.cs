using System;
using System.Diagnostics.Contracts;
using System.Threading.Tasks;

namespace Open.Threading;

internal static class IsActiveExtension
{
	/// <summary>
	/// Returns true if the target Task has not yet run, is waiting, or is running, else returns false.
	/// </summary>
	public static bool IsActive(this Task target)
	{
		if (target is null) throw new ArgumentNullException(nameof(target));
		Contract.EndContractBlock();

		return target.Status switch
		{
			TaskStatus.Created or
			TaskStatus.Running or
			TaskStatus.WaitingForActivation or
			TaskStatus.WaitingForChildrenToComplete or
			TaskStatus.WaitingToRun => true,
			_ => false,
		};
	}
}
