using Open.Disposable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Open.Threading;

public interface IReadOnlyModificationSynchronizer
{
	void Reading(Action action);

	T Reading<T>(Func<T> action);
}

public interface IModificationSynchronizer : IReadOnlyModificationSynchronizer
{
	bool Modifying(
		out int version,
		Func<bool>? condition,
		Func<bool> action,
		Action<int>? onModify = null);

	bool Modifying(out int version, Action action, bool assumeChange = false);

	bool Modifying<T>(out int version, ref T target, T newValue);

	// If this is modifiable, it will increment the version.
	int Poke();
}

[ExcludeFromCodeCoverage]
public static class ModificationSynchronizerExtensions
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool Modifying(
		this IModificationSynchronizer synchronizer,
		out int version, Func<bool> action)
		=> synchronizer.Modifying(out version, null, action);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool Modifying(
		this IModificationSynchronizer synchronizer,
		Func<bool> action)
		=> synchronizer.Modifying(out _, null, action);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool Modifying(
		this IModificationSynchronizer synchronizer,
		Func<bool>? condition,
		Func<bool> action,
		Action<int>? onModify = null)
		=> synchronizer.Modifying(out _, condition, action, onModify);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool Modifying(
		this IModificationSynchronizer synchronizer,
		Action action, bool assumeChange = false)
		=> synchronizer.Modifying(out _, action, assumeChange);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool Modifying<T>(
		this IModificationSynchronizer synchronizer, 
		ref T target, T newValue)
		=> synchronizer.Modifying(out _, ref target, newValue);
}

public sealed class ReadOnlyModificationSynchronizer
	: IModificationSynchronizer
{
	public void Reading(Action action) => action();

	public T Reading<T>(Func<T> action) => action();

	private const string ReadOnlyMessage = "Synchronizer is read-only.";

	public bool Modifying(out int version, Action action, bool assumeChange = false)
		=> throw new NotSupportedException(ReadOnlyMessage);

	public bool Modifying(out int version, Func<bool> action)
		=> throw new NotSupportedException(ReadOnlyMessage);

	public bool Modifying(out int version, Func<bool>? condition, Func<bool> action, Action<int>? onModified = null)
		=> throw new NotSupportedException(ReadOnlyMessage);

	public bool Modifying<T>(out int version, ref T target, T newValue)
		=> throw new NotSupportedException(ReadOnlyMessage);

	// Does nothing.
	public int Poke() => 0;

	static ReadOnlyModificationSynchronizer? _instance;
	public static ReadOnlyModificationSynchronizer Instance => LazyInitializer.EnsureInitialized(ref _instance)!;
}

public class ModificationSynchronizer
	: DisposableBase, IModificationSynchronizer
{
	public event EventHandler? Modified;

	protected int _modifyingDepth;
	protected int _version;

	public int Version => _version;

	public int IncrementVersion() => Interlocked.Increment(ref _version);

	public int Poke()
	{
		Modifying(out int version, null, () => true);
		return version;
	}

	protected override void OnBeforeDispose() => Modified = null; // Clean events before swap.

	protected override void OnDispose() => Modified = null; // Just in case.

	public virtual void Reading(Action action)
	{
		AssertIsAlive();
		action();
	}

	public virtual T Reading<T>(Func<T> action)
	{
		AssertIsAlive();
		return action();
	}

	protected void SignalModified()
		=> Modified?.Invoke(this, EventArgs.Empty);

	public bool Modifying(
		out int version,
		Action action,
		bool assumeChange = false)
		=> Modifying(out version, null, () =>
		{
			int ver = _version; // Capture the version so that if changes occur indirectly...
			action();
			return assumeChange || ver != _version;
		});

	public virtual bool Modifying(
		out int version,
		Func<bool>? condition,
		Func<bool> action,
		Action<int>? onModify = null)
	{
		AssertIsAlive();
		int ver = version = _version; // Capture the version so that if changes occur indirectly...
		if (condition is not null && !condition())
			return false;

		Interlocked.Increment(ref _modifyingDepth);
		bool modified = action();
		if (modified) version = IncrementVersion();
		if (Interlocked.Decrement(ref _modifyingDepth) == 0
		&& (modified || ver != _version))
		{
			// At zero depth and version change? Signal.
			SignalModified();
		}

		if (modified) onModify?.Invoke(version);
		return modified;
	}

	public virtual bool Modifying<T>(
		out int version,
		ref T target,
		T newValue)
	{
		AssertIsAlive();
		if (target is null ? newValue is null : target.Equals(newValue))
		{
			version = _version;
			return false;
		}

		version = IncrementVersion();
		target = newValue;
		SignalModified();

		return true;
	}

}

