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
        
        public ObservableCollection<CategorySettingsGroup> SettingsByCategory
        {
            get => _settingsByCategory;
            set
            {
                _settingsByCategory = value;
                // Уведомление об изменении свойства для привязки
            }
        }

        public FfmpegSettingsExpander()
        {
            this.InitializeComponent();
            this.Loaded += FfmpegSettingsExpander_Loaded;
            
            // Устанавливаем свойства доступности
            AutomationProperties.SetName(this, "Настройки FFMPEG");
            AutomationProperties.SetHelpText(this, "Настройки параметров FFMPEG для воспроизведения аудио");
            
            // Инициализируем коллекцию
            SettingsByCategory = new ObservableCollection<CategorySettingsGroup>();
        }

        private async void FfmpegSettingsExpander_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadSettingsAsync();
        }

        private async Task LoadSettingsAsync()
        {
            try
            {
                LoadingProgress.Visibility = Visibility.Visible;
                SettingsContainer.Visibility = Visibility.Collapsed;
                ErrorMessage.Visibility = Visibility.Collapsed;

                // Загружаем настройки асинхронно
                await Task.Run(() =>
                {
                    var settings = FfmpegSettingsManager.LoadAllSettings();
                    
                    // Группируем по категориям
                    var grouped = settings
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

                    // Обновляем UI в основном потоке
                    _ = this.DispatcherQueue.TryEnqueue(() =>
                    {
                        SettingsByCategory.Clear();
                        foreach (var group in grouped)
                        {
                            SettingsByCategory.Add(group);
                        }

                        LoadingProgress.Visibility = Visibility.Collapsed;
                        SettingsContainer.Visibility = Visibility.Visible;
                    });
                });
            }
            catch (Exception ex)
            {
                LoadingProgress.Visibility = Visibility.Collapsed;
                ErrorMessage.Text = $"Ошибка загрузки настроек: {ex.Message}";
                ErrorMessage.Visibility = Visibility.Visible;
                System.Diagnostics.Debug.WriteLine($"[FfmpegSettingsExpander] Load error: {ex}");
            }
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
                // Собираем все настройки из всех категорий
                var allSettings = new List<FfmpegSettingItem>();
                foreach (var categoryGroup in SettingsByCategory)
                {
                    foreach (var setting in categoryGroup.Settings)
                    {
                        // Валидируем настройку перед сохранением
                        if (!FfmpegSettingsManager.ValidateSettingValue(setting.Key, setting.Value))
                        {
                            ShowMessage($"Некорректное значение для настройки '{setting.Key}': {setting.Value}", "Ошибка");
                            return;
                        }
                        allSettings.Add(setting);
                    }
                }

                // Сохраняем настройки
                await Task.Run(() => FfmpegSettingsManager.SaveAllSettings(allSettings));
                
                ShowMessage("Настройки успешно сохранены", "Успех");
            }
            catch (Exception ex)
            {
                ShowMessage($"Ошибка сохранения настроек: {ex.Message}", "Ошибка");
                System.Diagnostics.Debug.WriteLine($"[FfmpegSettingsExpander] Save error: {ex}");
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
                    await Task.Run(() => FfmpegSettingsManager.ResetToDefaults());
                    await LoadSettingsAsync();
                    ShowMessage("Настройки сброшены к значениям по умолчанию", "Успех");
                }
                catch (Exception ex)
                {
                    ShowMessage($"Ошибка сброса настроек: {ex.Message}", "Ошибка");
                    System.Diagnostics.Debug.WriteLine($"[FfmpegSettingsExpander] Reset error: {ex}");
                }
            }
        }

        private async void AddCustomSetting_Click(object sender, RoutedEventArgs e)
        {
            var key = NewSettingKey.Text?.Trim();
            var value = NewSettingValue.Text?.Trim();

            if (string.IsNullOrWhiteSpace(key))
            {
                ShowMessage("Введите ключ для новой настройки", "Ошибка");
                return;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                ShowMessage("Введите значение для новой настройки", "Ошибка");
                return;
            }

            // Проверяем, не существует ли уже такая настройка
            var existingSettings = FfmpegSettingsManager.LoadAllSettings();
            if (existingSettings.Any(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
            {
                ShowMessage($"Настройка с ключом '{key}' уже существует", "Ошибка");
                return;
            }

            try
            {
                // Добавляем новую настройку
                await Task.Run(() => FfmpegSettingsManager.AddCustomSetting(key, value));
                
                // Обновляем список настроек
                await LoadSettingsAsync();
                
                // Очищаем поля ввода
                NewSettingKey.Text = "";
                NewSettingValue.Text = "";
                
                ShowMessage($"Настройка '{key}' успешно добавлена", "Успех");
            }
            catch (Exception ex)
            {
                ShowMessage($"Ошибка добавления настройки: {ex.Message}", "Ошибка");
                System.Diagnostics.Debug.WriteLine($"[FfmpegSettingsExpander] Add custom setting error: {ex}");
            }
        }

        private async void DeleteCustomSetting_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is FfmpegSettingItem setting)
            {
                if (!setting.IsCustom)
                {
                    ShowMessage("Можно удалять только пользовательские настройки", "Ошибка");
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
                        await Task.Run(() => FfmpegSettingsManager.DeleteSetting(setting.Key));
                        
                        // Удаляем настройку из текущего отображения
                        foreach (var categoryGroup in SettingsByCategory)
                        {
                            if (categoryGroup.Settings.Contains(setting))
                            {
                                categoryGroup.Settings.Remove(setting);
                                break;
                            }
                        }
                        
                        ShowMessage($"Настройка '{setting.Key}' успешно удалена", "Успех");
                    }
                    catch (Exception ex)
                    {
                        ShowMessage($"Ошибка удаления настройки: {ex.Message}", "Ошибка");
                        System.Diagnostics.Debug.WriteLine($"[FfmpegSettingsExpander] Delete setting error: {ex}");
                    }
                }
            }
        }

        private void ShowMessage(string message, string title)
        {
            // В реальном приложении здесь можно использовать Snackbar или InfoBar
            // Для простоты используем ContentDialog
            _ = this.DispatcherQueue.TryEnqueue(async () =>
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