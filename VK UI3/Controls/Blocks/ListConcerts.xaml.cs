using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MusicX.Core.Models;
using MusicX.Core.Models.General;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using VK_UI3.VKs;
using VK_UI3.VKs.IVK;

namespace VK_UI3.Controls.Blocks
{
    public sealed partial class ListConcerts : UserControl, IBlockAdder
    {
        ObservableCollection<Concert> concerts = new();
        private SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);
        private Block localBlock;

        public ListConcerts()
        {
            this.InitializeComponent();
            this.Loading += ListConcerts_Loading;
            this.Loaded += ListConcerts_Loaded;
            this.Unloaded += ListConcerts_Unloaded;
        }

        private void ListConcerts_Loaded(object sender, RoutedEventArgs e)
        {
            if (myControl.CheckIfAllContentIsVisible())
                load();
        }

        private void ListConcerts_Unloaded(object sender, RoutedEventArgs e)
        {
            this.Unloaded -= ListConcerts_Unloaded;
            myControl.loadMore = null;
        }

        public bool itsAll
        {
            get
            {
                if (localBlock == null) return true;
                if (localBlock.NextFrom == null) return true; else return false;
            }
        }

        private async void load()
        {
            await semaphore.WaitAsync();
            try
            {
                if (localBlock.NextFrom == null) return;
                var a = await VK.vkService.GetSectionAsync(localBlock.Id, localBlock.NextFrom);
                localBlock.NextFrom = a.Section.NextFrom;
                if (a.Concerts == null) return;
                this.DispatcherQueue.TryEnqueue(async () =>
                {
                    foreach (var item in a.Concerts)
                    {
                        concerts.Add(item);
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

        private void ListConcerts_Loading(FrameworkElement sender, object args)
        {
            try
            {
                if (DataContext is not Block block)
                    return;
                concerts.Clear();
                localBlock = block;

                switch (localBlock.Layout.Name)
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

                var conc = (DataContext as Block).Concerts;
                if (conc != null)
                {
                    foreach (var item in conc)
                    {
                        concerts.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                // Логирование ошибки
                System.Diagnostics.Debug.WriteLine($"ListConcerts loading error: {ex.Message}");
            }
        }

        public void AddBlock(Block block)
        {
            if (localBlock != null)
                localBlock.NextFrom = block.NextFrom;
            this.DispatcherQueue.TryEnqueue(async () =>
            {
                if (block.Concerts != null)
                {
                    foreach (var item in block.Concerts)
                    {
                        concerts.Add(item);
                    }
                }
            });
        }
    }
}