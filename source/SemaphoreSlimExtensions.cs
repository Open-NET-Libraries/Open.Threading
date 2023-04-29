using System.Diagnostics;
using System.Diagnostics.Contracts;

namespace Open.Threading;

public static class SemaphoreSlimExtensions
{
	/// <summary>
	/// Executes an action within the context of a a Semaphore.
	/// </summary>
	/// <param name="target">The semaphore instance</param>
	/// <param name="closure">The action to execute.</param>
	public static void Execute(this Semaphore target, Action closure)
	{
		if (target is null)
			throw new ArgumentNullException(nameof(target));
		if (closure is null)
			throw new ArgumentNullException(nameof(closure));
		Contract.EndContractBlock();

		target.WaitOne();
		try
		{
			closure();
		}
		finally
		{
			target.Release();
		}
	}

	/// <summary>
	/// Executes an action within the context of a a SemaphoreSlim.
	/// </summary>
	/// <param name="target">The semaphore instance</param>
	/// <param name="closure">The action to execute.</param>
	public static void Execute(this SemaphoreSlim target, Action closure)
	{
		if (target is null)
			throw new ArgumentNullException(nameof(target));
		if (closure is null)
			throw new ArgumentNullException(nameof(closure));
		Contract.EndContractBlock();

		target.Wait();
		try
		{
			closure();
		}
		finally
		{
			target.Release();
		}
	}

	/// <summary>
	/// Executes a function within the context of a a Semaphore.
	/// </summary>
	/// <typeparam name="T">Type of the result.</typeparam>
	/// <param name="target">The semaphore instance</param>
	/// <param name="closure">The function to execute.</param>
	/// <returns>The value of the function.</returns>
	public static T Execute<T>(this Semaphore target, Func<T> closure)
	{
		if (target is null)
			throw new ArgumentNullException(nameof(target));
		if (closure is null)
			throw new ArgumentNullException(nameof(closure));
		Contract.EndContractBlock();

		target.WaitOne();
		try
		{
			return closure();
		}
		finally
		{
			target.Release();
		}
	}

	/// <summary>
	/// Executes a function within the context of a a SemaphoreSlim.
	/// </summary>
	/// <typeparam name="T">Type of the result.</typeparam>
	/// <param name="target">The semaphore instance</param>
	/// <param name="closure">The function to execute.</param>
	/// <returns>The value of the function.</returns>
	public static T Execute<T>(this SemaphoreSlim target, Func<T> closure)
	{
		if (target is null)
			throw new ArgumentNullException(nameof(target));
		if (closure is null)
			throw new ArgumentNullException(nameof(closure));
		Contract.EndContractBlock();

		target.Wait();
		try
		{
			return closure();
		}
		finally
		{
			target.Release();
		}
	}

	/// <summary>
	/// Executes a task within the context of a a SemaphoreSlim.
	/// </summary>
	/// <typeparam name="T">Type of the result.</typeparam>
	/// <param name="target">The semaphore instance</param>
	/// <param name="closure">The function to execute as a task.</param>
	/// <returns>A task containing the result.</returns>
	public static async Task<T> ExecuteAsync<T>(this SemaphoreSlim target, Func<T> closure, CancellationToken token = default)
	{
		if (target is null)
			throw new ArgumentNullException(nameof(target));
		if (closure is null)
			throw new ArgumentNullException(nameof(closure));
		Contract.EndContractBlock();

		await target.WaitAsync(token).ConfigureAwait(false);
		try
		{
			return closure();
		}
		finally
		{
			target.Release();
		}
	}

	/// <summary>
	/// Awaits a task within the context of a a SemaphoreSlim.
	/// </summary>
	/// <typeparam name="T">Type of the result.</typeparam>
	/// <param name="target">The semaphore instance</param>
	/// <param name="task">The task being waited on.</param>
	/// <returns>The task provided.</returns>
	public static async Task<T> TaskWaitAsync<T>(this SemaphoreSlim target, Task<T> task, CancellationToken token = default)
	{
		if (target is null)
			throw new ArgumentNullException(nameof(target));
		if (task is null)
			throw new ArgumentNullException(nameof(task));
		Contract.EndContractBlock();

		await target.WaitAsync(token).ConfigureAwait(false);
		try
		{
			return await task;
		}
		finally
		{
			target.Release();
		}
	}

	/// <summary>
	/// Awaits a task within the context of a a SemaphoreSlim.
	/// </summary>
	/// <typeparam name="T">Type of the result.</typeparam>
	/// <param name="target">The semaphore instance</param>
	/// <param name="task">The task being waited on.</param>
	/// <returns>The task provided.</returns>
	public static async Task<T> WaitAsync<T>(this SemaphoreSlim target, ValueTask<T> task, CancellationToken token = default)
	{
		if (target is null) throw new ArgumentNullException(nameof(target));
		Contract.EndContractBlock();

		await target.WaitAsync(token).ConfigureAwait(false);
		try
		{
			return await task;
		}
		finally
		{
			target.Release();
		}
	}


	/// <summary>
	/// Executes a task within the context of a a SemaphoreSlim.
	/// </summary>
	/// <typeparam name="T">Type of the result.</typeparam>
	/// <param name="target">The semaphore instance</param>
	/// <param name="factory">The delegate to create the task to be waited on.</param>
	/// <returns>The task provided.</returns>
	public static async Task<T> TaskExecuteAsync<T>(this SemaphoreSlim target, Func<Task<T>> factory, CancellationToken token = default)
	{
		if (target is null)
			throw new ArgumentNullException(nameof(target));
		if (factory is null)
			throw new ArgumentNullException(nameof(factory));
		Contract.EndContractBlock();

		await target.WaitAsync(token).ConfigureAwait(false);
		try
		{
			return await factory();
		}
		finally
		{
			target.Release();
		}
	}

	/// <summary>
	/// Awaits a task within the context of a a SemaphoreSlim.
	/// </summary>
	/// <typeparam name="T">Type of the result.</typeparam>
	/// <param name="target">The semaphore instance</param>
	/// <param name="factory">The delegate to create the task to be waited on.</param>
	/// <returns>The task provided.</returns>
	public static async Task<T> ExecuteAsync<T>(this SemaphoreSlim target, Func<ValueTask<T>> factory, CancellationToken token = default)
	{
		if (target is null) throw new ArgumentNullException(nameof(target));
		Contract.EndContractBlock();

		await target.WaitAsync(token).ConfigureAwait(false);
		try
		{
			return await factory();
		}
		finally
		{
			target.Release();
		}
	}
}