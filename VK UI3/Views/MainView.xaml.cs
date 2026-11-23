using KrotosNavigationFrame;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using MusicX.Core.Models;
using MusicX.Core.Services;
using MusicX.Services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using VK_UI3.Controllers;
using VK_UI3.DB;
using VK_UI3.DownloadTrack;
using VK_UI3.Helpers;
using VK_UI3.Helpers.Animations;
using VK_UI3.Services;
using VK_UI3.Views.ModalsPages;
using VK_UI3.Views.Notification;
using VK_UI3.Views.Tabs;
using VK_UI3.VKs;
using VK_UI3.VKs.IVK;
using VkNet.Model.Attachments;
using static System.Collections.Specialized.BitVector32;
using static VK_UI3.DB.AccountsDB;
using static VK_UI3.Views.SectionView;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace VK_UI3.Views
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainView : Microsoft.UI.Xaml.Controls.Page, INotifyPropertyChanged
    {
        public static MainView mainView;

        /// <summary>Реестр вкладок (создаётся в конструкторе, доступен статическим хелперам).</summary>
        public static TabManager TabManager { get; private set; }

        /// <summary>Активная вкладка.</summary>
        public static TabEntry ActiveTab => TabManager?.ActiveTab;

        /// <summary>Стек навигации активной вкладки (совместимость со старым API).</summary>
        public static NavigateFrame frame => ActiveTab?.ContentFrame;

        /// <summary>Стек навигации активной вкладки.</summary>
        public NavigateFrame ContentFramePublic => ActiveTab?.ContentFrame;

        public MainView()
        {
            this.InitializeComponent();


            // FramePlayer.RenderTransform = trans;
            TabManager = new TabManager(Tabs);
            TabManager.HookTabViewEvents();
            TabManager.ActiveTabChanged += TabManager_ActiveTabChanged;
            TabManager.TabCreated += TabManager_TabCreated;
            TabManager.CloseRequested += TabManager_CloseRequested;
            TabManager.AddTabRequested += TabManager_AddTabRequested;
            mainView = this;

            //OpenMyPage(SectionType.MyListAudio);

            this.Loaded += MainView_Loaded;
            this.Loading += MainView_Loading;

        }

        private void MainView_Loading(FrameworkElement sender, object args)
        {
            NavWiv.IsPaneOpen = false;

            if (!new CheckFFmpeg().IsExist())
            {
                MainWindow.mainWindow.requstDownloadFFMpegAsync();
            }
        }

        public void updateAllWithReacreate()
        {
            TempPlayLists.TempPlayLists.updateNextRequest = true;

            // При смене аккаунта состояние всех вкладок невалидно: оставляем только домашнюю.
            var home = TabManager?.Tabs.FirstOrDefault();
            if (home != null)
                TabManager.CloseAllExcept(home);

            if (ActiveTab?.ContentFrame?.Children.Count > 0)
            {
                _ = CreateNavigation();
            }
            else
            {
                OpenMyPage(SectionType.MyListAudio);
            }
        }

        private void MainView_Unloaded(object sender, RoutedEventArgs e)
        {
            DetachActiveTab();
            NavWiv.BackRequested -= NavWiv_BackRequested;
            AccountsDB.ChanhgeActiveAccount -= ChangeAccount;
            this.KeyDown -= MainView_KeyDown;
    
            this.Unloaded -= MainView_Unloaded;





            MainWindow.mainWindow.onBackClicked -= back;

            CollapseAnimation.Completed -= CollapseAnimation_Completed;
            CollapseAnimationPlayingList.Completed -= CollapseAnimationPlayingList_Completed;


            this.Unloaded -= MainView_Unloaded;
        }

        private void MainView_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.F5)
            {
                MainWindow.onRefreshClickedvoid();
            }
        }


        public void openGenerator(IVKGetAudio iVKGetAudio , string unicID, string genBy = "genBy")
        {
            var a = new CreatePlayList(iVKGetAudio, genBy, unicID);
            OpenGenerator(a);

        }
        public void openGenerator(AudioPlaylist playList, string unicID, string genBy = null)
        {
            var a = new CreatePlayList(playList, true, genBy, unicID);
            OpenGenerator(a);
        }
        private void OpenGenerator(CreatePlayList createPlayList)
        {
            // Ctrl + «Создать плейлист» — вместо диалога открываем содержимое в новой вкладке.
            if (InputState.IsCtrlPressed())
            {
                var tab = TabManager.CreateAndActivate(
                    new TabKey(SectionType.None, "create-playlist:" + Guid.NewGuid()),
                    "Создание плейлиста",
                    "\uE710");

                tab.NavigationParameter = createPlayList;
                tab.ContentFrame.Children.Add(new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = createPlayList
                });
                return;
            }

            ContentDialog dialog = new CustomDialog();

            dialog.XamlRoot = this.XamlRoot;
            dialog.Transitions = new TransitionCollection
                {
                new PopupThemeTransition()
                };

        

            dialog.Content = createPlayList;
            dialog.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            EventHandler cancelPressedHandler = null;
            cancelPressedHandler = (s, e) =>
            {
                try
                {
                    if (dialog != null)
                        dialog.Hide();
                    dialog = null;
                }
                catch
                {

                }
                finally
                {
                    createPlayList.cancelPressed -= cancelPressedHandler;
                }
            };

            createPlayList.cancelPressed += cancelPressedHandler;


            dialog.ShowAsync();

        }

        /// <summary>
        /// Наполняет корень новой вкладки. Контент размещается в NavigateFrame вкладки,
        /// поэтому у вкладки остаётся собственный стек навигации.
        /// </summary>
        private static void NavigateTabRoot(TabEntry tab, Action navigate)
        {
            if (tab == null)
                return;

            tab.NeedsRestore = false;
            try
            {
                navigate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainView] NavigateTabRoot failed: {ex.Message}");
            }
        }


        private static DispatcherQueue dispatcherQueue = null;
        private void MainView_Loaded(object sender, RoutedEventArgs e)
        {

            NavWiv.IsPaneOpen = false;
            //OpenMyPage(SectionType.MyListAudio);
            AttachActiveTab();

            NavWiv.BackRequested += NavWiv_BackRequested;

            MainWindow.mainWindow.onBackClicked += back;

            AccountsDB.ChanhgeActiveAccount += ChangeAccount;
            CollapseAnimation.Completed += CollapseAnimation_Completed;
            CollapseAnimationPlayingList.Completed += CollapseAnimationPlayingList_Completed;
            ;


            this.KeyDown += MainView_KeyDown;

            this.Unloaded += MainView_Unloaded;

            _ = CreateNavigation();
            dispatcherQueue = this.DispatcherQueue;

            ConfigureSettingsButtonAccessibility();

            MainWindow.mainWindow.MainWindow_showRefresh();

            CheckMemberVK();

        }

        /// <summary>
        /// Настраивает доступность встроенной кнопки «Параметры» для экранного диктора (Narrator),
        /// чтобы незрячие пользователи понимали, как открыть настройки (включая горячие клавиши).
        /// </summary>
        private void ConfigureSettingsButtonAccessibility()
        {
            try
            {
                var settingsButton = FindSettingsButton(NavWiv);
                if (settingsButton == null)
                {
                    // Кнопка настроек может создаваться при первом раскрытии панели — пробуем позже.
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        var btn = FindSettingsButton(NavWiv);
                        ApplySettingsButtonAccessibility(btn);
                    });
                    return;
                }

                ApplySettingsButtonAccessibility(settingsButton);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainView] ConfigureSettingsButtonAccessibility failed: {ex.Message}");
            }
        }

        private static void ApplySettingsButtonAccessibility(Microsoft.UI.Xaml.Controls.Button settingsButton)
        {
            if (settingsButton == null) return;

            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(settingsButton, "Параметры");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(settingsButton,
                "Открывает настройки приложения. Внутри раздела «Горячие клавиши» можно настроить управление воспроизведением.");
        }

        /// <summary>
        /// Рекурсивно ищет встроенную кнопку настроек NavigationView.
        /// </summary>
        private static Microsoft.UI.Xaml.Controls.Button FindSettingsButton(DependencyObject root)
        {
            if (root == null) return null;

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is Microsoft.UI.Xaml.Controls.Button button && button.Name == "SettingsButton")
                {
                    return button;
                }

                var result = FindSettingsButton(child);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private void CollapseAnimationPlayingList_Completed(object sender, object e)
        {
            if (SectionViewPageNowPlayingList.Opacity == 0)
                SectionViewPageNowPlayingList.Visibility = Visibility.Collapsed;
        }

        private void CheckMemberVK()
        {
            var setting = DB.SettingsTable.GetSetting("dateMemberCheck");
            if (setting != null && !string.IsNullOrEmpty(setting.settingValue))
            {
                if (DateTime.TryParse(setting.settingValue, out var lastCheckDate))
                {
                    if (!((DateTime.Now.Date - lastCheckDate.Date).TotalDays >= 30))
                    {
                        return;
                    }
                }
            }

            DB.SettingsTable.SetSetting("dateMemberCheck", DateTime.Now.Date.ToString());

            new Notification.Notification("У нас есть ТГ канал!", @"Привет! 🖐

Я разработчик Music M.

У нас есть ТГ канал, куда публикуются новости о разработке, уведомления о релизе новых версий и некоторые другие вещи. 🚀

Вы всегда можете задать мне свои вопросы и предложить что-то новое, поделиться своими мыслями касательно разработки, интерфейса и других деталей. Возможно Вам чего-то не хватает, что может было бы Вам полезно, а мне интересно реализовывать.
",
                new ButtonNotification("Телеграм", new Action(() =>
                {

                    Process.Start(new ProcessStartInfo
                    {
                        UseShellExecute = true,
                        FileName = "https://t.me/music_m_rework"
                    });

                }), true)
            );
        }



        private void CollapseAnimation_Completed(object sender, object e)
        {
            if (LyricPage.Opacity == 0)
            {
                LyricPage.Visibility = Visibility.Collapsed;

                // Панель закрыта и не в полноэкранном режиме — останавливаем синхронизацию,
                // чтобы таймер/загрузка текста не работали вхолостую.
                if (LyricPage.Parent == MainGridLir)
                    LyricPage.Disable();
            }
        }

        private async void back(object sender, EventArgs e)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                var current = ActiveTab?.ContentFrame;
                if (current != null && current.CanGoBack)
                    current.GoBack();
            });
        }

        private void ChangeAccount(object sender, EventArgs e)
        {
            updateAllWithReacreate();
        }



        private void NavWiv_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        {
            var current = ActiveTab?.ContentFrame;
            if (current != null && current.CanGoBack)
            {
                current.GoBack();
            }
        }

        /// <summary>
        /// Обновляет состояние шапки (кнопка «Назад», поиск) по стеку навигации
        /// указанной вкладки. Для неактивных вкладок шапка не трогается.
        /// </summary>
        private void UpdateChromeForTab(TabEntry tab)
        {
            if (tab == null || !ReferenceEquals(tab, ActiveTab))
                return;

            var current = tab.ContentFrame;

            if (current != null && current.CanGoBack && !navToAnotherPage)
            {
                MainWindow.mainWindow.backBTNShow();
            }
            else
            {
                MainWindow.mainWindow.backBTNHide();
            }

            if (current?.frameCurrent?.sectionType == SectionType.Search)
            {
                showSearch();
            }
            else
            {
                hideSearch();
            }
            navToAnotherPage = false;
        }

        /// <summary>Обновляет шапку для текущей активной вкладки.</summary>
        private void UpdateChromeForActiveTab() => UpdateChromeForTab(ActiveTab);

        protected void OnPropertyChanged(string propertyName)
        {
            this.DispatcherQueue.TryEnqueue(async () =>
            {
                //   this.ImgUri = uri;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            });
        }



        public List<AnimatedNavMenuController> navMenuControllers = new();
        private CancellationTokenSource _cts = new CancellationTokenSource();
        //   private CancellationTokenSource cts = null;
        
        // Поле для хранения предзагруженных данных "Что слушают"
        private List<ListeningItem> _whatListeningData;

        private async Task CreateNavigation()
        {
            // Отменить предыдущую задачу, если она была запущена
            _cts.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            // Домашняя вкладка создаётся один раз; при пересоздании (смена аккаунта) — заново.
            if (TabManager.Tabs.Count == 0)
            {
                var home = TabManager.CreateAndActivate(
                    new TabKey(SectionType.MyListAudio, string.Empty),
                    "Моя музыка",
                    "\uEC4F",
                    isClosable: true);
                home.NavigationParameter = null;
            }

            await Task.Yield();


            _ = Task.Run(async () =>
            {
                try
                {
                    this.DispatcherQueue.TryEnqueue(async () =>
                    {
                        // Удаляем существующую кнопку повторной попытки, если она есть
                        var existingRetryButton = NavWiv.MenuItems.OfType<NavigationViewItem>()
                        .FirstOrDefault(item => item.Tag?.ToString() == "retry_button");
                    });

                    ObservableCollection<NavSettings> navSettings = new ObservableCollection<NavSettings>();

                    this.DispatcherQueue.TryEnqueue(async () =>
                    {
                        // Восстанавливаем сохранённые вкладки до первого перехода по меню.
                        TryRestoreTabs();

                        NavWiv.SelectedItem = 0;
                        navigateInvoke();
                        ClearMenuItems();
                    });

                    var catalogs = await VK.vkService.GetAudioCatalogAsync();


                    token.ThrowIfCancellationRequested();

                    
                    var updatesSection = await VK.vkService.GetAudioCatalogAsync("https://vk.ru/audio?section=updates");

                    token.ThrowIfCancellationRequested();
                    if (updatesSection.Catalog?.Sections?.Count > 0)
                    {
                        var section = updatesSection.Catalog.Sections[0];
                        section.Title = "Подписки";
                        catalogs.Catalog.Sections.Insert(catalogs.Catalog.Sections.Count - 1, section);
                    }



                    var sectionsService = StaticService.Container.GetRequiredService<ICustomSectionsService>();
                    catalogs.Catalog.Sections.AddRange(await sectionsService.GetSectionsAsync().ToArrayAsync());

                    var icons = GetIcons();

                    token.ThrowIfCancellationRequested();

                    foreach (var section in catalogs.Catalog.Sections)
                    {
                        var navSet = CreateNavSettings(section, icons);
                        if (navSet != null)
                        {
                            navSettings.Add(navSet);
                        }
                    }

                    // Загружаем данные "Что слушают" в фоне
                    try
                    {
                        var whatListeningService = StaticService.Container.GetRequiredService<IWhatListeningService>();
                        _whatListeningData = await whatListeningService.GetWhatListeningAsync();
                    }
                    catch
                    {
                        // Если загрузка не удалась — просто не добавляем кнопку
                        _whatListeningData = null;
                    }

                    int index = 0;
                    this.DispatcherQueue.TryEnqueue(async () =>
                    {
                        index = NavWiv.MenuItems.IndexOf(NavWiv.MenuItems.OfType<NavigationViewItemHeader>().First());
                    });

                    token.ThrowIfCancellationRequested();

                    this.DispatcherQueue.TryEnqueue(async () =>
                    {
                        foreach (var setting in navSettings)
                        {
                            token.ThrowIfCancellationRequested();

                            var navViewItem = new AnimatedNavMenuController
                            {
                                navSettings = setting,
                                Content = setting.MyMusicItem,
                                Icon = new FontIcon { Glyph = setting.Icon }
                            };
                            NavWiv.MenuItems.Insert(index, navViewItem);
                            navMenuControllers.Add(navViewItem);
                            index++;

                        }

                        // Добавляем "Что слушают" после "Для вас", только если данные загрузились
                        if (_whatListeningData != null && _whatListeningData.Count > 0)
                        {
                            var whatListeningSet = new NavSettings()
                            {
                                Icon = "\uE783",
                                MyMusicItem = "Что слушают",
                                section = null
                            };

                            var navViewItem = new AnimatedNavMenuController
                            {
                                navSettings = whatListeningSet,
                                Content = whatListeningSet.MyMusicItem,
                                Icon = new FontIcon { Glyph = whatListeningSet.Icon }
                            };

                            // Находим индекс элемента "Для вас" и вставляем после него
                            int insertIndex = -1;
                            for (int i = 0; i < NavWiv.MenuItems.Count; i++)
                            {
                                if (NavWiv.MenuItems[i] is NavigationViewItem item &&
                                    item.Content?.ToString().Equals("Для Вас", StringComparison.OrdinalIgnoreCase) == true)
                                {
                                    insertIndex = i + 1;
                                    break;
                                }
                            }

                            if (insertIndex >= 0)
                            {
                                NavWiv.MenuItems.Insert(insertIndex, navViewItem);
                            }
                            else
                            {
                                NavWiv.MenuItems.Add(navViewItem);
                            }
                            navMenuControllers.Add(navViewItem);
                        }
                    });


                }
                catch (OperationCanceledException)
                {
                    
                }
                catch (Exception e)
                {
                    this.DispatcherQueue.TryEnqueue(async () =>
                    {
                        AddRetryButton();
                    });
                }


            }, token);
        }

        // Метод для добавления кнопки повторной попытки
        private void AddRetryButton()
        {
            // Удаляем существующую кнопку повторной попытки, если она есть
            var existingRetryButton = NavWiv.MenuItems.OfType<NavigationViewItem>()
                .FirstOrDefault(item => item.Tag?.ToString() == "retry_button");

            if (existingRetryButton != null)
            {
                NavWiv.MenuItems.Remove(existingRetryButton);
            }

            // Создаем новую кнопку "Запросить повторно"
            var navSet =  new NavSettings() { Icon = "\uE149", MyMusicItem = "Запросить повторно", section = null };

            int index = 0;
            this.DispatcherQueue.TryEnqueue(async () =>
            {
                index = NavWiv.MenuItems.IndexOf(NavWiv.MenuItems.OfType<NavigationViewItemHeader>().First());
            });

            this.DispatcherQueue.TryEnqueue(async () =>
            {
                    var navViewItem = new AnimatedNavMenuController
                    {
                        navSettings = navSet,
                        Content = navSet.MyMusicItem,
                        Icon = new FontIcon { Glyph = navSet.Icon }
                    };
                    NavWiv.MenuItems.Insert(index, navViewItem);
                    navMenuControllers.Add(navViewItem);
                    index++;
            });

        }


        private void ClearMenuItems()
        {
            foreach (var item in navMenuControllers)
            {

                item.deleted += OnItemDeleted;


                item.delete();
            }
            navMenuControllers.Clear();
        }


        private void OnItemDeleted(object sender, EventArgs e)
        {
            // Отписываемся от события
            ((AnimatedNavMenuController)sender).deleted -= OnItemDeleted;
            NavWiv.MenuItems.Remove(sender);
        }

        private List<string> GetIcons()
        {
            try
            {
                return new List<string>
                {
                    "\uE142", // MusicInfo
                    "\uE189", // Audio
                    "\uE102", // Play
                    "\uE103", // Pause
                    "\uE101", // Stop
                    "\uE100", // Forward
                    "\uE106", // Back
                    "\uE108", // Previous
                    "\uE107", // Next
                    "\uE767", // Volume
                    "\uE74F", // Mute
                    "\uE10C", // More
                    "\uE158", // Pictures
                    "\uE707", // Map
                    "\uE787", // CalendarDay
                    "\uE141", // Bookmarks
                };
            }
            catch (Exception e)
            {
                return null;
            }
        }


        private NavSettings? CreateNavSettings(MusicX.Core.Models.Section section, List<string> icons)
        {
            string icon;

            switch (section.Title.ToLower())
            {
                case "главная":
                    icon = "\uE10F"; // Home
                    break;
                case "моя музыка":
                    icon = "\uE189"; // Audio
                    section.Title = "Музыка";
                    break;
                case "обзор":
                    icon = "\uECA5"; // PreviewLink
                    break;
                case "подкасты":
                    icon = "\uE1D6"; // Microphone
                    break;
                case "подписки":
                    icon = "\uE734"; // Favorite
                    break;
                case "каталоги":
                    icon = "\uE1D3"; // Library
                    break;
                case "поиск":
                    icon = "\uE11A"; // Find
                    break;
                case "книги и шоу":
                    return null;
                    break;
                case "радио":
                    return null;
                    break;
                case "детям":
                    return null;
                default:
                    icon = icons[0];
                    icons.RemoveAt(0);
                    break;
            }

            return new NavSettings() { Icon = icon, MyMusicItem = section.Title, section = section };
        }



        public async void RemoveNavItems()
        {
            var separator = NavWiv.MenuItems.OfType<NavigationViewItemSeparator>().First();
            var header = NavWiv.MenuItems.OfType<NavigationViewItemHeader>().First();

            int startIndex = NavWiv.MenuItems.IndexOf(separator);
            int endIndex = NavWiv.MenuItems.IndexOf(header);

            for (int i = endIndex; i > startIndex; i--)
            {
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    NavWiv.MenuItems.RemoveAt(i);
                });
            }
        }





        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            //OpenMyPage(SectionType.MyListAudio);
            // Проверьте, является ли переданный параметр типом NavigationInfo
            if (e.Parameter is NavigationInfo navigationInfo)
            {
                // Используйте navigationInfo здесь
                // ((MainWindow) navigationInfo.SourcePageType).GoLogin();
                MainWindow.mainWindow = (MainWindow)navigationInfo.SourcePageType;
            }

        }





        private void ListViewItem_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
          //  NavWiv.IsPaneOpen = false;

        }

        // Объект TranslateTransform, который будет использоваться для анимации
        TranslateTransform trans = new TranslateTransform();

        // Storyboard, который будет использоваться для управления анимацией
        Storyboard sb = new Storyboard();

        public event PropertyChangedEventHandler PropertyChanged;




        bool navToAnotherPage = false;
  

        #region Tab-aware navigation

        /// <summary>
        /// Открывает содержимое: при зажатом Ctrl — в новой вкладке, иначе в активной.
        /// Если открытых вкладок нет — создаёт новую.
        /// </summary>
        private static bool OpenInTab(WaitParameters parameters, TabKey key, string header, string glyph, bool forceNew)
        {
            if (mainView == null || TabManager == null)
                return false;

            var manager = TabManager;

            TabEntry tab = null;

            if (!forceNew && key != null)
            {
                var existing = manager.Find(key);
                if (existing != null)
                {
                    // Пустая вкладка (например, домашняя, созданная при старте, или
                    // восстановленная) ещё без содержимого — грузим в неё контент.
                    if (IsTabEmpty(existing))
                    {
                        tab = existing;
                        manager.Activate(existing);
                    }
                    else
                    {
                        manager.Activate(existing);
                        mainView.UpdateChromeForActiveTab();
                        return true;
                    }
                }
            }

            if (tab == null)
            {
                tab = (forceNew || manager.ActiveTab == null)
                    ? manager.CreateAndActivate(key, header, glyph)
                    : manager.ActiveTab;
            }

            if (tab == null)
                return false;

            tab.Key = key;
            tab.NavigationParameter = parameters;
            tab.Header = header;
            tab.Glyph = glyph;
            tab.ApplyHeader();

            tab.ContentFrame.Navigate(typeof(WaitView), parameters, new DrillInNavigationTransitionInfo());
            mainView.UpdateChromeForActiveTab();
            mainView.UpdateEmptyState();
            return true;
        }

        private static bool IsForceNewTab() => InputState.IsCtrlPressed();

        /// <summary>Вкладка ещё не содержит страниц (контент не загружался).</summary>
        private static bool IsTabEmpty(TabEntry tab)
            => tab?.ContentFrame != null && tab.ContentFrame.Children.Count == 0;

        /// <summary>Закрывает панель очереди перед навигацией (поведение прежней реализации).</summary>
        private static void CloseNowPlayingPanel()
        {
            if (mainView != null && !mainView.PlayNowPanelListCLosed)
            {
                mainView.TogglePlayNowPanel();
            }
        }

        #endregion

        public static void OpenMyPage(SectionType sectionType)
        {
            CloseNowPlayingPanel();

            var sectionView = new WaitParameters();
            sectionView.sectionType = sectionType;
            OpenInTab(sectionView, new TabKey(sectionType, string.Empty), GetSectionHeader(sectionType), GetSectionGlyph(sectionType), IsForceNewTab());
        }


        public static void OpenPlayListLists(long? id = null, OpenedPlayList openedPlayList = OpenedPlayList.all)
        {
            CloseNowPlayingPanel();

            var sectionView = new WaitParameters();
            if (id == null)
                id = activeAccount.id;
            sectionView.sectionType = SectionType.UserPlayListList;
            sectionView.SectionID = id.ToString();
            sectionView.openedPlayList = openedPlayList;

            var header = openedPlayList switch
            {
                OpenedPlayList.UserPlayList => "Мои плейлисты",
                OpenedPlayList.UserAlbums => "Плейлисты",
                _ => "Плейлисты"
            };
            var glyph = openedPlayList switch
            {
                OpenedPlayList.UserPlayList => "\uE93C",
                OpenedPlayList.UserAlbums => "\uEB51",
                _ => "\uEB51"
            };

            OpenInTab(sectionView, new TabKey(SectionType.UserPlayListList, $"{id}|{openedPlayList}"), header, glyph, IsForceNewTab());
        }
        public static void OpenIVkAudio(IVKGetAudio iVKGetAudio)
        {
            CloseNowPlayingPanel();


            var waitParameters = new WaitParameters();
            waitParameters.sectionType = SectionType.CustomIVKGetAudio;
            waitParameters.iVKGetAudio = iVKGetAudio;
            OpenInTab(waitParameters, new TabKey(SectionType.CustomIVKGetAudio, iVKGetAudio?.GetHashCode().ToString()), "Плейлист", "\uE189", IsForceNewTab());
        }

        public static void OpenPlayList(AudioPlaylist playlist)
        {
            CloseNowPlayingPanel();


            var waitParameters = new WaitParameters();
            waitParameters.sectionType = SectionType.PlayList;
            waitParameters.Playlist = playlist;
            OpenInTab(waitParameters, BuildPlaylistKey(playlist), playlist?.Title ?? "Плейлист", "\uE189", IsForceNewTab());
        }

        public static void OpenPlayList(IVKGetAudio iVKGetAudio)
        {
            CloseNowPlayingPanel();


            var sectionView = new WaitParameters();
                sectionView.sectionType = SectionType.PlayList;
                sectionView.iVKGetAudio = iVKGetAudio;
            OpenInTab(sectionView, new TabKey(SectionType.PlayList, iVKGetAudio?.GetHashCode().ToString()), "Плейлист", "\uE189", IsForceNewTab());
        }

        public static void OpenPlayList(long AlbumID, long AlbumOwnerID, string? AlbumAccessKey = null)
        {
            CloseNowPlayingPanel();


            var sectionView = new WaitParameters();
            sectionView.sectionType = SectionType.PlayList;
            MusicX.Core.Models.Playlist playlist = new MusicX.Core.Models.Playlist();
            playlist.Id = AlbumID;
            playlist.OwnerId = AlbumOwnerID;
            if (!(AlbumAccessKey is null))
                playlist.AccessKey = AlbumAccessKey;
            PlayListVK playListVK = new PlayListVK(playlist, dispatcherQueue);
            sectionView.iVKGetAudio = playListVK;

            var key = new TabKey(SectionType.PlayList, $"{AlbumOwnerID}_{AlbumID}_{AlbumAccessKey}");
            OpenInTab(sectionView, key, "Плейлист", "\uE189", IsForceNewTab());
        }

        public static void OpenSection(string sectionID, SectionType sectionType = SectionType.None)
        {
            OpenSection(sectionID, sectionType, null, null);
        }

        /// <summary>
        /// Открывает раздел с явными названием и иконкой (для разделов, загружаемых из сети).
        /// </summary>
        public static void OpenSection(string sectionID, SectionType sectionType, string header, string glyph)
        {
            CloseNowPlayingPanel();

            var sectionView = new WaitParameters();
            sectionView.SectionID = sectionID;
            if (sectionView.SectionID != "search")
            {
                sectionView.sectionType = sectionType;
            }
            else
            {
                sectionView.sectionType = SectionType.Search;
            }

            var tabHeader = string.IsNullOrWhiteSpace(header)
                ? GetSectionHeader(sectionView.sectionType)
                : header;
            var tabGlyph = string.IsNullOrWhiteSpace(glyph)
                ? GetSectionGlyph(sectionView.sectionType)
                : glyph;

            OpenInTab(sectionView, new TabKey(sectionView.sectionType, sectionID), tabHeader, tabGlyph, IsForceNewTab());
        }

        public static void OpenSearchSection(string searchString)
        {

            CloseNowPlayingPanel();

            var sectionView = new WaitParameters();
            sectionView.searchText = searchString;
            sectionView.SectionID = "search";
        
            sectionView.sectionType = SectionType.Search;
  
            OpenInTab(sectionView, new TabKey(SectionType.Search, searchString), "Поиск", "\uE11A", IsForceNewTab());
        }

        private static TabKey BuildPlaylistKey(AudioPlaylist playlist)
        {
            if (playlist == null)
                return new TabKey(SectionType.PlayList, Guid.NewGuid().ToString());

            return new TabKey(SectionType.PlayList, $"{playlist.OwnerId}_{playlist.Id}_{playlist.AccessKey}");
        }

        private static string GetSectionHeader(SectionType type) => type switch
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

        private static string GetSectionGlyph(SectionType type) => type switch
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


        public void SearchSetText(string Text)
        {
            Search.searchtext = Text;
        }

        public void showSearch() 
        {
            Search.Show();
        }

        public void hideSearch()
        {
            Search.Hide();
        }

        private void NavWiv_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            navigateInvoke();
        }

        bool lyrClosed = true;

        public bool PlayNowPanelListCLosed { get; set; } = true;

        public void ToggleLyricsPanel()
        {

            if (!PlayNowPanelListCLosed)
            {
                TogglePlayNowPanel(); 
            }

            if (lyrClosed)
            {
                ExpandLyricsPanel();
            }
            else
            {
                CollapseLyricsPanel();
            }
            lyrClosed = !lyrClosed;


        }
       


        public async void CollapseLyricsPanel()
        {
            this.DispatcherQueue.TryEnqueue(async () =>
            {
                ExpandAnimation.Pause();
                CollapseAnimation.Begin();
                LyricPage.Disable();
            });

        }

        public async void ExpandLyricsPanel()
        {
            LyricPage.Visibility = Visibility.Visible;
            CollapseAnimation.Pause();
            ExpandAnimation.Begin();
            LyricPage.Enable();
            _ = LyricPage.LoadLyricsAsync();
        }


        public void TogglePlayNowPanel()
        {

            if (!lyrClosed)
            {
                ToggleLyricsPanel();
            }

            if (PlayNowPanelListCLosed)
            {
                ExpandePlayNowPanel();
            }
            else
            {
                SectionViewPageNowPlayingList.Content = null;
                frame.ClearBackStack();
                CollapseePlayNowPanel();
            }
            PlayNowPanelListCLosed = !PlayNowPanelListCLosed;

        }

        private void CollapseePlayNowPanel()
        {
            ExpandAnimationPlayingList.Pause();
            CollapseAnimationPlayingList.Begin();

            //    LyricPage.Disable();

        }

        private void ExpandePlayNowPanel()
        {
            SectionViewPageNowPlayingList.Visibility = Visibility.Visible;
            CollapseAnimationPlayingList.Pause();

            ExpandAnimationPlayingList.Begin();

            if (VK_UI3.Services.MediaPlayerService.iVKGetAudio != null)
            {
                var waitParameters = new WaitParameters();
                waitParameters.sectionType = SectionType.CustomIVKGetAudio;
                waitParameters.iVKGetAudio = VK_UI3.Services.MediaPlayerService.iVKGetAudio;
                SectionViewPageNowPlayingList.Navigate(typeof(WaitView), waitParameters, new DrillInNavigationTransitionInfo());
            }
        }

        #region FullScreen lyrics reparenting

        // Родитель LyricsPage в MainView (запоминается при переносе в полноэкранный режим).
        private Panel _lyricsPageHomeParent;
        private int _lyricsPageHomeRow;
        private bool _lyricsToolbarWasCollapsed;
        private bool _lyricsSettingsWasOpen;

        /// <summary>
        /// Открепляет существующий <see cref="LyricsPage"/> от MainView для показа в полноэкранном режиме.
        /// Второй экземпляр BetterLyric/таймера не создаётся — переносится тот же контрол.
        /// </summary>
        public LyricsPage DetachLyricsPageForFullScreen()
        {
            if (LyricPage == null || LyricPage.Parent is not Panel parent)
                return null;

            _lyricsPageHomeParent = parent;
            _lyricsPageHomeRow = Grid.GetRow(LyricPage);

            // Синхронизацию в MainView останавливаем: пока страница в полноэкранном окне,
            // её таймером управляет полноэкранный режим (один BetterLyric — один таймер).
            _lyricsToolbarWasCollapsed = lyrClosed;
            LyricPage.Disable();

            // Скрываем тулбар и настройки — в полноэкранном режиме нужен только текст.
            _lyricsSettingsWasOpen = LyricPage.IsSettingsPaneOpen;
            LyricPage.SetFullScreenMode(true);

            parent.Children.Remove(LyricPage);
            return LyricPage;
        }

        /// <summary>
        /// Возвращает <see cref="LyricsPage"/> обратно в MainView и восстанавливает оформление.
        /// </summary>
        public void AttachLyricsPageBack(LyricsPage page)
        {
            if (page == null)
                return;

            page.SetFullScreenMode(false);
            page.ClearOpacityOverride();

            var parent = _lyricsPageHomeParent ?? (Panel)LyricPage?.Parent;
            if (parent != null && page.Parent != parent)
            {
                if (page.Parent is Panel oldParent)
                    oldParent.Children.Remove(page);

                Grid.SetRow(page, _lyricsPageHomeRow);
                parent.Children.Add(page);
            }

            // Возвращаемся к обычному режиму MainView.
            if (_lyricsToolbarWasCollapsed)
            {
                // Панель была закрыта до входа в fullscreen — оставляем её свёрнутой и без синхронизации.
                page.Disable();
                page.Visibility = Visibility.Collapsed;
                page.Opacity = 0;
                lyrClosed = true;
            }
            else
            {
                // Панель была открыта — продолжаем синхронизацию в MainView.
                page.Visibility = Visibility.Visible;
                page.Opacity = 1;
                page.Enable();
            }

            _lyricsPageHomeParent = null;
        }

        #endregion

        internal void setNewPlayingList(IVKGetAudio value)
        {
            if (PlayNowPanelListCLosed)
                return;
            if (value != null)
            {
                var waitParameters = new WaitParameters();
                waitParameters.sectionType = SectionType.CustomIVKGetAudio;
                waitParameters.iVKGetAudio = VK_UI3.Services.MediaPlayerService.iVKGetAudio;

                this.DispatcherQueue.TryEnqueue(async () =>
                {
                    SectionViewPageNowPlayingList.Navigate(typeof(WaitView), waitParameters, new DrillInNavigationTransitionInfo());

                });

            }
        }

        /// <summary>
        /// Навигация по пунктам меню. Без Ctrl — активируется существующая вкладка
        /// с этим разделом (или раздел открывается в активной вкладке),
        /// с Ctrl — всегда создаётся новая вкладка.
        /// </summary>
        private void navigateInvoke()
        {
            var invokedItem = NavWiv.SelectedItem as NavigationViewItem;

            if (invokedItem == null)
            {
                NavWiv.SelectedItem = NavWiv.MenuItems[0];
                invokedItem = NavWiv.SelectedItem as NavigationViewItem;
            }

            if (invokedItem == null || invokedItem.Content == null)
                return;

            navToAnotherPage = true;

            switch (invokedItem.Content.ToString().ToLower())
            {
                case "моя музыка":
                    OpenMyPage(SectionType.MyListAudio);
                    break;

                case "мои плейлисты":
                    OpenPlayListLists(openedPlayList: OpenedPlayList.UserPlayList);
                    break;

                case "для вас":
                    OpenPlayList(-21, AccountsDB.activeAccount.id);
                    break;

                case "плейлисты":
                    OpenPlayListLists(openedPlayList: OpenedPlayList.UserAlbums);
                    break;

                case "параметры":
                    OpenSettingsTab();
                    break;

                case "музыка друзей":
                    OpenMyPage(SectionType.LoadFriends);
                    break;

                case "вложения":
                    OpenMyPage(SectionType.ConversDialogs);
                    break;

                case "запросить повторно":
                    CreateNavigation();
                    return;

                case "что слушают":
                    OpenWhatListeningTab();
                    break;

                default:
                    // Разделы, загружаемые из сети (каталоги ВК): передаём в вкладку
                    // название и иконку из пункта меню.
                    var Item = NavWiv.SelectedItem as NavMenuController;
                    if (Item?.navSettings == null)
                        return;

                    var sectionId = Item.navSettings.section?.Id ?? Item.Content?.ToString();
                    if (string.IsNullOrWhiteSpace(sectionId))
                        return;

                    OpenSection(
                        sectionId,
                        SectionType.None,
                        Item.navSettings.MyMusicItem,
                        Item.navSettings.Icon);
                    break;
            }

            var active = ActiveTab?.ContentFrame;
            active?.ClearBackStack();
            MainWindow.mainWindow.backBTNHide();
        }

        /// <summary>Открывает настройки отдельной вкладкой (или активирует её).</summary>
        private void OpenSettingsTab()
        {
            hideSearch();

            if (!IsForceNewTab())
            {
                var existing = TabManager.Find(new TabKey(SectionType.None, "settings"));
                if (existing != null)
                {
                    TabManager.Activate(existing);
                    UpdateChromeForActiveTab();
                    return;
                }
            }

            var tab = IsForceNewTab()
                ? TabManager.CreateAndActivate(new TabKey(SectionType.None, "settings"), "Параметры", "\uE713")
                : ActiveTab ?? TabManager.CreateAndActivate(new TabKey(SectionType.None, "settings"), "Параметры", "\uE713");

            tab.Key = new TabKey(SectionType.None, "settings");
            tab.Header = "Параметры";
            tab.Glyph = "\uE713";
            tab.ApplyHeader();

            tab.ContentFrame.Navigate(typeof(Settings.SettingsPage), null, new DrillInNavigationTransitionInfo());
            UpdateChromeForActiveTab();
            UpdateEmptyState();
        }

        /// <summary>Открывает «Что слушают» отдельной вкладкой (или активирует её).</summary>
        private void OpenWhatListeningTab()
        {
            if (!IsForceNewTab())
            {
                var existing = TabManager.Find(new TabKey(SectionType.WhatListening, string.Empty));
                if (existing != null)
                {
                    TabManager.Activate(existing);
                    UpdateChromeForActiveTab();
                    return;
                }
            }

            var tab = IsForceNewTab()
                ? TabManager.CreateAndActivate(new TabKey(SectionType.WhatListening, string.Empty), "Что слушают", "\uE783")
                : ActiveTab ?? TabManager.CreateAndActivate(new TabKey(SectionType.WhatListening, string.Empty), "Что слушают", "\uE783");

            tab.Key = new TabKey(SectionType.WhatListening, string.Empty);
            tab.Header = "Что слушают";
            tab.Glyph = "\uE783";
            tab.ApplyHeader();

            tab.ContentFrame.Navigate(typeof(WhatListeningPage), _whatListeningData, new DrillInNavigationTransitionInfo());
            UpdateChromeForActiveTab();
            UpdateEmptyState();
        }

        #region Tab plumbing

        /// <summary>Подписывается на события активной вкладки (кнопка «Назад», поиск).</summary>
        private void AttachActiveTab()
        {
            DetachActiveTab();

            var current = ActiveTab;
            UpdateEmptyState();
            if (current == null)
                return;

            current.ContentFrame.Navigated -= ContentFrame_Navigated;
            current.ContentFrame.Navigated += ContentFrame_Navigated;
        }

        private void DetachActiveTab()
        {
            var current = ActiveTab;
            if (current == null)
                return;

            current.ContentFrame.Navigated -= ContentFrame_Navigated;
        }

        private void ContentFrame_Navigated(object sender, object e)
        {
            UpdateChromeForActiveTab();
        }

        /// <summary>Закрытие вкладки по кнопке закрытия или Ctrl+F4.</summary>
        private void TabManager_CloseRequested(object sender, TabEntry entry)
        {
            if (entry == null || TabManager == null)
                return;

            TabManager.Close(entry);

            AttachActiveTab();
            UpdateChromeForActiveTab();
            SyncMenuSelection();
            SaveTabs();
            UpdateEmptyState();
        }

        private void TabManager_ActiveTabChanged(object sender, TabEntry e)
        {
            AttachActiveTab();
            UpdateChromeForActiveTab();
            SyncMenuSelection();

            if (e != null && e.NeedsRestore)
                RestoreTabContent(e);

            SaveTabs();
        }

        private void TabManager_TabCreated(object sender, TabEntry e)
        {
            if (e?.ContentFrame == null)
                return;

            e.ContentFrame.Navigated += (s, _) =>
            {
                if (ReferenceEquals(e, ActiveTab))
                    UpdateChromeForActiveTab();
            };
        }

        #region Tabs persistence

        private const string TabsSettingKey = "openedTabs";

        /// <summary>Сохраняет вкладки перед закрытием приложения (вызывается из MainWindow).</summary>
        public void SaveTabsForExit() => SaveTabs();

        /// <summary>Сериализует открытые вкладки, чтобы восстановить их при следующем запуске.</summary>
        private void SaveTabs()
        {
            try
            {
                if (TabManager == null)
                    return;

                var saved = TabManager.Tabs
                    .Where(t => t.Key != null)
                    .Select(t => new SavedTab
                    {
                        Type = (int)t.Key.Type,
                        Id = t.Key.Id,
                        Header = t.Header,
                        Glyph = t.Glyph,
                        IsClosable = t.IsClosable,
                        IsActive = ReferenceEquals(t, ActiveTab)
                    })
                    .ToList();

                var json = System.Text.Json.JsonSerializer.Serialize(saved);
                SettingsTable.SetSetting(TabsSettingKey, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainView] SaveTabs failed: {ex.Message}");
            }
        }

        /// <summary>Восстанавливает сохранённые вкладки (лениво: контент грузится при активации).</summary>
        private void TryRestoreTabs()
        {
            try
            {
                var setting = SettingsTable.GetSetting(TabsSettingKey);
                if (setting == null || string.IsNullOrWhiteSpace(setting.settingValue))
                    return;

                var saved = System.Text.Json.JsonSerializer.Deserialize<List<SavedTab>>(setting.settingValue);
                if (saved == null || saved.Count == 0)
                    return;

                // Первая вкладка уже создана как домашняя (если пользователь не закрыл все вкладки).
                var home = TabManager.Tabs.FirstOrDefault();
                TabEntry activeToRestore = null;

                if (saved.Count == 1 && home != null && home.ContentFrame.Children.Count == 0)
                    return;

                foreach (var item in saved)
                {
                    var type = (SectionType)item.Type;
                    if (home != null && home.Key != null && home.Key.Equals(new TabKey(type, item.Id)))
                        continue;

                    var key = new TabKey(type, item.Id);
                    if (TabManager.Find(key) != null)
                        continue;

                    var tab = TabManager.Create(key, item.Header ?? "Раздел", item.Glyph, item.IsClosable);
                    tab.NeedsRestore = true;
                    if (item.IsActive)
                        activeToRestore = tab;
                }

                if (activeToRestore != null)
                    TabManager.Activate(activeToRestore);

                // Активация восстановленной вкладки подгружает её содержимое.
                if (activeToRestore != null)
                    RestoreTabContent(activeToRestore);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainView] TryRestoreTabs failed: {ex.Message}");
            }
        }

        /// <summary>Подгружает содержимое восстановленной вкладки по её ключу.</summary>
        private void RestoreTabContent(TabEntry tab)
        {
            if (tab?.Key == null || !tab.NeedsRestore)
                return;

            switch (tab.Key.Type)
            {
                case SectionType.PlayList when tab.Key.Id.Contains('_'):
                    var parts = tab.Key.Id.Split('_');
                    if (parts.Length >= 2 &&
                        long.TryParse(parts[0], out var ownerId) &&
                        long.TryParse(parts[1], out var albumId))
                    {
                        var accessKey = parts.Length >= 3 && !string.IsNullOrEmpty(parts[2]) ? parts[2] : null;
                        OpenPlayListInTab(tab, albumId, ownerId, accessKey);
                    }
                    break;

                case SectionType.Search:
                    var parameters = new WaitParameters
                    {
                        sectionType = SectionType.Search,
                        SectionID = "search",
                        searchText = tab.Key.Id
                    };
                    tab.NeedsRestore = false;
                    tab.ContentFrame.Navigate(typeof(WaitView), parameters, new DrillInNavigationTransitionInfo());
                    break;

                default:
                    var wait = new WaitParameters { sectionType = tab.Key.Type, SectionID = tab.Key.Id };
                    tab.NeedsRestore = false;
                    tab.ContentFrame.Navigate(typeof(WaitView), wait, new DrillInNavigationTransitionInfo());
                    break;
            }
        }

        /// <summary>Открывает плейлист в конкретной (восстановленной) вкладке.</summary>
        private static void OpenPlayListInTab(TabEntry tab, long albumId, long ownerId, string accessKey)
        {
            var sectionView = new WaitParameters { sectionType = SectionType.PlayList };
            var playlist = new MusicX.Core.Models.Playlist { Id = albumId, OwnerId = ownerId };
            if (!string.IsNullOrEmpty(accessKey))
                playlist.AccessKey = accessKey;

            sectionView.iVKGetAudio = new PlayListVK(playlist, dispatcherQueue);
            tab.NeedsRestore = false;
            tab.ContentFrame.Navigate(typeof(WaitView), sectionView, new DrillInNavigationTransitionInfo());
        }

        private sealed class SavedTab
        {
            public int Type { get; set; }
            public string Id { get; set; }
            public string Header { get; set; }
            public string Glyph { get; set; }
            public bool IsClosable { get; set; }
            public bool IsActive { get; set; }
        }

        #endregion

        private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TabManager == null)
                return;

            AttachActiveTab();
            UpdateChromeForActiveTab();
            SyncMenuSelection();
        }

        private void Tabs_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
        {
            var entry = TabManager?.FromItem(args.Tab);
            if (entry == null)
                return;

            TabManager.Close(entry);

            AttachActiveTab();
            UpdateChromeForActiveTab();
            SyncMenuSelection();
            SaveTabs();
            UpdateEmptyState();
        }

        /// <summary>
        /// Кнопка «+» в ленте вкладок: открывает «Мою музыку» пользователя
        /// в новой вкладке.
        /// </summary>
        private void TabManager_AddTabRequested(object sender, EventArgs e)
        {
            var tab = TabManager.CreateAndActivate(
                new TabKey(SectionType.MyListAudio, $"new-{Guid.NewGuid()}"),
                "Моя музыка",
                "\uEC4F");

            var parameters = new WaitParameters { sectionType = SectionType.MyListAudio };
            tab.NavigationParameter = parameters;
            tab.ContentFrame.Navigate(typeof(WaitView), parameters, new DrillInNavigationTransitionInfo());

            UpdateChromeForActiveTab();
            UpdateEmptyState();
            SaveTabs();
        }

        /// <summary>Показывает заглушку, когда открытых вкладок нет.</summary>
        private void UpdateEmptyState()
        {
            if (EmptyState == null)
                return;

            var isEmpty = TabManager == null || TabManager.Tabs.Count == 0;
            EmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;

            UpdateTabStripVisibility();
        }

        #region Tab strip collapse (one tab)

        /// <summary>
        /// Настройки автоскрытия ленты вкладок при одной вкладке.
        /// <see cref="HiddenOffset"/> — на сколько уезжает вверх (по Y) контейнер вкладок,
        /// чтобы лента полностью ушла из вида; <see cref="AnimationDuration"/> — длительность.
        /// </summary>
        private static class TabStripCollapse
        {
            public const double HiddenOffset = 42;
            public static readonly TimeSpan AnimationDuration = TimeSpan.FromSeconds(0.25);
        }

        private CancellationTokenSource _tabStripCts;
        private bool _tabStripCollapsed;

        /// <summary>
        /// Скрывает ленту вкладок, когда открыта ровно одна вкладка: верхний Margin
        /// контейнера уводится в минус и контент поднимается на место ленты.
        /// </summary>
        private void UpdateTabStripVisibility()
        {
            if (TabsHost == null)
                return;

            var shouldCollapse = TabManager != null && TabManager.Tabs.Count == 1;
            if (shouldCollapse == _tabStripCollapsed)
                return;

            _tabStripCollapsed = shouldCollapse;

            var target = shouldCollapse
                ? new Thickness(0, -TabStripCollapse.HiddenOffset, 0, 0)
                : new Thickness(0);

            _ = AnimateTabsHostMargin(target, TabStripCollapse.AnimationDuration);
        }

        /// <summary>
        /// Анимация Margin контейнера вкладок — по тому же принципу, что и
        /// <c>AnimatedButton.AnimateMargin</c> (интерполяция Thickness по шагам).
        /// </summary>
        private async Task AnimateTabsHostMargin(Thickness newMargin, TimeSpan duration)
        {
            if (TabsHost == null)
                return;

            if (TabsHost.Margin == newMargin)
                return;

            _tabStripCts?.Cancel();
            _tabStripCts = new CancellationTokenSource();
            var token = _tabStripCts.Token;

            var current = TabsHost.Margin;
            double leftStep = (newMargin.Left - current.Left) / duration.TotalMilliseconds;
            double topStep = (newMargin.Top - current.Top) / duration.TotalMilliseconds;
            double rightStep = (newMargin.Right - current.Right) / duration.TotalMilliseconds;
            double bottomStep = (newMargin.Bottom - current.Bottom) / duration.TotalMilliseconds;

            var left = current.Left;
            var top = current.Top;
            var right = current.Right;
            var bottom = current.Bottom;

            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.Elapsed < duration)
            {
                if (token.IsCancellationRequested)
                    return;

                double elapsed = stopwatch.Elapsed.TotalMilliseconds;
                TabsHost.Margin = new Thickness(
                    left + leftStep * elapsed,
                    top + topStep * elapsed,
                    right + rightStep * elapsed,
                    bottom + bottomStep * elapsed);

                await Task.Delay(1);
            }

            TabsHost.Margin = newMargin;
            stopwatch.Stop();
        }

        #endregion

        /// <summary>Кнопка в заглушке: открывает «Мою музыку» в новой вкладке.</summary>
        private void OpenHomeTab_Click(object sender, RoutedEventArgs e)
        {
            OpenMyPage(SectionType.MyListAudio);
            UpdateEmptyState();
        }

        /// <summary>Выделяет в меню пункт, соответствующий активной вкладке.</summary>
        private void SyncMenuSelection()
        {
            var tab = ActiveTab;
            if (tab?.Key == null)
                return;

            try
            {
                foreach (var menuItem in NavWiv.MenuItems)
                {
                    if (menuItem is NavigationViewItem item && item.Content is string content)
                    {
                        var key = content.ToLower();
                        var matches = (tab.Key.Type, key) switch
                        {
                            (SectionType.MyListAudio, "моя музыка") => true,
                            (SectionType.UserPlayListList, "мои плейлисты") => true,
                            (SectionType.UserPlayListList, "плейлисты") => true,
                            (SectionType.LoadFriends, "музыка друзей") => true,
                            (SectionType.ConversDialogs, "вложения") => true,
                            (SectionType.WhatListening, "что слушают") => true,
                            (SectionType.Search, "поиск") => true,
                            (SectionType.PlayList, "для вас") => tab.Key.Id == $"{AccountsDB.activeAccount.id}_-21_",
                            _ => false
                        };

                        if (matches)
                        {
                            NavWiv.SelectedItem = item;
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainView] SyncMenuSelection failed: {ex.Message}");
            }
        }

        #endregion

     
    }



    public class NavigationInfo
    {
        public Object SourcePageType { get; set; }
        // Добавьте здесь другие свойства, если это необходимо

    }
}
