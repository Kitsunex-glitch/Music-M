using DiscordRPC;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MusicX.Core.Models;
using System;
using VK_UI3.Helpers.Animations;
using VK_UI3.VKs;
using Windows.System;

namespace VK_UI3.Controls
{
    public sealed partial class MerchControl : UserControl
    {
        private AnimationsChangeText titleAnim;
        private AnimationsChangeText descrAnim;
        private AnimationsChangeImage changeImage;

        public MerchControl()
        {
            this.InitializeComponent();

            titleAnim = new AnimationsChangeText(Title, this.DispatcherQueue);
            descrAnim = new AnimationsChangeText(Description, this.DispatcherQueue);
            changeImage = new AnimationsChangeImage(MerchImage, this.DispatcherQueue);


            this.Unloaded += MerchControl_Unloaded;
            this.Loaded += MerchControl_Loaded;
            DataContextChanged += MerchControl_DataContextChanged;

            flyOutm.Opening += FlyOutm_Opening;
            flyOutm.Closing += FlyOutm_Closing;
        }

        private void MerchControl_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
        {
            if (DataContext is MarketItem marketItem)
            {
                UpdateMarketItemInfo(marketItem);
            }
        }

        private void UpdateMarketItemInfo(MarketItem marketItem)
        {
            titleAnim.ChangeTextWithAnimation(marketItem.Title);
            descrAnim.ChangeTextWithAnimation(marketItem.Description);
            changeImage.ChangeImageWithAnimation(marketItem.ThumbPhoto);

            TextPrice.Text = GetFormattedPrice(marketItem);

            bool Available = marketItem.Availability != 0;

            if (Available)
            {
                // === НЕ ДОСТУПЕН ===
                ActionButton.IsEnabled = false;
                ActionIcon.Glyph = "\uE8A8"; // Замок

                // Визуальное затемнение изображения
                OverlayGrid.Opacity = 0.7;   // Делаем маску заметной
                BadgeText.Opacity = 1;       // Показываем текст "НЕ ДОСТУПЕН"

                // Меняем фон блока цены на красный (или любой другой яркий цвет)
                gridPrice.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.DarkRed);
                TextPrice.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
                TextPrice.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
            }
            else
            {
                // === ДОСТУПЕН ===
                ActionButton.IsEnabled = true;
                ActionIcon.Glyph = "\uE8A7"; // Корзина/покупка

                // Убираем затемнение
                OverlayGrid.Opacity = 0;
                BadgeText.Opacity = 0;
                BookmarkGrid.Opacity = 0;

                // Возвращаем стандартный стиль
                gridPrice.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                if (Application.Current.Resources.TryGetValue("AcrylicBackgroundFillColorDefaultBrush", out var acrylicBrush))
                {
                    gridPrice.Background = acrylicBrush as Microsoft.UI.Xaml.Media.Brush;
                }
                TextPrice.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White); // Или стандартный цвет текста
                TextPrice.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
            }
            // Обновляем иконку закладки
            UpdateBookmarkIcon(marketItem.IsFavorite);
        }

        private string GetFormattedPrice(MarketItem marketItem)
        {
            if (marketItem?.Price?.Text != null)
                return marketItem.Price.Text;

            if (marketItem?.Price?.Amount != null && marketItem.Price.Currency != null)
            {
                if (double.TryParse(marketItem.Price.Amount, out var amount))
                {
                    var priceInRubles = amount / 100;
                    return $"{priceInRubles:F2} {marketItem.Price.Currency.Title}";
                }
            }

            return "Цена не указана";
        }

        private void MerchControl_Loaded(object sender, RoutedEventArgs e)
        {
        }

        private void MerchControl_Unloaded(object sender, RoutedEventArgs e)
        {
            this.Unloaded -= MerchControl_Unloaded;
            this.Loaded -= MerchControl_Loaded;
            DataContextChanged -= MerchControl_DataContextChanged;
            flyOutm.Opening -= FlyOutm_Opening;
            flyOutm.Closing -= FlyOutm_Closing;
        }

        private void FlyOutm_Closing(Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase sender, Microsoft.UI.Xaml.Controls.Primitives.FlyoutBaseClosingEventArgs args)
        {
        }

        private void FlyOutm_Opening(object sender, object e)
        {
        }

        private void UserControl_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            FadeInAnimationGridPlayIcon.Begin();
        }

        private void UserControl_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            FadeOutAnimationGridPlayIcon.Begin();
        }

        private async void UserControl_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (DataContext is MarketItem marketItem)
            {
                await OpenMarketItemAsync(marketItem);
            }
        }

        private async void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MarketItem marketItem)
            {
                await OpenMarketItemAsync(marketItem);
            }
        }

        private async void ShareItem_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MarketItem marketItem)
            {
                await ShareMarketItemAsync(marketItem);
            }
        }

        private async void OpenInBrowser_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MarketItem marketItem)
            {
                await OpenMarketItemAsync(marketItem);
            }
        }

        private async void BookmarkButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MarketItem marketItem)
            {
                await ToggleBookmarkAsync(marketItem);
            }
        }

        private static async System.Threading.Tasks.Task OpenMarketItemAsync(MarketItem marketItem)
        {
            try
            {
                var url = marketItem.MarketUrl ?? marketItem.Url;
                if (!string.IsNullOrEmpty(url))
                {
                    var uri = new Uri(url);
                    await Launcher.LaunchUriAsync(uri);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening market item: {ex.Message}");
            }
        }

        private static async System.Threading.Tasks.Task ShareMarketItemAsync(MarketItem marketItem)
        {
            try
            {
                var shareText = $"{marketItem.Title}\n{marketItem.Price?.Text}\n{marketItem.MarketUrl ?? marketItem.Url}";

                var dataTransferManager = Windows.ApplicationModel.DataTransfer.DataTransferManager.GetForCurrentView();
                dataTransferManager.DataRequested += (sender, args) =>
                {
                    args.Request.Data.SetText(shareText);
                    args.Request.Data.Properties.Title = marketItem.Title;
                    args.Request.Data.Properties.Description = marketItem.Description;
                };

                Windows.ApplicationModel.DataTransfer.DataTransferManager.ShowShareUI();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error sharing market item: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task ToggleBookmarkAsync(MarketItem marketItem)
        {
            try
            {
                var parameters = new VkNet.Utils.VkParameters
                {
                    { "owner_id", marketItem.OwnerId },
                    { "id", marketItem.Id }
                };
                // Определяем, добавляем или удаляем
                if (marketItem.IsFavorite)
                {
                    // Удаляем из закладок
                    var response = await VK.api.CallAsync("fave.removeProduct", parameters);
                    marketItem.IsFavorite = false;
                    System.Diagnostics.Debug.WriteLine($"Bookmark removed for market item {marketItem.Id}");
                }
                else
                {
                    // Добавляем в закладки
                    var response = await VK.api.CallAsync("fave.addProduct", parameters);
                    marketItem.IsFavorite = true;
                    System.Diagnostics.Debug.WriteLine($"Bookmark added for market item {marketItem.Id}");
                }
                // Обновляем иконку
                UpdateBookmarkIcon(marketItem.IsFavorite);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error toggling bookmark: {ex.Message}");
            }
        }

        private void UpdateBookmarkIcon(bool isFavorite)
        {
            if (BookMarkIcon != null)
            {
                // Выбираем путь в зависимости от состояния
                string path = isFavorite
                    ? "ms-appx:///Assets/SVGs/bookmark-black-shape.svg"
                    : "ms-appx:///Assets/SVGs/bookmark-white.svg";



                BookMarkIcon.UriSource = new Uri(path);
            }
        }

    }
}