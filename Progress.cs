using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Open.Threading;

public class Progress
{
	public Progress()
	{
		Index = 0;
		Count = 0;

		var now = DateTime.Now;
		Started = now;
		LastUpdated = now;
	}

	public DateTime Started;
	public DateTime LastUpdated
	{
		get;
		private set;
	}

	long _count;
	public long Count
	{
		get => _count;
		set => Interlocked.Exchange(ref _count, value);
	}

	long _index;
	public long Index
	{
		get => _index;
		set
		{
			LastUpdated = DateTime.Now;
			Interlocked.Exchange(ref _index, value);
		}
	}

	public void IncrementIndex() => Interlocked.Increment(ref _index);

	public void Start(int? newCount = null)
	{
		Success = null;
		Started = DateTime.Now;
		Index = 0;
		if (newCount.HasValue)
			Count = newCount.Value;
		if (Count == 0)
			Count = 1;
	}

	public void Run(Action closure, bool propagateException = false)
	{
		if (closure is null)
			throw new ArgumentNullException(nameof(closure));

		Start();
		try
		{
			closure();
			Finish();
		}
		catch (Exception ex)
		{
			Failed(ex.ToString());
			if (propagateException)
				throw;
		}
	}

#if NETSTANDARD2_1
	[return: MaybeNull]
#endif
	public T Execute<T>(Func<T> query, bool propagateException = false)
	{
		if (query is null)
			throw new ArgumentNullException(nameof(query));

		Start();
		try
		{
			var result = query();
			Finish();
			return result;
		}
		catch (Exception ex)
		{
			Failed(ex.ToString());
			if (propagateException)
				throw;
			return default!;
		}
	}

	public void Finish(bool success = true)
	{
		if (Count == 0)
			Count = 1;
		Index = Count;
		Success = success;
	}

	public bool HasStarted => Count != 0;

	public double Value
	{
		get
		{
			if (Count == 0)
				return 0; // Signify it hasn'T started.

			return (double)Index / Count;
		}
	}

	public bool? Success
	{
		get;
		set;
	}

	public string? Message
	{
		get;
		set;
	}

	public void Failed(Exception ex)
	{
		if (ex is null)
			throw new ArgumentNullException(nameof(ex));

		Failed(ex.ToString());
	}

	public void Failed(string message)
	{
		Message = message;
		Success = false;
		if (Count == 0)
			Count = 1;
		Index = Count;
	}

	public TimeSpan ElapsedSinceLastUpdated => LastUpdated - Started;

	public TimeSpan Elapsed => DateTime.Now - Started;

	public TimeSpan TimeSinceLastUpdate => DateTime.Now - LastUpdated;

	public TimeSpan EstimatedTimeLeft
	{
		get
		{
			if (Index == 0 || Count == 0)
				return TimeSpan.MaxValue;

			var remaining = Count - Index;
			var ticks = remaining * Elapsed.Ticks / Index;

			// Bloat samples at the beginning...
			ticks += ticks * remaining / Count;

			return TimeSpan.FromTicks(ticks);
		}
	}

	public string EstimatedTimeLeftString
		=> EstimatedTimeLeft.ToString();//.ToStringVerbose();
}
