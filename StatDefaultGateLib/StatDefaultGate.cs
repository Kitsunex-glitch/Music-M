using Newtonsoft.Json;
using StatDefaultGateLib.Models;

namespace StatDefaultGateLib
{
    public class StatDefaultGate
    {
        public StatDefaultGate(string token)
        {
            Token = token;
        }

        public static Uri StatDefaultGateUri { get; } = new Uri("https://StatDefaultGate.ru/open_api/");
        public string Token { get; set; }

        /// <summary>
        /// Флаг, указывающий, включена ли отправка статистики.
        /// По умолчанию true. Устанавливается из настроек приложения.
        /// </summary>
        public static bool IsEnabled { get; set; } = true;

        public void SetToken(string token) {
            Token = token;
        }

        public async Task SendEvent(Event @event) {

            // Если сбор статистики отключён — не отправляем
            if (!IsEnabled)
                return;

            try
            {

                var client = new HttpClient();
                var request = new HttpRequestMessage(HttpMethod.Post, $"{StatDefaultGateUri}events");
                request.Headers.Add("token", Token);
                var content = new StringContent(
                    JsonConvert.SerializeObject(@event)
                    , null, "application/json");
                request.Content = content;
                var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();
                Console.WriteLine(await response.Content.ReadAsStringAsync());

            }
            catch (Exception e)
            {
            
            
            }
        }
    }
}
