using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using VK_UI3.Models;
using VK_UI3.Services;

namespace VK_UI3.Views.Settings
{
    public sealed partial class FfmpegSettingsExpander : Expander
    {
        private ObservableCollection<CategorySettingsGroup> _settingsByCategory;
        private Dictionary<string, CategorySettingsGroup> _categoryLookup;
        private bool _isInitialized = false;
        private bool _isLoading = false;

        public ObservableCollection<CategorySettingsGroup> SettingsByCategory
        {
            get => _settingsByCategory;
            set
            {
                _settingsByCategory = value;
                BuildCategoryLookup();
            }
        }

        public FfmpegSettingsExpander()
        {
            this.InitializeComponent();
            this.Loaded += FfmpegSettingsExpander_Loaded;
            this.Expanding += FfmpegSettingsExpander_Expanding;

            AutomationProperties.SetName(this, "Настройки FFMPEG");
            AutomationProperties.SetHelpText(this, "Настройки параметров FFMPEG для воспроизведения аудио");

            SettingsByCategory = new ObservableCollection<CategorySettingsGroup>();
        }

        private async void FfmpegSettingsExpander_Expanding(Expander sender, ExpanderExpandingEventArgs args)
        {
            if (!_isInitialized && !_isLoading)
            {
               await LoadSettingsAsync();
            }
        }

        private void BuildCategoryLookup()
        {
            _categoryLookup = SettingsByCategory.ToDictionary(g => g.CategoryName);
        }

        private async void FfmpegSettingsExpander_Loaded(object sender, RoutedEventArgs e)
        {
            // Не загружаем автоматически, ждем раскрытия экспандера
        }

      
        private async Task LoadSettingsAsync()
        {
            if (_isLoading) return;

            try
            {
                _isLoading = true;
                LoadingProgress.Visibility = Visibility.Visible;
                SettingsContainer.Visibility = Visibility.Collapsed;
                ErrorMessage.Visibility = Visibility.Collapsed;

                var settings = await Task.Run(() => FfmpegSettingsManager.LoadAllSettings());

                // Группируем и создаем ObservableCollection в UI потоке
                await this.DispatcherQueue.EnqueueAsync(() =>
                {
                    var grouped = GroupSettings(settings);

                    // ОТЛАДКА: выводим все ключи
                    foreach (var group in grouped)
                    {
                        System.Diagnostics.Debug.WriteLine($"=== Категория: {group.CategoryName} ===");
                        foreach (var item in group.Settings)
                        {
                            System.Diagnostics.Debug.WriteLine($"Key: '{item.Key}', Value: '{item.Value}', IsCustom: {item.IsCustom}");
                        }
                    }

                    SettingsByCategory.Clear();
                    foreach (var group in grouped)
                    {
                        SettingsByCategory.Add(group);
                    }

                    // Принудительно обновляем UI
                    SettingsContainer.ItemsSource = null;
                    SettingsContainer.ItemsSource = SettingsByCategory;

                    BuildCategoryLookup();
                    _isInitialized = true;

                    LoadingProgress.Visibility = Visibility.Collapsed;
                    SettingsContainer.Visibility = Visibility.Visible;
                });
            }
            catch (Exception ex)
            {
                await this.DispatcherQueue.EnqueueAsync(() =>
                {
                    LoadingProgress.Visibility = Visibility.Collapsed;
                    ErrorMessage.Text = $"Ошибка загрузки настроек: {ex.Message}";
                    ErrorMessage.Visibility = Visibility.Visible;
                });

                System.Diagnostics.Debug.WriteLine($"[FfmpegSettingsExpander] Load error: {ex}");
            }
            finally
            {
                _isLoading = false;
            }
        }

        private List<CategorySettingsGroup> GroupSettings(IEnumerable<FfmpegSettingItem> settings)
        {
            return settings
                .GroupBy(s => s.Category)
                .Select(g => new CategorySettingsGroup
                {
                    CategoryName = g.Key,
                    Settings = new ObservableCollection<FfmpegSettingItem>(
                        g.OrderBy(s => s.Key).Select(item => new FfmpegSettingItem
                        {
                            Key = item.Key,
                            Value = item.Value,
                            Category = item.Category,
                            IsCustom = item.IsCustom,
                            Description = item.Description,
                        })
                    )
                })
                .OrderBy(g => GetCategoryOrder(g.CategoryName))
                .ToList();
        }

