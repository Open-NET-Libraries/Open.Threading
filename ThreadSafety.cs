using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;


namespace Open.Threading
{
	/// <summary>
	/// Library class for executing different thread safe synchronization techniques.
	/// </summary>
	public static class ThreadSafety
	{
		public static bool IsValidSyncObject(object syncObject)
			=> syncObject switch // Avoid the lock object being immutable...
			{
				string _ or
				ValueType _ or
				null => false,
				_ => true,
			};

		internal static void ValidateSyncObject(object syncObject)
		{
			if (syncObject is null)
				throw new ArgumentNullException(nameof(syncObject));
			if (!IsValidSyncObject(syncObject))
				throw new ArgumentException("Is not valid sync object.", nameof(syncObject));
		}

		internal static void ValidateMillisecondsTimeout(int? millisecondsTimeout)
		{
			if ((millisecondsTimeout ?? 0) < 0)
				throw new ArgumentOutOfRangeException(nameof(millisecondsTimeout), millisecondsTimeout, "Cannot be a negative value.");
		}

		public static bool InterlockedExchangeIfLessThanComparison(ref int location, int comparison, int newValue)
		{
			int initialValue;
			do
			{
				initialValue = location;
				if (initialValue >= comparison) return false;
			}
			while (Interlocked.CompareExchange(ref location, newValue, initialValue) != initialValue);
			return true;
		}

		public static bool InterlockedIncrementIfLessThanComparison(ref int location, int comparison, out int value)
		{
			int initialValue;
			do
			{
				initialValue = location;
				value = initialValue + 1;
				if (initialValue >= comparison) return false;
			}
			while (Interlocked.CompareExchange(ref location, value, initialValue) != initialValue);
			return true;
		}

		/// <summary>
		/// Applies a lock on the syncObject before executing the provided Action.
		/// This is more of a sample method and is direclty equivalient to using the lock keyword and is meant to be the equivalent of the below method without a timeout...
		/// </summary>
		public static void Lock<TSync>(TSync syncObject, Action closure) where TSync : class
		{
			ValidateSyncObject(syncObject);
			if (closure is null)
				throw new ArgumentNullException(nameof(closure));
			Contract.EndContractBlock();

			lock (syncObject)
				closure();
		}

		/// <summary>
		/// Applies a lock on the syncObject before executing the provided Action.
		/// This is more of a sample method and is direclty equivalient to using the lock keyword and is meant to be the equivalent of the below method without a timeout...
		/// </summary>
		/// <returns>The action of the query.</returns>
		public static T Lock<TSync, T>(TSync syncObject, Func<T> closure) where TSync : class
		{
			ValidateSyncObject(syncObject);
			if (closure is null)
				throw new ArgumentNullException(nameof(closure));
			Contract.EndContractBlock();

			lock (syncObject)
				return closure();
		}


		/// <summary>
		/// Applies a lock on the syncObject before executing the provided Action with a timeout.
		/// Throws a TimeoutException if throwsOnTimeout is true (default) and a lock could not be aquired.
		/// </summary>
		/// <param name="syncObject">Object used for synchronization.</param>
		/// <param name="closure">The query to execute once a lock is acquired.</param>
		/// <param name="millisecondsTimeout">Maximum time allowed to wait for a lock.</param>
		/// <param name="throwsOnTimeout">If true and a timeout is reached, then a TimeoutException is thrown.
		/// If false and a timeout is reached, then it this method returns false and allows the caller to handle the failed lock.</param>
		/// <returns>
		/// True if a lock was acquired and the Action executed.
		/// False if throwsOnTimeout is false and could not acquire a lock.
		/// </returns>
		public static bool Lock<TSync>(TSync syncObject, Action closure, int millisecondsTimeout, bool throwsOnTimeout = true) where TSync : class
		{
			ValidateSyncObject(syncObject);
			if (closure is null)
				throw new ArgumentNullException(nameof(closure));
			if (millisecondsTimeout < 0)
				throw new ArgumentOutOfRangeException(nameof(closure), millisecondsTimeout, "Cannot be a negative value.");
			Contract.EndContractBlock();

			var lockTaken = false;
			try
			{
				Monitor.TryEnter(syncObject, millisecondsTimeout, ref lockTaken);
				if (lockTaken)
				{
					closure();

				}
				else
				{
					if (throwsOnTimeout)
						throw new TimeoutException("Could not gain a lock within the timeout specified.");
				}
			}
			finally
			{
				if (lockTaken)
					Monitor.Exit(syncObject);
			}

			return lockTaken;
		}


		/// <summary>
		/// Attempts to acquire a lock on the syncObject before executing the provided Action with an optional timeout.
		/// </summary>
		/// <param name="syncObject">Object used for synchronization.</param>
		/// <param name="closure">The query to execute once a lock is acquired.</param>
		/// <param name="millisecondsTimeout">Maximum time allowed to wait for a lock.</param>
		/// <returns>
		/// True if a lock was acquired and the Action executed.
		/// </returns>
		public static bool TryLock<TSync>(TSync syncObject, Action closure, int millisecondsTimeout = 0) where TSync : class
		{
			ValidateSyncObject(syncObject);
			if (closure is null)
				throw new ArgumentNullException(nameof(closure));
			if (millisecondsTimeout < 0)
				throw new ArgumentOutOfRangeException(nameof(closure), millisecondsTimeout, "Cannot be a negative value.");
			Contract.EndContractBlock();

			var lockTaken = false;
			try
			{
				if (millisecondsTimeout == 0)
					Monitor.TryEnter(syncObject, ref lockTaken);
				else
					Monitor.TryEnter(syncObject, millisecondsTimeout, ref lockTaken);

				if (lockTaken)
					closure();
			}
			finally
			{
				if (lockTaken)
					Monitor.Exit(syncObject);
			}

			return lockTaken;
		}



