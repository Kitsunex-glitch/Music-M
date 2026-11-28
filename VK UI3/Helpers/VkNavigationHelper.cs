using MusicX.Core.Models;
using MusicX.Services;
using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VK_UI3.Views;
using VK_UI3.VKs;
using static VK_UI3.Views.SectionView;

namespace VK_UI3.Helpers
{
    public static class VkNavigationHelper
    {
        /// <summary>
        /// Пытается открыть контент через внутреннюю навигацию приложения.
        /// Если не получается — возвращает false, чтобы вызвавший код открыл URL в браузере.
        /// </summary>
        public static bool TryNavigate(ListeningContentItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Url))
                return false;

            try
            {
                // Мини-приложения VK (vk.ru/appXXXX) открываются в WebView2 с мостом.
                if (TryOpenMiniAppAsync(item.Url, item.Name))
                    return true;

                return item.Type switch
                {
                    ContentType.Playlist => TryOpenPlaylist(item.Url),
                    ContentType.Album => TryOpenAlbum(item.Url),
                    ContentType.Artist => TryOpenArtist(item.Url),
                    ContentType.Track => false, // Треки открываем только в браузере
                    _ => false
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VkNavigationHelper] Error navigating to {item.Url}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Пытается открыть мини-приложение VK (vk.ru/appXXXX) в WebView2 с мостом.
        /// Возвращает true, если URL распознан как мини-приложение (загрузка идёт асинхронно).
        /// </summary>
        private static bool TryOpenMiniAppAsync(string url, string title)
        {
            if (!IsMiniAppUrl(url, out var appId))
                return false;

            _ = OpenMiniAppCoreAsync(appId, url, title);
            return true;
        }

        private static async Task OpenMiniAppCoreAsync(string appId, string url, string title)
        {
            string webviewUrl = null;

            try
            {
                var vkService = VK.vkService;
                var response = await vkService.GetMiniApp(appId, url);
                webviewUrl = response?.Object?.WebviewUrl;

                if (string.IsNullOrEmpty(webviewUrl))
                    webviewUrl = response?.EmbeddedUri?.ViewUrl;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VkNavigationHelper] Ошибка открытия мини-приложения {appId}: {ex.Message}");
            }

            // VK отдаёт webview_url не для всех приложений (например, для игр),
            // поэтому в крайнем случае открываем стандартный адрес приложения —
            // мини-приложение само поднимется через мост.
            if (string.IsNullOrEmpty(webviewUrl))
                webviewUrl = $"https://vk.ru/{appId}";

            MainView.OpenMiniApp(
                appId,
                webviewUrl,
                string.IsNullOrWhiteSpace(title) ? "Мини-приложение" : title);
        }

        private static bool IsMiniAppUrl(string url, out string appId)
        {
            var match = Regex.Match(url, @"https://vk\.(?:com|ru)/(?:games/)?(?<app>app\-?[0-9]+)(?>[\?\#].+)?$");
            appId = match.Success ? match.Groups["app"].Value : null;
            return match.Success;
        }

        /// <summary>
        /// Пытается открыть плейлист по URL вида:
        /// https://vk.ru/music/playlist/{ownerId}_{playlistId}_{accessKey}
        /// https://vk.ru/music/playlist/{ownerId}_{playlistId}_{accessKey}
        /// </summary>
        private static bool TryOpenPlaylist(string url)
        {
            // Формат: https://vk.ru/music/playlist/12345_67890_abc123
            if (!IsPlaylistUrl(url))
                return false;

            var parts = ExtractPlaylistParts(url);
            if (parts == null)
                return false;

            MainView.OpenPlayList(parts.Value.playlistId, parts.Value.ownerId, parts.Value.accessKey);
            return true;
        }

        /// <summary>
        /// Пытается открыть альбом по URL вида:
        /// https://vk.ru/music/album/{ownerId}_{albumId}_{accessKey}
        /// </summary>
        private static bool TryOpenAlbum(string url)
        {
            // Формат: https://vk.ru/music/album/12345_67890_abc123
            if (!IsAlbumUrl(url))
                return false;

            var parts = ExtractPlaylistParts(url);
            if (parts == null)
                return false;

            MainView.OpenPlayList(parts.Value.playlistId, parts.Value.ownerId, parts.Value.accessKey);
            return true;
        }

        /// <summary>
        /// Пытается открыть артиста по URL вида:
        /// https://vk.ru/artist/{artistId}
        /// https://vk.ru/artist/{artistId}
        /// </summary>
        private static bool TryOpenArtist(string url)
        {
            // Формат: https://vk.ru/artist/SomeArtistName или https://vk.ru/artist/SomeArtistName
            var uri = new Uri(url);
            var segments = uri.Segments;

            // segments: /, artist/, {artistId}/
            if (segments.Length < 2)
                return false;

            // Ищем сегмент "artist/" и берём следующий за ним
            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (segments[i].TrimEnd('/').Equals("artist", StringComparison.OrdinalIgnoreCase))
                {
                    var artistId = segments[i + 1].TrimEnd('/');
                    if (!string.IsNullOrWhiteSpace(artistId))
                    {
                        MainView.OpenSection(artistId, SectionType.Artist);
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Извлекает ownerId, playlistId и accessKey из URL плейлиста/альбома.
        /// Ожидаемый формат: .../playlist/{ownerId}_{playlistId}_{accessKey}
        /// </summary>
        private static (long ownerId, long playlistId, string accessKey)? ExtractPlaylistParts(string url)
        {
            try
            {
                var uri = new Uri(url);
                var lastSegment = uri.Segments[^1].TrimEnd('/');
                var splited = lastSegment.Split('_');

                if (splited.Length < 2)
                    return null;

                if (!long.TryParse(splited[0], out var ownerId))
                    return null;

                if (!long.TryParse(splited[1], out var playlistId))
                    return null;

                var accessKey = splited.Length >= 3 ? splited[2] : null;

                return (ownerId, playlistId, accessKey);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsPlaylistUrl(string url)
        {
            return Regex.IsMatch(url, @"https://vk\.(ru|com)/music/playlist/\d+_\d+_[a-z0-9]+$", RegexOptions.IgnoreCase);
        }

        private static bool IsAlbumUrl(string url)
        {
            return Regex.IsMatch(url, @"https://vk\.(com|ru)/music/album/\d+_\d+_[a-z0-9]+$", RegexOptions.IgnoreCase);
        }
    }
}