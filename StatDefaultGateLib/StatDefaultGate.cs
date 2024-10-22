using Newtonsoft.Json;
using StatDefaultGateLib.Models;

namespace StatDefaultGateLib
{
    public class StatDefaultGate
    {
        public static Uri StatDefaultGateUri { get; } = new Uri("https://StatDefaultGate.ru/open_api/");
        public static string Token { get; set; }

        public static void SetToken(string token) {
            Token = token;
        }

        public static async Task SendEvent(Event @event) {


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
    }
}