		/// <summary>
		/// Sychronizes executing the Action only if the condition is true.
		/// </summary>
		/// 
		/// <param name="syncObject">Object used for synchronization.</param>
		/// <param name="condition">Logic function to execute DCL pattern.  Passes in a boolean that is true for when a lock is held.  The return value indicates if a lock is still needed and the query should be executed.
		/// Note: Passing a boolean to the condition when a lock is acquired helps if it is important to the cosuming logic to avoid recursive locking.</param>
		/// <param name="closure">The closure to execute once a lock is acquired.  Only executes if the condition returns true.</param>
		/// <returns>
		/// True if the Action executed.
		/// </returns>						
		public static bool LockConditional<TSync>(TSync syncObject, Func<bool, bool> condition, Action closure) where TSync : class
		{
			ValidateSyncObject(syncObject);
			if (condition is null)
				throw new ArgumentNullException(nameof(condition));
			if (closure is null)
				throw new ArgumentNullException(nameof(closure));
			Contract.EndContractBlock();

			if (!condition(false)) return false;
			lock (syncObject)
			{
				if (!condition(true)) return false;
				closure();
			}
			return true;
		}

		/// <summary>
		/// Sychronizes executing the Action only if the condition is true.
		/// </summary>
		/// 
		/// <param name="syncObject">Object used for synchronization.</param>
		/// <param name="condition">Logic function to execute DCL pattern.  The return value indicates if a lock is still needed and the query should be executed.</param>
		/// <param name="closure">The closure to execute once a lock is acquired.  Only executes if the condition returns true.</param>
		/// <returns>
		/// True if the Action executed.
		/// </returns>		
		public static bool LockConditional<TSync>(TSync syncObject, Func<bool> condition, Action closure) where TSync : class
		{
			ValidateSyncObject(syncObject);
			if (condition is null)
				throw new ArgumentNullException(nameof(condition));
			if (closure is null)
				throw new ArgumentNullException(nameof(closure));
			Contract.EndContractBlock();

			if (!condition()) return false;
			lock (syncObject)
			{
				if (!condition()) return false;
				closure();
			}
			return true;
		}

		/// <summary>
		/// Sychronizes executing the Action only if the condition is true and using a timeout.
		/// Throws a TimeoutException if throwsOnTimeout is true (default) and a lock was needed but could not be aquired.
		/// </summary>
		/// 
		/// <param name="syncObject">Object used for synchronization.</param>
		/// <param name="condition">Logic function to execute DCL pattern.  Passes in a boolean that is true for when a lock is held.  The return value indicates if a lock is still needed and the query should be executed.
		/// Note: Passing a boolean to the condition when a lock is acquired helps if it is important to the cosuming logic to avoid recursive locking.</param>
		/// <param name="closure">The closure to execute once a lock is acquired.  Only executes if the condition returns true.</param>
		/// <param name="millisecondsTimeout">Maximum time allowed to wait for a lock.</param>
		/// <param name="throwsOnTimeout">If true and a timeout is reached, then a TimeoutException is thrown.</param>
		///
		/// <returns>
		/// True if a lock was acquired and the Action executed.
		/// False if throwsOnTimeout is false and could not acquire a lock.
		/// </returns>
		public static bool LockConditional<TSync>(TSync syncObject, Func<bool, bool> condition, Action closure, int millisecondsTimeout, bool throwsOnTimeout = true) where TSync : class
		{
			ValidateSyncObject(syncObject);
			if (condition is null)
				throw new ArgumentNullException(nameof(condition));
			if (closure is null)
				throw new ArgumentNullException(nameof(closure));
			ValidateMillisecondsTimeout(millisecondsTimeout);
			Contract.EndContractBlock();

			var lockTaken = false;
			if (condition(false))
			{
				try
				{
					Monitor.TryEnter(syncObject, millisecondsTimeout, ref lockTaken);
					if (lockTaken)
					{
						if (condition(true))
						{
							closure();
							return true;
						}
					}
					else
					{
						if (throwsOnTimeout)
							throw new TimeoutException("Could not gain a lock within the timeout specified.");
					}


				}
				finally
				{
					if (lockTaken) Monitor.Exit(syncObject);
				}
			}

			return lockTaken;
		}

		/// <summary>
		/// Sychronizes executing the Action only if the condition is true and using a timeout.
		/// Throws a TimeoutException if throwsOnTimeout is true (default) and a lock was needed but could not be aquired.
		/// </summary>
		/// 
		/// <param name="syncObject">Object used for synchronization.</param>
		/// <param name="condition">Logic function to execute DCL pattern.  The return value indicates if a lock is still needed and the query should be executed.</param>
		/// <param name="closure">The closure to execute once a lock is acquired.  Only executes if the condition returns true.</param>
		/// <param name="millisecondsTimeout">Maximum time allowed to wait for a lock.</param>
		/// <param name="throwsOnTimeout">If true and a timeout is reached, then a TimeoutException is thrown.</param>
		///
		/// <returns>
		/// True if a lock was acquired and the Action executed.
		/// False if throwsOnTimeout is false and could not acquire a lock.
		/// </returns>
		public static bool LockConditional<TSync>(TSync syncObject, Func<bool> condition, Action closure, int millisecondsTimeout, bool throwsOnTimeout = true) where TSync : class
		{
			ValidateSyncObject(syncObject);
			if (condition is null)
				throw new ArgumentNullException(nameof(condition));
			if (closure is null)
				throw new ArgumentNullException(nameof(closure));
			ValidateMillisecondsTimeout(millisecondsTimeout);
			Contract.EndContractBlock();

			return LockConditional(syncObject, (locked) => condition(), closure, millisecondsTimeout, throwsOnTimeout);
		}

		/// <summary>
		/// Uses the provided lock object to sychronize acquiring the target value.
		/// If the target value is not set it sets the target to the query response.
		/// LazyIntializer will also work but this does not have the constraints of LazyInitializer.
		/// </summary>
		public static T LockIfNull<TSync, T>(TSync syncObject, ref T target, Func<T> closure) where TSync : class
		{
			ValidateSyncObject(syncObject);
			if (closure is null)
				throw new ArgumentNullException(nameof(closure));
			Contract.EndContractBlock();

			if (target is null)
				lock (syncObject)
					if (target is null)
						target = closure();
			return target;
		}

