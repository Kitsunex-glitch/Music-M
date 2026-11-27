using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace MusicX.Core.Models
{
    public class Button
    {
        [JsonExtensionData]
        private Dictionary<string, object> AdditionalData { get; set; }


        [JsonProperty("action")]
        public ActionButton Action { get; set; }

        [JsonProperty("section_id")]
        public string SectionId { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("ref_items_count")]
        public int RefItemsCount { get; set; }

        [JsonProperty("ref_layout_name")]
        public string RefLayoutName { get; set; }

        [JsonProperty("ref_data_type")]
        public string RefDataType { get; set; }

        [JsonProperty("block_id")]
        public string BlockId { get; set; }

        [JsonProperty("options")]
        public List<OptionButton> Options { get; set; } = new List<OptionButton>();
        
        [JsonProperty("artist_id")]
        public string ArtistId { get; set; }
        
        [JsonProperty("is_following")]
        public bool IsFollowing { get; set; }
        
        [JsonProperty("owner_id")]
        public long OwnerId { get; set; }
        
        [JsonProperty("description")]
        public string Description { get; set; }
        
        [JsonProperty("images")]
        public List<Image> Images { get; set; } = new List<Image>();
        
        [JsonProperty("foreground_images")]
        public List<Image> ForegroundImages { get; set; } = new List<Image>();
        
        [JsonProperty("id")]
        public string Id { get; set; }
        
        [JsonProperty("mix_id")]
        public string MixId { get; set; }
        
        // entity_id может быть как числом (владелец/пользователь), так и строкой
        // вида "owner_id_audio_id" (например "-2001486552_156486552"),
        // поэтому храним как строку.
        [JsonProperty("entity_id")]
        public string EntityId { get; set; }

        [JsonProperty("mix_options")]
        public string? MixOptions { get; set; }
    }
}
