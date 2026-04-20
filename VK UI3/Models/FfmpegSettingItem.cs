using System;
using System.ComponentModel;

namespace VK_UI3.Models
{
    public class FfmpegSettingItem : INotifyPropertyChanged
    {
        private string _key;
        private string _value;
        private string _description;
        private string _category;
        private bool _isCustom;

        public string Key
        {
            get => _key;
            set
            {
                if (_key != value)
                {
                    _key = value;
                    OnPropertyChanged(nameof(Key));
                }
            }
        }

        public string Value
        {
            get => _value;
            set
            {
                if (_value != value)
                {
                    _value = value;
                    OnPropertyChanged(nameof(Value));
                    OnPropertyChanged(nameof(DisplayValue));
                }
            }
        }

        public string DisplayValue => string.IsNullOrEmpty(Value) ? "(не задано)" : Value;

        public string Description
        {
            get => _description;
            set
            {
                if (_description != value)
                {
                    _description = value;
                    OnPropertyChanged(nameof(Description));
                }
            }
        }

        public string Category
        {
            get => _category;
            set
            {
                if (_category != value)
                {
                    _category = value;
                    OnPropertyChanged(nameof(Category));
                }
            }
        }

        public bool IsCustom
        {
            get => _isCustom;
            set
            {
                if (_isCustom != value)
                {
                    _isCustom = value;
                    OnPropertyChanged(nameof(IsCustom));
                }
            }
        }

        public FfmpegSettingItem() { }

        public FfmpegSettingItem(string key, string value, string description = "", string category = "Общие", bool isCustom = false)
        {
            Key = key;
            Value = value;
            Description = description;
            Category = category;
            IsCustom = isCustom;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString()
        {
            return $"{Key}={Value}";
        }

        public bool Validate()
        {
            if (string.IsNullOrWhiteSpace(Key))
                return false;

            // Базовая валидация значений
            switch (Key)
            {
                case "http_persistent":
                case "reconnect":
                case "reconnect_at_eof":
                case "reconnect_streamed":
                case "reconnect_on_network_error":
                case "tcp_nodelay":
                    return Value == "0" || Value == "1";

                case "reconnect_delay_max":
                case "buffer_size":
                case "max_buffer_size":
                case "probesize":
                case "analyzeduration":
                    return int.TryParse(Value, out _) && int.Parse(Value) >= 0;

                default:
                    return !string.IsNullOrWhiteSpace(Value);
            }
        }

        public static string GetDefaultDescription(string key)
        {
            return key switch
            {
                "http_persistent" => "Использовать постоянные HTTP соединения (1 - включено, 0 - выключено)",
                "reconnect" => "Автоматически переподключаться при разрыве соединения (1 - включено, 0 - выключено)",
                "reconnect_at_eof" => "Переподключаться при достижении конца файла (1 - включено, 0 - выключено)",
                "reconnect_streamed" => "Переподключаться для потокового контента (1 - включено, 0 - выключено)",
                "reconnect_delay_max" => "Максимальная задержка переподключения в секундах",
                "reconnect_on_network_error" => "Переподключаться при сетевых ошибках (1 - включено, 0 - выключено)",
                "reconnect_on_http_error" => "Переподключаться при HTTP ошибках (например: '4xx,5xx')",
                "tcp_nodelay" => "Отключить алгоритм Nagle для TCP (1 - включено, 0 - выключено)",
                "buffer_size" => "Размер буфера чтения в байтах",
                "max_buffer_size" => "Максимальный размер буфера в байтах",
                "probesize" => "Размер данных для анализа формата в байтах",
                "analyzeduration" => "Длительность анализа в микросекундах",
                "fflags" => "Флаги демуксера (например: 'nobuffer+fastseek+flush_packets')",
                "user_agent" => "User-Agent для HTTP запросов",
                _ => "Пользовательская настройка FFMPEG"
            };
        }

        public static string GetDefaultCategory(string key)
        {
            if (key.StartsWith("reconnect") || key == "http_persistent" || key == "tcp_nodelay" || key == "user_agent")
                return "Сеть";
            
            if (key.Contains("buffer") || key.Contains("size") || key == "probesize" || key == "analyzeduration")
                return "Буфер";
            
            if (key == "fflags")
                return "Дополнительно";
            
            return "Общие";
        }

        public static string GetDefaultValue(string key)
        {
            return key switch
            {
                "http_persistent" => "1",
                "reconnect" => "1",
                "reconnect_at_eof" => "1",
                "reconnect_streamed" => "1",
                "reconnect_delay_max" => "10",
                "reconnect_on_network_error" => "1",
                "reconnect_on_http_error" => "4xx,5xx",
                "tcp_nodelay" => "1",
                "buffer_size" => "1048576",
                "max_buffer_size" => "4194304",
                "probesize" => "524288",
                "analyzeduration" => "500000",
                "fflags" => "nobuffer+fastseek+flush_packets",
                "user_agent" => "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                _ => ""
            };
        }
    }
}