using System.Collections.Generic;

namespace SequentialBlobIntegrator.Models
{
    public class IntegrationPayload()
    {
        public string Key { get; set; }
        public long TicksStamp { get; set; }
        public IntegrationHttpRequest IntegrationHttpRequest { get; set; }

    }
    public class IntegrationHttpRequest
    {
        public string Url { get; set; }
        public int Timeout { get; set; } = 15;
        public Dictionary<string, string[]> Headers { get; set; }
        public string HttpMethod { get; set; }
        public string Content { get; set; }
        public bool PollIf202 { get; set; }
    }

    //public class BlobPayload()
    //{
    //    public string Url { get; set; }
    //    public string Content { get; set; }
    //}

    public class Lock
    {
        public bool IsLocked { get; set; }
        public long TicksStamp { get; set; }
    }

    //public class TestJsonContent()
    //{
    //    public string Firstname { get; set; }
    //    public string Lastname { get; set; }
    //}
}
