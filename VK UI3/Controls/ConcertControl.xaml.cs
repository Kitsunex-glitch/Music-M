using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MusicX.Core.Models;
using MusicX.Core.Models.General;
using System;
using System.Globalization;
using System.Linq;
using VK_UI3.Helpers.Animations;

namespace VK_UI3.Controls
{
    public sealed partial class ConcertControl : UserControl
    {
        private Concert _concert;
        private AnimationsChangeImage _animationsChangeImage;

        public ConcertControl()
        {
            this.InitializeComponent();
            this.DataContextChanged += ConcertControl_DataContextChanged;
            _animationsChangeImage = new AnimationsChangeImage(ConcertImage, this.DispatcherQueue);
        }

        private void ConcertControl_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
        {
            if (DataContext is Concert concert)
            {
                _concert = concert;
                UpdateUI();
            }
        }

        private void UpdateUI()
        {
            if (_concert == null) return;

            // Заголовок
            TitleText.Text = _concert.ConcertData?.Title ?? "Концерт";

            // Место
            PlaceText.Text = _concert.ConcertData?.PlaceTitle ?? "";

            // Город и дата
            var city = _concert.ConcertData?.City?.Title;
            var dateStr = _concert.ConcertData?.StartDateTime;
            DateTime? date = null;
            if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                date = parsed;

            CityDateText.Text = $"{(city ?? "Город не указан")} • {(date?.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("ru-RU")) ?? "Дата не указана")}";

            // Цена
            var price = _concert.ConcertData?.MinPrice ?? 0;
            PriceText.Text = price > 0 ? $"от {price} ₽" : "Цена не указана";

            // Кнопка действия
            ActionButton.Content = _concert.PurchaseAction?.Title ?? "Купить";

            // Изображение
            var imageUrl = _concert.ConcertData?.Image?.FirstOrDefault()?.Url;
            if (!string.IsNullOrEmpty(imageUrl))
            {
                _animationsChangeImage.ChangeImageWithAnimation(imageUrl);
            }
            else
            {
                ConcertImage.Source = null;
            }

            // Дата для DateText
            UpdateDateText(date);
        }

        private void UpdateDateText(DateTime? date)
        {
            if (date.HasValue)
            {
                // Формат: день с новой строки и месяц в верхнем регистре (например, "22\nНОЯ")
                string day = date.Value.Day.ToString();
                string month = GetShortMonthName(date.Value.Month);
                DateText.Text = $"{day}\n{month}";
                DateText.Visibility = Visibility.Visible;
            }
            else
            {
                DateText.Visibility = Visibility.Collapsed;
            }
        }

        private static string GetShortMonthName(int month)
        {
            switch (month)
            {
                case 1: return "ЯНВ";
                case 2: return "ФЕВ";
                case 3: return "МАР";
                case 4: return "АПР";
                case 5: return "МАЙ";
                case 6: return "ИЮН";
                case 7: return "ИЮЛ";
                case 8: return "АВГ";
                case 9: return "СЕН";
                case 10: return "ОКТ";
                case 11: return "НОЯ";
                case 12: return "ДЕК";
                default: return "";
            }
        }

        private void UserControl_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            // Показываем оверлей с анимацией
            ShowOverlayAnimation.Begin();
            // Анимация масштабирования при наведении
            HideAnimation.Pause();
            ShowAnimation.Begin();
        }

        private void UserControl_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            // Скрываем оверлей с анимацией
            HideOverlayAnimation.Begin();
            // Возвращаем масштаб
            ShowAnimation.Pause();
            HideAnimation.Begin();
        }

        private void UserControl_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // Анимация нажатия
            PressAnimation.Begin();
        }

        private void UserControl_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            // Анимация отпускания
            ReleaseAnimation.Begin();
            // При клике на карточку открываем страницу концерта
            OpenConcertPage();
        }

        private void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            // Обработка действия покупки
            if (_concert?.PurchaseAction?.Action?.Url != null)
            {
                // Открываем URL в браузере
                OpenUrl(_concert.PurchaseAction.Action.Url);
            }
            else if (_concert?.ConcertData?.PageUrl != null)
            {
                OpenUrl(_concert.ConcertData.PageUrl);
            }
        }

        private void OpenConcertPage()
        {
            if (_concert?.ConcertData?.PageUrl != null)
            {
                OpenUrl(_concert.ConcertData.PageUrl);
            }
        }

        private void OpenUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                Windows.System.Launcher.LaunchUriAsync(uri).AsTask();
            }
            catch { }
        }
    }
}