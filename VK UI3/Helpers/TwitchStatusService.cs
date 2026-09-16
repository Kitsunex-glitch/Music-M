using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace VK_UI3.Helpers
{
    /// <summary>
    /// Модель статуса стримера, возвращаемая endpoint'ом https://little-hill-8dc9.fairdarkworker.workers.dev/
    /// (формат Twitch API Helix /streams).
    /// </summary>
    public class TwitchStreamStatus
    {
        public string id { get; set; }
        public string user_id { get; set; }
        public string user_login { get; set; }
        public string user_name { get; set; }
        public string game_id { get; set; }
        public string game_name { get; set; }

        /// <summary>Значение "live" означает, что стример сейчас в эфире.</summary>
        public string type { get; set; }

        public string title { get; set; }
        public int viewer_count { get; set; }
        public string started_at { get; set; }
        public string language { get; set; }
        public string thumbnail_url { get; set; }
        public List<string> tags { get; set; } = new List<string>();
        public bool is_mature { get; set; }

        /// <summary>Короткий логин стримера (если поле присутствует в ответе).</summary>
        public string user { get; set; }

        /// <summary>
        /// Логин стримера для ссылок (предпочитаем user_login, иначе user, иначе user_name).
        /// </summary>
        public string Login => !string.IsNullOrWhiteSpace(user_login) ? user_login : (!string.IsNullOrWhiteSpace(user) ? user : user_name);

        /// <summary>Отображаемое имя стримера.</summary>
        public string DisplayName => !string.IsNullOrWhiteSpace(user_name) ? user_name : Login;

        /// <summary>
        /// Признак того, что стример сейчас в эфире.
        /// Считаем "live", если поле type равно "live".
        /// </summary>
        public bool IsLive => string.Equals(type, "live", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Сервис опроса статуса стримеров (Twitch).
    /// </summary>
    internal class TwitchStatusService : IDisposable
    {
        private const string StatusUrl = "https://little-hill-8dc9.fairdarkworker.workers.dev/";
        private readonly HttpClient _client;

        public TwitchStatusService()
        {
            _client = new HttpClient();
            _client.Timeout = TimeSpan.FromSeconds(15);
        }

        /// <summary>
        /// Получает список статусов всех отслеживаемых стримеров.
        /// При ошибке сети/парсинга возвращает пустой список и логирует ошибку.
        /// </summary>
        public async Task<List<TwitchStreamStatus>> GetStatusesAsync()
        {
            try
            {
                using var response = await _client.GetAsync(StatusUrl);
                response.EnsureSuccessStatusCode();

                string jsonString = await response.Content.ReadAsStringAsync();
                return Deserialize(jsonString);
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки статусов Twitch: {ex.Message}");
                return new List<TwitchStreamStatus>();
            }
            catch (TaskCanceledException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Таймаут запроса статусов Twitch: {ex.Message}");
                return new List<TwitchStreamStatus>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка обработки статусов Twitch: {ex.Message}");
                return new List<TwitchStreamStatus>();
            }
        }

        private static List<TwitchStreamStatus> Deserialize(string jsonString)
        {
            if (string.IsNullOrWhiteSpace(jsonString))
                return new List<TwitchStreamStatus>();

            try
            {
                var statuses = JsonSerializer.Deserialize<List<TwitchStreamStatus>>(
                    jsonString,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                return statuses ?? new List<TwitchStreamStatus>();
            }
            catch (JsonException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка десериализации статусов Twitch: {ex.Message}");
                return new List<TwitchStreamStatus>();
            }
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}