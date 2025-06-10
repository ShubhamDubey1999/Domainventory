using ClosedXML.Excel;
using DocumentFormat.OpenXml.Spreadsheet;
using Domainventory.Manager;
using Domainventory.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Domainventory.Controllers
{
	public class HomeController : Controller
	{
		private readonly IWebHostEnvironment _environment;
		private readonly ILogger<HomeController> _logger;
		private readonly IHubContext<DomainHub> _hubContext;
		Dictionary<string, WhoisServerInfo> _whoisServers;
		public HomeController(ILogger<HomeController> logger, IHubContext<DomainHub> hubContext, IWebHostEnvironment environment)
		{
			_logger = logger;
			_hubContext = hubContext;
			_environment = environment;
			_whoisServers = LoadWhoisServers();
		}
		private Dictionary<string, WhoisServerInfo> LoadWhoisServers()
		{
			var path = Path.Combine(_environment.WebRootPath,"js", "servers.json");
			var json = System.IO.File.ReadAllText(path);

			var options = new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			};

			return JsonSerializer.Deserialize<Dictionary<string, WhoisServerInfo>>(json, options);
		}
		public IActionResult Index()
		{
			ViewBag.Tlds = Tlds();
			return View();
		}
		private static List<string> Tlds()
		{
			return new List<string>
					{
						"com",
						"net",
						"org",
						"io",
						"computer",
						"ac",
						"academy",
						"actor",
						"ae",
						"aero",
						"af",
						"ag",
						"agency",
						"ai",
						"am",
						"archi",
						"arpa",
						"_as",
						"asia",
						"associates",
						"at",
						"au",
						"aw",
						"ax",
						"az",
						"bar",
						"bargains",
						"be",
						"berlin",
						"bg",
						"bi",
						"bike",
						"biz",
						"bj",
						"blackfriday",
						"bn",
						"boutique",
						"build",
						"builders",
						"bw",
						"by",
						"ca",
						"cab",
						"camera",
						"camp",
						"captial",
						"cards",
						"careers",
						"cat",
						"catering",
						"cc",
						"center",
						"ceo",
						"cf",
						"ch",
						"cheap",
						"christmas",
						"ci",
						"cl",
						"cleaning",
						"clothing",
						"club",
						"cn",
						"co",
						"codes",
						"coffee",
						"college",
						"cologne",
						"community",
						"company",
						"construction",
						"contractors",
						"cooking",
						"cool",
						"coop",
						"country",
						"cruises",
						"cx",
						"cz",
						"dating",
						"de",
						"democrat",
						"desi",
						"diamonds",
						"directory",
						"dk",
						"dm",
						"domains",
						"dz",
						"ec",
						"edu",
						"education",
						"ee",
						"email",
						"engineering",
						"enterprises",
						"equipment",
						"es",
						"estate",
						"eu",
						"eus",
						"events",
						"expert",
						"exposed",
						"farm",
						"feedback",
						"fi",
						"fish",
						"fishing",
						"flights",
						"florist",
						"fo",
						"foo",
						"foundation",
						"fr",
						"frogans",
						"futbol",
						"ga",
						"gal",
						"gd",
						"gg",
						"gi",
						"gift",
						"gl",
						"glass",
						"gop",
						"gov",
						"graphics",
						"gripe",
						"gs",
						"guitars",
						"guru",
						"gy",
						"haus",
						"hk",
						"hn",
						"holiday",
						"horse",
						"house",
						"hr",
						"ht",
						"hu",
						"id",
						"ie",
						"il",
						"im",
						"immobilien",
						"_in",
						"industries",
						"institute",
						"_int",
						"international",
						"iq",
						"ir",
						"_is",
						"it",
						"je",
						"jobs",
						"jp",
						"kaufen",
						"ke",
						"kg",
						"ki",
						"kitchen",
						"kiwi",
						"koeln",
						"kr",
						"kz",
						"la",
						"land",
						"lease",
						"li",
						"lighting",
						"limo",
						"link",
						"london",
						"lt",
						"lu",
						"luxury",
						"lv",
						"ly",
						"ma",
						"management",
						"mango",
						"marketing",
						"md",
						"me",
						"media",
						"menu",
						"mg",
						"miami",
						"mk",
						"ml",
						"mn",
						"mo",
						"mobi",
						"moda",
						"monash",
						"mp",
						"ms",
						"mu",
						"museum",
						"mx",
						"my",
						"na",
						"name",
						"nc",
						"nf",
						"ng",
						"ninja",
						"nl",
						"no",
						"nu",
						"nz",
						"om",
						"onl",
						"paris",
						"partners",
						"parts",
						"pe",
						"pf",
						"photo",
						"photography",
						"photos",
						"pics",
						"pictures",
						"pl",
						"plumbing",
						"pm",
						"post",
						"pr",
						"pro",
						"productions",
						"properties",
						"pt",
						"pub",
						"pw",
						"qa",
						"quebec",
						"re",
						"recipes",
						"reisen",
						"rentals",
						"repair",
						"report",
						"rest",
						"reviews",
						"rich",
						"ro",
						"rocks",
						"rodeo",
						"rs",
						"ru",
						"ruhr",
						"sa",
						"saarland",
						"sb",
						"sc",
						"se",
						"services",
						"sexy",
						"sg",
						"sh",
						"shoes",
						"si",
						"singles",
						"sk",
						"sm",
						"sn",
						"so",
						"social",
						"solar",
						"solutions",
						"soy",
						"st",
						"su",
						"supplies",
						"supply",
						"support",
						"sx",
						"sy",
						"systems",
						"tattoo",
						"tc",
						"technology",
						"tel",
						"tf",
						"th",
						"tienda",
						"tips",
						"tk",
						"tl",
						"tm",
						"tn",
						"to",
						"today",
						"tools",
						"town",
						"toys",
						"tr",
						"training",
						"travel",
						"tv",
						"tw",
						"tz",
						"ua",
						"ug",
						"uk",
						"university",
						"us",
						"uy",
						"black",
						"blue",
						"info",
						"kim",
						"pink",
						"red",
						"shiksha",
						"uz",
						"vacations",
						"vc",
						"ve",
						"vegas",
						"ventures",
						"vg",
						"viajes",
						"villas",
						"vision",
						"vodka",
						"voting",
						"voyage",
						"vu",
						"wang",
						"watch",
						"wed",
						"wf",
						"wien",
						"wiki",
						"works",
						"ws",
						"xxx",
						"xyz",
						"yt",
						"ryukyu",
						"zm",
						"zone",
						"couk",
						"coin",
						"coid",
						"myid"
					};
		}

		[HttpPost]
		public async Task<IActionResult> CheckDomains([FromBody] DomainRequest request)
		{
			DomainCheckManager.TokenSource.Cancel();
			DomainCheckManager.TokenSource = new CancellationTokenSource();

			var token = DomainCheckManager.TokenSource.Token;

			if (request.Domains == null || !request.Domains.Any())
			{
				await _hubContext.Clients.All.SendAsync("DomainChecked", "No domains provided.");
				return BadRequest("No domains provided.");
			}

			List<string> domainsToCheck = (request.Tlds != null && request.Tlds.Any())
				? request.Domains.SelectMany(d => request.Tlds.Select(tld => $"{d}.{tld}")).ToList()
				: request.Domains.ToList();

			int availableCount = 0, unavailableCount = 0, errorCount = 0, processedCount = 0;
			int totalCount = domainsToCheck.Count;

			var semaphore = new SemaphoreSlim(100);
			var tasks = domainsToCheck.Select(async domain =>
			{
				await semaphore.WaitAsync();
				try
				{
					if (token.IsCancellationRequested)
						return;

					// Wait here if paused
					await DomainCheckManager.PauseEvent.WaitAsync();

					if (!IsValidDomain(domain))
					{
						Interlocked.Increment(ref errorCount);
						await _hubContext.Clients.All.SendAsync("DomainChecked", $"{domain} - Invalid domain format");
						return;
					}

					try
					{
						var entry = await Dns.GetHostEntryAsync(domain);
						Interlocked.Increment(ref unavailableCount);
						await _hubContext.Clients.All.SendAsync("DomainChecked", $"{domain} - Unavailable");
					}
					catch (SocketException ex) when (ex.SocketErrorCode == SocketError.HostNotFound || ex.SocketErrorCode == SocketError.NoData)
					{
						var tld = domain.Split('.').LastOrDefault()?.ToLower();
						if (tld != null && _whoisServers.TryGetValue(tld, out var whoisInfo))
						{
							try
							{
								var whoisResponse = await QueryWhoisServer(whoisInfo.Server, domain);

								if (whoisResponse.Contains(whoisInfo.NotFound, StringComparison.OrdinalIgnoreCase))
								{
									Interlocked.Increment(ref availableCount);
									await _hubContext.Clients.All.SendAsync("DomainChecked", $"{domain} - Available (WHOIS)");
								}
								else
								{
									Interlocked.Increment(ref unavailableCount);
									await _hubContext.Clients.All.SendAsync("DomainChecked", $"{domain} - Unavailable (WHOIS)");
								}
							}
							catch (Exception whoisEx)
							{
								Interlocked.Increment(ref errorCount);
								await _hubContext.Clients.All.SendAsync("DomainChecked", $"{domain} - WHOIS error: {whoisEx.Message}");
							}
						}
						else
						{
							Interlocked.Increment(ref availableCount); // fallback if WHOIS info not found
							await _hubContext.Clients.All.SendAsync("DomainChecked", $"{domain} - Available (No DNS data)");
						}
					}
					catch (SocketException ex) when (ex.SocketErrorCode == SocketError.HostNotFound)
					{
						Interlocked.Increment(ref availableCount);
						await _hubContext.Clients.All.SendAsync("DomainChecked", $"{domain} - Available");
					}
					catch (SocketException ex) when (ex.SocketErrorCode == SocketError.NoData)
					{
						Interlocked.Increment(ref availableCount);
						await _hubContext.Clients.All.SendAsync("DomainChecked", $"{domain} - Available (No DNS data)");
					}
				}
				catch (Exception ex)
				{
					Interlocked.Increment(ref errorCount);
					await _hubContext.Clients.All.SendAsync("DomainChecked", $"{domain} - Error: {ex.Message}");
				}
				finally
				{
					if (!token.IsCancellationRequested)
					{
						int done = Interlocked.Increment(ref processedCount);
						await _hubContext.Clients.All.SendAsync("ProgressUpdate", new
						{
							Available = availableCount,
							Unavailable = unavailableCount,
							Error = errorCount,
							Processed = done,
							Total = totalCount
						});
					}
					semaphore.Release();
				}
			});


			await Task.WhenAll(tasks);
			return Ok("Processing complete.");
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


		private static bool IsValidDomain(string domain)
		{
			// Basic domain name validation
			string pattern = @"^(?!-)[A-Za-z0-9-]{1,63}(?<!-)\.[A-Za-z]{2,}$";
			return Regex.IsMatch(domain, pattern);
		}
		[HttpPost]
		public IActionResult CancelCheck()
		{
			DomainCheckManager.TokenSource.Cancel();
			return Ok("Cancelled");
		}
		[HttpPost]
		public IActionResult PauseCheck()
		{
			DomainCheckManager.PauseEvent.Reset();
			return Ok("Paused");
		}

		[HttpPost]
		public IActionResult ResumeCheck()
		{
			DomainCheckManager.PauseEvent.Set();
			return Ok("Resumed");
		}

		[HttpPost]
		public async Task<IActionResult> ImportDomains(IFormFile domainFile)
		{
			if (domainFile == null || domainFile.Length == 0)
			{
				TempData["Error"] = "Please upload a valid file.";
				return RedirectToAction("Index");
			}

			var domains = new List<string>();

			using (var stream = new MemoryStream())
			{
				await domainFile.CopyToAsync(stream);
				stream.Position = 0;

				if (domainFile.FileName.EndsWith(".csv"))
				{
					using var reader = new StreamReader(stream);
					bool isFirstLine = true;
					while (!reader.EndOfStream)
					{
						var line = await reader.ReadLineAsync();
						if (isFirstLine)
						{
							isFirstLine = false;
							continue; // skip header
						}

						if (!string.IsNullOrWhiteSpace(line))
						{
							var cells = line.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
							domains.AddRange(cells);
						}
					}
				}
				else if (domainFile.FileName.EndsWith(".xls") || domainFile.FileName.EndsWith(".xlsx"))
				{
					using var workbook = new XLWorkbook(stream);
					var worksheet = workbook.Worksheets.FirstOrDefault();
					if (worksheet != null)
					{
						domains = new List<string>();
						var rowCount = worksheet.LastRowUsed().RowNumber();
						var colCount = worksheet.LastColumnUsed().ColumnNumber();

						for (int row = 2; row <= rowCount; row++) // skip header row
						{
							for (int col = 1; col <= colCount; col++)
							{
								var value = worksheet.Cell(row, col).GetValue<string>().Trim();
								if (!string.IsNullOrWhiteSpace(value))
								{
									var parts = value.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
									domains.AddRange(parts);
								}
							}
						}
					}
				}
			}

			//var uniqueDomains = domains.Distinct().ToList();
			var uniqueDomains = domains.ToList();

			return Json(new { domains = string.Join("\n", uniqueDomains) });

		}
	}
}
