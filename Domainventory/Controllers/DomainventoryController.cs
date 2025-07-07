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
using ClosedXML.Excel;

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
			static bool HasDot(string s) => s.Contains('.');
			var domainsToCheck =
				request.Domains
					   .Select(d => hasPrefixOrSuffix ? $"{request.Prefix}{d}{request.Suffix}" : d)
					   .SelectMany(d =>
						   HasDot(d)
							   ? new[] { d } 
							   : (hasTlds
									 ? request.Tlds.Select(tld => $"{d}.{tld}")  : new[] { d }))
					   .Where(d => request.Maxlength == 0 ||
								   d.Split('.')[0].Length <= request.Maxlength)
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
						DomainResult dr;   // will hold the final object

						if (!IsValidDomain(domain))
						{
							sw.Stop();
							dr = new DomainResult
							{
								Domain = domain,
								Status = "Invalid domain format",
								Resource = "Local",
								WhoisServer = "",
								TimeTaken = sw.Elapsed.ToString(@"hh\:mm\:ss\.fff")
							};
						}
						else
						{
							try
							{
								/* DNS CHECK */
								var entry = await GetHostEntryWithTimeout(domain, TimeSpan.FromSeconds(2));
								var ip = entry?.AddressList?.FirstOrDefault();
								if (ip is not null)
								{
									sw.Stop();
									dr = new DomainResult
									{
										Domain = domain,
										Status = $"Unavailable (DNS) - {ip}",
										Resource = "DNS",
										WhoisServer = "",
										TimeTaken = sw.Elapsed.ToString(@"hh\:mm\:ss\.fff")
									};
								}
								else
								{
									/* WHOIS CHECK */
									var tld = domain.Split('.').LastOrDefault()?.ToLowerInvariant();
									if (tld is not null && _whoisServers.TryGetValue(tld, out var whoisInfo))
									{
										try
										{
											var response = await QueryWhoisServerWithTimeout(
															   whoisInfo.Server, domain, TimeSpan.FromSeconds(3));
											sw.Stop();

											bool available = response.Contains(
												whoisInfo.NotFound, StringComparison.OrdinalIgnoreCase);

											dr = new DomainResult
											{
												Domain = domain,
												Status = available ? "Available (WHOIS)"
																		 : "Unavailable (WHOIS)",
												Resource = "WHOIS",
												WhoisServer = whoisInfo.Server,
												TimeTaken = sw.Elapsed.ToString(@"hh\:mm\:ss\.fff")
											};
										}
										catch (Exception whoisEx)
										{
											sw.Stop();
											dr = new DomainResult
											{
												Domain = domain,
												Status = $"WHOIS error: {whoisEx.Message}",
												Resource = "WHOIS",
												WhoisServer = whoisInfo.Server,
												TimeTaken = sw.Elapsed.ToString(@"hh\:mm\:ss\.fff")
											};
										}
									}
									else
									{
										sw.Stop();
										dr = new DomainResult
										{
											Domain = domain,
											Status = "Available (No DNS data)",
											Resource = "DNS",
											WhoisServer = "",
											TimeTaken = sw.Elapsed.ToString(@"hh\:mm\:ss\.fff")
										};
									}
								}
							}
							catch (SocketException ex) when (
								   ex.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData)
							{
								sw.Stop();
								dr = new DomainResult
								{
									Domain = domain,
									Status = "Available (DNS failed)",
									Resource = "DNS",
									WhoisServer = "",
									TimeTaken = sw.Elapsed.ToString(@"hh\:mm\:ss\.fff")
								};
							}
						}

						results.Add(dr);
					}
					catch (Exception ex)
					{
						sw.Stop();
						results.Add(new DomainResult
						{
							Domain = domain,
							Status = $"Error: {ex.Message}",
							Resource = "System",
							WhoisServer = "",
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

		public IActionResult DownloadExcel(string csvFilePath)
		{
			if (!System.IO.File.Exists(csvFilePath))
				return NotFound("CSV file not found.");

			using (var workbook = new XLWorkbook())
			{
				var worksheet = workbook.Worksheets.Add("Sheet1");

				var lines = System.IO.File.ReadAllLines(csvFilePath);
				for (int i = 0; i < lines.Length; i++)
				{
					var values = lines[i].Split(',');
					for (int j = 0; j < values.Length; j++)
					{
						worksheet.Cell(i + 1, j + 1).Value = values[j];
					}
				}

				using (var stream = new MemoryStream())
				{
					workbook.SaveAs(stream);
					stream.Position = 0;

					return File(
						stream.ToArray(),
						"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
						"exported.xlsx"
					);
				}
			}
		}
		public IActionResult DownloadTxt(string filePath)
		{
			if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
				return BadRequest("Invalid or missing file path.");

			try
			{
				var csvLines = System.IO.File.ReadAllLines(filePath);
				var txtContent = string.Join(Environment.NewLine, csvLines);

				var bytes = System.Text.Encoding.UTF8.GetBytes(txtContent);
				var fileName = Path.GetFileNameWithoutExtension(filePath) + ".txt";

				return File(bytes, "text/plain", fileName);
			}
			catch (Exception ex)
			{
				return StatusCode(500, "Error while processing the file: " + ex.Message);
			}
		}
		public IActionResult DownloadJSON(string filePath)
		{
			if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
				return BadRequest("Invalid or missing file path.");

			try
			{
				var lines = System.IO.File.ReadAllLines(filePath);

				if (lines.Length < 2)
					return BadRequest("CSV does not contain enough data.");

				var headers = lines[0].Split(',').Select(h => h.Trim()).ToArray();
				var records = new List<Dictionary<string, string>>();

				for (int i = 1; i < lines.Length; i++)
				{
					var values = lines[i].Split(',').Select(v => v.Trim()).ToArray();
					var record = new Dictionary<string, string>();

					for (int j = 0; j < headers.Length; j++)
					{
						var key = headers[j];
						var value = j < values.Length ? values[j] : string.Empty;
						record[key] = value;
					}

					records.Add(record);
				}

				var json = System.Text.Json.JsonSerializer.Serialize(records, new System.Text.Json.JsonSerializerOptions
				{
					WriteIndented = true
				});

				var bytes = System.Text.Encoding.UTF8.GetBytes(json);
				var fileName = Path.GetFileNameWithoutExtension(filePath) + ".json";

				return File(bytes, "application/json", fileName);
			}
			catch (Exception ex)
			{
				return StatusCode(500, "Error while processing CSV: " + ex.Message);
			}
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
					var result = await CheckDomainAvailabilityInternal(suggestion);
					//if (await CheckDomainAvailabilityInternal(suggestion))
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



		private async Task<DomainResult> CheckDomainAvailabilityInternal(string domain)
		{
			if (!IsValidDomain(domain))
				return new DomainResult { Domain = domain, Status = "Invalid" };

			try
			{
				var entry = await GetHostEntryWithTimeout(domain, TimeSpan.FromSeconds(2));

				if (entry?.AddressList?.FirstOrDefault() is not null)
					return new DomainResult { Domain = domain, Status = "Unavailable (DNS)" };

				var tld = domain.Split('.').LastOrDefault()?.ToLowerInvariant();

				if (tld is not null && _whoisServers.TryGetValue(tld, out var whoisInfo))
				{
					var response = await QueryWhoisServerWithTimeout(whoisInfo.Server, domain, TimeSpan.FromSeconds(3));
					var available = response.Contains(whoisInfo.NotFound, StringComparison.OrdinalIgnoreCase);

					return new DomainResult
					{
						Domain = domain,
						Status = available ? "Available" : "Unavailable (WHOIS)"
					};
				}

				return new DomainResult { Domain = domain, Status = "Error: Unsupported TLD" };
			}
			catch (SocketException ex) when (ex.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData)
			{
				var tld = domain.Split('.').LastOrDefault()?.ToLowerInvariant();

				if (tld is not null && _whoisServers.TryGetValue(tld, out var whoisInfo))
				{
					try
					{
						var response = await QueryWhoisServerWithTimeout(whoisInfo.Server, domain, TimeSpan.FromSeconds(3));
						var available = response.Contains(whoisInfo.NotFound, StringComparison.OrdinalIgnoreCase);

						return new DomainResult
						{
							Domain = domain,
							Status = available ? "Available" : "Unavailable (WHOIS)"
						};
					}
					catch (Exception whoisEx)
					{
						return new DomainResult
						{
							Domain = domain,
							Status = $"Error: {whoisEx.Message}"
						};
					}
				}

				return new DomainResult { Domain = domain, Status = "Error: Unsupported TLD" };
			}
			catch (Exception ex)
			{
				return new DomainResult { Domain = domain, Status = $"Error: {ex.Message}" };
			}
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
		//public sealed record DomainResult
		//{
		//	public int Id { get; init; }
		//	public string Domain { get; init; } = "";
		//	public string Status { get; init; } = "";          // same as Availability
		//	public int Length => Domain.Split('.')[0].Length;
		//	public string Message { get; init; } = "";
		//	public string WhoisServer { get; init; } = "";
		//	public string Resource { get; init; } = "";          // "DNS" / "WHOIS"
		//	public string ExecuteTime { get; init; } = "";          // formatted hh:mm:ss.fff
		//	public string Actions { get; init; } = "";          // keep blank for now
		//}

		public async Task<DomainCheckSummary> AISuggestDomains(string prompt)
		{
			try
			{
				// 1 ── AI suggestions
				var aiResponse = await GetDomainSuggestionsFromAI(prompt);
				var suggestions = aiResponse
					.Split(new[] { '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
					.Select(s => s.Trim().Trim('-', '.', '•'))
					.Where(s => s.Length > 3)
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToList();

				// 2 ── state & helpers
				var results = new ConcurrentBag<DomainResult>();
				var csvLines = new ConcurrentBag<string>();
				var semaphore = new SemaphoreSlim(10);
				var stopwatchAll = Stopwatch.StartNew();
				var idCounter = 0;                         // thread‑safe via Interlocked

				// 3 ── CSV file scaffold
				var baseCsvFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "ai-suggestions");
				var requestFolder = $"AISuggest_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}_{Guid.NewGuid()}";
				var csvFolder = Path.Combine(baseCsvFolder, requestFolder);
				Directory.CreateDirectory(csvFolder);

				var csvFilePath = Path.Combine(
					csvFolder, $"AISuggestions_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}.csv");

				const string csvHeader =
					"Id,Domain,Availability,Length,Message,WhoisServer,Resource,ExecuteTime,Actions\n";
				await System.IO.File.WriteAllTextAsync(csvFilePath, csvHeader, Encoding.UTF8);

				// 4 ── parallel checks
				var tasks = suggestions.Select(async suggestion =>
				{
					await semaphore.WaitAsync();
					var sw = Stopwatch.StartNew();

					try
					{
						string status, message = "", whoisServer = "", resource;
						IPAddress? ip = null;

						if (!IsValidDomain(suggestion))
						{
							status = "Invalid domain format";
							resource = "Local";
						}
						else
						{
							try
							{
								// DNS
								var entry = await GetHostEntryWithTimeout(suggestion, TimeSpan.FromSeconds(2));
								ip = entry?.AddressList?.FirstOrDefault();
								if (ip is not null)
								{
									status = $"Unavailable (DNS) - {ip}";
									resource = "DNS";
								}
								else
								{
									// WHOIS
									var tld = suggestion.Split('.').LastOrDefault()?.ToLowerInvariant();
									if (tld is not null && _whoisServers.TryGetValue(tld, out var whoisInfo))
									{
										try
										{
											var response = await QueryWhoisServerWithTimeout(
															   whoisInfo.Server, suggestion, TimeSpan.FromSeconds(3));

											bool available = response.Contains(
												whoisInfo.NotFound, StringComparison.OrdinalIgnoreCase);

											status = available ? "Available (WHOIS)" : "Unavailable (WHOIS)";
											whoisServer = whoisInfo.Server;
											resource = "WHOIS";
										}
										catch (Exception whoisEx)
										{
											status = $"WHOIS error";
											message = whoisEx.Message;
											whoisServer = whoisInfo.Server;
											resource = "WHOIS";
										}
									}
									else
									{
										status = "Available (No DNS data)";
										resource = "DNS";
									}
								}
							}
							catch (SocketException ex) when (
								   ex.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData)
							{
								status = "Available (DNS failed)";
								resource = "DNS";
							}
							catch (Exception ex)
							{
								status = "Error";
								message = ex.Message;
								resource = "System";
							}
						}

						sw.Stop();
						var id = Interlocked.Increment(ref idCounter);

						var result = new DomainResult
						{
							Id = id,
							Domain = suggestion,
							Status = status,
							Message = message,
							WhoisServer = whoisServer,
							Resource = resource,
							TimeTaken = sw.Elapsed.ToString(@"hh\:mm\:ss\.fff"),
							Actions = ""
						};

						results.Add(result);

						csvLines.Add(string.Join(',',
							result.Id,
							result.Domain,
							result.Status.Replace(",", " "),
							result.Length,
							result.Message.Replace(",", " "),
							result.WhoisServer,
							result.Resource,
							result.TimeTaken,
							result.Actions));
					}
					finally
					{
						semaphore.Release();
					}
				});

				await Task.WhenAll(tasks);
				stopwatchAll.Stop();

				// 5 ── single flush to CSV
				await System.IO.File.AppendAllLinesAsync(csvFilePath, csvLines.OrderBy(l => l));

				// 6 ── summary
				var finalResults = results
					.OrderBy(r => r.Id)
					.ToList();

				return new DomainCheckSummary
				{
					Available = finalResults.Count(r => r.Status.StartsWith("Available", StringComparison.OrdinalIgnoreCase)),
					Unavailable = finalResults.Count(r => r.Status.Contains("Unavailable", StringComparison.OrdinalIgnoreCase)),
					Error = finalResults.Count(r => r.Status.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
														 r.Status.Contains("Invalid", StringComparison.OrdinalIgnoreCase)),
					Total = finalResults.Count,
					TimeTaken = stopwatchAll.Elapsed.ToString(@"hh\:mm\:ss"),
					Results = finalResults,
					CsvFileName = csvFilePath
				};
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				return new DomainCheckSummary
				{
					Available = 0,
					Unavailable = 0,
					Error = 1,
					Total = 0,
					TimeTaken = "0:00:00",
					Results = new List<DomainResult>(),
					CsvFileName = string.Empty
				};
			}
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
			prompt += ", Only provide domain names without any additional text or explanations. , and don't give serial numbers.";
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
