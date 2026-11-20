using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using VK_UI3.VKs.Ext;
using System.Linq;

namespace VK_UI3.Views.ModalsPages
{
    public class SumbitCaptcha : EventArgs { }

    public sealed partial class CaptchaEnter : Page
    {
        public event EventHandler SumbitPressed;
        public string CaptchaUri { get; set; }
        public string RedirectUri { get; set; }
        public bool IsWebCaptcha => !string.IsNullOrEmpty(RedirectUri);
        public TaskCompletionSource<string> Submitted { get; set; } = new();
        public ContentDialog ParentDialog { get; set; }

        public CaptchaEnter()
        {
            InitializeComponent();
            CancelButton.Click += CancelButton_Click;
        }

        public CaptchaEnter(CaptchaEnter captchaEnter)
        {
            Submitted = captchaEnter.Submitted;
            CaptchaUri = captchaEnter.CaptchaUri;
            RedirectUri = captchaEnter.RedirectUri;
            InitializeComponent();
            CancelButton.Click += CancelButton_Click;
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (IsWebCaptcha)
            {
                CaptchaWebView.Visibility = Visibility.Visible;
                CaptchaWebView.NavigationCompleted += CaptchaWebView_NavigationCompleted;
                await InitializeWebView2Async();
            }
            else
            {
                CaptchaImage.Visibility = Visibility.Visible;
                CodeBox.Visibility = Visibility.Visible;
                ConfirmButton.Visibility = Visibility.Visible;

                // Классическая капча: картинка приходит отдельным url, а не в CaptchaUri.
                // Раньше Source вообще не выставлялся — картинка оставалась пустой.
                try
                {
                    if (!string.IsNullOrEmpty(CaptchaUri))
                        CaptchaImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(CaptchaUri));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Captcha] Не удалось загрузить изображение: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        private async Task InitializeWebView2Async()
        {
            try
            {
                // Инициализация WebView2
                await CaptchaWebView.EnsureCoreWebView2Async();

                // Настройки WebView2
                CaptchaWebView.CoreWebView2.Settings.IsScriptEnabled = true;
                CaptchaWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;

                // Подписка на события
                CaptchaWebView.WebMessageReceived += CaptchaWebView_WebMessageReceived;

                // СЕТЕВОЙ ПЕРЕХВАТ (основной путь).
                //
                // Раньше здесь присваивался window.VKCaptcha.onResult — это собственный
                // колбэк капчи, и мы его «отбирали». В него прилетает служебный key сессии
                // (тот же, что в redirect-URL), а не success_token, поэтому VK отвергал код
                // и выдавал новую капчу — бесконечный цикл.
                //
                // Теперь берём success_token из ответа captchaNotRobot.check.
                CaptchaWebView.CoreWebView2.AddWebResourceRequestedFilter(
                    "*", CoreWebView2WebResourceContext.All);

                SubscribeToNetworkEvents(CaptchaWebView.CoreWebView2);

                // ВАЖНО: WebResourceResponseReceived приходит только для главного фрейма.
                // Капча VK ID живёт внутри iframe — там свои события и свой контекст скриптов.
                // Поэтому в каждый созданный фрейм внедряем JS-перехватчик XHR/fetch.
                CaptchaWebView.CoreWebView2.FrameCreated += (s, args) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[Captcha] Создан фрейм: {args.Frame.FrameId} ({args.Frame.Name})");

                    args.Frame.DOMContentLoaded += (_, _) => InjectResultInterceptor(args.Frame);
                };

                // Дублирующий путь: перехват XHR/fetch на странице и во фреймах.
                // Ловит результат, даже если сетевые события WebView2 не пришли.
                CaptchaWebView.CoreWebView2.DOMContentLoaded += async (_, _) =>
                    await InjectResultInterceptor(CaptchaWebView.CoreWebView2);

                await CaptchaWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    ResultInterceptorScript);

                // Загрузка HTML с капчей
                CaptchaWebView.Source = new Uri(RedirectUri);
            }
            catch (Exception ex)
            {
                // Любая ошибка инициализации WebView2 (нет рантайма, битый url и т.п.)
                // не должна оставлять запрос капчи в вечном ожидании.
                System.Diagnostics.Debug.WriteLine(
                    $"[Captcha] Ошибка инициализации WebView2: {ex.GetType().Name}: {ex.Message}");

                Submitted.TrySetResult(null);
                ParentDialog?.Hide();
            }
        }

