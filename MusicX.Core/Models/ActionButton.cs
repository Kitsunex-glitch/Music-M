using Newtonsoft.Json;

namespace MusicX.Core.Models
{
    public class ActionButton
    {
        [JsonExtensionData]
        private Dictionary<string, object> AdditionalData { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("target")]
        public string Target { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("consume_reason")]
        public string ConsumeReason { get; set; }
    }
}
