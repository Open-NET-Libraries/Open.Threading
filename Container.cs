using Open.Disposable;
using System.Diagnostics.CodeAnalysis;

namespace Open.Threading;

public delegate void ValueInitialzedEventHandler<in T>(object source, T initValue);
public delegate void ValueUpdatedEventHandler<in T>(object source, T originalValue, T newValue);

public interface IContainValue<out T>
{
	/// <summary>
	/// Indicates whether or not the source has been set.
	/// </summary>
	bool HasValue { get; }

	/// <summary>
	/// Accessor for retrieving the source.
	/// </summary>
	T Value { get; }

	/// <summary>
	/// Method to retreive the contained source.
	/// Can be used to pass as an Func&lt;TLock&gt; delegate.
	/// </summary>
	/// <returns>The contained source.</returns>
	T GetValue();
}

/// <summary>
/// Interface for acting as a 'container' for values.  Similar to Nullable but as an interface.
/// </summary>
/// <typeparam name="T">The type to be contained.</typeparam>
// ReSharper disable once InheritdocConsiderUsage
public interface IContainer<T> : IContainValue<T>, IDisposable, IDisposalState // To ensure manual cleanup is implmented.
{
	/// <summary>
	/// Initalizes or updates the contained source.
	/// </summary>
	/// <param name="value">The source to udpate with.</param>
	/// <returns>True if the source is updated.</returns>
	bool SetValue(T value);

	// ReSharper disable EventNeverSubscribedTo.Global
	event ValueInitialzedEventHandler<T> ValueInitialzed;
	event ValueUpdatedEventHandler<T> ValueUpdated;
	event EventHandler BeforeDispose;
	// ReSharper restore EventNeverSubscribedTo.Global
}

/// <summary>
/// Base structure for acting as a disposable 'Thread-Safe Container' for values.  Similar to Nullable but as a class.
/// </summary>
/// <typeparam name="T">The type to be contained.</typeparam>
/// <inheritdoc cref="DisposableBase" />
/// <inheritdoc cref="IContainer&lt;T&gt;" />
// ReSharper disable once InheritdocConsiderUsage
public abstract class ContainerBase<T> : DisposableBase, IContainer<T>
{
	protected readonly ReaderWriterLockSlim SyncLock;

#if NETSTANDARD2_1
	[AllowNull]
#endif
	private T _value = default!;

	/// <summary>
	/// Parameterless constructor.
	/// </summary>
	protected ContainerBase() => SyncLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

	/// <summary>
	/// Initializes with source given.
	/// </summary>
	/// <param name="value">Value to be stored.</param>
	// ReSharper disable once InheritdocConsiderUsage
	protected ContainerBase(T value)
		: this() => SetValue(value);

	#region IContainer<TLock> Members
	/// <inheritdoc />
	public T Value
	{
		get => GetValue();
		set => SetValue(value);
	}

	/// <inheritdoc />
	public T GetValue() => GetOrUpdate(Eval);

	/// <inheritdoc />
	public bool SetValue(T value)
	{
		AssertIsAlive();
		var init = false;

		var original = SyncLock.Write(() =>
		{
			AssertIsAlive();

			var o = _value;
			_value = value;
			init = !HasValue;
			HasValue = true;
			return o;
		});

		if (init)
			OnValueInitializedInternal(value);

		var equal = original is null ? value is null : original.Equals(value);
		if (!equal)
			OnValueUpdatedInternal(original, value);

		return !equal;
	}

	/// <inheritdoc />
	public bool HasValue { get; private set; }
	#endregion

	#region On Value Initialized/Updated
	public event ValueInitialzedEventHandler<T>? ValueInitialzed;
	public event ValueUpdatedEventHandler<T>? ValueUpdated;

	protected virtual void OnValueInitialized(T initValue) { }
	protected virtual void OnValueUpdated(T originalValue, T newValue) { }

