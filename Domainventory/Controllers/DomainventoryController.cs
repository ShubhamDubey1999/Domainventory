using Domainventory.Models;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Net;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Diagnostics;
using System.Text;
using System.Net.Http.Headers;
using Newtonsoft.Json.Linq;

namespace Domainventory.Controllers
{
	public class DomainventoryController : Controller
	{
		private static ConcurrentDictionary<string, ProgressInfo> _progressStore = new();
		private static readonly SemaphoreSlim _csvWriteSemaphore = new SemaphoreSlim(1, 1);
		Dictionary<string, WhoisServerInfo> _whoisServers;
		private readonly IWebHostEnvironment _environment;
		private IConfiguration _config;
		private static readonly Regex DomainRegex = new(@"^(?!-)(?:[A-Za-z0-9-]{1,63}\.)+[A-Za-z]{2,}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		private static bool IsValidDomain(string domain) => DomainRegex.IsMatch(domain);

		public DomainventoryController(IWebHostEnvironment environment, IConfiguration configuration)
		{
			_environment = environment;
			_whoisServers = LoadWhoisServers();
			this._config = configuration;
		}
		private Dictionary<string, WhoisServerInfo> LoadWhoisServers()
		{
			var path = Path.Combine(_environment.WebRootPath, "js", "servers.json");
			var json = System.IO.File.ReadAllText(path);

			var options = new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			};

			return JsonSerializer.Deserialize<Dictionary<string, WhoisServerInfo>>(json, options);
		}
		public IActionResult Index()
		{
			ViewBag.Tlds = _whoisServers.Keys.ToList();
			return View();
		}

		public IActionResult Index2()
		{
			ViewBag.Tlds = _whoisServers.Keys.ToList();
			return View();
		}


		[HttpPost]
		public async Task<IActionResult> CheckDomains([FromBody] DomainRequest request, string requestId, CancellationToken cancellationToken)
		{
			if (request.Domains == null || !request.Domains.Any())
				return BadRequest("No domains provided.");
			var summary = await DomainCheck(request, requestId, cancellationToken);
			return Ok(new
			{
				summary.Available,
				summary.Unavailable,
				summary.Error,
				summary.Total,
				TimeTakenInSeconds = summary.TimeTaken,
				Results = summary.Results,
				CsvFileName = summary.CsvFileName
			});
		}

		public IActionResult GetProgress(string requestId)
		{
			if (_progressStore.TryGetValue(requestId, out var progress))
				return Ok(progress);

			return Json(new { Processed = 0, Total = 100 });
		}

		public async Task<DomainCheckSummary> DomainCheck(DomainRequest request, string requestId, CancellationToken cancellationToken)
		{
			int processedCount = 0;

			var progress = new ProgressInfo
			{
				Total = 0,
				Processed = 0
			};
			var stopwatch = Stopwatch.StartNew();
			var hasPrefixOrSuffix = !string.IsNullOrEmpty(request.Prefix) || !string.IsNullOrEmpty(request.Suffix);
			var hasTlds = request.Tlds?.Any() == true;
			var domainsToCheck = request.Domains
								.Select(d => hasPrefixOrSuffix ? $"{request.Prefix}{d}{request.Suffix}" : d)
								.SelectMany(d => hasTlds ? request.Tlds.Select(tld => $"{d}.{tld}") : new[] { d })
								.Where(d => request.Maxlength == 0 || d.Split(".")[0].Length == request.Maxlength)
								.ToList();
			progress.Total = domainsToCheck.Count;
			_progressStore[requestId] = progress;

			int MaxConcurrency = 100, BatchSize = 500;
			var results = new ConcurrentBag<DomainResult>();

			using var semaphore = new SemaphoreSlim(MaxConcurrency);

			var baseCsvFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "domain-results");
			CleanupOldFolders(baseCsvFolder);
			if (!Directory.Exists(baseCsvFolder))
				Directory.CreateDirectory(baseCsvFolder);

			var requestFolderName = $"Run_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}_{Guid.NewGuid()}";
			var csvFolder = Path.Combine(baseCsvFolder, requestFolderName);
			Directory.CreateDirectory(csvFolder);

			var csvFileName = $"DomainResults_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}.csv";
			var csvFilePath = Path.Combine(csvFolder, csvFileName);

			await System.IO.File.WriteAllTextAsync(csvFilePath, "Id,Domain,Availability,Length,Message,WhoisServer,Resource,ExecuteTime,Actions\n", Encoding.UTF8);

