using MusicX.Core.Models;
using MusicX.Core.Models.General;
using System.Diagnostics;
using System.Reflection;

namespace MusicX.Core.Helpers
{
    public static class VkBlockHelper
    {
        // Константы для магических строк
        private const string DataTypeRadiostations = "radiostations";
        private const string DataTypeEmpty = "empty";
        private const string DataTypeAction = "action";
        private const string DataTypeMusicRecommendedPlaylists = "music_recommended_playlists";
        private const string LayoutNameSnippetsBanner = "snippets_banner";
        private const string LayoutNameHorizontalButtons = "horizontal_buttons";
        private const string ActionTypeOpenSection = "open_section";
        private const string UrlNoMusicContent = "no_music_content";
        private const string UrlSubscription = "subscription";
        private const string UrlCombo = "combo";
        private const string UrlVkApp = "https://vk.ru/app";
        private const string UrlVkMusic = "https://vk.ru/vk_music";
        private const string BannerTextWithSubscription = "с подпиской";
        private const string UrlAudioOffline = "audio_offline";
        private const string UrlRadiostations = "radiostations";
        private const string UrlMusicTransfer = "music_transfer";

        /// <summary>
        /// Обрабатывает ответ VK, заполняя связанные сущности в блоках и удаляя нежелательные блоки.
        /// </summary>
        public static ResponseData Process(this ResponseData response)
        {
            SetPlaylistsOwners(response);

            ProcessReplacements(response);
            ProcessBlock(response);
            ProcessSectionBlocks(response);

            return response;
        }

        private static void SetPlaylistsOwners(ResponseData response)
        {
            if (response.Playlists == null) return;

            foreach (var playlist in response.Playlists)
            {
                SetPlaylistOwner(playlist, response);
            }
        }

        private static void SetPlaylistOwner(Playlist playlist, ResponseData response)
        {
            var ownerId = playlist.Original?.OwnerId ?? playlist.OwnerId;
            if (ownerId < 0)
            {
                // Группа
                var groupId = -ownerId;
                var group = response.Groups?.SingleOrDefault(g => g.Id == groupId);
                playlist.groupOwner = group;
                playlist.OwnerName = group?.Name;
            }
            else
            {
                // Пользователь
                var user = response.Profiles?.SingleOrDefault(p => p.Id == ownerId);
                playlist.userOwner = user;
                playlist.OwnerName = user != null ? $"{user.FirstName} {user.LastName}" : null;
            }
        }

        private static void ProcessReplacements(ResponseData response)
        {
            if (response.Replacements == null) return;

            foreach (var replaceModel in response.Replacements.ReplacementsModels)
            {
                foreach (var block in replaceModel.ToBlocks)
                {
                    FillBlockEntities(block, response);
                }
            }
        }

        private static void ProcessBlock(ResponseData response)
        {
            if (response.Block != null)
            {
                FillBlockEntities(response.Block, response);
            }
        }

        private static void ProcessSectionBlocks(ResponseData response)
        {
            if (response.Section == null) return;

            foreach (var block in response.Section.Blocks)
            {
                FillBlockEntities(block, response);
            }

            RemoveSnippetsBanner(response.Section);
            RemoveUnwantedBlocks(response.Section);
            MergeActionButtons(response.Section);
        }

        private static void RemoveSnippetsBanner(Section section)
        {
            var snippetsBannerIndex = section.Blocks.FindIndex(b => b is { Layout.Name: LayoutNameSnippetsBanner });
            if (snippetsBannerIndex >= 0)
            {
                section.Blocks.RemoveAt(snippetsBannerIndex);
                // Удаляем лишний разделитель
                if (snippetsBannerIndex < section.Blocks.Count)
                    section.Blocks.RemoveAt(snippetsBannerIndex);
            }
        }

        private static void RemoveUnwantedBlocks(Section section)
        {
            section.Blocks.RemoveAll(block =>
                IsRadioOrEmptyBlock(block) ||
                HasUnwantedBanners(block) ||
                HasUnwantedLinks(block) ||
                HasNoMusicContentUrl(block)
            );
        }

        private static bool IsRadioOrEmptyBlock(Block block)
        {
            return block is { DataType: DataTypeRadiostations or DataTypeEmpty }
                   || block is { Layout.Title: "Радиостанции" or "Эфиры" or "Популярные подкасты" };
        }

