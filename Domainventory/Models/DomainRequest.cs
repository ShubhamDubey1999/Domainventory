using System.Text.Json.Serialization;

namespace Domainventory.Models
{

	public class DomainRequest
	{
		public List<string> Domains { get; set; } = new();
		public List<string> Tlds { get; set; } = new();
        public string Suffix { get; set; }
        public string Prefix { get; set; }
		public int Maxlength { get; set; } = 0;
    }
	public class DomainCheckSummary
	{
		public int Available { get; set; }
		public int Unavailable { get; set; }
		public int Error { get; set; }
		public int Total { get; set; }
		public string TimeTaken { get; set; }
        public string CsvFileName { get; set; }
        public List<DomainResult> Results { get; set; } = new();
	}

	public class WhoisServerInfo
	{
		[JsonPropertyName("server")]
		public string Server { get; set; }
		[JsonPropertyName("not_found")]
		public string NotFound { get; set; }
	}
	public class ProgressInfo
	{
		public int Total { get; set; }
		public int Processed { get; set; }
		public double Percentage => Total == 0 ? 0 : (double)Processed / Total * 100;
	}


	public class DomainResult
    {
        public int Id { get; set; }              // New
        public string Domain { get; set; }
        public string Status { get; set; }       // Availability
        public int Length => Domain?.Length ?? 0; // Computed
        public string Message { get; set; }      // Optional extra message
        public string WhoisServer { get; set; }  // If applicable
        public string Resource { get; set; }     // Optional
        public string TimeTaken { get; set; }    // Execute Time
        public string Actions { get; set; }      // Usually empty or placeholder
    }

}