        /// <summary>
        /// JS-перехватчик результата капчи.
        /// <para>
        /// НЕ трогает <c>VKCaptcha.onResult</c> (там служебный ключ сессии, а не результат).
        /// Перехватывает XHR и fetch, ищет <c>success_token</c> в ответе
        /// <c>captchaNotRobot.check</c> и пересылает его в хост.
        /// </para>
        /// </summary>
        private const string ResultInterceptorScript = @"
            (function() {
                if (window.__vkCaptchaHookInstalled) { return; }
                window.__vkCaptchaHookInstalled = true;

                function post(token) {
                    try {
                        if (token && token.length > 20) {
                            window.chrome.webview.postMessage(JSON.stringify({ type: 'success', token: token }));
                        }
                    } catch (e) {}
                }

                // Только метод проверки капчи. Раньше проверялась подстрока 'captcha',
                // под которую попадал JS-бандл капчи — оттуда бралось имя переменной
                // success_token вместо значения, и в VK уходил мусор.
                var CHECK_METHOD = 'captchaNotRobot.check';

                function isCheckUrl(url) {
                    return !!url && url.indexOf(CHECK_METHOD) !== -1;
                }

                function scan(text) {
                    try {
                        if (!text || text.indexOf('success_token') === -1) { return; }
                        if (text.charAt(0) !== '{') { return; }
                        var data = JSON.parse(text);
                        var holder = data && data.response ? data.response : data;
                        if (holder && typeof holder.success_token === 'string') {
                            post(holder.success_token);
                        }
                    } catch (e) {}
                }

                // XHR
                var open = XMLHttpRequest.prototype.open;
                var send = XMLHttpRequest.prototype.send;
                XMLHttpRequest.prototype.open = function() {
                    try { this.__vkUrl = arguments[1]; } catch (e) {}
                    return open.apply(this, arguments);
                };
                XMLHttpRequest.prototype.send = function() {
                    var self = this;
                    try {
                        self.addEventListener('load', function() {
                            try {
                                if (isCheckUrl(self.__vkUrl)) {
                                    scan(self.responseText);
                                }
                            } catch (e) {}
                        });
                    } catch (e) {}
                    return send.apply(this, arguments);
                };

                // fetch
                if (window.fetch) {
                    var origFetch = window.fetch;
                    window.fetch = function() {
                        var url = arguments[0];
                        if (url && typeof url !== 'string') { url = url.url; }
                        return origFetch.apply(this, arguments).then(function(resp) {
                            try {
                                if (isCheckUrl(url)) {
                                    resp.clone().text().then(scan).catch(function(){});
                                }
                            } catch (e) {}
                            return resp;
                        });
                    };
                }
            })();
        ";

        /// <summary>
        /// URL метода VK, который возвращает РЕАЛЬНЫЙ <c>success_token</c> после решения капчи.
        /// </summary>
        private const string CaptchaCheckMethod = "/method/captchaNotRobot.check";