		/// <summary>
		/// Guarantees only a single execution occurs instead of a optimistic concurrency.
		/// 
		/// Ensures not only thread safety but also only a single operation.
		/// </summary>
		/// <typeparam name="T">The type of the value.</typeparam>
		/// <param name="lazy">The Lazy instance to initialize.</param>
		/// <param name="factory">The value generator.</param>
		/// <returns>The generated value.</returns>
		public static T InitializeValue<T>(ref Lazy<T> lazy, Func<T> factory)
		{
			LazyInitializer.EnsureInitialized(ref lazy, () => new Lazy<T>(factory, LazyThreadSafetyMode.ExecutionAndPublication));
			return lazy.Value;
		}

		private static readonly ConditionalWeakTable<object, ReadWriteHelper<object>> _sychronizeReadWriteRegistry
			= new();

		private static ReadWriteHelper<object> GetReadWriteHelper(object key)
		{
			if (key is null)
				throw new ArgumentNullException(nameof(key));
			Contract.EndContractBlock();
			var result = _sychronizeReadWriteRegistry.GetOrCreateValue(key);
			if (result is null)
				throw new NullReferenceException();
			return result;
		}

		/// <summary>
		/// Manages a read-only conditional operation and resultant write locked operation of any target and specifc key of that object.
		/// </summary>
		/// <typeparam name="TSync">Type of the object sync context.</typeparam>
		/// <param name="syncObject">The main object that defines the synchronization context.</param>
		/// <param name="key">The key that represents what value will change.</param>
		/// <param name="condition">The condition function that if true, allows procedurally allows for a write lock.  If at any time this function is false, the closure will not execute.</param>
		/// <param name="closure">The function to execute while under a write lock if the condition remains true.</param>
		/// <param name="millisecondsTimeout">An optional value to allow for timeout.</param>
		/// <param name="throwsOnTimeout">If true, and a millisecondsTimeout value is provided, a TimeoutException will be thrown if the timeout is reached the instead of this method returning false.</param>
		/// <returns>True if a lock is aquired.  False if throwsOnTimeout is false and was unable to acquire a lock.</returns>
		/// <exception cref="TimeoutException">Unable to acquire a lock.</exception>
		public static bool SynchronizeReadWrite<TSync>(
			TSync syncObject,
			object key, Func<LockType, bool> condition, Action closure,
			int? millisecondsTimeout = null, bool throwsOnTimeout = true) where TSync : class
		{
			ValidateSyncObject(syncObject);
			if (key is null)
				throw new ArgumentNullException(nameof(key));
			if (condition is null)
				throw new ArgumentNullException(nameof(condition));
			if (closure is null)
				throw new ArgumentNullException(nameof(closure));
			ValidateMillisecondsTimeout(millisecondsTimeout);
			Contract.EndContractBlock();

			return GetReadWriteHelper(syncObject)
					.ReadWriteConditionalOptimized(key,
						condition, closure, millisecondsTimeout, throwsOnTimeout);

		}


		/// <summary>
		/// Manages a read-only conditional operation and resultant write locked operation of any target and specifc key of that object.
		/// </summary>
		/// <typeparam name="TSync">Type of the object sync context.</typeparam>
		/// <typeparam name="T">Type of the result.</typeparam>
		/// <param name="syncObject">The main object that defines the synchronization context.</param>
		/// <param name="key">The key that represents what value will change.</param>
		/// <param name="result">The reference to become the result if a result is acquired from the closure during write.</param>
		/// <param name="condition">The condition function that if true, allows procedurally allows for a write lock.  If at any time this function is false, the closure will not execute.</param>
		/// <param name="closure">The function to execute while under a write lock if the condition remains true.  'result' becomes the return value.</param>
		/// <param name="millisecondsTimeout">An optional value to allow for timeout.</param>
		/// <param name="throwsOnTimeout">If true, and a millisecondsTimeout value is provided, a TimeoutException will be thrown if the timeout is reached the instead of this method returning false.</param>
		/// <returns>True if a lock is aquired.  False if throwsOnTimeout is false and was unable to acquire a lock.</returns>
		/// <exception cref="TimeoutException">Unable to acquire a lock.</exception>
		public static bool SynchronizeReadWrite<TSync, T>(
			TSync syncObject,
			object key, ref T result, Func<LockType, bool> condition, Func<T> closure,
			int? millisecondsTimeout = null, bool throwsOnTimeout = true) where TSync : class
		{
			ValidateSyncObject(syncObject);
			if (key is null)
				throw new ArgumentNullException(nameof(key));
			if (condition is null)
				throw new ArgumentNullException(nameof(condition));
			if (closure is null)
				throw new ArgumentNullException(nameof(closure));
			ValidateMillisecondsTimeout(millisecondsTimeout);
			Contract.EndContractBlock();

			return GetReadWriteHelper(syncObject)
					.ReadWriteConditionalOptimized(key, ref result,
						condition, closure, millisecondsTimeout, throwsOnTimeout);

		}

		/// <summary>
		/// Manages a read-only conditional operation and resultant write locked operation of any target and specifc key of that object.
		/// 
		/// A bit more robust (but heavier) version that first synchronizes a read-lock on the syncObject before testing the condition.
		/// Subsequently will use a write lock on the syncObject while already having a write lock on the key.
		/// </summary>
		/// <typeparam name="TSync">Type of the object sync context.</typeparam>
		/// <param name="syncObject">The main object that defines the synchronization context.</param>
		/// <param name="key">The key that represents what value will change.</param>
		/// <param name="condition">The condition function that if true, allows procedurally allows for a write lock.  If at any time this function is false, the closure will not execute.</param>
		/// <param name="closure">The function to execute while under a write lock if the condition remains true.</param>
		/// <param name="millisecondsTimeout">An optional value to allow for timeout.</param>
		/// <param name="throwsOnTimeout">If true, and a millisecondsTimeout value is provided, a TimeoutException will be thrown if the timeout is reached the instead of this method returning false.</param>
		/// <returns>True if a lock is aquired.  False if throwsOnTimeout is false and was unable to acquire a lock.</returns>
		/// <exception cref="TimeoutException">Unable to acquire a lock.</exception>
		public static bool SynchronizeReadWriteKeyAndObject<TSync>(
			TSync syncObject,
			object key, Func<LockType, bool> condition, Action closure,
			int? millisecondsTimeout = null, bool throwsOnTimeout = true) where TSync : class
		{
			ValidateSyncObject(syncObject);
			if (key is null)
				throw new ArgumentNullException(nameof(key));
			if (condition is null)
				throw new ArgumentNullException(nameof(condition));
			if (closure is null)
				throw new ArgumentNullException(nameof(closure));
			ValidateMillisecondsTimeout(millisecondsTimeout);
			Contract.EndContractBlock();

			return SynchronizeReadWrite(syncObject, key,
				lockType => SynchronizeRead(syncObject, () => condition(LockType.Read), millisecondsTimeout, throwsOnTimeout),
				() => SynchronizeReadWrite(syncObject, condition, closure, millisecondsTimeout, throwsOnTimeout),
				millisecondsTimeout,
				throwsOnTimeout);
		}

