using BenchmarkDotNet.Attributes;

namespace Open.Threading.Benchmarking;

public class ArrayBenchmark
{
	const int Length = 3;
	const int Count = Length * 10000000;
	readonly int[] TestData = Enumerable.Range(0, Length).ToArray();
	readonly object[] Locks = Enumerable.Range(0, Length).Select(_=>new object()).ToArray();
	readonly ReaderWriterLockSlim RWLock = new();
	readonly ReaderWriterLockSlim[] RWLocks = Enumerable.Range(0, Length).Select(_ => new ReaderWriterLockSlim()).ToArray();

	[Benchmark(Baseline = true)]
	public void RandomParallelReads()
		=> Parallel.For(0, Count, i => _ = TestData[i % Length]);

	[Benchmark]
	public void RandomParallelSingleLockReads()
		=> Parallel.For(0, Count, i =>
		{
			lock (TestData) _ = TestData[i % Length];
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
	public void RandomParallelRWLockedReads()
		=> Parallel.For(0, Count, i =>
		{
			var n = i % Length;
			using var rwlock = RWLocks[n].ReadLock();
			_ = TestData[n];
		});
}