	private void OnValueInitializedInternal(T initValue)
	{
		OnValueInitialized(initValue);
		ValueInitialzed?.Invoke(this, initValue);
	}

	private void OnValueUpdatedInternal(T originalValue, T newValue)
	{
		OnValueUpdated(originalValue, newValue);
		ValueUpdated?.Invoke(this, originalValue, newValue);
	}
	#endregion

	/// <summary>
	/// Override this property to alter how the source is queried.
	/// </summary>
	protected virtual T Eval() => _value;

	/// <summary>
	/// Thread-safe means of acquiring both the Value and HasValue source.
	/// </summary>
	/// <param name="value">The out source to set.</param>
	/// <returns>True if the source is set.</returns>
	public bool GetValue(out T value)
	{
		var hv = false;
		value = SyncLock.Read(() =>
		{
			var r = GetValue();
			hv = HasValue;
			return r;
		});
		return hv;
	}

	/// <summary>
	/// If a source doesn't not already exist, it will be set by the valueFactory.
	/// </summary>
	/// <param name="valueFactory">The Func&lt;TLock&gt; for creating the source if it's missing.</param>
	public T GetOrUpdate(Func<T> valueFactory)
	{
		var result = _value;
		if (valueFactory is null)
			return result;

		SyncLock.ReadWriteConditional(
			(_) =>
			{
				AssertIsAlive();
				result = _value;
				return !HasValue;
			},
			() => SetValue(result = valueFactory())
		);
		return result;
	}

	protected void SetHasValue(bool value)
	{
		AssertIsAlive();

		HasValue = value;
	}

	/// <summary>
	/// Resets the state of the container.
	/// </summary>
	public void Reset()
	{
		if (!HasValue) return;
		SyncLock.Write(() => SetHasValue(false));
		OnReset();
	}

	protected virtual void OnReset()
	{
	}

	protected override void OnDispose()
	{
	}
}

/// <inheritdoc />
/// <summary>
/// Thread-Safe container class for allowing deferred delegates to create a missing source. Similar to Lazy&lt;TLock&gt;.
/// This class is the same as ContainerLight&lt;TLock&gt; but never throws away it's scheduler and can be re-rendered at anytime.
/// </summary>
/// <typeparam name="T">The type to be contained.</typeparam>
public class Container<T> : ContainerBase<T>
{
	private Func<T>? _valueFactory;
	public Container() { }
	public Container(T value) : base(value) { }

	public Container(Func<T> valueFactory) => ValueFactory = valueFactory;

	public Func<T>? ValueFactory
	{
		get => _valueFactory;
		set => SyncLock.Write(() => _valueFactory = value);
	}

	public void EnsureValueFactory(Func<T> valueFactory, bool ensuredIfValuePresent = false)
	{
		if (_valueFactory is null || ensuredIfValuePresent && HasValue)
		{
			SyncLock.Write(() =>
			{
				if (_valueFactory is null || ensuredIfValuePresent && HasValue)
					_valueFactory = valueFactory;
			});
		}
	}

	protected override T Eval() => SyncLock.Read(_valueFactory ?? base.Eval);

	protected override void OnDispose()
	{
		base.OnDispose();
		_valueFactory = null;
	}
}

/// <inheritdoc />
/// <summary>
/// Thread-Safe container class for allowing deferred delegates to create a missing source.
/// This class is the same as Container&lt;TLock&gt; but will only evaluate it's source delegate once and then release it.
/// </summary>
/// <typeparam name="T">The type to be contained.</typeparam>
public class ContainerLight<T> : Container<T>
{
	public ContainerLight() { }
	public ContainerLight(T value) : base(value) { }

	public ContainerLight(Func<T> valueFactory)
		: base(valueFactory) { }

	protected override T Eval()
		=> SyncLock.Read(() =>
		{
			var result = base.Eval();
			ValueFactory = null;
			return result;
		});
}