		/// <summary>
		/// Manages a read-only conditional operation and resultant write locked operation of any target and specifc key of that object.
		/// 
		/// A bit more robust (but heavier) version that first synchronizes a read-lock on the syncObject before testing the condition.
		/// Subsequently will use a write lock on the syncObject while already having a write lock on the key.
		/// </summary>
		/// <typeparam name="TSync">Type of the object sync context.</typeparam>
		/// <typeparam name="T">Type of the result.</typeparam>
		/// <param name="syncObject">The main object that defines the synchronization context.</param>
		/// <param name="key">The key that represents what value will change.</param>
		/// <param name="result">The reference to become the result if a result is acquired from the closure during write.</param>
		/// <param name="condition">The condition function that if true, allows procedurally allows for a write lock.  If at any time this function is false, the closure will not execute.</param>
		/// <param name="closure">The function to execute while under a write lock if the condition remains true.  'result' becomes the return value.</param>
		/// <param name="millisecondsTimeout">An optional value to allow for timeout.</param>
		/// <param name="throwsOnTimeout">If true, and a millisecondsTimeout value is provided, a TimeoutException will be thrown if the timeout is reached the instead of this method returning false.</param>
		/// <returns>True if a lock is aquired.  False if throwsOnTimeout is false and was unable to acquire a lock.</returns>
		/// <exception cref="TimeoutException">Unable to acquire a lock.</exception>
		public static bool SynchronizeReadWriteKeyAndObject<TSync, T>(
			TSync syncObject,
			object key, ref T result, Func<LockType, bool> condition, Func<T> closure,
			int? millisecondsTimeout = null, bool throwsOnTimeout = true) where TSync : class
		{
			ValidateSyncObject(syncObject);
			if (key is null)
				throw new ArgumentNullException(nameof(key));
			if (condition is null)
				throw new ArgumentNullException(nameof(condition));
			if (closure is null)
				throw new ArgumentNullException(nameof(closure));
			ValidateMillisecondsTimeout(millisecondsTimeout);
			Contract.EndContractBlock();


			var r = result;
			var written = false;

			var synced = SynchronizeReadWriteKeyAndObject(
				syncObject, key, condition, () =>
				{
					r = closure();
					written = true;
				}, millisecondsTimeout, throwsOnTimeout);

			if (written)
				result = r;

			return synced;
		}

		/// <summary>
		/// Manages a read-only conditional operation and resultant write locked operation of any target.
		/// </summary>
		/// <typeparam name="TSync">Type of the object sync context.</typeparam>
		/// <param name="syncObject">The main object that defines the synchronization context.</param>
		/// <param name="condition">The condition function that if true, allows procedurally allows for a write lock.  If at any time this function is false, the closure will not execute.</param>
		/// <param name="closure">The function to execute while under a write lock if the condition remains true.</param>
		/// <param name="millisecondsTimeout">An optional value to allow for timeout.</param>
		/// <param name="throwsOnTimeout">If true, and a millisecondsTimeout value is provided, a TimeoutException will be thrown if the timeout is reached the instead of this method returning false.</param>
		/// <returns>True if a lock is aquired.  False if throwsOnTimeout is false and was unable to acquire a lock.</returns>
		/// <exception cref="TimeoutException">Unable to acquire a lock.</exception>
		public static bool SynchronizeReadWrite<TSync>(
			TSync syncObject,
			Func<LockType, bool> condition, Action closure,
			int? millisecondsTimeout = null, bool throwsOnTimeout = true) where TSync : class
		{
			ValidateSyncObject(syncObject);
			if (condition is null)
				throw new ArgumentNullException(nameof(condition));
			if (closure is null)
				throw new ArgumentNullException(nameof(closure));
			ValidateMillisecondsTimeout(millisecondsTimeout);
			Contract.EndContractBlock();

			return SynchronizeReadWrite(syncObject, syncObject, condition, closure, millisecondsTimeout, throwsOnTimeout);
		}

		/// <summary>
		/// Manages a read-only conditional operation and resultant write locked operation of any target.
		/// </summary>
		/// <typeparam name="TSync">Type of the object sync context.</typeparam>
		/// <typeparam name="T">Type of the result.</typeparam>
		/// <param name="syncObject">The main object that defines the synchronization context.</param>
		/// <param name="result">The reference to become the result if a result is acquired from the closure during write.</param>
		/// <param name="condition">The condition function that if true, allows procedurally allows for a write lock.  If at any time this function is false, the closure will not execute.</param>
		/// <param name="closure">The function to execute while under a write lock if the condition remains true.  'result' becomes the return value.</param>
		/// <param name="millisecondsTimeout">An optional value to allow for timeout.</param>
		/// <param name="throwsOnTimeout">If true, and a millisecondsTimeout value is provided, a TimeoutException will be thrown if the timeout is reached the instead of this method returning false.</param>
		/// <returns>True if a lock is aquired.  False if throwsOnTimeout is false and was unable to acquire a lock.</returns>
		/// <exception cref="TimeoutException">Unable to acquire a lock.</exception>
		public static bool SynchronizeReadWrite<TSync, T>(
			TSync syncObject,
			ref T result, Func<LockType, bool> condition, Func<T> closure,
			int? millisecondsTimeout = null, bool throwsOnTimeout = true) where TSync : class
		{
			ValidateSyncObject(syncObject);
			if (condition is null)
				throw new ArgumentNullException(nameof(condition));
			if (closure is null)
				throw new ArgumentNullException(nameof(closure));
			ValidateMillisecondsTimeout(millisecondsTimeout);
			Contract.EndContractBlock();

			return SynchronizeReadWrite(syncObject, syncObject, ref result, condition, closure, millisecondsTimeout, throwsOnTimeout);
		}

