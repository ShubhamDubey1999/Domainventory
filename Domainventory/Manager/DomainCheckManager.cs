namespace Domainventory.Manager
{
	public static class DomainCheckManager
	{
		public static CancellationTokenSource TokenSource = new();
		public static AsyncManualResetEvent PauseEvent = new AsyncManualResetEvent(true);

	}

}
