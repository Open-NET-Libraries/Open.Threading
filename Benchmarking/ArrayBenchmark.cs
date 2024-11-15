using BenchmarkDotNet.Attributes;

namespace Open.Threading.Benchmarking;

public class ArrayBenchmark
{
	const int Length = 3;
	const int Count = Length * 1000000;
	readonly System.Threading.Lock SysLock = new();
	readonly int[] TestData = Enumerable.Range(0, Length).ToArray();
	readonly System.Threading.Lock[] Locks = Enumerable.Range(0, Length).Select(_ => new System.Threading.Lock()).ToArray();
	readonly ReaderWriterLockSlim RWLock = new();
	readonly ReaderWriterLockSlim[] RWLocks = Enumerable.Range(0, Length).Select(_ => new ReaderWriterLockSlim()).ToArray();

	[Benchmark(Baseline = true)]
	public void RandomParallelReads()
		=> Parallel.For(0, Count, i => _ = TestData[i % Length]);

	[Benchmark]
	public void RandomParallelSingleMonitorLockReads()
		=> Parallel.For(0, Count, i =>
		{
			lock (TestData) _ = TestData[i % Length];
		});

	[Benchmark]
	public void RandomParallelSingleSystemLockReads()
		=> Parallel.For(0, Count, i =>
		{
			lock (SysLock) _ = TestData[i % Length];
		});

	[Benchmark]
	public void RandomParallelSingleRWLockReads()
		=> Parallel.For(0, Count, i =>
		{
			using var rwlock = RWLock.ReadLock();
			_ = TestData[i % Length];
		});

	[Benchmark]
	public void RandomParallelLockedReads()
		=> Parallel.For(0, Count, i =>
		{
			var n = i % Length;
			lock (Locks[n]) _ = TestData[n];
		});

	[Benchmark]
	public void RandomParallelUsingLockedReads()
		=> Parallel.For(0, Count, i =>
		{
			var n = i % Length;
			using var @lock = new Lock(Locks[n]);
			_ = TestData[n];
		});

	[Benchmark]
	public void RandomParallelRWLockedReads()
		=> Parallel.For(0, Count, i =>
		{
			var n = i % Length;
			using var rwlock = RWLocks[n].ReadLock();
			_ = TestData[n];
		});
}