		/// <summary>
		/// Manages a read-only operation of any target and the provided key and returns the value from the closure.
		/// </summary>
		/// <typeparam name="TSync">Type of the object sync context.</typeparam>
		/// <typeparam name="T">Type of the result.</typeparam>
		/// <param name="syncObject">The main object that defines the synchronization context.</param>
		/// <param name="key">The key that represents what value being read from.</param>
		/// <param name="closure">The function to execute while under a read lock.</param>
		/// <param name="millisecondsTimeout">An optional value to allow for timeout. Because this returns a value then there must be a way to signal that a value in a read lock was not possible.  If a millisecondsTimeout is provided a TimeoutException will be thrown if the timeout is reached.</param>
		/// <returns>True if a lock is aquired.  False if throwsOnTimeout is false and was unable to acquire a lock.</returns>
		/// <exception cref="TimeoutException">Unable to acquire a lock.</exception>
		public static T SynchronizeRead<TSync, T>(TSync syncObject, object key, Func<T> closure, int? millisecondsTimeout = null) where TSync : class
		{
			ValidateSyncObject(syncObject);
			if (key is null)
				throw new ArgumentNullException(nameof(key));
			if (closure is null)
				throw new ArgumentNullException(nameof(closure));
			ValidateMillisecondsTimeout(millisecondsTimeout);
			Contract.EndContractBlock();

			return GetReadWriteHelper(syncObject)
				.ReadValue(key,
					closure, millisecondsTimeout);
		}

		/// <summary>
		/// Manages a read-only operation of any target and returns the value from the closure.
		/// </summary>
		/// <typeparam name="TSync">Type of the object sync context.</typeparam>
		/// <typeparam name="T">Type of the result.</typeparam>
		/// <param name="syncObject">The main object that defines the synchronization context.</param>
		/// <param name="closure">The function to execute while under a read lock.</param>
		/// <param name="millisecondsTimeout">An optional value to allow for timeout. Because this returns a value then there must be a way to signal that a value in a read lock was not possible.  If a millisecondsTimeout is provided a TimeoutException will be thrown if the timeout is reached.</param>
		/// <returns>The value from the closure.</returns>
		/// <exception cref="TimeoutException">Unable to acquire a lock.</exception>
		public static T SynchronizeRead<TSync, T>(TSync syncObject, Func<T> closure, int? millisecondsTimeout = null) where TSync : class
		{
			ValidateSyncObject(syncObject);
			if (closure is null)
				throw new ArgumentNullException(nameof(closure));
			ValidateMillisecondsTimeout(millisecondsTimeout);
			Contract.EndContractBlock();

			return SynchronizeRead(syncObject, syncObject, closure, millisecondsTimeout);
		}

		/// <summary>
		/// Manages a read-only operation of any target and the provided key.
		/// </summary>
		/// <typeparam name="TSync">Type of the object sync context.</typeparam>
		/// <param name="syncObject">The main object that defines the synchronization context.</param>
		/// <param name="key">The key that represents what value being read from.</param>
		/// <param name="closure">The function to execute while under a read lock.</param>
		/// <param name="millisecondsTimeout">An optional value to allow for timeout. Because this returns a value then there must be a way to signal that a value in a read lock was not possible.  If a millisecondsTimeout is provided a TimeoutException will be thrown if the timeout is reached.</param>
		/// <param name="throwsOnTimeout">If true, and a millisecondsTimeout value is provided, a TimeoutException will be thrown if the timeout is reached the instead of this method returning false.</param>
		/// <returns>True if a lock is aquired.  False if throwsOnTimeout is false and was unable to acquire a lock.</returns>
		/// <exception cref="TimeoutException">Unable to acquire a lock.</exception>
		public static bool SynchronizeRead<TSync>(TSync syncObject, object key, Action closure, int? millisecondsTimeout = null, bool throwsOnTimeout = true) where TSync : class
		{
			ValidateSyncObject(syncObject);
			if (key is null)
				throw new ArgumentNullException(nameof(key));
			if (closure is null)
				throw new ArgumentNullException(nameof(closure));
			ValidateMillisecondsTimeout(millisecondsTimeout);
			Contract.EndContractBlock();

			return GetReadWriteHelper(syncObject)
				.Read(key,
					closure, millisecondsTimeout, throwsOnTimeout);
		}

		/// <summary>
		/// Manages a read-only operation of any target.
		/// </summary>
		/// <typeparam name="TSync">Type of the object sync context.</typeparam>
		/// <param name="syncObject">The main object that defines the synchronization context.</param>
		/// <param name="closure">The function to execute while under a read lock.</param>
		/// <param name="millisecondsTimeout">An optional value to allow for timeout. Because this returns a value then there must be a way to signal that a value in a read lock was not possible.  If a millisecondsTimeout is provided a TimeoutException will be thrown if the timeout is reached.</param>
		/// <param name="throwsOnTimeout">If true, and a millisecondsTimeout value is provided, a TimeoutException will be thrown if the timeout is reached the instead of this method returning false.</param>
		/// <returns>The value from the closure.</returns>
		/// <exception cref="TimeoutException">Unable to acquire a lock.</exception>
		public static bool SynchronizeRead<TSync>(TSync syncObject, Action closure, int? millisecondsTimeout = null, bool throwsOnTimeout = true) where TSync : class
		{
			ValidateSyncObject(syncObject);
			if (closure is null)
				throw new ArgumentNullException(nameof(closure));
			ValidateMillisecondsTimeout(millisecondsTimeout);
			Contract.EndContractBlock();

			return SynchronizeRead(syncObject, syncObject, closure, millisecondsTimeout, throwsOnTimeout);
		}