        private static bool HasUnwantedBanners(Block block)
        {
            if (block is not { Banners.Count: > 0 }) return false;

            var removedCount = block.Banners.RemoveAll(banner =>
                banner.ClickAction?.Action.Url?.Contains(UrlSubscription) == true ||
                banner.ClickAction?.Action.Url?.Contains(UrlCombo) == true ||
                banner.ClickAction?.Action.Url?.Contains(UrlVkApp) == true ||
                banner.Text == BannerTextWithSubscription ||
                banner.ClickAction?.Action.Url?.Contains(UrlVkMusic) == true
            );

            return removedCount > 0 && block.Banners.Count == 0;
        }

        private static bool HasUnwantedLinks(Block block)
        {
            if (block is not { Links.Count: > 0 }) return false;

            var removedCount = block.Links.RemoveAll(link =>
                link.Url.Contains(UrlAudioOffline) ||
                link.Url.Contains(UrlRadiostations) ||
                link.Url.Contains(UrlMusicTransfer) ||
                link.Url.Contains(UrlNoMusicContent) ||
                link.Url.Contains(UrlSubscription)
            );

            return removedCount > 0 && block.Links.Count == 0;
        }

        private static bool HasNoMusicContentUrl(Block block)
        {
            return block.Url?.Contains(UrlNoMusicContent) == true;
        }

        private static void MergeActionButtons(Section section)
        {
            for (var i = 0; i < section.Blocks.Count; i++)
            {
                var block = section.Blocks[i];
                if (block.DataType == DataTypeAction && block.Layout?.Name == LayoutNameHorizontalButtons)
                {
                    var firstAction = block.Actions.FirstOrDefault();
                    if (firstAction?.Action.Type == ActionTypeOpenSection)
                    {
                        var refBlockIndex = section.Blocks.FindIndex(b =>
                            b.DataType == firstAction.RefDataType &&
                            b.Layout?.Name == firstAction.RefLayoutName) - 1;

                        if (refBlockIndex >= 0 && refBlockIndex < section.Blocks.Count)
                        {
                            section.Blocks[refBlockIndex].Actions.AddRange(block.Actions);
                            section.Blocks.RemoveAt(i);
                            i--;
                        }
                    }
                }
            }
        }

        private static void FillBlockEntities(Block block, ResponseData response)
        {
            FillAudios(block, response);
            FillPlaylists(block, response);
            FillGroups(block, response);
            FillMarketItems(block, response);    // <-- Заполнение мерча
            FillConcerts(block, response);       // <-- Заполнение концертов

            // Универсальное заполнение для простых случаев
            FillEntities(block.CatalogBannerIds, id => response.CatalogBanners?.SingleOrDefault(b => b.Id == id), block.Banners);
            FillEntities(block.LinksIds, id => response.Links?.SingleOrDefault(b => b.Id == id), block.Links);
            FillEntities(block.PlaceholdersIds, id => response.Placeholders?.SingleOrDefault(b => b.Id == id), block.Placeholders);
            FillEntities(block.SuggestionsIds, id => response.Suggestions?.FirstOrDefault(b => b.Id == id), block.Suggestions, useFirstOrDefault: true);
            FillEntities(block.ArtistsIds, id => response.Artists?.SingleOrDefault(b => b.Id == id), block.Artists);
            FillEntities(block.TextIds, id => response.Texts?.SingleOrDefault(b => b.Id == id), block.Texts);
            FillEntities(block.CuratorsIds, id => response.Curators?.SingleOrDefault(b => b.Id == id), block.Curators);
            FillEntities(block.PodcastSliderItemsIds, id => response.PodcastSliderItems?.SingleOrDefault(b => b.ItemId == id), block.PodcastSliderItems);
            FillEntities(block.PodcastEpisodesIds, id => response.PodcastEpisodes?.SingleOrDefault(b => b.OwnerId + "_" + b.Id == id), block.PodcastEpisodes);
            FillEntities(block.LongreadsIds, id => response.Longreads?.SingleOrDefault(b => b.OwnerId + "_" + b.Id == id), block.Longreads);
            FillEntities(block.VideosIds, id => response.Videos?.SingleOrDefault(b => b.OwnerId + "_" + b.Id == id), block.Videos);
            FillEntities(block.ArtistVideosIds, id => response.ArtistVideos?.SingleOrDefault(b => b.OwnerId + "_" + b.Id == id), block.ArtistVideos);
            FillEntities(block.MusicOwnerIds, id => response.MusicOwners?.SingleOrDefault(b => b.Id == id), block.MusicOwners);
            FillEntities(block.FollowingUpdateInfoIds, id => response.FollowingsUpdateInfos?.SingleOrDefault(b => b.Id == id), block.FollowingsUpdateInfos);
        }

