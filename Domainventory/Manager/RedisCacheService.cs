using StackExchange.Redis;
using System.Net;
using System.Text.Json;
namespace Domainventory.Manager
{

	public class RedisCacheService
	{
		private readonly IDatabase _redisDb;
		private readonly ConnectionMultiplexer _redis;

		public RedisCacheService(string connectionString)
		{
			_redis = ConnectionMultiplexer.Connect(connectionString);
			_redisDb = _redis.GetDatabase();
		}

		public async Task SaveToCacheAsync(string domain, IPHostEntry? entry)
		{
			var key = $"dns:{domain}";

			if (entry == null)
			{
				await _redisDb.StringSetAsync(key, "null", TimeSpan.FromHours(6));
			}
			else
			{
				var ips = entry.AddressList.Select(ip => ip.ToString()).ToArray();
				var json = JsonSerializer.Serialize(ips);
				await _redisDb.StringSetAsync(key, json, TimeSpan.FromHours(6));
			}
		}

		public async Task<IPHostEntry?> GetFromCacheAsync(string domain)
		{
			var key = $"dns:{domain}";
			var value = await _redisDb.StringGetAsync(key);

			if (value.IsNullOrEmpty) return null;
			if (value == "null") return null;

			try
			{
				var ipStrings = JsonSerializer.Deserialize<string[]>(value!);
				var ips = ipStrings?.Select(IPAddress.Parse).ToArray();

				return new IPHostEntry
				{
					HostName = domain,
					AddressList = ips ?? Array.Empty<IPAddress>()
				};
			}
			catch
			{
				return null;
			}
		}

		public async Task ClearAllDnsCacheAsync()
		{
			var endpoints = _redis.GetEndPoints();
			var server = _redis.GetServer(endpoints[0]);

			foreach (var key in server.Keys(pattern: "dns:*"))
			{
				await _redisDb.KeyDeleteAsync(key);
			}
		}
	}

}
