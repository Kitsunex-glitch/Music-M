using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using MusicX.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VK_UI3.Helpers.Animations;
using VK_UI3.VKs.IVK;
using Windows.UI;

namespace VK_UI3.Controls
{
    public sealed partial class ArtistMixButton : UserControl
    {
        private MusicX.Core.Models.Button _button;
        private AnimationsChangeText _titleAnim;
        private AnimationsChangeText _descriptionAnim;
        private AnimationsChangeFontIcon _animationsChangeFontIcon;

        public ArtistMixButton()
        {
            this.InitializeComponent();
            this.Loaded += ArtistMixButton_Loaded;
            this.Unloaded += ArtistMixButton_Unloaded;
            this.DataContextChanged += ArtistMixButton_DataContextChanged;
        }

        private void ArtistMixButton_Loaded(object sender, RoutedEventArgs e)
        {
            // Unloaded наступает и при переключении вкладок — подписываемся в Loaded,
            // иначе после возврата на вкладку картинки перестанут обновляться.
            this.DataContextChanged -= ArtistMixButton_DataContextChanged;
            this.DataContextChanged += ArtistMixButton_DataContextChanged;

            // Инициализация анимаций (пересоздаём после выгрузки — они привязаны к элементам).
            _titleAnim = new AnimationsChangeText(TitleText, this.DispatcherQueue);
            _descriptionAnim = new AnimationsChangeText(DescriptionText, this.DispatcherQueue);
            _animationsChangeFontIcon = new AnimationsChangeFontIcon(PlayPause, this.DispatcherQueue);

            // DataContext не менялся при возврате на вкладку — синхронизируем вручную.
            SyncButtonFromDataContext();
            UpdateDisplay();
        }

        private void ArtistMixButton_Unloaded(object sender, RoutedEventArgs e)
        {
            // Только отписка от DataContext: остальное восстановит Loaded.
            this.DataContextChanged -= ArtistMixButton_DataContextChanged;
        }

        private void ArtistMixButton_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
        {
            SyncButtonFromDataContext();
            UpdateDisplay();
        }

        /// <summary>
        /// Берёт модель кнопки из DataContext. Если DataContext не того типа — сбрасываем,
        /// чтобы не показывать данные предыдущего (переиспользованного) контейнера.
        /// </summary>
        private void SyncButtonFromDataContext()
        {
            _button = DataContext as MusicX.Core.Models.Button;
        }

        public MusicX.Core.Models.Button Button
        {
            get => _button;
            set
            {
                _button = value;
                UpdateDisplay();
            }
        }

        private void UpdateDisplay()
        {
            if (TitleText == null)
                return;

            // Загрузка изображений
            LoadImages();

            if (_button == null)
            {
                TitleText.Text = string.Empty;
                DescriptionText.Text = string.Empty;
                return;
            }

            // Обновление текстов
            TitleText.Text = _button.Title ?? string.Empty;
            DescriptionText.Text = _button.Description ?? string.Empty;
        }

        private void LoadImages()
        {
            if (fadeOp == null || GridThumbs == null || GridThumbsForeground == null)
                return;

            if (_button?.Images == null || _button.Images.Count == 0)
            {
                // Скрыть GridThumbs, показать placeholder
                fadeOp.Opacity = 0;
                GridThumbs.Children.Clear();
                GridThumbsForeground.Children.Clear();
                return;
            }

            // Показать GridThumbs
            fadeOp.Opacity = 1;

            // Очистка предыдущих изображений
            GridThumbs.Children.Clear();
            GridThumbsForeground.Children.Clear();

            // Загружаем изображения через вспомогательный метод
            foreach (var item in _button.Images)
            {
                GridThumbs.AddImagesToGrid(item.Url);
            }

            // ForegroundImages может отсутствовать у некоторых кнопок — иначе NRE.
            if (_button.ForegroundImages != null)
            {
                foreach (var item in _button.ForegroundImages)
                {
                    if (!string.IsNullOrEmpty(item?.Url))
                        GridThumbsForeground.AddImagesToGrid(item.Url);
                }
            }
        }

        private void UserControl_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            // Анимация появления кнопки воспроизведения
            FadeInAnimationGridPlayIcon.Begin();
        }

        private void UserControl_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            // Анимация исчезновения кнопки воспроизведения
            FadeOutAnimationGridPlayIcon.Begin();
        }

        private void UserControl_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // Запуск микса при нажатии
            PlayMix();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            PlayMix();
        }

        private void PlayMix()
        {
            if (_button == null) return;

            // Используем MixId для запуска микса
            string mixId = _button.MixId;
            if (string.IsNullOrEmpty(mixId))
            {
                // Если MixId отсутствует, используем Id или EntityId
                mixId = _button.Id.ToString();
            }


            // 2. Исправляем создание MixOptions
            var mixOptions = new VK_UI3.VKs.IVK.MixOptions(mixId, EntityId: _button.EntityId.ToString());
            new MixAudio(mixOptions, this.DispatcherQueue);
        }
    }
}