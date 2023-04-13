namespace BroadcastManager2
{
    public class CloudflareDnsRecord
    {
        public string? HostId { get; set; }
        public string? HostName { get; set; }
        public string? ZoneId { get; set; }
        public string? ZoneName { get; set; }
        public bool Success { get; set; }
        public Exception? Exception { get; set; }
    }
}
