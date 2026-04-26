using Newtonsoft.Json;
using System.Collections.Generic;

namespace MusicX.Core.Models
{
    public class Concert
    {
        [JsonProperty("concert_data")]
        public ConcertData ConcertData { get; set; }

        [JsonProperty("purchase_action")]
        public ConcertPurchaseAction PurchaseAction { get; set; }

        [JsonProperty("track_code")]
        public string TrackCode { get; set; }
    }

    public class ConcertData
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("place_title")]
        public string PlaceTitle { get; set; }

        [JsonProperty("age_category")]
        public string AgeCategory { get; set; }

        [JsonProperty("city")]
        public City City { get; set; }

        [JsonProperty("min_price")]
        public int MinPrice { get; set; }

        [JsonProperty("start_datetime")]
        public string StartDateTime { get; set; }

        [JsonProperty("page_url")]
        public string PageUrl { get; set; }

        [JsonProperty("artists_ids")]
        public List<string> ArtistsIds { get; set; }

        [JsonProperty("image")]
        public List<Image> Image { get; set; }

        [JsonProperty("is_cancelled")]
        public bool IsCancelled { get; set; }

        [JsonProperty("cancelled_description")]
        public string CancelledDescription { get; set; }
    }

    public class ConcertPurchaseAction
    {
        [JsonProperty("action")]
        public ConcertAction Action { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }
    }

    public class ConcertAction
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("target")]
        public string Target { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }
    }

    public class City
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }
    }
}