		/// <summary>
		/// Manages a write lock operation of any target and the provided key.
		/// </summary>
		/// <typeparam name="TSync">Type of the object sync context.</typeparam>
		/// <param name="syncObject">The main object that defines the synchronization context.</param>
		/// <param name="key">The key that represents what value being written to.</param>
		/// <param name="closure">The function to execute while under a write lock.</param>
		/// <param name="millisecondsTimeout">An optional value to allow for timeout. Because this returns a value then there must be a way to signal that a value in a read lock was not possible.  If a millisecondsTimeout is provided a TimeoutException will be thrown if the timeout is reached.</param>
		/// <param name="throwsOnTimeout">If true, and a millisecondsTimeout value is provided, a TimeoutException will be thrown if the timeout is reached the instead of this method returning false.</param>
		/// <returns>True if a lock is aquired.  False if throwsOnTimeout is false and was unable to acquire a lock.</returns>
		/// <exception cref="TimeoutException">Unable to acquire a lock.</exception>
		public static bool SynchronizeWrite<TSync>(TSync syncObject, object key, Action closure, int? millisecondsTimeout = null, bool throwsOnTimeout = true) where TSync : class
		{
			ValidateSyncObject(syncObject);
			if (key is null)
				throw new ArgumentNullException(nameof(key));
			if (closure is null)
				throw new ArgumentNullException(nameof(closure));
			ValidateMillisecondsTimeout(millisecondsTimeout);
			Contract.EndContractBlock();

			return GetReadWriteHelper(syncObject)
				.Write(key,
					closure, millisecondsTimeout, throwsOnTimeout);
		}

		/// <summary>
		/// Manages a write lock operation of any target.
		/// </summary>
		/// <typeparam name="TSync">Type of the object sync context.</typeparam>
		/// <param name="syncObject">The main object that defines the synchronization context.</param>
		/// <param name="closure">The function to execute while under a write lock.</param>
		/// <param name="millisecondsTimeout">An optional value to allow for timeout. Because this returns a value then there must be a way to signal that a value in a read lock was not possible.  If a millisecondsTimeout is provided a TimeoutException will be thrown if the timeout is reached.</param>
		/// <param name="throwsOnTimeout">If true, and a millisecondsTimeout value is provided, a TimeoutException will be thrown if the timeout is reached the instead of this method returning false.</param>
		/// <returns>True if a lock is aquired.  False if throwsOnTimeout is false and was unable to acquire a lock.</returns>
		/// <exception cref="TimeoutException">Unable to acquire a lock.</exception>
		public static bool SynchronizeWrite<TSync>(TSync syncObject, Action closure, int? millisecondsTimeout = null, bool throwsOnTimeout = true) where TSync : class
		{
			ValidateSyncObject(syncObject);
			if (closure is null)
				throw new ArgumentNullException(nameof(closure));
			ValidateMillisecondsTimeout(millisecondsTimeout);
			Contract.EndContractBlock();

			return SynchronizeWrite(syncObject, syncObject, closure, millisecondsTimeout, throwsOnTimeout);
		}

		/// <summary>
		/// A class that can be used as a locking context for an object and then selectively locks individual keys.
		/// 
		/// Example: Coupling this with Dictionary could simplify synchronized access to the key-values.
		/// </summary>
		/// <typeparam name="TKey">The type of the key.</typeparam>
		/// <typeparam name="TSyncObject">The type of the object.</typeparam>
		public class Helper<TKey, TSyncObject>
			where TSyncObject : class, new()
		{
			protected readonly ConcurrentDictionary<TKey, TSyncObject> _locks = new();

			/// <summary>
			/// Returns a unique object based on the provied cacheKey for use in synchronization.
			/// </summary>
			public TSyncObject this[TKey key]
			{
				get
				{
					if (key is null)
						throw new ArgumentNullException(nameof(key));
					Contract.EndContractBlock();

					return _locks.GetOrAdd(key, k => new TSyncObject())
						?? new TSyncObject(); // Satisfies code contracts... (Will never actually occur).
				}
			}

			/// <summary>
			/// Clears all synchronization objects.
			/// </summary>
			public void Reset() => _locks.Clear();


			/// <summary>
			/// Sychronizes executing the Action based on the cacheKey provided.
			/// </summary>
			public void Lock(TKey key, Action closure)
			{
				if (key is null)
					throw new ArgumentNullException(nameof(key));
				if (closure is null)
					throw new ArgumentNullException(nameof(closure));
				Contract.EndContractBlock();

				ThreadSafety.Lock(this[key], closure);
			}

			/// <summary>
			/// Sychronizes executing the Action based on the cacheKey provided using a timeout.
			/// Throws a TimeoutException if throwsOnTimeout is true (default) and a lock could not be aquired.
			/// </summary>
			public bool Lock(TKey key, Action closure, int millisecondsTimeout, bool throwsOnTimeout = true)
				=> ThreadSafety.Lock(this[key], closure, millisecondsTimeout, throwsOnTimeout);

			/// <summary>
			/// Attempts to sychronize executing the Action based on the cacheKey provided using a timeout.
			/// </summary>
			public bool TryLock(TKey key, Action closure, int millisecondsTimeout)
				=> ThreadSafety.TryLock(this[key], closure, millisecondsTimeout);

			/// <summary>
			/// Sychronizes executing the Action only if the condition is true based on the cacheKey provided.
			/// </summary>
			public void LockConditional(TKey key, Func<bool> condition, Action closure)
				=> ThreadSafety.LockConditional(this[key], condition, closure);

			/// <summary>
			/// Sychronizes executing the Action only if the condition is true based on the cacheKey provided using a timeout.
			/// Throws a TimeoutException if throwsOnTimeout is true (default) and a lock could not be aquired.
			/// </summary>
			public bool LockConditional(TKey key, Func<bool> condition, Action closure, int millisecondsTimeout, bool throwsOnTimeout)
				=> ThreadSafety.LockConditional(this[key], condition, closure, millisecondsTimeout, throwsOnTimeout);


		}


		/// <inheritdoc />
		/// <summary>
		/// A class that can be used as a locking context for an object and then selectively locks individual keys.
		/// Example: Coupling this with Dictionary could simplify synchronized access to the key-values.
		/// </summary>
		/// <typeparam name="TKey">The type of the key.</typeparam>
		public class Helper<TKey> : Helper<TKey, object>
			where TKey : class
		{
		}




