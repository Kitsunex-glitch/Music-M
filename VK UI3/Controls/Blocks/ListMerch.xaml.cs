using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MusicX.Core.Models;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using VK_UI3.VKs;
using VK_UI3.VKs.IVK;

namespace VK_UI3.Controls.Blocks
{
    public sealed partial class ListMerch : UserControl, IBlockAdder
    {
        ObservableCollection<MarketItem> merch = new();

        public ListMerch()
        {
            this.InitializeComponent();

            this.Loading += ListMerch_Loading;
            this.Loaded += ListMerch_Loaded;
            this.Unloaded += ListMerch_Unloaded;
        }

        private void ListMerch_Loaded(object sender, RoutedEventArgs e)
        {
            if ((bool) localBlock?.Layout?.Name?.Contains("list"))
            {
                myControl.scrollVi.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                myControl.scrollVi.HorizontalScrollMode = ScrollMode.Disabled;
                myControl.scrollVi.IsScrollInertiaEnabled = false;
                myControl.scrollVi.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                myControl.scrollVi.VerticalScrollMode = ScrollMode.Disabled;
            }

            if (myControl.CheckIfAllContentIsVisible())
                load();
        }

        private void ListMerch_Unloaded(object sender, RoutedEventArgs e)
        {
            this.Unloaded -= ListMerch_Unloaded;
            myControl.loadMore = null;
        }

        private SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);

        public bool itsAll => localBlock?.NextFrom == null;

        private async void load()
        {
            await semaphore.WaitAsync();
            try
            {
                if (localBlock?.NextFrom == null) return;

                var a = await VK.vkService.GetSectionAsync(localBlock.Id, localBlock.NextFrom);
                if (a?.Section == null) return;

                localBlock.NextFrom = a.Section.NextFrom;

                // Ищем блок с мерчем в полученной секции
                Block merchBlock = null;
                foreach (var block in a.Section.Blocks)
                {
                    if (block.MarketItems != null && block.MarketItems.Count > 0)
                    {
                        merchBlock = block;
                        break;
                    }
                }

                if (merchBlock == null || merchBlock.MarketItems == null) return;

                this.DispatcherQueue.TryEnqueue(() =>
                {
                    foreach (var item in merchBlock.MarketItems)
                    {
                        merch.Add(item);
                    }

                    if (myControl.CheckIfAllContentIsVisible())
                        load();
                });
            }
            finally
            {
                semaphore.Release();
            }
        }

        private Block localBlock;

        private void ListMerch_Loading(FrameworkElement sender, object args)
        {
            try
            {
                if (DataContext is not Block block)
                    return;

                merch.Clear();
                localBlock = block;

                switch (localBlock.Layout?.Name)
                {
                    case "list":
                        myControl.disableLoadMode = true;
                        break;
                    default:
                        myControl.loadMore = load;
                        myControl.ItemsPanelTemplate = (ItemsPanelTemplate)Microsoft.UI.Xaml.Application.Current.Resources["GlobalItemsPanelTemplate"];
                        break;
                }

                myControl.ItemTemplate = this.Resources["default"] as DataTemplate;

                if (block.MarketItems != null)
                {
                    foreach (var item in block.MarketItems)
                    {
                        merch.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ListMerch_Loading: {ex.Message}");
            }
        }

        public void AddBlock(Block block)
        {
            if (localBlock != null)
                localBlock.NextFrom = block.NextFrom;

            this.DispatcherQueue.TryEnqueue(() =>
            {
                if (block.MarketItems != null)
                {
                    foreach (var item in block.MarketItems)
                    {
                        merch.Add(item);
                    }
                }
            });
        }
    }
}