namespace Domainventory.Manager
{
	public class AsyncManualResetEvent
	{
		private volatile TaskCompletionSource<bool> _tcs = new TaskCompletionSource<bool>();

		public AsyncManualResetEvent(bool initialState)
		{
			if (initialState)
				_tcs.SetResult(true);
		}
		public Task WaitAsync(CancellationToken token)
		{
			return _tcs.Task.WaitAsync(token); // .NET 6+
		}
		public Task WaitAsync() => _tcs.Task;

		public void Set()
		{
			var tcs = _tcs;
			Task.Run(() => tcs.TrySetResult(true));
		}

		public void Reset()
		{
			while (true)
			{
				var tcs = _tcs;
				if (!tcs.Task.IsCompleted ||
					Interlocked.CompareExchange(ref _tcs, new TaskCompletionSource<bool>(), tcs) == tcs)
					return;
			}
		}
	}
}
