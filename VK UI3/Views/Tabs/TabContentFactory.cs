using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using VK_UI3.DB;
using VK_UI3.Views.ModalsPages;
using VK_UI3.VKs.IVK;
using VkNet.Model.Attachments;
using static VK_UI3.Views.SectionView;

namespace VK_UI3.Views.Tabs
{
    /// <summary>
    /// Строит описания вкладок (<see cref="TabContent"/>) для всех экранов приложения.
    /// Логика одна для главного окна и для окна полноэкранного плеера:
    /// оболочка только решает, в какой ленте создать вкладку.
    /// </summary>
    public static class TabContentFactory
    {
        #region Section navigation

        /// <summary>Раздел каталога / страница пользователя / поиск (через <see cref="WaitView"/>).</summary>
        public static TabContent Section(WaitParameters parameters, TabKey key, string header, string glyph)
        {
            return new TabContent(key, header, glyph, tab =>
            {
                tab.NavigationParameter = parameters;
                tab.ContentFrame.Navigate(typeof(WaitView), parameters, new DrillInNavigationTransitionInfo());
            });
        }

        /// <summary>Плейлист по идентификаторам альбома (owner_id + album_id + access_key).</summary>
        public static TabContent PlaylistById(long albumId, long ownerId, string accessKey, string header, string glyph)
        {
            var key = new TabKey(SectionType.PlayList, $"{ownerId}_{albumId}_{accessKey}");
            return new TabContent(key, header, glyph, tab =>
            {
                var parameters = new WaitParameters { sectionType = SectionType.PlayList };
                var playlist = new MusicX.Core.Models.Playlist { Id = albumId, OwnerId = ownerId };
                if (!string.IsNullOrEmpty(accessKey))
                    playlist.AccessKey = accessKey;

                parameters.iVKGetAudio = new PlayListVK(playlist, MainView.DispatcherQueuePublic);
                tab.NavigationParameter = parameters;
                tab.ContentFrame.Navigate(typeof(WaitView), parameters, new DrillInNavigationTransitionInfo());
            });
        }

        /// <summary>Плейлист из модели VK.</summary>
        public static TabContent Playlist(AudioPlaylist playlist, string header, string glyph)
        {
            var key = playlist == null
                ? new TabKey(SectionType.PlayList, Guid.NewGuid().ToString())
                : new TabKey(SectionType.PlayList, $"{playlist.OwnerId}_{playlist.Id}_{playlist.AccessKey}");

            return new TabContent(key, header, glyph, tab =>
            {
                var parameters = new WaitParameters
                {
                    sectionType = SectionType.PlayList,
                    Playlist = playlist
                };
                tab.NavigationParameter = parameters;
                tab.ContentFrame.Navigate(typeof(WaitView), parameters, new DrillInNavigationTransitionInfo());
            });
        }

        /// <summary>Очередь / произвольный источник (IVKGetAudio).</summary>
        public static TabContent IVkAudio(IVKGetAudio source, TabKey key, string header, string glyph)
        {
            return new TabContent(key, header, glyph, tab =>
            {
                var parameters = new WaitParameters
                {
                    sectionType = SectionType.CustomIVKGetAudio,
                    iVKGetAudio = source
                };
                tab.NavigationParameter = parameters;
                tab.ContentFrame.Navigate(typeof(WaitView), parameters, new DrillInNavigationTransitionInfo());
            });
        }

        /// <summary>Плейлист, представленный IVKGetAudio.</summary>
        public static TabContent PlaylistSource(IVKGetAudio source, string header, string glyph)
        {
            var key = new TabKey(SectionType.PlayList, source?.GetHashCode().ToString());
            return new TabContent(key, header, glyph, tab =>
            {
                var parameters = new WaitParameters
                {
                    sectionType = SectionType.PlayList,
                    iVKGetAudio = source
                };
                tab.NavigationParameter = parameters;
                tab.ContentFrame.Navigate(typeof(WaitView), parameters, new DrillInNavigationTransitionInfo());
            });
        }

        #endregion

        #region Standalone pages

