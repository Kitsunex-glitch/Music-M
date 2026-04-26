using Newtonsoft.Json;

using VkNet.Model;

namespace MusicX.Core.Models.General
{
    public class ResponseData
    {

        [JsonProperty("market_items")]
        public List<MarketItem> MarketItems { get; set; } = new List<MarketItem>();

        [JsonProperty("section")]
        public Section Section { get; set; }

        [JsonProperty("catalog")]
        public Catalog Catalog { get; set; }

        [JsonProperty("block")]
        public Block Block { get; set; }

        [JsonProperty("catalog_banners")]
        public List<CatalogBanner> CatalogBanners { get; set; }

        [JsonProperty("audios")]
        public List<Audio> Audios { get; set; }

        [JsonProperty("playlists")]
        public List<Playlist> Playlists { get; set; }

        [JsonProperty("playlist")]
        public Playlist Playlist { get; set; }

        [JsonProperty("links")]
        public List<Link> Links { get; set; }

        [JsonProperty("artists")]
        public List<Artist> Artists { get; set; }

        [JsonProperty("suggestions")]
        public List<Suggestion> Suggestions { get; set; }

        [JsonProperty("curators")]
        public List<Curator> Curators { get; set; }

        [JsonProperty("groups")]
        public List<Group> Groups { get; set; }

        [JsonProperty("texts")]
        public List<Text> Texts { get; set; }

        [JsonProperty("items")]
        public List<Audio> Items { get; set; }

        [JsonProperty("replacements")]
        public Replacements Replacements { get; set; }

        [JsonProperty("profiles")]
        public List<User> Profiles { get; set; }

        [JsonProperty("longreads")]
        public List<Longread> Longreads { get; set; }

        [JsonProperty("podcast_episodes")]
        public List<PodcastEpisode> PodcastEpisodes { get; set; }

        [JsonProperty("podcast_slider_items")]
        public List<PodcastSliderItem> PodcastSliderItems { get; set; }

        [JsonProperty("recommended_playlists")]
        public List<RecommendedPlaylist> RecommendedPlaylists { get; set; }

        [JsonProperty("videos")]
        public List<Video> Videos { get; set; }

        [JsonProperty("artist_videos")]
        public List<Video> ArtistVideos { get; set; }

        [JsonProperty("placeholders")]
        public List<Placeholder> Placeholders { get; set; }

        [JsonProperty("music_owners")]
        public List<MusicOwner> MusicOwners { get; set; }

        [JsonProperty("audio_followings_update_info")]
        public List<AudioFollowingsUpdateInfo> FollowingsUpdateInfos { get; set; }

        [JsonProperty("concerts")]
        public List<Concert> Concerts { get; set; }

  
    }
}