        /// <summary>
        /// Подписывает конкретный CoreWebView2 (главный фрейм или дочерний)
        /// на событие получения ответа сети и ищет в нём <c>success_token</c>.
        /// </summary>
        private void SubscribeToNetworkEvents(CoreWebView2 core)
        {
            core.WebResourceResponseReceived += async (s, e) =>
            {
                try
                {
                    var uri = e.Request.Uri;

                    // Фронтенд капчи после успеха закрывает сессию этим методом.
                    // Сигналим, чтобы окно не закрылось раньше времени.
                    if (uri.IndexOf(CaptchaEndSessionMethod, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        System.Diagnostics.Debug.WriteLine("[Captcha] endSession выполнен");
                        _endSessionCompleted.TrySetResult(true);
                        return;
                    }

                    // ЖЁСТКИЙ ФИЛЬТР ПО URL.
                    //
                    // Раньше проверялось лишь наличие подстроки "captcha" — под это попадал
                    // JS-бандл веб-капчи (entry.243b7a49.js, ~723 КБ). В нём "success_token"
                    // встречается как ИМЯ ПЕРЕМЕННОЙ, а не как значение. В итоге в VK уходил
                    // случайный кусок JS длиной ~1217 символов, VK его отвергал и выдавал
                    // новую капчу — отсюда цикл и JsonReaderException при парсинге JS как JSON.
                    //
                    // Реальный токен отдаёт ТОЛЬКО этот метод API.
                    if (uri.IndexOf(CaptchaCheckMethod, StringComparison.OrdinalIgnoreCase) < 0)
                        return;

                    if (e.Response == null)
                        return;

                    if (e.Response.StatusCode is < 200 or >= 300)
                        return;

                    // Дополнительная защита: нам нужен JSON, а не скрипт/HTML.
                    string? contentType = null;
                    try
                    {
                        contentType = e.Response.Headers.GetHeader("Content-Type");
                    }
                    catch
                    {
                        // Заголовки могут быть недоступны — ниже всё равно проверим JSON.
                    }

                    if (!string.IsNullOrEmpty(contentType)
                        && contentType.IndexOf("json", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[Captcha] Пропуск {CaptchaCheckMethod}: Content-Type={contentType}");
                        return;
                    }

                    var body = await ReadResponseBodyAsync(e.Response);
                    if (string.IsNullOrEmpty(body))
                        return;

                    // Проверяем, что это действительно JSON-объект, до парсинга.
                    var trimmed = body.TrimStart();
                    if (trimmed.Length == 0 || trimmed[0] != '{')
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[Captcha] Пропуск {CaptchaCheckMethod}: тело не JSON (начинается с '{trimmed.FirstOrDefault()}')");
                        return;
                    }

                    System.Diagnostics.Debug.WriteLine(
                        $"[Captcha] Ответ {CaptchaCheckMethod}: тело {body.Length} символов");

                    using var doc = JsonDocument.Parse(body);
                    if (TryExtractSuccessToken(doc.RootElement, out var token))
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[Captcha] success_token извлечён (длина {token.Length}, начало '{token[..Math.Min(8, token.Length)]}...')");

                        DeliverToken(token);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[Captcha] В ответе {CaptchaCheckMethod} нет валидного success_token");
                    }
                }
                catch (JsonException ex)
                {
                    // Сюда попадать не должны — фильтр по Content-Type и '{' отсекает JS.
                    System.Diagnostics.Debug.WriteLine(
                        $"[Captcha] Тело не является корректным JSON: {ex.Message}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Captcha] Ошибка чтения ответа сети: {ex.GetType().Name}: {ex.Message}");
                }
            };
        }

