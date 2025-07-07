using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class DailyDirectoryCleaner : BackgroundService
{
	private readonly ILogger<DailyDirectoryCleaner> _log;
	private readonly string[] _folders;      // target folders
	private readonly string _stampFile;     // stores last run time (ISO‑8601)

	public DailyDirectoryCleaner(
		ILogger<DailyDirectoryCleaner> log,
		IWebHostEnvironment env)
	{
		_log = log;

		_folders = new[]
		{
			Path.Combine(env.ContentRootPath, "wwwroot", "domain-results"),
			Path.Combine(env.ContentRootPath, "wwwroot", "ai-suggestions")
		};

		_stampFile = Path.Combine(env.ContentRootPath, "wwwroot", "last-clean.txt");
	}

	/* -------------------------------------------------------------------- */
	/*  Main loop                                                           */
	/* -------------------------------------------------------------------- */
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		// ── immediate catch‑up if missed for > 24 h
		if (NeedImmediateCleanup())
			RunCleanup();

		while (!stoppingToken.IsCancellationRequested)
		{
			var now = DateTime.Now;
			var target = now.Date.AddHours(3);              // today 03:00
			if (now >= target) target = target.AddDays(1);  // else next 03:00

			try
			{
				await Task.Delay(target - now, stoppingToken);
			}
			catch (TaskCanceledException)
			{
				break; // shutting down
			}

			RunCleanup();
		}
	}

	/* -------------------------------------------------------------------- */
	/*  Helpers                                                             */
	/* -------------------------------------------------------------------- */
	private bool NeedImmediateCleanup()
	{
		if (!File.Exists(_stampFile)) return true;

		if (!DateTime.TryParse(File.ReadAllText(_stampFile), out var lastRun))
			return true;                                       // corrupt stamp → run now

		return (DateTime.Now - lastRun) > TimeSpan.FromHours(24);
	}

	private void RunCleanup()
	{
		foreach (var folder in _folders)
		{
			try
			{
				CleanFolder(folder);
				_log.LogInformation("Cleaned folder {Folder} at {Time:O}", folder, DateTime.Now);
			}
			catch (Exception ex)
			{
				_log.LogError(ex, "Error cleaning folder {Folder}", folder);
			}
		}

		// update stamp
		try
		{
			File.WriteAllText(_stampFile, DateTime.Now.ToString("O"));
		}
		catch (Exception ex)
		{
			_log.LogWarning(ex, "Failed to write clean‑up stamp file");
		}
	}

	/// <summary>Delete all files &amp; subfolders but keep the parent folder.</summary>
	private static void CleanFolder(string folder)
	{
		if (!Directory.Exists(folder))
		{
			Directory.CreateDirectory(folder);
			return;
		}

		foreach (var dir in Directory.EnumerateDirectories(folder))
			Directory.Delete(dir, true);

		foreach (var file in Directory.EnumerateFiles(folder))
			File.Delete(file);
	}
}
