using System;
using System.Collections.Generic;
using System.Linq;
using VK_UI3.DB;
using VK_UI3.Models;

namespace VK_UI3.Services
{
    public static class FfmpegSettingsManager
    {
        private const string SettingsPrefix = "ffmpeg_";
        private static Dictionary<string, FfmpegSettingItem> _cachedSettings;

        public static Dictionary<string, string> DefaultSettings { get; } = new Dictionary<string, string>
        {
            { "http_persistent", "1" },
            { "reconnect", "1" },
            { "reconnect_at_eof", "1" },
            { "reconnect_streamed", "1" },
            { "reconnect_delay_max", "2" },
            { "reconnect_on_network_error", "1" },
            { "reconnect_on_http_error", "4xx,5xx" },
            { "tcp_nodelay", "1" },
            { "buffer_size", "524288" },     
            { "max_buffer_size", "1048576" }, 
            { "probesize", "1048576" },      
            { "analyzeduration", "500000" },  
            { "fflags", "fastseek+flush_packets" },
            { "user_agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36" }
        };


        public static List<FfmpegSettingItem> LoadAllSettings(bool forceReload = false)
        {
            if (_cachedSettings != null && !forceReload)
            {
                return _cachedSettings.Values.ToList();
            }

            _cachedSettings = new Dictionary<string, FfmpegSettingItem>();

            // Загружаем стандартные настройки
            foreach (var kvp in DefaultSettings)
            {
                var settingKey = SettingsPrefix + kvp.Key;
                var dbSetting = SettingsTable.GetSetting(settingKey, kvp.Value);
                
                var settingItem = new FfmpegSettingItem(
                    key: kvp.Key,
                    value: dbSetting?.settingValue ?? kvp.Value,
                    description: FfmpegSettingItem.GetDefaultDescription(kvp.Key),
                    category: FfmpegSettingItem.GetDefaultCategory(kvp.Key),
                    isCustom: false
                );
                
                _cachedSettings[kvp.Key] = settingItem;
            }

            // Загружаем пользовательские настройки
            LoadCustomSettings();

            return _cachedSettings.Values.ToList();
        }

        private static void LoadCustomSettings()
        {
            try
            {
                var allSettings = DatabaseHandler.getConnect().Query<SettingsTable>("SELECT * FROM SettingsTable WHERE settingName LIKE 'ffmpeg_custom_%'");
                
                foreach (var setting in allSettings)
                {
                    var key = setting.settingName.Replace(SettingsPrefix + "custom_", "");
                    if (!_cachedSettings.ContainsKey(key))
                    {
                        var settingItem = new FfmpegSettingItem(
                            key: key,
                            value: setting.settingValue,
                            description: "Пользовательская настройка FFMPEG",
                            category: "Пользовательские",
                            isCustom: true
                        );
                        
                        _cachedSettings[key] = settingItem;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FfmpegSettingsManager] Error loading custom settings: {ex.Message}");
            }
        }

        public static void SaveSetting(FfmpegSettingItem setting)
        {
            if (setting == null || string.IsNullOrWhiteSpace(setting.Key))
                return;

            var settingKey = setting.IsCustom 
                ? SettingsPrefix + "custom_" + setting.Key
                : SettingsPrefix + setting.Key;

            SettingsTable.SetSetting(settingKey, setting.Value);
            
            // Обновляем кэш
            if (_cachedSettings != null)
            {
                _cachedSettings[setting.Key] = setting;
            }
        }

        public static void SaveAllSettings(IEnumerable<FfmpegSettingItem> settings)
        {
            foreach (var setting in settings)
            {
                SaveSetting(setting);
            }
        }

        public static void DeleteSetting(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            // Проверяем, является ли настройка стандартной
            if (DefaultSettings.ContainsKey(key))
            {
                // Для стандартных настроек просто сбрасываем к значению по умолчанию
                var settingKey = SettingsPrefix + key;
                SettingsTable.SetSetting(settingKey, DefaultSettings[key]);
                
                if (_cachedSettings != null && _cachedSettings.ContainsKey(key))
                {
                    _cachedSettings[key].Value = DefaultSettings[key];
                }
            }
            else
            {
                // Для пользовательских настроек удаляем из БД
                var settingKey = SettingsPrefix + "custom_" + key;
                SettingsTable.RemoveSetting(settingKey);
                
                if (_cachedSettings != null)
                {
                    _cachedSettings.Remove(key);
                }
            }
        }

        public static void ResetToDefaults()
        {
            // Удаляем все настройки FFMPEG из БД
            try
            {
                DatabaseHandler.getConnect().Query<SettingsTable>("DELETE FROM SettingsTable WHERE settingName LIKE 'ffmpeg_%'");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FfmpegSettingsManager] Error resetting settings: {ex.Message}");
            }

            // Сбрасываем кэш
            _cachedSettings = null;
            
            // Загружаем стандартные значения (они автоматически создадутся в БД при следующей загрузке)
            LoadAllSettings(true);
        }

