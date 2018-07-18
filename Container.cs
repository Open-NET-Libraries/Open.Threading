using Open.Disposable;
using System;
using System.Threading;


namespace Open.Threading
{
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
	/// <typeparam name="TLock">The type to be contained.</typeparam>
	public interface IContainer<T> : IContainValue<T>, IDisposable // To ensure manual cleanup is implmented.
	{
		/// <summary>
		/// Accessor for storing or retrieving the source.
		/// </summary>
		new T Value { get; set; }


		/// <summary>
		/// Returns true if already disposed.
		/// </summary>
		bool IsDisposed { get; }

		/// <summary>
		/// Initalizes or updates the contained source.
		/// </summary>
		/// <param name="source">The source to udpate with.</param>
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
	/// <typeparam name="TLock">The type to be contained.</typeparam>
	public class ContainerBase<T> : DisposableBase, IContainer<T>
	{
		protected readonly ReaderWriterLockSlim SyncLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
		private T _value;

		/// <summary>
		/// Parameterless constructor.
		/// </summary>
		public ContainerBase() : base()
		{
		}

		/// <summary>
		/// Initializes with source given.
		/// </summary>
		/// <param name="source">Value to be stored.</param>
		public ContainerBase(T value)
			: this()
		{
			SetValue(value);
		}


		#region IContainer<TLock> Members
		/// <summary>
		/// Accessor for storing or retrieving the source.
		/// </summary>
		public T Value
		{
			get { return GetValue(); }
			set { SetValue(value); }
		}

		/// <summary>
		/// Method to retreive the contained source.
		/// Can be used to pass as an Func&lt;TLock&gt; delegate. 
		/// </summary>
		/// <returns>The contained source.</returns>
		public T GetValue()
		{
			return GetOrUpdate(Eval);
		}

		/// <summary>
		/// Initalizes or updates the contained source.
		/// </summary>
		/// <param name="source">The source to udpate with.</param>
		/// <returns>True if the source is updated.</returns>
		public bool SetValue(T value)
		{
			AssertIsAlive();
			var init = false;

			var original = SyncLock.WriteValue(() =>
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

			var updating = !original.Equals(value);
			if (updating)
				OnValueUpdatedInternal(original, value);

			return updating;
		}


		/// <summary>
		/// Indicates whether or not the source has been set.
		/// </summary>
		public bool HasValue { get; private set; }
		#endregion


		#region On Value Initialized/Updated
		public event ValueInitialzedEventHandler<T> ValueInitialzed;
		public event ValueUpdatedEventHandler<T> ValueUpdated;

		// ReSharper disable VirtualMemberNeverOverriden.Global
		// ReSharper disable UnusedParameter.Global
		protected virtual void OnValueInitialized(T initValue) { }
		protected virtual void OnValueUpdated(T originalValue, T newValue) { }
		// ReSharper restore UnusedParameter.Global
		// ReSharper restore VirtualMemberNeverOverriden.Global

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
		protected virtual T Eval()
		{
			return _value;
		}

		/// <summary>
		/// Thread-safe means of acquiring both the Value and HasValue source.
		/// </summary>
		/// <param name="source">The out source to set.</param>
		/// <returns>True if the source is set.</returns>
		public bool GetValue(out T value)
		{
			var hv = false;
			value = SyncLock.ReadValue(() =>
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
			if (valueFactory != null)
			{
				SyncLock.ReadWriteConditional((locked) =>
				{
					AssertIsAlive();
					result = _value;
					return !HasValue;
				}, () =>
					SetValue(result = valueFactory())
				);
			}
			return result;
		}

		protected void SetHasValue(bool value)
		{
			AssertIsAlive();

			HasValue = value;
		}

		public void Reset()
		{
			if (HasValue)
			{
				SyncLock.Write(() => SetHasValue(false));
				OnReset();
			}
		}

		protected virtual void OnReset()
		{
		}


		protected override void OnDispose(bool calledExplicitly)
		{

		}

	}



	/// <summary>
	/// Thread-Safe container class for allowing deferred delegates to create a missing source. Similar to Lazy&lt;TLock&gt;.
	/// This class is the same as ContainerLight&lt;TLock&gt; but never throws away it's scheduler and can be re-rendered at anytime.
	/// </summary>
	/// <typeparam name="TLock">The type to be contained.</typeparam>
	public class Container<T> : ContainerBase<T>
	{
		private Func<T> _valueFactory;
		public Container() { }
		public Container(T value) : base(value) { }

		public Container(Func<T> valueFactory)
		{
			ValueFactory = valueFactory;
		}

		public Func<T> ValueFactory
		{
			get { return _valueFactory; }
			set
			{
				SyncLock.WriteValue(() => _valueFactory = value);
			}
		}

		public void EnsureValueFactory(Func<T> valueFactory, bool ensuredIfValuePresent = false)
		{
			if (_valueFactory == null || ensuredIfValuePresent && HasValue)
			{
				SyncLock.Write(() =>
				{
					if (_valueFactory == null || ensuredIfValuePresent && HasValue)
						_valueFactory = valueFactory;
				});
			}
		}

		protected override T Eval()
		{
			return SyncLock.ReadValue(_valueFactory ?? base.Eval);
		}

		protected override void OnDispose(bool calledExplicitly)
		{
			base.OnDispose(calledExplicitly);
			_valueFactory = null;
		}
	}



	/// <summary>
	/// Thread-Safe container class for allowing deferred delegates to create a missing source.
	/// This class is the same as Container&lt;TLock&gt; but will only evaluate it's source delegate once and then release it.
	/// </summary>
	/// <typeparam name="TLock">The type to be contained.</typeparam>
	public class ContainerLight<T> : Container<T>
	{
		public ContainerLight() { }
		public ContainerLight(T value) : base(value) { }

		public ContainerLight(Func<T> valueFactory)
			: base(valueFactory) { }

		protected override T Eval()
		{
			return SyncLock.ReadValue(() =>
			{
				var result = base.Eval();
				ValueFactory = null;
				return result;
			});
		}
	}
}