        /// <summary>Параметры (SettingsPage).</summary>
        public static TabContent Settings()
        {
            return new TabContent(new TabKey(SectionType.None, "settings"), "Параметры", "\uE713", tab =>
            {
                tab.NavigationParameter = null;
                tab.ContentFrame.Navigate(typeof(Settings.SettingsPage), null, new DrillInNavigationTransitionInfo());
            });
        }

        /// <summary>«Что слушают».</summary>
        public static TabContent WhatListening(object data)
        {
            return new TabContent(new TabKey(SectionType.WhatListening, string.Empty), "Что слушают", "\uE783", tab =>
            {
                tab.NavigationParameter = data;
                tab.ContentFrame.Navigate(typeof(WhatListeningPage), data, new DrillInNavigationTransitionInfo());
            });
        }

        /// <summary>Создание плейлиста (открывается страницей вместо диалога).</summary>
        public static TabContent CreatePlaylist(CreatePlayList page)
        {
            return new TabContent(
                new TabKey(SectionType.None, "create-playlist:" + Guid.NewGuid()),
                "Создание плейлиста",
                "\uE710",
                tab =>
                {
                    tab.NavigationParameter = page;
                    tab.ContentFrame.Children.Add(new ScrollViewer
                    {
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Content = page
                    });
                });
        }

        #endregion

        #region Headers / glyphs

        public static string GetSectionHeader(SectionType type) => type switch
        {
            SectionType.MyListAudio => "Моя музыка",
            SectionType.LoadFriends => "Музыка друзей",
            SectionType.ConversDialogs => "Вложения",
            SectionType.Artist => "Артист",
            SectionType.UserSection => "Профиль",
            SectionType.Search => "Поиск",
            SectionType.UserPlayListList => "Плейлисты",
            SectionType.PlayList => "Плейлист",
            SectionType.WhatListening => "Что слушают",
            _ => "Раздел"
        };

        public static string GetSectionGlyph(SectionType type) => type switch
        {
            SectionType.MyListAudio => "\uEC4F",
            SectionType.LoadFriends => "\uE716",
            SectionType.ConversDialogs => "\uE8F2",
            SectionType.Artist => "\uE77B",
            SectionType.UserSection => "\uE77B",
            SectionType.Search => "\uE11A",
            SectionType.UserPlayListList => "\uE93C",
            SectionType.PlayList => "\uE189",
            SectionType.WhatListening => "\uE783",
            _ => "\uE10F"
        };

        #endregion

        #region Restore

        /// <summary>
        /// Восстанавливает содержимое вкладки по её ключу (после перезапуска
        /// или после переноса вкладки из окна плеера в главное окно).
        /// </summary>
        public static void RestoreInto(TabEntry tab)
        {
            if (tab?.Key == null)
                return;

            tab.NeedsRestore = false;

            switch (tab.Key.Type)
            {
                case SectionType.PlayList when tab.Key.Id != null && tab.Key.Id.Contains('_'):
                    var parts = tab.Key.Id.Split('_');
                    if (parts.Length >= 2 &&
                        long.TryParse(parts[0], out var ownerId) &&
                        long.TryParse(parts[1], out var albumId))
                    {
                        var accessKey = parts.Length >= 3 && !string.IsNullOrEmpty(parts[2]) ? parts[2] : null;
                        PlaylistById(albumId, ownerId, accessKey, tab.Header, tab.Glyph)
                            .Navigate(tab);
                        return;
                    }
                    break;

                case SectionType.Search:
                    var search = new WaitParameters
                    {
                        sectionType = SectionType.Search,
                        SectionID = "search",
                        searchText = tab.Key.Id
                    };
                    tab.NavigationParameter = search;
                    tab.ContentFrame.Navigate(typeof(WaitView), search, new DrillInNavigationTransitionInfo());
                    return;

                case SectionType.None when tab.Key.Id == "settings":
                    Settings().Navigate(tab);
                    return;
            }

            var wait = new WaitParameters { sectionType = tab.Key.Type, SectionID = tab.Key.Id };
            tab.NavigationParameter = wait;
            tab.ContentFrame.Navigate(typeof(WaitView), wait, new DrillInNavigationTransitionInfo());
        }

        #endregion
    }
}
