using System;

namespace Open.Threading
{
	public interface ICancellable : IDisposable
	{
		/// <summary>
		/// Returns true if cancelled.
		/// Returns false if already run or already cancelled or unable to cancel.
		/// </summary>
		/// <returns></returns>
		bool Cancel();
	}
}
