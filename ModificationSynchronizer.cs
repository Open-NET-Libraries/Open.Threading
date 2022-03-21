﻿using Open.Disposable;

namespace Open.Threading;

public interface IReadOnlyModificationSynchronizer
{
	void Reading(Action action);

	T Reading<T>(Func<T> action);
}

public interface IModificationSynchronizer : IReadOnlyModificationSynchronizer
{
	bool Modifying(Func<bool> condition, Func<bool> action);
	bool Modifying(Func<bool> action);

	bool Modifying(Action action, bool assumeChange = false);

	bool Modifying<T>(ref T target, T newValue);

	// If this is modifiable, it will increment the version.
	void Poke();
}

public sealed class ReadOnlyModificationSynchronizer : IModificationSynchronizer
{
	public void Reading(Action action) => action();

	public T Reading<T>(Func<T> action) => action();

	public bool Modifying(Action action, bool assumeChange = false) => throw new NotSupportedException("Synchronizer is read-only.");

	public bool Modifying(Func<bool> action) => throw new NotSupportedException("Synchronizer is read-only.");

	public bool Modifying(Func<bool> condition, Func<bool> action) => throw new NotSupportedException("Synchronizer is read-only.");

	public bool Modifying<T>(ref T target, T newValue) => throw new NotSupportedException("Synchronizer is read-only.");

	public void Poke()
	{
		// Does nothing.
	}

	static ReadOnlyModificationSynchronizer? _instance;
	public static ReadOnlyModificationSynchronizer Instance => LazyInitializer.EnsureInitialized(ref _instance)!;
}

public class ModificationSynchronizer : DisposableBase, IModificationSynchronizer
{
	public event EventHandler? Modified;

	protected int _modifyingDepth;
	protected int _version;

	public int Version => _version;

	// ReSharper disable once MemberCanBeProtected.Global
	public void IncrementVersion() => Interlocked.Increment(ref _version);

	public void Poke() => Modifying(() => true);

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

	protected void SignalModified() => Modified?.Invoke(this, EventArgs.Empty);

	public bool Modifying(Func<bool> action) => Modifying(null, action);

	public bool Modifying(Action action, bool assumeChange = false)
		=> Modifying(() =>
		{
			int ver = _version; // Capture the version so that if changes occur indirectly...
			action();
			return assumeChange || ver != _version;
		});

	public virtual bool Modifying(Func<bool>? condition, Func<bool> action)
	{
		AssertIsAlive();
		if (condition is not null && !condition())
			return false;

        int ver = _version; // Capture the version so that if changes occur indirectly...
		Interlocked.Increment(ref _modifyingDepth);
        bool modified = action();
		if (modified) IncrementVersion();
		// At zero depth and version change? Signal.
		if (Interlocked.Decrement(ref _modifyingDepth) == 0 && ver != _version)
			SignalModified();

		return modified;
	}

	public virtual bool Modifying<T>(ref T target, T newValue)
	{
		AssertIsAlive();
		if (target is null ? newValue is null : target.Equals(newValue)) return false;

		IncrementVersion();
		target = newValue;
		SignalModified();

		return true;
	}
}

public sealed class SimpleLockingModificationSynchronizer : ModificationSynchronizer
{
	readonly object _sync;

	public SimpleLockingModificationSynchronizer(object? sync = null) => _sync = sync ?? new object();

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

	public override bool Modifying(Func<bool>? condition, Func<bool> action)
	{
        bool modified = false;
		ThreadSafety.LockConditional(
			_sync,
			() => AssertIsAlive() && (condition is null || condition()),
			() => modified = base.Modifying(null, action)
		);
		return modified;
	}

	public override bool Modifying<T>(ref T target, T newValue)
	{
		AssertIsAlive();
		if (target is null ? newValue is null : target.Equals(newValue))
			return false;

		lock (_sync)
			return base.Modifying(ref target, newValue);
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

	protected override void OnDispose()
	{
		base.OnDispose();
		IDisposable? s = null;
		// OnDispose() is only called once so _sync cannot be null at this point.
		if (!_sync!.TryWrite(10  /* Give any cleanup a chance. */, () => s = Cleanup() ))
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
        ReaderWriterLockSlim? sync = _sync ?? throw new ObjectDisposedException(GetType().ToString());
		sync.Read(action);
	}

	public override T Reading<T>(Func<T> action)
	{
		AssertIsAlive();
        ReaderWriterLockSlim? sync = _sync ?? throw new ObjectDisposedException(GetType().ToString());
		return sync.Read(action);
	}

	public override bool Modifying(Func<bool>? condition, Func<bool> action)
	{
		AssertIsAlive();
        ReaderWriterLockSlim? sync = _sync ?? throw new ObjectDisposedException(GetType().ToString());

        return (condition is null || sync.Read(condition)) // Try and early invalidate.
			&& sync.WriteConditional(
				() => AssertIsAlive() && (condition is null || condition()),
				() => base.Modifying(null, action));
    }

    public override bool Modifying<T>(ref T target, T newValue)
    {
        AssertIsAlive();
        if (target is null ? newValue is null : target.Equals(newValue)) return false;

        ReaderWriterLockSlim? sync = _sync ?? throw new ObjectDisposedException(GetType().ToString());
        // Note, there's no need for _modifyingDepth recursion tracking here.
        using UpgradableReadLock readLock = sync.UpgradableReadLock();
        AssertIsAlive();

        //var ver = _version; // Capture the version so that if changes occur indirectly...
		if (target is null ? newValue is null : target.Equals(newValue)) return false;

        using (WriteLock writeLock = sync.WriteLock())
        {
            IncrementVersion();
            target = newValue;
        }

        // Events will be triggered but this thread will still have the upgradable read.
        SignalModified();
        return true;
    }
}
