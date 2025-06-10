using System.Collections.Concurrent;

namespace Domainventory.Models
{
	public static class ConnectionMapping
	{
		private static readonly ConcurrentDictionary<string, string> _map = new();

		public static void AddOrUpdate(string clientSessionId, string connectionId) =>
			_map[clientSessionId] = connectionId;

		public static string? GetConnectionId(string clientSessionId) =>
			_map.TryGetValue(clientSessionId, out var connectionId) ? connectionId : null;

		public static void RemoveByConnectionId(string connectionId)
		{
			foreach (var pair in _map.Where(p => p.Value == connectionId).ToList())
			{
				_map.TryRemove(pair.Key, out _);
			}
		}
	}

}
