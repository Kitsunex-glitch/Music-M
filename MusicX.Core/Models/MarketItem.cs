using Newtonsoft.Json;

namespace MusicX.Core.Models
{
    public class MarketItem
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("owner_id")]
        public long OwnerId { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("price")]
        public MarketPrice Price { get; set; }

        [JsonProperty("availability")]
        public int Availability { get; set; }

        [JsonProperty("thumb_photo")]
        public string ThumbPhoto { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("market_url")]
        public string MarketUrl { get; set; }

        [JsonProperty("item_type")]
        public int ItemType { get; set; }

        [JsonProperty("is_favorite")]
        public bool IsFavorite { get; set; }

        [JsonProperty("is_owner")]
        public bool IsOwner { get; set; }

        [JsonProperty("is_adult")]
        public bool IsAdult { get; set; }

        [JsonProperty("is_hardblocked")]
        public bool IsHardblocked { get; set; }

        [JsonProperty("has_group_access")]
        public bool HasGroupAccess { get; set; }

        [JsonProperty("category")]
        public MarketCategory Category { get; set; }

        [JsonProperty("thumb")]
        public List<MarketThumb> Thumb { get; set; } = new List<MarketThumb>();

        [JsonProperty("csrf_hashes")]
        public string CsrfHashes { get; set; }
    }

    public class MarketPrice
    {
        [JsonProperty("amount")]
        public string Amount { get; set; }

        [JsonProperty("currency")]
        public MarketCurrency Currency { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }

        [JsonProperty("price_type")]
        public int PriceType { get; set; }

        [JsonProperty("price_unit")]
        public int PriceUnit { get; set; }
    }

    public class MarketCurrency
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }
    }

    public class MarketCategory
    {
        [JsonProperty("inner_type")]
        public string InnerType { get; set; }

        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("is_v2")]
        public bool IsV2 { get; set; }

        [JsonProperty("parent")]
        public MarketCategory Parent { get; set; }
    }

    public class MarketThumb
    {
        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("width")]
        public int Width { get; set; }

        [JsonProperty("height")]
        public int Height { get; set; }
    }
}