        private int GetCategoryOrder(string category)
        {
            return category switch
            {
                "Сеть" => 1,
                "Буфер" => 2,
                "Дополнительно" => 3,
                "Общие" => 4,
                "Пользовательские" => 5,
                _ => 99
            };
        }

        private async void SaveAllSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveButton.IsEnabled = false;

                // Собираем все настройки для сохранения
                var allSettings = SettingsByCategory
                    .SelectMany(g => g.Settings)
                    .ToList();

                // Валидация
                foreach (var setting in allSettings)
                {
                    if (!FfmpegSettingsManager.ValidateSettingValue(setting.Key, setting.Value))
                    {
                        await ShowMessageAsync($"Некорректное значение для настройки '{setting.Key}': {setting.Value}", "Ошибка");
                        return;
                    }
                }

                // Сохраняем в фоне
                await Task.Run(() => FfmpegSettingsManager.SaveAllSettings(allSettings));

                await ShowMessageAsync("Настройки успешно сохранены", "Успех");
            }
            catch (Exception ex)
            {
                await ShowMessageAsync($"Ошибка сохранения настроек: {ex.Message}", "Ошибка");
                System.Diagnostics.Debug.WriteLine($"[FfmpegSettingsExpander] Save error: {ex}");
            }
            finally
            {
                SaveButton.IsEnabled = true;
            }
        }

        private async void ResetToDefaults_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Сброс настроек",
                Content = "Вы уверены, что хотите сбросить все настройки FFMPEG к значениям по умолчанию?",
                PrimaryButtonText = "Сбросить",
                CloseButtonText = "Отмена",
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                try
                {
                    ResetButton.IsEnabled = false;

                    await Task.Run(() => FfmpegSettingsManager.ResetToDefaults());

                    // Перезагружаем только если уже были загружены
                    if (_isInitialized)
                    {
                        var settings = await Task.Run(() => FfmpegSettingsManager.LoadAllSettings());

                        await this.DispatcherQueue.EnqueueAsync(() =>
                        {
                            var grouped = GroupSettings(settings);

                            SettingsByCategory.Clear();
                            foreach (var group in grouped)
                            {
                                SettingsByCategory.Add(group);
                            }

                            BuildCategoryLookup();
                        });
                    }

                    await ShowMessageAsync("Настройки сброшены к значениям по умолчанию", "Успех");
                }
                catch (Exception ex)
                {
                    await ShowMessageAsync($"Ошибка сброса настроек: {ex.Message}", "Ошибка");
                    System.Diagnostics.Debug.WriteLine($"[FfmpegSettingsExpander] Reset error: {ex}");
                }
                finally
                {
                    ResetButton.IsEnabled = true;
                }
            }
        }

        private async void AddCustomSetting_Click(object sender, RoutedEventArgs e)
        {
            var key = NewSettingKey.Text?.Trim();
            var value = NewSettingValue.Text?.Trim();

            if (string.IsNullOrWhiteSpace(key))
            {
                await ShowMessageAsync("Введите ключ для новой настройки", "Ошибка");
                return;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                await ShowMessageAsync("Введите значение для новой настройки", "Ошибка");
                return;
            }

            // Проверяем существование в текущей коллекции
            if (SettingsByCategory.SelectMany(g => g.Settings).Any(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
            {
                await ShowMessageAsync($"Настройка с ключом '{key}' уже существует", "Ошибка");
                return;
            }

            try
            {
                AddButton.IsEnabled = false;

                // Создаем новую настройку
                var newSetting = new FfmpegSettingItem
                {
                    Key = key,
                    Value = value,
                    Category = "Пользовательские",
                    IsCustom = true,
                    Description = "Пользовательская настройка"
                };

                // Сохраняем в фоне
                await Task.Run(() => FfmpegSettingsManager.AddCustomSetting(key, value));

                // Добавляем в UI без перезагрузки всего списка
                await this.DispatcherQueue.EnqueueAsync(() =>
                {
                    AddSettingToUI(newSetting);

                    // Очищаем поля ввода
                    NewSettingKey.Text = "";
                    NewSettingValue.Text = "";
                });

                await ShowMessageAsync($"Настройка '{key}' успешно добавлена", "Успех");
            }
            catch (Exception ex)
            {
                await ShowMessageAsync($"Ошибка добавления настройки: {ex.Message}", "Ошибка");
                System.Diagnostics.Debug.WriteLine($"[FfmpegSettingsExpander] Add custom setting error: {ex}");
            }
            finally
            {
                AddButton.IsEnabled = true;
            }
        }

        private void AddSettingToUI(FfmpegSettingItem setting)
        {
            // Находим или создаем категорию "Пользовательские"
            if (!_categoryLookup.TryGetValue("Пользовательские", out var categoryGroup))
            {
                categoryGroup = new CategorySettingsGroup
                {
                    CategoryName = "Пользовательские",
                    Settings = new ObservableCollection<FfmpegSettingItem>()
                };

                // Вставляем в нужное место согласно сортировке
                var insertIndex = FindInsertIndexForCategory("Пользовательские");
                SettingsByCategory.Insert(insertIndex, categoryGroup);
                _categoryLookup["Пользовательские"] = categoryGroup;
            }

            // Добавляем настройку в алфавитном порядке
            var settings = categoryGroup.Settings;
            var insertPos = 0;
            while (insertPos < settings.Count &&
                   string.Compare(settings[insertPos].Key, setting.Key, StringComparison.OrdinalIgnoreCase) < 0)
            {
                insertPos++;
            }

            settings.Insert(insertPos, setting);
        }

        private int FindInsertIndexForCategory(string categoryName)
        {
            var targetOrder = GetCategoryOrder(categoryName);

            for (int i = 0; i < SettingsByCategory.Count; i++)
            {
                var currentOrder = GetCategoryOrder(SettingsByCategory[i].CategoryName);
                if (currentOrder > targetOrder)
                {
                    return i;
                }
            }

            return SettingsByCategory.Count;
        }

        private async void DeleteCustomSetting_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is FfmpegSettingItem setting)
            {
                if (!setting.IsCustom)
                {
                    await ShowMessageAsync("Можно удалять только пользовательские настройки", "Ошибка");
                    return;
                }

                var dialog = new ContentDialog
                {
                    Title = "Удаление настройки",
                    Content = $"Вы уверены, что хотите удалить настройку '{setting.Key}'?",
                    PrimaryButtonText = "Удалить",
                    CloseButtonText = "Отмена",
                    XamlRoot = this.XamlRoot
                };

                var result = await dialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    try
                    {
                        button.IsEnabled = false;

                        // Удаляем из хранилища
                        await Task.Run(() => FfmpegSettingsManager.DeleteSetting(setting.Key));

                        // Удаляем из UI
                        await this.DispatcherQueue.EnqueueAsync(() =>
                        {
                            RemoveSettingFromUI(setting);
                        });

                        await ShowMessageAsync($"Настройка '{setting.Key}' успешно удалена", "Успех");
                    }
                    catch (Exception ex)
                    {
                        await ShowMessageAsync($"Ошибка удаления настройки: {ex.Message}", "Ошибка");
                        System.Diagnostics.Debug.WriteLine($"[FfmpegSettingsExpander] Delete setting error: {ex}");
                    }
                    finally
                    {
                        button.IsEnabled = true;
                    }
                }
            }
        }

        private void RemoveSettingFromUI(FfmpegSettingItem setting)
        {
            if (_categoryLookup.TryGetValue(setting.Category, out var categoryGroup))
            {
                categoryGroup.Settings.Remove(setting);

                // Если категория опустела и это не базовая категория, удаляем её
                if (categoryGroup.Settings.Count == 0 && setting.Category == "Пользовательские")
                {
                    SettingsByCategory.Remove(categoryGroup);
                    _categoryLookup.Remove(setting.Category);
                }
            }
        }

        private Task ShowMessageAsync(string message, string title)
        {
            return this.DispatcherQueue.EnqueueAsync(async () =>
            {
                var dialog = new ContentDialog
                {
                    Title = title,
                    Content = message,
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };

                await dialog.ShowAsync();
            });
        }
    }

    public class CategorySettingsGroup
    {
        public string CategoryName { get; set; }
        public ObservableCollection<FfmpegSettingItem> Settings { get; set; }
    }
}

// Расширение для удобной работы с DispatcherQueue
public static class DispatcherQueueExtensions
{
    public static Task EnqueueAsync(this Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue, Action action)
    {
        var tcs = new TaskCompletionSource<bool>();

        dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                action();
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }
}