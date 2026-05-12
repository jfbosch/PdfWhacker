using PdfWhacker;

namespace PdfWhacker.Tests;

public class FileReadyWaiterTests : IDisposable
{
	private readonly string _tempDir;

	public FileReadyWaiterTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), "pdfwhacker-waiter-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_tempDir);
	}

	public void Dispose()
	{
		try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
	}

	[Fact]
	public void Returns_true_for_an_existing_readable_file()
	{
		string path = Path.Combine(_tempDir, "ready.bin");
		File.WriteAllText(path, "hello");

		Assert.True(FileReadyWaiter.TryWait(path, TimeSpan.FromSeconds(1)));
	}

	[Fact]
	public void Returns_false_when_file_never_appears_within_timeout()
	{
		string path = Path.Combine(_tempDir, "never.bin");

		var sw = System.Diagnostics.Stopwatch.StartNew();
		bool result = FileReadyWaiter.TryWait(path, TimeSpan.FromMilliseconds(200), pollInterval: TimeSpan.FromMilliseconds(50));
		sw.Stop();

		Assert.False(result);
		Assert.True(sw.ElapsedMilliseconds >= 180, $"Returned too early: {sw.ElapsedMilliseconds}ms");
		Assert.True(sw.ElapsedMilliseconds < 2000, $"Took far too long: {sw.ElapsedMilliseconds}ms");
	}

	[Fact]
	public void Returns_false_when_file_remains_exclusively_locked()
	{
		string path = Path.Combine(_tempDir, "locked.bin");
		File.WriteAllText(path, "data");

		using var holder = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

		var sw = System.Diagnostics.Stopwatch.StartNew();
		bool result = FileReadyWaiter.TryWait(path, TimeSpan.FromMilliseconds(300), pollInterval: TimeSpan.FromMilliseconds(50));
		sw.Stop();

		Assert.False(result);
		Assert.True(sw.ElapsedMilliseconds >= 250, $"Returned too early: {sw.ElapsedMilliseconds}ms");
	}

	[Fact]
	public async Task Returns_true_once_file_lock_is_released()
	{
		string path = Path.Combine(_tempDir, "released.bin");
		File.WriteAllText(path, "data");

		var holder = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

		// Release the lock after 150ms; waiter should pick it up well within its 2-second budget.
		var releaser = Task.Run(async () =>
		{
			await Task.Delay(150, TestContext.Current.CancellationToken);
			holder.Dispose();
		}, TestContext.Current.CancellationToken);

		bool result = FileReadyWaiter.TryWait(path, TimeSpan.FromSeconds(2), pollInterval: TimeSpan.FromMilliseconds(50));
		await releaser;

		Assert.True(result);
	}

	[Fact]
	public void Returns_false_for_a_zero_byte_file_within_timeout()
	{
		// Per FileReadyWaiter contract, a zero-length file is not considered "ready"
		// because empty.Length > 0 is the readiness signal.
		string path = Path.Combine(_tempDir, "empty.bin");
		using (File.Create(path)) { }

		bool result = FileReadyWaiter.TryWait(path, TimeSpan.FromMilliseconds(200), pollInterval: TimeSpan.FromMilliseconds(50));
		Assert.False(result);
	}
}