        public static Dictionary<string, string> GetSettingsDictionary()
        {
            var settings = LoadAllSettings();
            return settings.ToDictionary(s => s.Key, s => s.Value);
        }

        public static FfmpegSettingItem GetSetting(string key)
        {
            if (_cachedSettings == null)
            {
                LoadAllSettings();
            }

            if (_cachedSettings.TryGetValue(key, out var setting))
            {
                return setting;
            }

            // Если настройка не найдена, создаем новую (для пользовательских)
            if (DefaultSettings.ContainsKey(key))
            {
                var settingItem = new FfmpegSettingItem(
                    key: key,
                    value: DefaultSettings[key],
                    description: FfmpegSettingItem.GetDefaultDescription(key),
                    category: FfmpegSettingItem.GetDefaultCategory(key),
                    isCustom: false
                );
                
                _cachedSettings[key] = settingItem;
                return settingItem;
            }

            return null;
        }

        public static void AddCustomSetting(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key) || DefaultSettings.ContainsKey(key))
                return;

            var settingItem = new FfmpegSettingItem(
                key: key,
                value: value,
                description: "Пользовательская настройка FFMPEG",
                category: "Пользовательские",
                isCustom: true
            );

            SaveSetting(settingItem);
            
            if (_cachedSettings != null)
            {
                _cachedSettings[key] = settingItem;
            }
        }

        public static List<FfmpegSettingItem> GetSettingsByCategory(string category)
        {
            var settings = LoadAllSettings();
            return settings.Where(s => s.Category == category).ToList();
        }

        public static List<string> GetCategories()
        {
            var settings = LoadAllSettings();
            return settings.Select(s => s.Category).Distinct().ToList();
        }

        public static bool ValidateSettingValue(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                return false;

            // Базовая валидация в зависимости от типа настройки
            switch (key)
            {
                case "http_persistent":
                case "reconnect":
                case "reconnect_at_eof":
                case "reconnect_streamed":
                case "reconnect_on_network_error":
                case "tcp_nodelay":
                    return value == "0" || value == "1";

                case "reconnect_delay_max":
                    return int.TryParse(value, out int delay) && delay >= 0 && delay <= 60;

                case "buffer_size":
                case "max_buffer_size":
                case "probesize":
                case "analyzeduration":
                    return int.TryParse(value, out int size) && size >= 0;

                case "reconnect_on_http_error":
                    // Проверяем формат типа "4xx,5xx"
                    return !string.IsNullOrWhiteSpace(value) && value.Length <= 50;

                case "fflags":
                case "user_agent":
                    return !string.IsNullOrWhiteSpace(value) && value.Length <= 500;

                default:
                    // Для пользовательских настроек
                    return !string.IsNullOrWhiteSpace(value) && value.Length <= 1000;
            }
        }
    }
}