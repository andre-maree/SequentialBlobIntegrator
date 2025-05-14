namespace SequentialBlobIntegrator.Models
{
    public class RootPayload() : BlobPayload
    {
        public string Key { get; set; }
        public long TicksStamp { get; set; }

        public string HttpMethod { get; set; }
    }

    public class BlobPayload()
    {
        public string Url { get; set; }
        public string Content { get; set; }
    }

    public class Lock
    {
        public bool IsLocked { get; set; }
        public long TicksStamp { get; set; }
    }

    public class TestJsonContent()
    {
        public string Firstname { get; set; }
        public string Lastname { get; set; }
    }
}
