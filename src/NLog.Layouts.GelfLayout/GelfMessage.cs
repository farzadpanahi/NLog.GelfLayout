using System;
using Newtonsoft.Json;

namespace NLog.Layouts.GelfLayout
{
    [JsonObject(MemberSerialization.OptIn)]
    public class GelfMessage
    {
        [JsonProperty("facility")]
        public string Facility { get; set; }

        [JsonProperty("file")]
        public string File { get; set; }

        [JsonProperty("full_message")]
        public string FullMessage { get; set; }

        [JsonProperty("host")]
        public string Host { get; set; }

        [JsonProperty("level")]
        public int Level { get; set; }

        [JsonProperty("line")]
        public int Line { get; set; }

        [JsonProperty("short_message")]
        public string ShortMessage { get; set; }

        [JsonProperty("timestamp")]
        public decimal Timestamp { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }
    }
}
