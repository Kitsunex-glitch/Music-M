using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using MusicX.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VK_UI3.Controls;
using VK_UI3.DB;
using VK_UI3.Helpers;
using VK_UI3.Views.ModalsPages;
using VK_UI3.VKs;
using VkNet.Model.Attachments;
using VkNet.Utils;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace VK_UI3.Views
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public enum OpenedPlayList
    {
        all,
        UserPlayList,
        UserAlbums
    }
    public class UserPlayListParameters
    {
        public VkCollection<AudioPlaylist> VKaudioPlaylists;
        public uint offset = 0;
        public long? UserId = null;
        public OpenedPlayList openedPlayList;
        public bool LoadedAll;
    }
    public sealed partial class UserPlayList : Microsoft.UI.Xaml.Controls.Page
    {

        public ObservableRangeCollection<AudioPlaylist> audioPlaylists { get; set; } = new ObservableRangeCollection<AudioPlaylist>();

        public UserPlayList()
        {
            this.InitializeComponent();
            this.Loaded += UserPlayList_Loaded;
            this.Loading += UserPlayList_Loading;

            this.Unloaded += UserPlayList_Unloaded;

        }
        public UserPlayList(List<VkNet.Model.Attachments.Audio> audioList)
        {
            this.InitializeComponent();
            this.Loaded += UserPlayList_Loaded;
            this.Loading += UserPlayList_Loading;
            this.Unloaded += UserPlayList_Unloaded;
            this.audioList = audioList;
        }

        private void UserPlayList_Unloaded(object sender, RoutedEventArgs e)
        {
            this.Loaded -= UserPlayList_Loaded;
            this.Loading -= UserPlayList_Loading;

            this.Unloaded -= UserPlayList_Unloaded;

            if (scrollViewer != null)
                this.scrollViewer.ViewChanged -= ScrollViewer_ViewChanged;
        }




        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {

            this.Loaded -= UserPlayList_Loaded;
            this.Loading -= UserPlayList_Loading;
            try
            {
                if (scrollViewer != null)
                    this.scrollViewer.ViewChanged -= ScrollViewer_ViewChanged;
            }
            catch (Exception ex)
            {

            }
        }

        List<VkNet.Model.Attachments.Audio> audioList = null;

        UserPlayListParameters parameters;

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            var userPlayList = e.Parameter as UserPlayListParameters;
            if (userPlayList == null)
                return;
            parameters = userPlayList;
        }



        private bool CheckIfAllContentIsVisible(ScrollViewer scrollViewer)
        {
            if (scrollViewer.ViewportHeight >= scrollViewer.ExtentHeight - 50)
            {
                return true;
            }
            return false;
        }

        private void UserPlayList_Loading(FrameworkElement sender, object args)
        {
            if (parameters == null) parameters = new UserPlayListParameters();
            if (parameters.LoadedAll)
            {
                LoadingIndicator.Visibility = Visibility.Collapsed;
            }
            if (parameters.openedPlayList != OpenedPlayList.UserPlayList)
            {
                CreateButton.Visibility = Visibility.Collapsed;
            }
            if (audioList != null)
            {
                this.Background = Application.Current.Resources["AcrylicBackgroundFillColorDefaultBrush"] as SolidColorBrush;
                CreateButton.Visibility = Visibility.Collapsed;
                //this.Margin = new Thickness(-25);
                Cancel.Visibility = Visibility.Visible;
            }
        }
        ScrollViewer scrollViewer { get; set; }
        private void UserPlayList_Loaded(object sender, RoutedEventArgs e)
        {
            scrollViewer = SmallHelpers.FindScrollViewer(gridV);
            if (audioList != null)
            {
                gridV.SelectionMode = ListViewSelectionMode.Single;
                this.parameters.openedPlayList = OpenedPlayList.UserPlayList;
                MainGrid.Background = (Brush)Application.Current.Resources["AcrylicBackgroundFillColorDefaultBrush"];
            }

            if (parameters.VKaudioPlaylists != null)
            {
                addToList(parameters.VKaudioPlaylists);
            }
            else
            {

            }

            if (CheckIfAllContentIsVisible(scrollViewer))
            {
                loadMoreAsync();
            }

            this.scrollViewer.ViewChanged += ScrollViewer_ViewChanged;
        }

        private void ScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {

            var isAtBottom = scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 50;
            if (isAtBottom)
            {
                loadMoreAsync();
            }
        }

        /// <summary>
        /// Ключи уже показанных плейлистов — защита от дублей при повторных запросах
        /// (VK может отдать тот же плейлист на соседних страницах пагинации).
        /// </summary>
        private readonly HashSet<(long OwnerId, long Id)> _addedPlaylists = new();

        void addToList(VkCollection<AudioPlaylist> VKaudioPlaylists)
        {
            int added = 0;
            int filteredOut = 0;
            int duplicates = 0;

            foreach (var item in VKaudioPlaylists)
            {
                bool isUserPlaylist = item.OwnerId == AccountsDB.activeAccount.id && item.Original == null;
                bool isVisible = parameters.openedPlayList switch
                {
                    OpenedPlayList.all => true,
                    OpenedPlayList.UserPlayList => isUserPlaylist,
                    OpenedPlayList.UserAlbums => !isUserPlaylist,
                    _ => false
                };

                if (!isVisible)
                {
                    filteredOut++;
                    continue;
                }

                if (!_addedPlaylists.Add((item.OwnerId, item.Id)))
                {
                    duplicates++;
                    continue;
                }

                audioPlaylists.Add(item);
                added++;
            }

            System.Diagnostics.Debug.WriteLine(
                $"[UserPlayList] addToList: получено={VKaudioPlaylists.Count}, добавлено={added}, " +
                $"отфильтровано={filteredOut}, дублей={duplicates}, всего в списке={audioPlaylists.Count}, " +
                $"offset={parameters.offset}, loadedAll={parameters.LoadedAll}");
        }



        bool nowLoaded = false;

        /// <summary>Размер страницы пагинации плейлистов.</summary>
        private const int PlaylistsPageSize = 100;

        /// <summary>Сколько страниц максимум догружаем за один заход, пока экран не заполнится.</summary>
        private const int MaxAutoFillPages = 20;

        internal EventHandler selectedPlayList;

        /// <summary>
        /// Догружает следующую страницу плейлистов.
        /// <para>
        /// offset увеличивается на реально полученное количество ДО фильтрации —
        /// иначе отфильтрованные записи (чужие альбомы, оригиналы) съедали бы часть
        /// страницы, и часть плейлистов пользователя никогда не запрашивалась.
        /// </para>
        /// </summary>
        private async Task loadMoreAsync()
        {
            if (nowLoaded)
                return;

            if (parameters == null)
                parameters = new UserPlayListParameters();

            if (parameters.UserId == null)
                parameters.UserId = AccountsDB.activeAccount.id;

            if (parameters.LoadedAll)
                return;

            nowLoaded = true;

            try
            {
                for (int page = 0; page < MaxAutoFillPages; page++)
                {
                    if (parameters.LoadedAll)
                        break;

                    var requestOffset = parameters.offset;
                    var response = await VK.api.Audio.GetPlaylistsAsync(
                        (long)parameters.UserId, PlaylistsPageSize, requestOffset);

                    int received = response?.Count ?? 0;

                    // Offset двигаем на сырое количество, а не на количество после фильтра.
                    parameters.offset += (uint)received;

                    if (received > 0)
                        addToList(response);

                    // Признак конца списка — только по сырому ответу API.
                    if (received < PlaylistsPageSize)
                    {
                        parameters.LoadedAll = true;
                        LoadingIndicator.Visibility = Visibility.Collapsed;
                    }

                    System.Diagnostics.Debug.WriteLine(
                        $"[UserPlayList] страница: requestOffset={requestOffset}, получено={received}, " +
                        $"nextOffset={parameters.offset}, loadedAll={parameters.LoadedAll}");

                    if (received == 0)
                        break;

                    // Экран ещё не заполнен — догружаем следующую страницу.
                    if (parameters.LoadedAll || !CheckIfAllContentIsVisible(scrollViewer))
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[UserPlayList] ошибка загрузки плейлистов (offset={parameters.offset}): " +
                    $"{ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                LoadingIndicator.IsActive = false;
                nowLoaded = false;
            }
        }


        private async void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            ContentDialog dialog = new CustomDialog();

            dialog.Transitions = new TransitionCollection
                {
                    new PopupThemeTransition()
                };

            // XamlRoot must be set in the case of a ContentDialog running in a Desktop app
            dialog.XamlRoot = this.XamlRoot;

            var a = new CreatePlayList();
            dialog.Content = a;
            dialog.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);

            void CancelPressedHandler(object s, EventArgs e)
            {
                if (dialog != null)
                    dialog.Hide();
                dialog = null;

                if (s != null && s is AudioPlaylist)
                {
                    var playlist = s as AudioPlaylist;
                    this.audioPlaylists.Insert(0, playlist);
                }
            }

            a.cancelPressed += CancelPressedHandler;


            void CloseHandler(ContentDialog sender, ContentDialogClosedEventArgs args)
            {

                a.cancelPressed -= CancelPressedHandler;
            }

            dialog.Closed += CloseHandler;

            dialog.ShowAsync();
        }

        private void gridV_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Проверка на наличие выбранных элементов
            if (e.AddedItems.Count == 0)
            {
                return;
            }

            // Получение индекса выбранного элемента
            int selectedIndex = gridV.Items.IndexOf(e.AddedItems[0]);

            // Проверка на корректность индекса
            if (selectedIndex < 0 || selectedIndex >= audioPlaylists.Count)
            {
                return;
            }

            // Получение ID плейлиста
            var playlistId = audioPlaylists[selectedIndex].Id;

            List<string> lists = new List<string>();
            foreach (var item in audioList)
            {
                lists.Add($"{item.OwnerId}_{item.Id}");
            }

            // Добавление аудио в плейлист
            VK.api.Audio.AddToPlaylistAsync((long)audioPlaylists[selectedIndex].OwnerId, playlistId, lists);

            // Вызов события
            selectedPlayList?.Invoke(playlistId, EventArgs.Empty);
        }

        private void gridV_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (audioList == null) return;
            var control = args.ItemContainer.ContentTemplateRoot as PlaylistControl;
            if (control != null)
            {
                control.IsHitTestVisible = false;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            selectedPlayList?.Invoke(null, EventArgs.Empty);
        }
    }
}
