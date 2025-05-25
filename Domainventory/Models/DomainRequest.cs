namespace Domainventory.Models
{

    public class DomainRequest
    {
        public List<string> Domains { get; set; } = new();
        public List<string> Tlds { get; set; } = new();
    }
}