			foreach (var batch in domainsToCheck.Chunk(BatchSize))
			{
				cancellationToken.ThrowIfCancellationRequested();

				var tasks = batch.Select(async domain =>
				{
					var sw = Stopwatch.StartNew();
					await semaphore.WaitAsync(cancellationToken);

					try
					{
						if (!IsValidDomain(domain))
						{
							sw.Stop();
							results.Add(new DomainResult
							{
								Domain = domain,
								Status = "Invalid domain format",
								TimeTaken = sw.Elapsed.ToString(@"hh\:mm\:ss\.fff")
							});
						}
						else
						{
							try
							{
								var entry = await GetHostEntryWithTimeout(domain, TimeSpan.FromSeconds(2));
								var ip = entry?.AddressList?.FirstOrDefault();
								sw.Stop();
								if (ip != null)
								{
									results.Add(new DomainResult
									{
										Domain = domain,
										Status = $"Unavailable (DNS) - {ip}",
										TimeTaken = sw.Elapsed.ToString(@"hh\:mm\:ss\.fff")
									});
								}
								else
								{
									var tld = domain.Split('.').LastOrDefault()?.ToLower();
									if (tld != null && _whoisServers.TryGetValue(tld, out var whoisInfo))
									{
										try
										{
											var response = await QueryWhoisServerWithTimeout(whoisInfo.Server, domain, TimeSpan.FromSeconds(3));
											sw.Stop();

											string status = response.Contains(whoisInfo.NotFound, StringComparison.OrdinalIgnoreCase)
												? "Available (WHOIS)"
												: "Unavailable (WHOIS)";

											results.Add(new DomainResult
											{
												Domain = domain,
												Status = status,
												TimeTaken = sw.Elapsed.ToString(@"hh\:mm\:ss\.fff")
											});
										}
										catch (Exception whoisEx)
										{
											sw.Stop();

											results.Add(new DomainResult
											{
												Domain = domain,
												Status = $"WHOIS error: {whoisEx.Message}",
												TimeTaken = sw.Elapsed.ToString(@"hh\:mm\:ss\.fff")
											});
										}
									}
									else
									{
										sw.Stop();
										results.Add(new DomainResult
										{
											Domain = domain,
											Status = "Available (No DNS data)",
											TimeTaken = sw.Elapsed.ToString(@"hh\:mm\:ss\.fff")
										});
									}
								}
							}
							catch (SocketException ex) when (ex.SocketErrorCode == SocketError.HostNotFound || ex.SocketErrorCode == SocketError.NoData)
							{
								//var tld = domain.Split('.').LastOrDefault()?.ToLower();
								//if (tld != null && _whoisServers.TryGetValue(tld, out var whoisInfo))
								//{
								//	try
								//	{
								//		var response = await QueryWhoisServerWithTimeout(whoisInfo.Server, domain, TimeSpan.FromSeconds(3));
								//		sw.Stop();

								//		string status = response.Contains(whoisInfo.NotFound, StringComparison.OrdinalIgnoreCase)
								//			? "Available (WHOIS)"
								//			: "Unavailable (WHOIS)";

								//		results.Add(new DomainResult
								//		{
								//			Domain = domain,
								//			Status = status,
								//			TimeTaken = sw.Elapsed.ToString(@"hh\:mm\:ss\.fff")
								//		});
								//	}
								//	catch (Exception whoisEx)
								//	{
								//		sw.Stop();

								//		results.Add(new DomainResult
								//		{
								//			Domain = domain,
								//			Status = $"WHOIS error: {whoisEx.Message}",
								//			TimeTaken = sw.Elapsed.ToString(@"hh\:mm\:ss\.fff")
								//		});
								//	}
								//}
								//else
								//{
								//	sw.Stop();
								//	results.Add(new DomainResult
								//	{
								//		Domain = domain,
								//		Status = "Available (No DNS data)",
								//		TimeTaken = sw.Elapsed.ToString(@"hh\:mm\:ss\.fff")
								//	});
								//}
							}
						}
					}
					catch (Exception ex)
					{
						sw.Stop();
						results.Add(new DomainResult
						{
							Domain = domain,
							Status = $"Error: {ex.Message}",
							TimeTaken = sw.Elapsed.ToString(@"hh\:mm\:ss\.fff")
						});
					}
					finally
					{
						Interlocked.Increment(ref processedCount);
						progress.Processed = processedCount;
						_progressStore[requestId] = progress;
						semaphore.Release();
					}
				});
				await Task.WhenAll(tasks);
			}
			await BufferedWriteToCsvAsync(csvFilePath, results, cancellationToken);
			//await AppendBatchToCsvAsync(csvFilePath, results, cancellationToken);
			stopwatch.Stop();

