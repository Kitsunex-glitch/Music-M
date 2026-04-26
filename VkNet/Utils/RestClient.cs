using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using VkNet.Abstractions.Utils;

namespace VkNet.Utils
{
    /// <inheritdoc />
    [UsedImplicitly]
    public class RestClient : IRestClient
    {
        private readonly ILogger<RestClient> _logger;
        private readonly bool _enableDebugLogging;

        // Настройки сериализации для минимизации размера запросов
        private static readonly JsonSerializerSettings JsonSettings = new()
        {
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore,
            Formatting = Formatting.None
        };

        /// <inheritdoc cref="RestClient"/>
        public RestClient(HttpClient httpClient, ILogger<RestClient> logger)
        {
            HttpClient = httpClient;
            _logger = logger;
            _enableDebugLogging = logger?.IsEnabled(LogLevel.Debug) == true;
        }

        /// <summary>
        /// Http client
        /// </summary>
        public HttpClient HttpClient { get; }

        /// <inheritdoc />
        [Obsolete("Use HttpClient to configure proxy. Documentation reference https://github.com/vknet/vk/wiki/Proxy-Configuration", true)]
        public IWebProxy Proxy { get; set; }

        /// <inheritdoc />
        [Obsolete("Use HttpClient to configure timeout. Documentation reference https://github.com/vknet/vk/wiki/Proxy-Configuration", true)]
        public TimeSpan Timeout { get; set; }

        /// <inheritdoc />
        public Task<HttpResponse<string>> GetAsync(Uri uri, IEnumerable<KeyValuePair<string, string>> parameters, Encoding encoding)
        {
            var url = Url.Combine(uri.ToString(), Url.QueryFrom(parameters.ToArray()));

            // Минимизируем логирование в продакшене
            if (_enableDebugLogging)
            {
                _logger?.LogDebug("GET request: {Url}", url);
            }

            return CallAsync(() => HttpClient.GetAsync(url), encoding);
        }

        /// <inheritdoc />
        public Task<HttpResponse<string>> PostAsync(Uri uri, IEnumerable<KeyValuePair<string, string>> parameters, Encoding encoding)
        {
            // Минимизируем логирование в продакшене
            if (_enableDebugLogging)
            {
                var json = JsonConvert.SerializeObject(parameters, JsonSettings);
                _logger?.LogDebug("POST request: {Uri}", uri);
            }

            var content = new FormUrlEncodedContent(parameters);

            return CallAsync(() => HttpClient.PostAsync(uri, content), encoding);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            HttpClient?.Dispose();
        }

        private async Task<HttpResponse<string>> CallAsync(Func<Task<HttpResponseMessage>> method, Encoding encoding)
        {
            var response = await method().ConfigureAwait(false);

            // Оптимизация: используем ReadAsStringAsync вместо промежуточного byte[]
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            // Асинхронное логирование с обрезанием длинных ответов
            if (_enableDebugLogging)
            {
                // Не ждем завершения логирования
                _ = Task.Run(() =>
                {
                    var logContent = content.Length > 500 ? content[..500] + "..." : content;
                    _logger?.LogDebug("Response (truncated if >500 chars): {Response}", logContent);
                });
            }

            var requestUri = response.RequestMessage?.RequestUri;
            var responseUri = response.Headers.Location;

            return response.IsSuccessStatusCode
                ? HttpResponse<string>.Success(response.StatusCode, content, requestUri, responseUri)
                : HttpResponse<string>.Fail(response.StatusCode, content, requestUri, responseUri);
        }
    }
}