public sealed class SimpleLockingModificationSynchronizer : ModificationSynchronizer
{
	readonly object _sync;

	public SimpleLockingModificationSynchronizer(object? sync = null) => _sync = sync ?? new object();

	protected override void OnBeforeDispose() =>
		// Allow for any current requests to complete.
		ThreadSafety.TryLock(_sync, () => { }, 1000);

	public override void Reading(Action action)
	{
		AssertIsAlive();
		lock (_sync) action();
	}

	public override T Reading<T>(Func<T> action)
	{
		AssertIsAlive();
		lock (_sync) return action();
	}

	public override bool Modifying(out int version, Func<bool>? condition, Func<bool> action, Action<int>? onModify = null)
	{
		bool modified = false;
		int ver = _version;
		ThreadSafety.LockConditional(
			_sync,
			() => AssertIsAlive() && (condition is null || condition()),
			() => modified = base.Modifying(out ver, null, action)
		);
		version = ver;
		if (modified) onModify?.Invoke(version);
		return modified;
	}

	public override bool Modifying<T>(out int version, ref T target, T newValue)
	{
		AssertIsAlive();
		if (target is null ? newValue is null : target.Equals(newValue))
		{
			version = _version;
			return false;
		}

		lock (_sync)
			return base.Modifying(out version, ref target, newValue);
	}
}

public sealed class ReadWriteModificationSynchronizer : ModificationSynchronizer
{
	ReaderWriterLockSlim? _sync;
	readonly bool _lockOwned;

	public ReadWriteModificationSynchronizer(ReaderWriterLockSlim? sync = null)
	{
		if (_sync is null) _lockOwned = true;
		_sync = sync ?? new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
	}

	IDisposable? Cleanup() => Interlocked.Exchange(ref _sync, null);

	protected override void OnBeforeDispose() =>
		// Allow for any current requests to complete.
		_sync!.TryWrite(1000, () => { });

	protected override void OnDispose()
	{
		base.OnDispose();
		IDisposable? s = null;
		// OnDispose() is only called once so _sync cannot be null at this point.
		if (!_sync!.TryWrite(10  /* Give any cleanup a chance. */, () => s = Cleanup()))
		{
			s = Cleanup();
		}

		if (_lockOwned)
		{
			s?.Dispose();
		}
	}

	public override void Reading(Action action)
	{
		AssertIsAlive();
		ReaderWriterLockSlim sync = _sync ?? throw new ObjectDisposedException(GetType().ToString());
		sync.Read(action);
	}

	public override T Reading<T>(Func<T> action)
	{
		AssertIsAlive();
		ReaderWriterLockSlim sync = _sync ?? throw new ObjectDisposedException(GetType().ToString());
		return sync.Read(action);
	}

	public override bool Modifying(out int version, Func<bool>? condition, Func<bool> action, Action<int>? onModify = null)
	{
		AssertIsAlive();
		ReaderWriterLockSlim sync = _sync ?? throw new ObjectDisposedException(GetType().ToString());

		var ver = _version;
		var modified =  (condition is null || sync.Read(condition)) // Try and early invalidate.
			&& sync.WriteConditional(
				() => AssertIsAlive() && (condition is null || condition()),
				() => base.Modifying(out ver, null, action));

		version = ver;
		if(modified) onModify?.Invoke(version);
		return modified;
	}

	public override bool Modifying<T>(out int version, ref T target, T newValue)
	{
		AssertIsAlive();
		if (target is null ? newValue is null : target.Equals(newValue))
		{
			version = _version;
			return false;
		}

		ReaderWriterLockSlim sync = _sync ?? throw new ObjectDisposedException(GetType().ToString());
		// Note, there's no need for _modifyingDepth recursion tracking here.
		using UpgradableReadLock readLock = sync.UpgradableReadLock();
		AssertIsAlive();

		//var ver = _version; // Capture the version so that if changes occur indirectly...
		if (target is null ? newValue is null : target.Equals(newValue))
		{
			version = _version;
			return false;
		}

		using (WriteLock writeLock = sync.WriteLock())
		{
			version = IncrementVersion();
			target = newValue;
		}

		// Events will be triggered but this thread will still have the upgradable read.
		SignalModified();
		return true;
	}
}
