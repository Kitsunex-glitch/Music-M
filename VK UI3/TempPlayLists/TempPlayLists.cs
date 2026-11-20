using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VK_UI3.DB;
using VK_UI3.VKs;
using VkNet.Model.Attachments;

namespace VK_UI3.TempPlayLists
{
    internal class TempPlayLists
    {
        private static List<AudioPlaylist> _audioPlaylists = new List<AudioPlaylist>();
        private static DateTime lastUpdate = DateTime.MinValue;

        private const int MaxCount = 5;
        private static bool isAllLoaded = false;
        private static uint offset = 0;
        private const uint RequestOffset = 100;
        public static bool updateNextRequest = true;

        public static async Task<List<AudioPlaylist>> GetPlayListAsync(bool updateNow = false)
        {
            if (
                    updateNow
                || (DateTime.Now - lastUpdate).TotalSeconds > 120
                || (_audioPlaylists.Count() < MaxCount && !isAllLoaded)
                || updateNextRequest
                )
            {
                offset = 0;
                updateNextRequest = false;
                isAllLoaded = false;
                _audioPlaylists.Clear();
                await LoadMorePlaylistsAsync();
                lastUpdate = DateTime.Now;
            }
            return _audioPlaylists;
        }

        private static async Task LoadMorePlaylistsAsync()
        {
            // Цикл вместо рекурсии: пустые страницы больше не могут увести
            // метод в бесконечный вызов самого себя.
            while (!isAllLoaded && _audioPlaylists.Count < MaxCount)
            {
                var requestOffset = offset;

                VkNet.Utils.VkCollection<AudioPlaylist> playlists;
                try
                {
                    playlists = await VK.api.Audio.GetPlaylistsAsync(
                        AccountsDB.activeAccount.id, RequestOffset, requestOffset);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[TempPlayLists] ошибка загрузки (offset={requestOffset}): {ex.GetType().Name}: {ex.Message}");
                    isAllLoaded = true;
                    break;
                }

                int received = playlists?.Count ?? 0;

                // Offset двигаем на реально полученное количество, а не на размер запроса:
                // иначе при короткой странице часть списка пропускается.
                offset += (uint)received;

                foreach (var playlist in playlists)
                {
                    if (!playlist.Permissions.Edit) continue;

                    _audioPlaylists.Add(playlist);
                    if (_audioPlaylists.Count >= MaxCount) break;
                }

                System.Diagnostics.Debug.WriteLine(
                    $"[TempPlayLists] requestOffset={requestOffset}, получено={received}, " +
                    $"в списке={_audioPlaylists.Count}, nextOffset={offset}");

                if (received < RequestOffset)
                {
                    isAllLoaded = true;
                }

                // Пустая страница — дальше запрашивать нечего.
                if (received == 0)
                {
                    isAllLoaded = true;
                }
            }
        }
    }
}