			return new DomainCheckSummary
			{
				Available = results.Count(x => x.Status.StartsWith("Available")),
				Unavailable = results.Count(x => x.Status.Contains("Unavailable")),
				Error = results.Count(x => x.Status.Contains("error") || x.Status.Contains("Invalid") || x.Status.Contains("Error")),
				Total = domainsToCheck.Count,
				TimeTaken = stopwatch.Elapsed.ToString(@"hh\:mm\:ss"),
				Results = results.OrderBy(r => r.Domain).ToList(),
				CsvFileName = csvFilePath
			};
		}

		private void CleanupOldFolders(string folderPath)
		{
			if (!Directory.Exists(folderPath))
				return;

			var oldDirs = Directory.GetDirectories(folderPath)
				.Where(dir =>
				{
					try
					{
						return Directory.GetCreationTimeUtc(dir).Date < DateTime.UtcNow.Date;
					}
					catch
					{
						// Skip folders if we can't read creation time
						return false;
					}
				}).ToList();

			Parallel.ForEach(oldDirs, dir =>
			{
				try
				{
					Directory.Delete(dir, true);
				}
				catch (Exception ex)
				{
					// Optionally log the error or handle cleanup failure
					Console.WriteLine($"Failed to delete directory {dir}: {ex.Message}");
				}
			});
		}

