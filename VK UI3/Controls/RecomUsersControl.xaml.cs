using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MusicX.Core.Models;
using System;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using VK_UI3.Helpers;
using VK_UI3.Views;
using VK_UI3.VKs.IVK;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace VK_UI3.Controls
{
    public sealed partial class RecomUsersControl : UserControl
    {
        public static Windows.UI.Color GetColorFromHex(string hexaColor)
        {
            //get the color as System.Drawing.Color
            var clr = (System.Drawing.Color)new ColorConverter().ConvertFromString(hexaColor);

            //convert it to Windows.UI.Color
            return Windows.UI.Color.FromArgb(clr.A, clr.R, clr.G, clr.B);
        }
        public RecomUsersControl()
        {
            this.InitializeComponent();

            // Unloaded наступает и при переключении вкладок — подписку ставим в Loaded,
            // иначе после возврата на вкладку карточка перестанет обновляться.
            this.Loaded += RecomUsersControl_Loaded;
            this.Unloaded += RecomUsersControl_Unloaded;
        }

        private void RecomUsersControl_Loaded(object sender, RoutedEventArgs e)
        {
            this.DataContextChanged -= RecomUsersControl_DataContextChanged;
            this.DataContextChanged += RecomUsersControl_DataContextChanged;

            animationsChangeImage ??= new Helpers.Animations.AnimationsChangeImage(AvatarImage, this.DispatcherQueue);

            // При возврате на вкладку DataContext не меняется — применяем вручную.
            ApplyDataContext(force: false);
        }

        private void RecomUsersControl_Unloaded(object sender, RoutedEventArgs e)
        {
            this.DataContextChanged -= RecomUsersControl_DataContextChanged;
        }

        RecommendedPlaylist _PlayList;
        private void RecomUsersControl_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
        {
            ApplyDataContext(force: true);
        }

        /// <summary>
        /// Применяет DataContext. force=false — только если контент ещё не отрисован
        /// (повторная загрузка после возврата на вкладку).
        /// </summary>
        private void ApplyDataContext(bool force)
        {
            var pl = DataContext as RecommendedPlaylist;
            if (pl == null)
            {
                // Контейнер переиспользован под другой тип — сбрасываем состояние.
                _PlayList = null;
                photo = null;
                return;
            }

            // Повторный Loaded с тем же DataContext и уже отрисованным контентом — не перерисовываем.
            if (!force && _PlayList != null && pl.Id == _PlayList.Id && PercentsTXT.Text?.Length > 0)
                return;

            _PlayList = pl;
            // Сбрасываем кеш фото, чтобы аватар перерисовался в переиспользованном контейнере.
            photo = null;

            animationsChangeImage ??= new Helpers.Animations.AnimationsChangeImage(AvatarImage, this.DispatcherQueue);

            update();
        }
        Helpers.Animations.AnimationsChangeImage animationsChangeImage;
        PlayListVK playListVK = null;
        Uri photo;
        ObservableCollection<ExtendedAudio> extendedAudios = new ObservableCollection<ExtendedAudio>();
        private void update()
        {
            if (_PlayList?.Playlist == null)
                return;

            playListVK = new PlayListVK(_PlayList, DispatcherQueue);

            if (!string.IsNullOrWhiteSpace(_PlayList.Percentage) &&
                double.TryParse(_PlayList.Percentage.Replace(".", ","), out var percentage))
            {
                PercentsTXT.Text = Math.Round(percentage * 100) + "%";
            }

            PercentsDescrTXT.Text = _PlayList.PercentageTitle;
            PlayListNameTXT.Text = _PlayList.Playlist.Title ?? string.Empty;

            // Цвет градиента приходит с сервера и может быть пустым/невалидным.
            if (!string.IsNullOrWhiteSpace(_PlayList.Color))
            {
                try
                {
                    var color = GetColorFromHex(_PlayList.Color);
                    var transparent = color;
                    transparent.A = 50;

                    gradStart.Color = color;
                    gradStop.Color = transparent;
                }
                catch
                {
                    // Некорректный цвет — оставляем предыдущий градиент.
                }
            }


            // User.Photo — геттер, возвращающий любое доступное фото профиля.
            Uri ph = _PlayList.Playlist.userOwner?.Photo
                     ?? _PlayList.Playlist.groupOwner?.Photo100;
            if (ph != photo)
            {
                AvatarImage.Opacity = 0;
                photo = ph;
            }

            // Аватар загружаем только при наличии URL, иначе Image остаётся пустым.
            if (ph != null)
                animationsChangeImage.ChangeImageWithAnimation(ph);


            UserNameTXT.Text = _PlayList.Playlist.OwnerName;
            if (playListVK.listAudio != null && !playListVK.listAudio.SequenceEqual(extendedAudios))
            {
                extendedAudios.Clear();
                foreach (var item in playListVK.listAudio)
                {
                    extendedAudios.Add(item);
                }
            }

        }

        private void StackPanel_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_PlayList?.Playlist == null)
                return;

            if (e.GetCurrentPoint(sender as UIElement).Properties.IsLeftButtonPressed)
            {
                MainView.OpenPlayList(_PlayList.Playlist);
            }
        }

        private void StackPanel_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            HideAnimation.Pause();
            ShowAnimation.Begin();
        }

        private void StackPanel_PointerExited(object sender, PointerRoutedEventArgs e)
        {

            ShowAnimation.Pause();
            HideAnimation.Begin();
        }
    }
}
