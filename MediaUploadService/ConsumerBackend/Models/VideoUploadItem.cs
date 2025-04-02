using System.Collections.Generic;

namespace MediaUploadService.ConsumerBackend.Models
{
    public class VideoUploadItem
    {
        public string VideoId { get; set; }
        public string TempPath { get; set; }
        public string FinalPath { get; set; }
        public Dictionary<string, object> Metadata { get; set; }
        public string ClientAddress { get; set; }
        public long Timestamp { get; set; }
    }
}