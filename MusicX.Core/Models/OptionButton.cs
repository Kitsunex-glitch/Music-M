using Newtonsoft.Json;

namespace MusicX.Core.Models
{
    public class OptionButton
    {
        [JsonExtensionData]
        private Dictionary<string, object> AdditionalData { get; set; }

        [JsonProperty("replacement_id")]
        public string ReplacementId { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }

        [JsonProperty("icon")]
        public string Icon { get; set; }

        [JsonProperty("selected")]
        public int Selected { get; set; }
    }
}