		private async Task AppendBatchToCsvAsync(string filePath, IEnumerable<DomainResult> batchResults, CancellationToken cancellationToken)
		{
			var sb = new StringBuilder();
			foreach (var result in batchResults.Select((value, i) => new { i, value }))
			{
				var line = $"{result.i + 1}," +
						   $"{EscapeCsv(result.value.Domain)}," +
						   $"{EscapeCsv(result.value.Status)}," +
						   $"{result.value.Length}," +
						   $"{EscapeCsv(result.value.Message)}," +
						   $"{EscapeCsv(result.value.WhoisServer)}," +
						   $"{EscapeCsv(result.value.Resource)}," +
						   $"{result.value.TimeTaken}," +
						   $"{EscapeCsv(result.value.Actions)}{Environment.NewLine}";
				sb.Append(line);
			}

			await _csvWriteSemaphore.WaitAsync(cancellationToken);
			try
			{
				await System.IO.File.AppendAllTextAsync(filePath, sb.ToString(), Encoding.UTF8, cancellationToken);
			}
			finally
			{
				_csvWriteSemaphore.Release();
			}
		}
		private async Task BufferedWriteToCsvAsync(string filePath, IEnumerable<DomainResult> allResults, CancellationToken cancellationToken)
		{
			await _csvWriteSemaphore.WaitAsync(cancellationToken);
			try
			{
				using var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read, bufferSize: 8192, useAsync: true);
				using var writer = new StreamWriter(fs, Encoding.UTF8);

				int id = 1;
				foreach (var result in allResults)
				{
					if (cancellationToken.IsCancellationRequested)
						break;

					var line = $"{id++}," +
							   $"{EscapeCsv(result.Domain)}," +
							   $"{EscapeCsv(result.Status)}," +
							   $"{result.Length}," +
							   $"{EscapeCsv(result.Message)}," +
							   $"{EscapeCsv(result.WhoisServer)}," +
							   $"{EscapeCsv(result.Resource)}," +
							   $"{result.TimeTaken}," +
							   $"{EscapeCsv(result.Actions)}";

					await writer.WriteLineAsync(line);
				}

				await writer.FlushAsync();
			}
			finally
			{
				_csvWriteSemaphore.Release();
			}
		}

		private async Task AppendResultToCsvAsync(string filePath, DomainResult result, CancellationToken cancellationToken)
		{
			var line = $"{result.Id}," +
					   $"{EscapeCsv(result.Domain)}," +
					   $"{EscapeCsv(result.Status)}," +
					   $"{result.Length}," +
					   $"{EscapeCsv(result.Message)}," +
					   $"{EscapeCsv(result.WhoisServer)}," +
					   $"{EscapeCsv(result.Resource)}," +
					   $"{result.TimeTaken}," +
					   $"{EscapeCsv(result.Actions)}{Environment.NewLine}";

			await _csvWriteSemaphore.WaitAsync(cancellationToken);
			try
			{
				await System.IO.File.AppendAllTextAsync(filePath, line, Encoding.UTF8, cancellationToken);
			}
			finally
			{
				_csvWriteSemaphore.Release();
			}
		}

		private string EscapeCsv(string s)
		{
			if (string.IsNullOrEmpty(s)) return "";

			if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
			{
				s = s.Replace("\"", "\"\"");
				return $"\"{s}\"";
			}
			else
			{
				return s;
			}
		}

		[HttpGet]
		public IActionResult GetDomainResultsPage(string relativeFilePath, int pageNumber = 1, int pageSize = 50)
		{
			if (string.IsNullOrEmpty(relativeFilePath))
			{
				return Ok(new
				{
					results = new List<object>(),
					totalRecords = 0,
					totalPages = 0
				});
			}

			var baseFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "domain-results");
			var csvFilePath = Path.Combine(baseFolder, relativeFilePath);

			if (!System.IO.File.Exists(csvFilePath))
			{
				return NotFound($"File '{relativeFilePath}' not found.");
			}

			var allLines = System.IO.File.ReadAllLines(csvFilePath);
			int totalRecords = allLines.Length - 1; // exclude header

			var dataLines = allLines.Skip(1);

			IEnumerable<string> pagedLines;

			if (pageSize == 0) // all rows
			{
				pagedLines = dataLines;
			}
			else
			{
				pagedLines = dataLines
					.Skip((pageNumber - 1) * pageSize)
					.Take(pageSize);
			}

			var resultRows = pagedLines.Select((line, index) =>
			{
				var cols = line.Split(',');
				return new
				{
					id = (pageSize == 0 ? index + 1 : (pageNumber - 1) * pageSize + index + 1),
					domain = cols[1],
					availability = cols[2],
					length = cols[1].Split('.')[0].Length,
					message = cols[4],
					whoisServer = cols[5],
					resource = cols[6],
					executeTime = cols[7],
					actions = cols[8]
				};
			});

			return Ok(new
			{
				results = resultRows,
				totalRecords = totalRecords,
				totalPages = pageSize == 0 ? 1 : (int)Math.Ceiling(totalRecords / (double)pageSize)
			});
		}
		//private static bool IsValidDomain(string domain)
		//{
		//	string pattern = @"^(?!-)[A-Za-z0-9-]{1,63}(?<!-)\.[A-Za-z]{2,}$";
		//	return Regex.IsMatch(domain, pattern);
		//}
		public async Task<string> QueryWhoisServerWithTimeout(string server, string domain, TimeSpan timeout)
		{
			using var cts = new CancellationTokenSource(timeout);
			try
			{
				return await QueryWhoisServer(server, domain).WaitAsync(cts.Token);
			}
			catch (OperationCanceledException)
			{
				return "WHOIS timeout";
			}
		}

		private async Task<string> QueryWhoisServer(string server, string domain)
		{
			using var tcpClient = new TcpClient();
			await tcpClient.ConnectAsync(server, 43);

			using var stream = tcpClient.GetStream();
			using var writer = new StreamWriter(stream) { AutoFlush = true };
			using var reader = new StreamReader(stream);

			await writer.WriteLineAsync(domain);
			return await reader.ReadToEndAsync();
		}
		private async Task<IPHostEntry?> GetHostEntryWithTimeout(string domain, TimeSpan timeout)
		{
			using var cts = new CancellationTokenSource(timeout);
			try
			{
				return await Dns.GetHostEntryAsync(domain).WaitAsync(cts.Token);
			}
			catch
			{
				return null;
			}
		}

		public IActionResult DownloadCsv(string fileName)
		{
			var filePath = Path.Combine("wwwroot", "DomainResults", fileName); // adjust path as needed
			if (!System.IO.File.Exists(filePath))
				return NotFound("File not found");

			var contentType = "text/csv";
			return PhysicalFile(filePath, contentType, "domain-results.csv");
		}

		[HttpGet]
		public async Task<IActionResult> SuggestedAvailableDomains(string domain)
		{
			if (string.IsNullOrWhiteSpace(domain) || !IsValidDomain(domain))
				return BadRequest("Invalid or missing domain.");

			var originalDomainAvailable = await CheckDomainAvailabilityInternal(domain);

			var suggestions = GenerateDomainSuggestions(domain);
			var availableSuggestions = new ConcurrentBag<string>();

			var semaphore = new SemaphoreSlim(10);

			var tasks = suggestions.Select(async suggestion =>
			{
				await semaphore.WaitAsync();

				try
				{
					if (await CheckDomainAvailabilityInternal(suggestion))
						availableSuggestions.Add(suggestion);
				}
				finally
				{
					semaphore.Release();
				}
			});

			await Task.WhenAll(tasks);

			return Ok(new
			{
				Available = originalDomainAvailable,
				Suggestions = availableSuggestions.Distinct().OrderBy(x => x).ToList()
			});
		}



		private async Task<bool> CheckDomainAvailabilityInternal(string domain)
		{
			if (!IsValidDomain(domain))
				return false;

			try
			{
				// DNS Check
				var entry = await GetHostEntryWithTimeout(domain, TimeSpan.FromSeconds(2));
				if (entry?.AddressList?.FirstOrDefault() != null)
					return false; // Has IP = taken

				// WHOIS Check
				var tld = domain.Split('.').LastOrDefault()?.ToLower();
				if (tld != null && _whoisServers.TryGetValue(tld, out var whoisInfo))
				{
					var response = await QueryWhoisServerWithTimeout(whoisInfo.Server, domain, TimeSpan.FromSeconds(3));
					return response.Contains(whoisInfo.NotFound, StringComparison.OrdinalIgnoreCase);
				}
			}
			catch (SocketException ex) when (ex.SocketErrorCode == SocketError.HostNotFound || ex.SocketErrorCode == SocketError.NoData)
			{
				// DNS failed, try WHOIS
				var tld = domain.Split('.').LastOrDefault()?.ToLower();
				if (tld != null && _whoisServers.TryGetValue(tld, out var whoisInfo))
				{
					try
					{
						var response = await QueryWhoisServerWithTimeout(whoisInfo.Server, domain, TimeSpan.FromSeconds(3));
						return response.Contains(whoisInfo.NotFound, StringComparison.OrdinalIgnoreCase);
					}
					catch
					{
						return false;
					}
				}
			}
			catch
			{
				// Any other error = treat as unavailable
			}

			return false;
		}


		private List<string> GenerateDomainSuggestions(string domain)
		{
			var baseName = domain.Split('.')[0]; // Extract domain name without TLD
			var tld = domain.Substring(domain.LastIndexOf(".")); // Extract TLD

			var commonSuffixes = new List<string> { "Group", "Solutions", "Consulting", "Analytics", "Tech", "Hub", "Experts", "Network", "Visionary" };
			var commonHyphenated = new List<string> { "-D", "-Tech", "-Solutions", "-Analytics", "-Consulting" };

			var suggestions = new List<string>();

			// Add hyphenated variations
			suggestions.AddRange(commonHyphenated.Select(suffix => $"{baseName}{suffix}{tld}"));

			// Add common suffix-based variations
			suggestions.AddRange(commonSuffixes.Select(suffix => $"{baseName}{suffix}{tld}"));

			// Generate alternate TLD suggestions
			var alternateTlds = new List<string> { ".net", ".org", ".co", ".info", ".ai" };
			suggestions.AddRange(alternateTlds.Select(altTld => $"{baseName}{altTld}"));

			return suggestions.Distinct().ToList();
		}
		public async Task<IActionResult> AISuggestDomains(string prompt)
		{
			if (string.IsNullOrWhiteSpace(prompt))
			{
				return BadRequest("Prompt cannot be empty.");
			}

			var aiResponse = await GetDomainSuggestionsFromAI(prompt);

			if (string.IsNullOrWhiteSpace(aiResponse))
			{
				return StatusCode(500, "AI service failed to return suggestions.");
			}

			// Extract domain list from response (naive split for now)
			var suggestions = aiResponse
				.Split(new[] { '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(s => s.Trim().Trim('-', '.', '•'))
				.Where(s => s.Length > 3)
				.ToList();

			return Ok(new { suggestions });
		}

		private async Task<string> GetDomainSuggestionsFromAI(string prompt)
		{
			var endpoint = _config["AISearch:Endpoint"];
			var secretKey = _config["AISearch:SecretKey"];
			var host = _config["AISearch:Host"];

			if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(secretKey) || string.IsNullOrWhiteSpace(host))
			{
				throw new InvalidOperationException("AI Search configuration is missing.");
			}

			var client = new HttpClient();

			var request = new HttpRequestMessage
			{
				Method = HttpMethod.Post,
				RequestUri = new Uri(endpoint),
				Headers =
				{
					{ "x-rapidapi-key", secretKey },
					{ "x-rapidapi-host", host }
				},
				Content = new StringContent(
					$"{{\"messages\":[{{\"role\":\"user\",\"content\":\"{prompt}\"}}],\"web_access\":false}}")
				{
					Headers =
					{
						ContentType = new MediaTypeHeaderValue("application/json")
					}
				}
			};

			using var response = await client.SendAsync(request);
			response.EnsureSuccessStatusCode();
			var body = await response.Content.ReadAsStringAsync();
			var json = JObject.Parse(body);
			var reply = json["result"]?.ToString() ?? "No response from AI.";

			return reply;
		}
	}
}