        /// <summary>Внедряет JS-перехватчик в главный фрейм.</summary>
        private async Task InjectResultInterceptor(CoreWebView2 core)
        {
            try
            {
                await core.ExecuteScriptAsync(ResultInterceptorScript);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Captcha] Не удалось внедрить перехватчик: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Внедряет JS-перехватчик в дочерний фрейм (iframe).
        /// <para>
        /// У <see cref="CoreWebView2Frame"/> нет событий сети, поэтому XHR/fetch
        /// перехватывается скриптом внутри самого фрейма.
        /// </para>
        /// </summary>
        private async Task InjectResultInterceptor(CoreWebView2Frame frame)
        {
            try
            {
                await frame.ExecuteScriptAsync(ResultInterceptorScript);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Captcha] Не удалось внедрить перехватчик во фрейм: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// URL метода, которым фронтенд капчи закрывает сессию после успеха.
        /// </summary>
        private const string CaptchaEndSessionMethod = "captchaNotRobot.endSession";

        /// <summary>
        /// Сигнал о том, что фронтенд капчи завершил сессию (endSession).
        /// Нужен, чтобы не закрыть окно раньше, чем капча корректно завершится на сервере.
        /// </summary>
        private readonly TaskCompletionSource<bool> _endSessionCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Максимальное ожидание endSession. Не влияет на успех входа:
        /// если фронтенд не прислал завершение, просто закрываем окно.
        /// </summary>
        private static readonly TimeSpan EndSessionTimeout = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Закрывает окно после того, как капча завершит сессию на сервере.
        /// <para>
        /// Фронтенд VK ID после получения success_token отправляет
        /// <c>captchaNotRobot.endSession</c> с session_token и ждёт {status:OK}.
        /// Если закрыть WebView2 сразу, этот запрос не успевает уйти и сессия
        /// остаётся незакрытой на сервере.
        /// </para>
        /// </summary>
        private async Task CloseAfterEndSessionAsync()
        {
            try
            {
                var completed = await Task.WhenAny(_endSessionCompleted.Task, Task.Delay(EndSessionTimeout));

                if (completed == _endSessionCompleted.Task)
                {
                    System.Diagnostics.Debug.WriteLine("[Captcha] endSession подтверждён — закрываем окно");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Captcha] endSession не пришёл за {EndSessionTimeout.TotalSeconds:0.#} с — закрываем окно принудительно");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Captcha] Ошибка ожидания endSession: {ex.Message}");
            }

            ParentDialog?.Hide();
        }

        /// <summary>
        /// Читает тело ответа WebView2 как строку.
        /// <para>
        /// ВАЖНО: <c>GetContentAsync()</c> возвращает WinRT-поток (<c>IRandomAccessStream</c>),
        /// а не <c>System.IO.Stream</c>. Прямой каст <c>(System.IO.Stream)</c> падал с
        /// <c>InvalidCastException</c> — из-за этого тело не читалось и токен не находился.
        /// </para>
        /// </summary>
        private static async Task<string> ReadResponseBodyAsync(CoreWebView2WebResourceResponseView response)
        {
            try
            {
                using var content = await response.GetContentAsync();
                if (content == null)
                    return null;

                using var reader = new System.IO.StreamReader(content.AsStreamForRead());
                return await reader.ReadToEndAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Captcha] Не удалось прочитать тело ответа: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Ищет <c>success_token</c> в произвольном JSON-ответе VK ID Captcha.
        /// Ответы бывают обёрнуты: <c>{response:{...}}</c>, <c>{data:{...}}</c> или плоские.
        /// </summary>
        private static bool TryExtractSuccessToken(JsonElement root, out string token)
        {
            token = null;

            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (root.TryGetProperty("success_token", out var direct) && direct.ValueKind == JsonValueKind.String)
            {
                var value = direct.GetString();
                if (IsPlausibleToken(value))
                {
                    token = value;
                    return true;
                }
            }

            foreach (var nested in new[] { "response", "data", "result" })
            {
                if (root.TryGetProperty(nested, out var child)
                    && TryExtractSuccessToken(child, out token))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Проверяет, что строка похожа на токен капчи, а не на служебное значение
        /// (например, короткий ключ сессии из redirect-URL).
        /// <para>
        /// Реальный <c>success_token</c> VK — это JWE: 5 сегментов через точку
        /// (заголовок.ключ.iv.шифртекст.тег), длиной порядка 1000+ символов.
        /// </para>
        /// </summary>
        private static bool IsPlausibleToken(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length < 20)
                return false;

            int dotCount = 0;

            foreach (var c in value)
            {
                bool allowed = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                    || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.' || c == '=';
                if (!allowed)
                    return false;

                if (c == '.')
                    dotCount++;
            }

            // JWE всегда содержит разделители сегментов.
            return dotCount >= 4;
        }

        /// <summary>
        /// Отдаёт найденный токен и закрывает окно (однократно).
        /// <para>
        /// Окно закрывается НЕ сразу: сначала ждём, пока фронтенд капчи отправит
        /// <c>captchaNotRobot.endSession</c>. Иначе сессия остаётся открытой на сервере,
        /// а WebView2 уничтожается вместе с окном раньше завершения запроса.
        /// </para>
        /// </summary>
        private void DeliverToken(string? token)
        {
            if (string.IsNullOrEmpty(token))
                return;

            System.Diagnostics.Debug.WriteLine($"[Captcha] Получен токен веб-капчи (длина {token.Length})");

            CaptchaSolverService.CacheSuccessfulKey(token);

            // Токен отдаём сразу — от него зависит продолжение авторизации.
            // Закрытие окна — отдельно, после endSession.
            if (Submitted.TrySetResult(token))
                _ = CloseAfterEndSessionAsync();
        }

        private void CaptchaWebView_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var message = e.TryGetWebMessageAsString();
                var data = JsonDocument.Parse(message).RootElement;

                if (!data.TryGetProperty("type", out var typeProp))
                    return;

                var type = typeProp.GetString();

                switch (type)
                {
                    case "success":
                        // Принимаем только success_token, прошедший проверку формата.
                        // Раньше сюда попадал любой `key` (служебный ключ сессии),
                        // и VK его отвергал — отсюда бесконечный цикл капчи.
                        if (data.TryGetProperty("token", out var tokenProp))
                        {
                            var value = tokenProp.GetString();
                            if (IsPlausibleToken(value))
                                DeliverToken(value);
                            else
                                System.Diagnostics.Debug.WriteLine(
                                    "[Captcha] Сообщение success с некорректным токеном — игнорируем");
                        }
                        break;

                    case "close":
                        // Пользователь явно закрыл капчу — возвращаем отказ.
                        Submitted.TrySetResult(null);
                        ParentDialog?.Hide();
                        break;

                    case "error":
                        // Ошибка страницы НЕ означает отказ пользователя:
                        // оставляем окно открытым, чтобы можно было попробовать снова.
                        System.Diagnostics.Debug.WriteLine("[Captcha] Страница капчи сообщила об ошибке — окно оставляем");
                        break;

                    default:
                        // Неизвестный тип (отладочный лог страницы и т.п.) — игнорируем.
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Captcha] Ошибка разбора сообщения WebView2: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            // Классическая капча: код вводит пользователь вручную.
            // Обрезаем пробелы и переносы — VK не примет код с ними.
            var code = CodeBox.Text?.Trim();

            if (string.IsNullOrEmpty(code))
                return;

            System.Diagnostics.Debug.WriteLine($"[Captcha] Введён код вручную (длина {code.Length})");

            CaptchaSolverService.CacheSuccessfulKey(code);
            Submitted.TrySetResult(code);
            ParentDialog?.Hide();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Submitted.TrySetResult(null);
            ParentDialog?.Hide();
        }

        // Обработка завершения навигации WebView2.
        private void CaptchaWebView_NavigationCompleted(object sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
        {
            try
            {
                if (!e.IsSuccess)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Captcha] Навигация WebView2 не удалась: {e.WebErrorStatus}");

                    Submitted.TrySetResult(null);
                    ParentDialog?.Hide();
                    return;
                }

                // ВАЖНО: key из query-строки редиректа НЕ извлекаем.
                //
                // Это служебный ключ сессии VK ID, а не success_token — VK его не принимает,
                // и раньше именно он закрывал окно с «успехом» через полсекунды после открытия,
                // после чего VK выдавал новую капчу. Результат решения берём только из
                // success_token (перехват сети в InitializeWebView2Async).
                var uri = CaptchaWebView.Source?.ToString();
                if (!string.IsNullOrEmpty(uri))
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Captcha] Навигация завершена: {new Uri(uri).AbsolutePath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Captcha] Ошибка обработки навигации: {ex.Message}");
            }
        }
    }
}