        private static void FillAudios(Block block, ResponseData response)
        {
            if (block.AudiosIds?.Count > 0)
            {
                foreach (var audioStringId in block.AudiosIds)
                {
                    try
                    {
                        var parts = audioStringId.Split('_');
                        if (parts.Length != 2) continue;

                        var ownerId = long.Parse(parts[0]);
                        var audioId = long.Parse(parts[1]);

                        var audio = response.Audios?.SingleOrDefault(a => a.Id == audioId && a.OwnerId == ownerId);
                        if (audio == null) continue;

                        audio.ParentBlockId = block.Id;
                        block.Audios.Add(audio);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error processing audio ID '{audioStringId}': {ex.Message}");
                    }
                }
            }
        }

        private static void FillPlaylists(Block block, ResponseData response)
        {
            if (block.PlaylistsIds?.Count == 0) return;

            if (block.DataType == DataTypeMusicRecommendedPlaylists)
            {
                FillRecommendedPlaylists(block, response);
            }
            else
            {
                FillRegularPlaylists(block, response);
            }
        }

        /// <summary>
        /// Заполняет блоки с информацией о товарах (мерч).
        /// </summary>
        private static void FillMarketItems(Block block, ResponseData response)
        {
            // Если в блоке уже есть MarketItems от JSON, используем их
            if (block.MarketItems != null && block.MarketItems.Count > 0)
            {
                return; // Уже заполнено при десериализации
            }

            // Если нет прямой ссылки, ищем по ID
            if (block.MarketItems == null)
                block.MarketItems = new List<MarketItem>();

            var marketItemIds = GetMarketItemIdsFromBlock(block);

            if (marketItemIds == null || marketItemIds.Count == 0) return;
            if (response.MarketItems == null) return;

            // Заполняем market_items по ID
            foreach (var marketItemId in marketItemIds)
            {
                try
                {
                    // ID приходит в формате "-60154984_12621524"
                    var parts = marketItemId.Split('_');
                    if (parts.Length != 2) continue;

                    // Парсим ownerId и itemId
                    var ownerId = long.Parse(parts[0]);
                    var itemId = long.Parse(parts[1]);

                    // Ищем в market_items
                    var marketItem = response.MarketItems.SingleOrDefault(m => m.OwnerId == ownerId && m.Id == itemId);
                    if (marketItem != null)
                    {
                        block.MarketItems.Add(marketItem);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error processing market item ID '{marketItemId}': {ex.Message}");
                }
            }
        }

        private static List<string> GetMarketItemIdsFromBlock(Block block)
        {
            // Проверяем поле market_item_ids (как в JSON)
            if (block.MarketItemIds != null && block.MarketItemIds.Count > 0)
                return block.MarketItemIds;

            // Проверяем snake_case вариант через рефлексию на всякий случай
            var snakeCaseName = "market_item_ids";

            var field = block.GetType().GetField(snakeCaseName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                var value = field.GetValue(block) as List<string>;
                if (value != null && value.Count > 0)
                    return value;
            }

            var property = block.GetType().GetProperty(snakeCaseName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property != null)
            {
                var value = property.GetValue(block) as List<string>;
                if (value != null && value.Count > 0)
                    return value;
            }

            return null;
        }

        /// <summary>
        /// Заполняет блоки с информацией о концертах.
        /// </summary>
        private static void FillConcerts(Block block, ResponseData response)
        {
            // Инициализируем коллекции, если они null
            block.Concerts ??= new List<Concert>();

            // Получаем ID концертов из разных возможных источников
            var concertIds = GetConcertIdsFromBlock(block);

            if (concertIds == null || concertIds.Count == 0) return;
            if (response.Concerts == null) return;

            FillEntities(concertIds, id => response.Concerts.SingleOrDefault(c => c.ConcertData?.Id == id), block.Concerts);
        }

        /// <summary>
        /// Извлекает ID концертов из блока, проверяя разные варианты имен свойств.
        /// </summary>
        private static List<string> GetConcertIdsFromBlock(Block block)
        {
            // Проверяем основное свойство
            if (block.ConcertsIds != null && block.ConcertsIds.Count > 0)
                return block.ConcertsIds;

            // Пытаемся найти поле/свойство с именем concerts_ids (snake_case)
            var snakeCaseName = "concerts_ids";

            // Проверяем поле
            var field = block.GetType().GetField(snakeCaseName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                var value = field.GetValue(block) as List<string>;
                if (value != null && value.Count > 0)
                    return value;
            }

            // Проверяем свойство
            var property = block.GetType().GetProperty(snakeCaseName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property != null)
            {
                var value = property.GetValue(block) as List<string>;
                if (value != null && value.Count > 0)
                    return value;
            }

            return null;
        }

        private static void FillRecommendedPlaylists(Block block, ResponseData response)
        {
            foreach (var lid in block.PlaylistsIds)
            {
                try
                {
                    var recommended = response.RecommendedPlaylists?.SingleOrDefault(b => b.OwnerId + "_" + b.Id == lid);
                    if (recommended == null) continue;

                    var playlist = response.Playlists?.SingleOrDefault(b => b.OwnerId + "_" + b.Id == lid);
                    recommended.Playlist = playlist;
                    block.RecommendedPlaylists.Add(recommended);

                    // Заполняем аудио внутри рекомендованного плейлиста
                    foreach (var aid in recommended.AudiosIds)
                    {
                        var audio = response.Audios?.SingleOrDefault(b => b.OwnerId + "_" + b.Id == aid);
                        if (audio != null)
                        {
                            recommended.Audios.Add(audio);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error processing recommended playlist ID '{lid}': {ex.Message}");
                }
            }
        }

        private static void FillRegularPlaylists(Block block, ResponseData response)
        {
            foreach (var playlistStringId in block.PlaylistsIds)
            {
                try
                {
                    var parts = playlistStringId.Split('_');
                    if (parts.Length != 2) continue;

                    var ownerId = long.Parse(parts[0]);
                    var playlistId = long.Parse(parts[1]);

                    var playlist = response.Playlists?.SingleOrDefault(p => p.Id == playlistId && p.OwnerId == ownerId);
                    if (playlist != null)
                    {
                        block.Playlists.Add(playlist);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error processing playlist ID '{playlistStringId}': {ex.Message}");
                }
            }
        }

        private static void FillGroups(Block block, ResponseData response)
        {
            FillEntities(block.GroupIds, id => response.Groups?.SingleOrDefault(b => b.Id == id), block.Groups);

            if (block.GroupsItemsIds?.Count > 0)
            {
                foreach (var groupItem in block.GroupsItemsIds)
                {
                    try
                    {
                        var group = response.Groups?.SingleOrDefault(b => b.Id == groupItem.Id);
                        if (group != null) block.Groups.Add(group);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error processing group item ID '{groupItem.Id}': {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Универсальный метод заполнения коллекции сущностей по ID.
        /// </summary>
        /// <typeparam name="TId">Тип идентификатора (string, long, etc.)</typeparam>
        /// <typeparam name="TEntity">Тип сущности</typeparam>
        /// <param name="ids">Коллекция идентификаторов</param>
        /// <param name="entityFinder">Функция поиска сущности по ID</param>
        /// <param name="targetCollection">Целевая коллекция для добавления найденных сущностей</param>
        /// <param name="useFirstOrDefault">Использовать FirstOrDefault вместо SingleOrDefault (для дубликатов)</param>
        /// <param name="afterAdd">Действие после добавления сущности (опционально)</param>
        private static void FillEntities<TId, TEntity>(
            List<TId> ids,
            Func<TId, TEntity> entityFinder,
            ICollection<TEntity> targetCollection,
            bool useFirstOrDefault = false,
            Action<TEntity> afterAdd = null)
        {
            // ИСПРАВЛЕНО: проверяем на null и пустоту
            if (ids == null || ids.Count == 0) return;

            // ИСПРАВЛЕНО: проверяем, что целевая коллекция не null
            if (targetCollection == null) return;

            foreach (var id in ids)
            {
                try
                {
                    var entity = entityFinder(id);
                    if (entity == null) continue;

                    targetCollection.Add(entity);
                    afterAdd?.Invoke(entity);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error processing ID '{id}': {ex.Message}");
                }
            }
        }
    }
}