		/// <inheritdoc />
		/// <summary>
		/// A class that can be used as a locking context for an object and then selectively locks individual keys.
		/// The keys are strings.
		/// Example: Coupling this with Dictionary could simplify synchronized access to the key-values.
		/// </summary>
		public class Helper : Helper<string>
		{

		}

		/// <summary>
		/// A set of extensions that helps synchronize and improve the robustness of file access.
		/// 
		/// If your application is multi-threaded but has exclusive access to files, this can help eliminate exceptions when attempting high-throughput file read/writes.
		/// </summary>
		public static class File
		{

			internal static void ValidatePath(string path)
			{
				if (path is null)
					throw new ArgumentNullException(nameof(path));
				if (string.IsNullOrWhiteSpace(path))
					throw new ArgumentException("Cannot be empty or white space.", nameof(path));
				Contract.Ensures(path != null);
				Contract.EndContractBlock();
			}

			static ReadWriteHelper<string>? _instance;
			private static ReadWriteHelper<string> Instance => LazyInitializer.EnsureInitialized(ref _instance, () => new ReadWriteHelper<string>())!;

			/// <summary>
			/// Manages registering a ReaderWriterLockSlim an synchronizing the provided query write access.
			/// </summary>
			public static bool WriteTo(string path, Action closure,
				int? millisecondsTimeout = null, bool throwsOnTimeout = false)
			{
				ValidatePath(path);
				Contract.EndContractBlock();

				return Instance.Write(path, closure, millisecondsTimeout, throwsOnTimeout);
			}

			/// <summary>
			/// Manages file stream write access and retries.
			/// </summary>
			private static void WriteToInternal(string path, Action<FileStream> closure,
				int retries = DEFAULT_RETRIES,
				int millisecondsRetryDelay = DEFAULT_RETRYDELAY,
				int? millisecondsTimeout = null,
				bool throwsOnTimeout = false,
				FileMode mode = FileMode.OpenOrCreate,
				FileAccess access = FileAccess.Write,
				FileShare share = FileShare.None)
			{
				if (closure is null)
					throw new ArgumentNullException(nameof(closure));
				Contract.EndContractBlock();

				WriteTo(path, () =>
				{
					using var fs = Unsafe.GetFileStream(path, retries, millisecondsRetryDelay, mode, access, share);
					closure(fs);
				},
				millisecondsTimeout, throwsOnTimeout);
			}

			/// <summary>
			/// Manages file stream read access and retries.
			/// </summary>
			public static void WriteTo(string path, Action<FileStream> closure,
				int retries = DEFAULT_RETRIES,
				int millisecondsRetryDelay = DEFAULT_RETRYDELAY,
				int? millisecondsTimeout = null,
				bool throwsOnTimeout = false)
			{
				WriteToInternal(path, closure, retries, millisecondsRetryDelay, millisecondsTimeout, throwsOnTimeout);
			}

			/// <summary>
			/// Manages file stream read access and retries.
			/// </summary>
			public static void AppendTo(string path, Action<FileStream> closure,
				int retries = DEFAULT_RETRIES,
				int millisecondsRetryDelay = DEFAULT_RETRYDELAY,
				int? millisecondsTimeout = null,
				bool throwsOnTimeout = false)
			{
				WriteToInternal(path, closure, retries, millisecondsRetryDelay, millisecondsTimeout, throwsOnTimeout, FileMode.Append);
			}

			/// <summary>
			/// Manages file stream read access and retries.
			/// </summary>
			public static void AppendLineTo(string path, string text,
				int retries = DEFAULT_RETRIES,
				int millisecondsRetryDelay = DEFAULT_RETRYDELAY,
				int? millisecondsTimeout = null,
				bool throwsOnTimeout = false)
			{
				if (text is null)
					throw new ArgumentNullException(nameof(text));
				Contract.EndContractBlock();

				AppendTo(path, fs =>
				{
					using var sw = new StreamWriter(fs);
					sw.WriteLine(text);
					sw.Flush();
				}, retries, millisecondsRetryDelay, millisecondsTimeout, throwsOnTimeout);
			}

			/// <summary>
			/// Manages registering a ReaderWriterLockSlim and synchronizing the provided query write access.
			/// </summary>
			public static T WriteTo<T>(string path, Func<T> closure,
				int? millisecondsTimeout = null)
			{
				ValidatePath(path);
				Contract.EndContractBlock();

				return Instance.WriteValue(path, closure, millisecondsTimeout);
			}

			/// <summary>
			/// Manages registering a ReaderWriterLockSlim and synchronizing the provided query read access.
			/// </summary>
			public static bool ReadFrom(string path, Action closure,
				int? millisecondsTimeout = null, bool throwsOnTimeout = false)
			{
				ValidatePath(path);
				Contract.EndContractBlock();

				return Instance.Read(path, closure, millisecondsTimeout, throwsOnTimeout);
			}

			/// <summary>
			/// Manages registering a ReaderWriterLockSlim and synchronizing the provided query read access.
			/// </summary>
			public static bool ReadFromUpgradeable(
				string path, Action closure,
				int? millisecondsTimeout = null, bool throwsOnTimeout = false)
			{
				ValidatePath(path);
				Contract.EndContractBlock();

				return Instance.ReadUpgradeable(path, closure, millisecondsTimeout, throwsOnTimeout);
			}

			/// <summary>
			/// Manages registering a ReaderWriterLockSlim and synchronizing the provided query read access.
			/// </summary>
			public static bool ReadFromUpgradeable<T>(
				out T result, string path, Func<T> closure,
				int? millisecondsTimeout = null, bool throwsOnTimeout = false)
			{
				ValidatePath(path);
				Contract.EndContractBlock();

				return Instance.ReadUpgradeable(path, out result, closure, millisecondsTimeout, throwsOnTimeout);
			}

			public static bool WriteToIfNotExists(
				string path, Action closure,
				int? millisecondsTimeout = null, bool throwsOnTimeout = false)
			{

				var writtenTo = false;
				if (!Exists(path))
				{
					ReadFromUpgradeable(out writtenTo, path, () =>
					{
						if (System.IO.File.Exists(path)) return false;
						WriteTo(path, closure, millisecondsTimeout, throwsOnTimeout);
						return true;
					});
				}
				return writtenTo;
			}

			/// <summary>
			/// Manages registering a ReaderWriterLockSlim an synchronizing the provided query read access.
			/// </summary>
			public static T ReadFrom<T>(string path, Func<T> closure,
				int? millisecondsTimeout = null)
			{
				ValidatePath(path);
				Contract.EndContractBlock();

				return Instance.ReadValue(path, closure, millisecondsTimeout);
			}

			private const int DEFAULT_RETRIES = 4;
			private const int DEFAULT_RETRYDELAY = 4;


			/// <summary>
			/// Manages file stream read access and retries.
			/// </summary>
			public static void ReadFrom(string path, Action<FileStream> closure,
				int retries = DEFAULT_RETRIES,
				int millisecondsRetryDelay = DEFAULT_RETRYDELAY,
				int? millisecondsTimeout = null,
				bool throwsOnTimeout = false)
			{
				ValidatePath(path);
				if (closure is null)
					throw new ArgumentNullException(nameof(closure));
				Contract.EndContractBlock();

				ReadFrom(path, () =>
				{
					using var fs = Unsafe.GetFileStreamForRead(path, retries, millisecondsRetryDelay);
					closure(fs);
				},
				millisecondsTimeout, throwsOnTimeout);
			}


			/// <summary>
			/// Manages file stream read access and retries.
			/// </summary>
			public static T ReadFrom<T>(string path, Func<FileStream, T> closure,
				int retries = DEFAULT_RETRIES,
				int millisecondsRetryDelay = DEFAULT_RETRYDELAY,
				int? millisecondsTimeout = null)
			{
				if (closure is null)
					throw new ArgumentNullException(nameof(closure));
				Contract.EndContractBlock();

				return ReadFrom(path, () =>
				{
					using var fs = Unsafe.GetFileStreamForRead(path, retries, millisecondsRetryDelay);
					return closure(fs);
				}, millisecondsTimeout);
			}

			public static string ReadToString(string path, int retries = DEFAULT_RETRIES,
				int millisecondsRetryDelay = DEFAULT_RETRYDELAY,
				int? millisecondsTimeout = null)
			{
				return ReadFrom(path, (fs) =>
				{
					using var reader = new StreamReader(fs);
					return reader.ReadToEnd();
				}, retries, millisecondsRetryDelay, millisecondsTimeout);
			}

			public static class Unsafe
			{
				public static FileStream GetFileStream(string path, int retries, int millisecondsRetryDelay,
					FileMode mode, FileAccess access, FileShare share, int bufferSize = 4096, bool async = false)
				{
					ValidatePath(path);
					Contract.EndContractBlock();

					FileStream? fs = null;
					var failCount = 0;
					do
					{
						// Need to retry in case of cross process locking...
						try
						{
							fs = new FileStream(path, mode, access, share, bufferSize, async);
							failCount = 0;
						}
						catch (IOException ioex)
						{
							failCount++;
							if (failCount > retries)
								throw;

							Debug.WriteLineIf(failCount == 1, "Error when acquring file stream: " + ioex.Message);
						}

						if (failCount != 0)
							Thread.Sleep(millisecondsRetryDelay);

					} while (failCount != 0);

					return fs!;
				}

				public static async Task<FileStream> GetFileStreamAsync(string path, int retries, int millisecondsRetryDelay,
					FileMode mode, FileAccess access, FileShare share, int bufferSize = 4096, bool async = true)
				{
					ValidatePath(path);
					Contract.EndContractBlock();

					FileStream? fs = null;
					var failCount = 0;
					do
					{
						// Need to retry in case of cross process locking...
						try
						{
							fs = new FileStream(path, mode, access, share, bufferSize, async);
							failCount = 0;
						}
						catch (IOException ioex)
						{
							failCount++;
							if (failCount > retries)
								throw;

							Debug.WriteLineIf(failCount == 1, "Error when acquring file stream: " + ioex.Message);
						}

						if (failCount != 0)
							await Task.Delay(millisecondsRetryDelay);

					} while (failCount != 0);

					return fs!;
				}

				public static FileStream GetFileStreamForRead(
					string path,
					int retries = DEFAULT_RETRIES,
					int millisecondsRetryDelay = DEFAULT_RETRYDELAY,
					int bufferSize = 4096, bool useAsync = false)
				{
					return GetFileStream(path, retries, millisecondsRetryDelay,
						FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync);
				}

				public static Task<FileStream> GetFileStreamForReadAsync(
				string path,
				int retries = DEFAULT_RETRIES,
				int millisecondsRetryDelay = DEFAULT_RETRYDELAY,
				int bufferSize = 4096, bool useAsync = false)
				{
					return GetFileStreamAsync(path, retries, millisecondsRetryDelay,
						FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync);
				}

			}





			public static FileStream GetFileStreamForRead(string path,
				int retries = DEFAULT_RETRIES,
				int millisecondsRetryDelay = DEFAULT_RETRYDELAY,
				int? millisecondsTimeout = null)
			{

				return ReadFrom(path, () => Unsafe.GetFileStreamForRead(path, retries, millisecondsRetryDelay), millisecondsTimeout);
			}



			/// <summary>
			/// Uses registered read access conditions to determine if a file exists.
			/// </summary>
			public static bool Exists(string path,
				int? millisecondsTimeout = null)
			{
				ValidatePath(path);

				return ReadFrom(path, () => System.IO.File.Exists(path), millisecondsTimeout);
			}

			public static void EnsureDirectory(string path,
				int? millisecondsTimeout = null)
			{
				ValidatePath(path);
				Contract.EndContractBlock();

				path = Path.GetDirectoryName(path);

				if (!ReadFrom(path, () => Directory.Exists(path), millisecondsTimeout))
				{
					ReadFromUpgradeable(path, () =>
					{
						if (!Directory.Exists(path))
							WriteTo(path,
								() =>
								{
									Debug.Assert(path != null);
									// ReSharper disable once AssignNullToNotNullAttribute
									return Directory.CreateDirectory(path);
								},
								millisecondsTimeout);
					});
				}
			}



		}
	}
}
