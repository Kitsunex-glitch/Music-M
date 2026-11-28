using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.Web.WebView2.Core;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Windows.Web.Http;
using MvvmHelpers;
using MusicX.Services;
using Microsoft.Extensions.DependencyInjection;

namespace VK_UI3.Views
{
    public class BridgeCallEntry
    {
        public string Time { get; init; } = string.Empty;
        public string Handler { get; init; } = string.Empty;
        public string Params { get; init; } = string.Empty;
        public string Result { get; init; } = string.Empty;
        public bool IsError { get; init; }
        public bool IsUnhandled { get; init; }

        public string Summary => $"{Time}  {Handler}";

        public string Details => IsError
            ? $"✗ → {Result}"
            : $"→ {Result}";
    }

    public class MiniAppViewModel(string appId, string url) : ObservableObject
    {
        public string AppId { get; } = appId;
        public string Url { get; } = url;

        public System.Collections.ObjectModel.ObservableCollection<BridgeCallEntry> BridgeCalls { get; } = new();

        private int _unhandledCount;
        public int UnhandledCount
        {
            get => _unhandledCount;
            set => SetProperty(ref _unhandledCount, value);
        }

        private bool _isLoading = true;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        private string? _errorMessage;
        public string? ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        private string _errorDetails = string.Empty;
        public string ErrorDetails
        {
            get => _errorDetails;
            set => SetProperty(ref _errorDetails, value);
        }
    }

    public sealed partial class MiniAppView : Page
    {
        private MiniAppViewModel ViewModel { get; set; }
        private bool _initialized;
        private VkBridgeService _bridgeService;
        private Action<string> _eventForwardHandler;
        private string? _lastBridgeUrl;
        private bool _isLegacyGame;
        private bool _retriedNavigation;

        private readonly object _callsLock = new();
        private DateTime _lastScrollAt = DateTime.MinValue;

        // Флаг «инспектор открыт» — чтобы не тратить время на ObservableCollection,
        // когда панель закрыта (это главный источник задержек ответов моста).
        private bool _inspectorVisible;

        public MiniAppView(MiniAppViewModel viewModel)
        {
            DataContext = ViewModel = viewModel;
            InitializeComponent();
            Loaded += MiniAppView_Loaded;
            Unloaded += MiniAppView_Unloaded;
        }

        public MiniAppView()
        {
            InitializeComponent();
            Loaded += MiniAppView_Loaded;
            Unloaded += MiniAppView_Unloaded;
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is MiniAppViewModel vm && ViewModel == null)
            {
                DataContext = ViewModel = vm;
                BindCallsList();
            }
        }

        private void BindCallsList()
        {
            if (CallsList != null && ViewModel != null)
                CallsList.ItemsSource = ViewModel.BridgeCalls;
        }

        private void MiniAppView_Loaded(object sender, RoutedEventArgs e)
        {
            if (_initialized) return;
            if (ViewModel == null) return;

            _initialized = true;
            BindCallsList();
            UpdateUnhandledCounter();
            LoadAsync().SafeFireAndForget();
        }

        private async Task LoadAsync()
        {
            try
            {
                var userDataFolder = VK_UI3.Helpers.AppDataPaths.GetFolder("WebView2");

                var environment = await CoreWebView2Environment.CreateWithOptionsAsync(
                    null,
                    userDataFolder,
                    new CoreWebView2EnvironmentOptions());

                await WebView.EnsureCoreWebView2Async(environment);

                try
                {
                    var profile = WebView.CoreWebView2.Profile;
                    profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark;
                }
                catch (Exception profileEx)
                {
                }

                var settings = WebView.CoreWebView2.Settings;
                settings.AreDevToolsEnabled = true;
                settings.IsStatusBarEnabled = false;
                settings.AreBrowserAcceleratorKeysEnabled = true;
                settings.IsPasswordAutosaveEnabled = false;
                settings.IsGeneralAutofillEnabled = false;
                settings.IsZoomControlEnabled = false;
                settings.IsBuiltInErrorPageEnabled = false;
                settings.IsWebMessageEnabled = true;
                settings.IsScriptEnabled = true;
                settings.AreHostObjectsAllowed = true;
                settings.AreDefaultScriptDialogsEnabled = true;

                // Скрипт 1: console.* + error/unhandledrejection -> host.
                await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
        """
(function () {
    const send = (level, args) => {
        try {
            const text = Array.prototype.map.call(args, (a) => {
                if (typeof a === 'string') return a
                try { return JSON.stringify(a) } catch (e) { return String(a) }
            }).join(' ')
            if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage(JSON.stringify({ bridge: true, handler: '__console', params: { level: level, text: text, url: location.href } }))
            }
        } catch (e) { }
    }

    ['error', 'warn'].forEach((level) => {
        const original = console[level]
        console[level] = function () {
            send(level, arguments)
            if (original) original.apply(console, arguments)
        }
    })

    window.addEventListener('error', (e) => send('error', ['[js-error] ' + e.message]))
    window.addEventListener('unhandledrejection', (e) => {
        const reason = e && e.reason
        send('error', ['[js-reject] ' + (reason && reason.message ? reason.message : reason)])
    })
})();
""");

                _isLegacyGame = IsLegacyGameUrl(ViewModel.Url);

                if (_isLegacyGame)
                {
                    await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
        """
(function () {
    const log = (channel, payload) => {
        try {
            console.error('[native-stub] ' + channel + ' ' + (typeof payload === 'string' ? payload : JSON.stringify(payload)))
        } catch (e) { }
    }

    if (!window.android_MainHost) {
        window.android_MainHost = {
            postMessage: (msg) => log('android_MainHost.postMessage', msg),
            getToken: () => { log('android_MainHost.getToken', ''); return '' },
            getLaunchParams: () => { log('android_MainHost.getLaunchParams', ''); return '' }
        }
    }

    if (!window.webkit) window.webkit = {}
    if (!window.webkit.messageHandlers) window.webkit.messageHandlers = {}
    if (!window.webkit.messageHandlers.main) {
        window.webkit.messageHandlers.main = {
            postMessage: (msg) => log('webkit.main.postMessage', msg)
        }
    }
})();
""");
                }

                _bridgeService = StaticService.Container.GetRequiredService<VkBridgeService>();
                _bridgeService.Load(ViewModel.AppId, ViewModel.Url);
                _bridgeService.AttachDispatcher(DispatcherQueue);

                WebView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
                WebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

                WebView.CoreWebView2.SourceChanged -= CoreWebView2_SourceChanged;
                WebView.CoreWebView2.SourceChanged += CoreWebView2_SourceChanged;
                WebView.CoreWebView2.FrameNavigationCompleted -= CoreWebView2_FrameNavigationCompleted;
                WebView.CoreWebView2.FrameNavigationCompleted += CoreWebView2_FrameNavigationCompleted;

                _eventForwardHandler = json =>
                {
                    try
                    {
                        // НЕ логируем синхронно — это блокирует отправку ответа в JS.
                        // LogBridge($"<<< event {json}");
                        WebView.CoreWebView2?.PostWebMessageAsJson(json);
                    }
                    catch
                    {
                        // WebView2 может быть уже закрыт — игнорируем.
                    }
                };
                _bridgeService.VKWebAppEvent -= _eventForwardHandler;
                _bridgeService.VKWebAppEvent += _eventForwardHandler;

                
                {
                    await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
        """
const requestPropsMap = {
  VKWebAppInit: () => [],
  VKWebAppAddToCommunity: () => [],
  VKWebAppAddToHomeScreen: () => [],
  VKWebAppAddToHomeScreenInfo: () => [],
  VKWebAppAllowMessagesFromGroup: ({group_id, key}) => [group_id, key],
  VKWebAppAllowNotifications: () => [],
  OKWebAppCallAPIMethod: () => [],
  VKWebAppCallAPIMethod: ({method, params}) => [method, JSON.stringify(params)],
  VKWebAppCopyText: ({text}) => [text],
  VKWebAppCreateHash: ({payload}) => [payload],
  VKWebAppDownloadFile: ({url, filename}) => [url, filename],
  VKWebAppGetAuthToken: ({app_id, scope}) => [app_id, scope],
  VKWebAppClose: () => [status, JSON.stringify(payload)],
  VKWebAppOpenApp: ({app_id, location}) => [app_id, location],
  VKWebAppDenyNotifications: () => [],
  VKWebAppFlashGetInfo: () => [],
  VKWebAppFlashSetLevel: ({level}) => [level],
  VKWebAppGetClientVersion: () => [],
  VKWebAppGetCommunityToken: ({app_id, group_id, scope}) => [app_id, group_id, scope],
  VKWebAppGetConfig: () => [],
  VKWebAppGetLaunchParams: () => [],
  VKWebAppAudioPause: () => [],
  VKWebAppGetEmail: () => [],
  VKWebAppGetFriends: ({multi}) => [multi],
  VKWebAppGetGeodata: () => [],
  VKWebAppGetGrantedPermissions: () => [],
  VKWebAppGetPersonalCard: ({type}) => [type],
  VKWebAppGetPhoneNumber: () => [],
  VKWebAppGetUserInfo: ({user_id, user_ids}) => [user_id, user_ids],
  VKWebAppJoinGroup: ({group_id}) => [group_id],
  VKWebAppLeaveGroup: ({group_id}) => [group_id],
  VKWebAppAddToMenu: () => [],
  VKWebAppOpenCodeReader: () => [],
  VKWebAppOpenContacts: () => [],
  VKWebAppOpenPayForm: () => [],
  VKWebAppOpenQR: () => [],
  VKWebAppResizeWindow: ({width, height}) => [width, height],
  VKWebAppScroll: ({top, speed}) => [top, speed],
  VKWebAppSendToClient: ({fragment}) => [fragment],
  VKWebAppSetLocation: ({location, replace_state}) => [location, replace_state],
  VKWebAppSetViewSettings: ({status_bar_style, action_bar_color, navigation_bar_color}) => [status_bar_style, action_bar_color, navigation_bar_color],
  VKWebAppShare: ({link}) => [link],
  VKWebAppShowCommunityWidgetPreviewBox: ({type, group_id, code}) => [type, group_id, code],
  VKWebAppShowImages: ({images, start_index}) => [images, start_index],
  VKWebAppShowInviteBox: () => [],
  VKWebAppShowLeaderBoardBox: ({user_result}) => [user_result],
  VKWebAppShowMessageBox: () => [],
  VKWebAppCheckBannerAd: () => [],
  VKWebAppHideBannerAd: () => [],
  VKWebAppShowBannerAd: () => [],
  VKWebAppShowNativeAds: () => [],
  VKWebAppCheckNativeAds:() => [],
  VKWebAppShowOrderBox:() => [],
  VKWebAppShowRequestBox:() => [],
  VKWebAppShowWallPostBox:() => [],
  VKWebAppShowSubscriptionBox:() => [],
  VKWebAppOpenWallPost: ({post_id, owner_id}) => [post_id, owner_id],
  VKWebAppStorageGet: ({keys}) => [keys],
  VKWebAppStorageGetKeys: ({count, offset}) => [count, offset],
  VKWebAppStorageSet: ({key, value}) => [key, value],
  VKWebAppTapticImpactOccurred: ({style}) => [style],
  VKWebAppTapticNotificationOccurred: ({type}) => [type],
  VKWebAppTapticSelectionChanged: () => [],
  VKWebAppAddToFavorites: () => [],
  VKWebAppSendPayload: ({group_id, payload}) => [group_id, payload],
  VKWebAppDisableSwipeBack: () => [],
  VKWebAppEnableSwipeBack: () => [],
  VKWebAppSetSwipeSettings: ({history}) => [history],
  VKWebAppShowStoryBox: () => [],
  VKWebAppAccelerometerStart: ({refresh_rate}) => [refresh_rate],
  VKWebAppAccelerometerStop: () => [],
  VKWebAppGyroscopeStart: () => [],
  VKWebAppGyroscopeStop: () => [],
  VKWebAppDeviceMotionStart: () => [],
  VKWebAppDeviceMotionStop: () => [],
  VKWebAppSubscribeStoryApp: () => [],
  VKWebAppGetGroupInfo: ({group_id}) => [group_id],
  VKWebAppLibverifyRequest: () => [],
  VKWebAppLibverifyCheck: () => [],
  VKWebAppRetargetingPixel: () => [],
  VKWebAppCheckAllowedScopes: ({scopes}) => [scopes],
  VKWebAppConversionHit: () => [],
  VKWebAppCheckSurvey: () => [],
  VKWebAppShowSurvey: () => [],
  VKWebAppScrollTop: () => [],
  VKWebAppScrollTopStart: () => [],
  VKWebAppScrollTopStop: () => [],
  VKWebAppShowSlidesSheet: () => [],
  VKWebAppTranslate: () => [],
  VKWebAppCallStart: () => [],
  VKWebAppCallJoin: () => [],
  VKWebAppCallGetStatus: () => [],
  VKWebAppRecommend: () => [],
  VKWebAppAddToProfile: () => [],
  VKWebAppSetSupportedHandlers: () => [],
  VKWebAppTrackEvent: () => []
};

(function () {
    const sendToHost = (message) => {
        try {
            const parsed = JSON.parse(message)
            const handler = parsed.handler
            const params = parsed.params
            if (!requestPropsMap[handler]) {
                if (window.chrome && window.chrome.webview) {
                    window.chrome.webview.postMessage(JSON.stringify({ bridge: true, handler, params }))
                }
                return
            }
            if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage(JSON.stringify({ bridge: true, handler, params }))
            }
        } catch (e) {
            console.error('Error calling bridge', e)
        }
    }

    if (!window.ReactNativeWebView) {
        window.ReactNativeWebView = { postMessage: sendToHost }
        if (window.parent === window) {
        const origPostMessage = window.postMessage.bind(window);
        window.parent.postMessage = function (message, targetOrigin) {
            if (message && typeof message === 'object' && message.handler) {
                sendToHost(JSON.stringify(message));
                return;
            }
            origPostMessage(message, targetOrigin || '*');
        };
    }

    } else if (!window.ReactNativeWebView.postMessage) {
        window.ReactNativeWebView.postMessage = sendToHost
    }
})();

if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener('message', (event) => {
        const ev = new Event('message')
        ev.data = typeof event.data === 'string' ? event.data : JSON.stringify(event.data)
        // VK Bridge SDK слушает window ('message'). Диспатчим на window,
        // иначе ответы моста до JS-SDK не доходят.
        window.dispatchEvent(ev)
    })
}
""");
                }

                NavigateToLaunchUrl(ViewModel.Url);
            }
            catch (Exception ex)
            {
                ShowError("Не удалось запустить мини-приложение", $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        private void NavigateToLaunchUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return;

            WebView.CoreWebView2.Navigate(uri.AbsoluteUri);
        }
        private static bool IsLegacyGameUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;

            return url.Contains("api.php", StringComparison.OrdinalIgnoreCase)
                || url.Contains("html5_game", StringComparison.OrdinalIgnoreCase)
                || url.Contains("api_url=", StringComparison.OrdinalIgnoreCase);
        }

        internal void OpenDevTools()
        {
            try
            {
                WebView.CoreWebView2?.OpenDevToolsWindow();
            }
            catch (Exception ex)
            {
            }
        }

        private async void CoreWebView2_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                string json;
                try
                {
                    json = args.TryGetWebMessageAsString();
                }
                catch
                {
                    json = args.WebMessageAsJson;
                }


                var node = JsonNode.Parse(json)?.AsObject();
                if (node == null)
                    return;

                if (node["bridge"]?.GetValue<bool>() != true)
                    return;

                var handler = node["handler"]?.GetValue<string>();
                var paramsNode = node["params"] as JsonObject;

                if (string.IsNullOrEmpty(handler))
                    return;

                if (handler == "__console")
                {
                    var level = paramsNode?["level"]?.ToString();
                    var text = paramsNode?["text"]?.ToString();
                    var pageUrl = paramsNode?["url"]?.ToString();
                    return;
                }

                if (_bridgeService == null)
                    return;


                var requestId = node["request_id"]?.ToString()
                                ?? paramsNode?["request_id"]?.ToString();

                AddCallEntry(handler, paramsNode?.ToJsonString(), "…", false, false);

                if (!string.IsNullOrEmpty(requestId))
                    _bridgeService.SetNextRequestId(requestId);

                await InvokeBridgeHandlerAsync(handler, paramsNode, requestId);

                // Сбрасываем request_id после обработки, чтобы он не прилип
                // к следующему вызову в том же async-контексте.
                _bridgeService.SetNextRequestId(null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MiniAppView] Ошибка обработки сообщения моста: {ex}");
            }
        }

        private Task InvokeBridgeHandlerAsync(string handler, JsonObject paramsNode, string? requestId)
        {
            MethodInfo? method;
            try
            {
                method = _bridgeService.GetType().GetMethod(handler,
                    BindingFlags.Public | BindingFlags.Instance);
            }
            catch (AmbiguousMatchException)
            {
                method = _bridgeService.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => string.Equals(m.Name, handler, StringComparison.Ordinal));
            }

            if (method == null)
            {
                AddCallEntry(handler, paramsNode?.ToJsonString(),
                    "НЕ ПОДДЕРЖИВАЕТСЯ клиентом (client_error)", true, true);

                try
                {
                    var errorPayload = JsonSerializer.Serialize(new
                    {
                        type = handler,
                        data = new
                        {
                            error_type = "client_error",
                            error_data = new
                            {
                                error_code = 1,
                                error_reason = "unsupported_method",
                                error_description = $"Метод {handler} не поддерживается клиентом"
                            },
                            request_id = requestId
                        }
                    });

                    WebView.CoreWebView2?.PostWebMessageAsJson(errorPayload);
                }
                catch { }

                return Task.CompletedTask;
            }

            var methodParams = method.GetParameters();
            var arguments = new object[methodParams.Length];

            for (var i = 0; i < methodParams.Length; i++)
            {
                var parameter = methodParams[i];

                if (string.Equals(parameter.Name, "jsonParams", StringComparison.OrdinalIgnoreCase))
                {
                    arguments[i] = paramsNode?.ToJsonString();
                    continue;
                }

                var argName = ToSnakeCase(parameter.Name);
                JsonNode? argNode = null;
                paramsNode?.TryGetPropertyValue(argName, out argNode);

                arguments[i] = ConvertJsonArgument(argNode, parameter.ParameterType);
            }

            try
            {
                method.Invoke(_bridgeService, arguments);
            }
            catch (TargetInvocationException tie)
            {
                var inner = tie.InnerException ?? tie;
                AddCallEntry(handler, paramsNode?.ToJsonString(),
                    $"{inner.GetType().Name}: {inner.Message}", true, false);

                try
                {
                    var errorPayload = JsonSerializer.Serialize(new
                    {
                        type = handler,
                        data = new
                        {
                            error_type = "client_error",
                            error_data = new
                            {
                                error_code = 2,
                                error_reason = "handler_exception",
                                error_description = inner.Message
                            },
                            request_id = requestId
                        }
                    });
                    WebView.CoreWebView2?.PostWebMessageAsJson(errorPayload);
                }
                catch { }
            }

            return Task.CompletedTask;
        }

        private static object? ConvertJsonArgument(JsonNode? node, Type targetType)
        {
            if (targetType == typeof(string))
                return node?.ToString();

            if (node == null)
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

            if (targetType == typeof(int))
                return int.TryParse(node.ToString(), out var i) ? i : 0;

            if (targetType == typeof(long))
                return long.TryParse(node.ToString(), out var l) ? l : 0L;

            if (targetType == typeof(bool))
                return bool.TryParse(node.ToString(), out var b) ? b : false;

            if (targetType == typeof(double))
                return double.TryParse(node.ToString(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0d;

            if (targetType == typeof(object[]))
            {
                return node is JsonArray arr
                    ? arr.Select(x => (object?)x?.ToString()).ToArray()
                    : Array.Empty<object>();
            }

            return node.ToString();
        }

        private static string ToSnakeCase(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;

            var sb = new System.Text.StringBuilder();
            foreach (var c in name)
            {
                if (char.IsUpper(c))
                {
                    if (sb.Length > 0)
                        sb.Append('_');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private void CoreWebView2_SourceChanged(CoreWebView2 sender, CoreWebView2SourceChangedEventArgs args)
        {
            UpdateBridgeUrl(sender?.Source);
        }

        private void CoreWebView2_FrameNavigationCompleted(CoreWebView2 sender,
            CoreWebView2NavigationCompletedEventArgs args)
        {
            try
            {
                var frameUrl = sender?.Source;             

                UpdateBridgeUrl(sender?.Source);
            }
            catch { }
        }

        private void UpdateBridgeUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url) || _bridgeService == null)
                return;

            if (string.Equals(_lastBridgeUrl, url, StringComparison.Ordinal))
                return;

            _lastBridgeUrl = url;
            _bridgeService.UpdateCurrentUrl(url);
        }

        private void WebView_OnNavigationStarting(WebView2 sender, CoreWebView2NavigationStartingEventArgs args)
        {
            ViewModel.IsLoading = true;
            ViewModel.ErrorMessage = null;
            ViewModel.ErrorDetails = string.Empty;


            _retriedNavigation = false;

            UpdateBridgeUrl(args.Uri);
            UpdateVisualState();
        }

        private void WebView_OnNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            ViewModel.IsLoading = false;



            if (!args.IsSuccess && args.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
            {
                if (!_retriedNavigation)
                {
                    _retriedNavigation = true;

                    _ = RetryNavigationAsync();
                    return;
                }

                ViewModel.ErrorMessage = "Произошла ошибка при загрузке страницы";
                ViewModel.ErrorDetails = args.HttpStatusCode == 0
                    ? args.WebErrorStatus.ToString()
                    : $"Код ошибки сервера: {(HttpStatusCode)args.HttpStatusCode}";
            }
            else if (args.IsSuccess)
            {
                _retriedNavigation = false;
            }

            UpdateVisualState();
        }

        private async Task RetryNavigationAsync()
        {
            try
            {
                await WebView.CoreWebView2.Profile.ClearBrowsingDataAsync(
                    CoreWebView2BrowsingDataKinds.DiskCache |
                    CoreWebView2BrowsingDataKinds.CacheStorage);

                ViewModel.IsLoading = true;
                UpdateVisualState();

                NavigateToLaunchUrl(_lastBridgeUrl ?? ViewModel.Url);
            }
            catch (Exception ex)
            {

                ViewModel.IsLoading = false;
                ShowError("Не удалось загрузить страницу", $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        private static readonly object _logLock = new();
        private const long MaxLogBytes = 5 * 1024 * 1024;



        private void DevToolsButton_Click(object sender, RoutedEventArgs e)
        {
            OpenDevTools();
        }

        private void BridgeLogButton_Click(object sender, RoutedEventArgs e)
        {
            _inspectorVisible = !_inspectorVisible;
            BridgeInspector.Visibility = _inspectorVisible
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (_inspectorVisible)
                UpdateUnhandledCounter();
        }

        private void CloseInspectorButton_Click(object sender, RoutedEventArgs e)
        {
            _inspectorVisible = false;
            BridgeInspector.Visibility = Visibility.Collapsed;
        }

        private void ClearCallsButton_Click(object sender, RoutedEventArgs e)
        {
            lock (_callsLock)
            {
                ViewModel.BridgeCalls.Clear();
                ViewModel.UnhandledCount = 0;
            }
            UpdateUnhandledCounter();
        }

        private void AddCallEntry(string handler, string? parameters, string? result, bool isError, bool isUnhandled)
        {
            // Если инспектор закрыт — не тратим время на ObservableCollection
            // и перерисовку ListView. Это главный источник задержек ответов моста.
            if (!_inspectorVisible)
                return;

            try
            {
                lock (_callsLock)
                {
                    ViewModel.BridgeCalls.Add(new BridgeCallEntry
                    {
                        Time = DateTime.Now.ToString("HH:mm:ss.fff"),
                        Handler = handler,
                        Params = parameters ?? string.Empty,
                        Result = result ?? string.Empty,
                        IsError = isError,
                        IsUnhandled = isUnhandled
                    });

                    while (ViewModel.BridgeCalls.Count > 300)
                        ViewModel.BridgeCalls.RemoveAt(0);

                    if (isUnhandled)
                    {
                        ViewModel.UnhandledCount++;
                        UpdateUnhandledCounter();
                    }
                }

                ScrollInspectorToEnd();
            }
            catch { }
        }

        private void ScrollInspectorToEnd()
        {
            try
            {
                if (!_inspectorVisible || ViewModel.BridgeCalls.Count == 0)
                    return;

                var now = DateTime.UtcNow;
                if ((now - _lastScrollAt).TotalMilliseconds < 250)
                    return;
                _lastScrollAt = now;

                CallsList.ScrollIntoView(ViewModel.BridgeCalls[^1]);
            }
            catch { }
        }

        private void UpdateUnhandledCounter()
        {
            try
            {
                UnhandledCounter.Text = $"вызовов: {ViewModel.BridgeCalls.Count}, " +
                                        $"неподдержанных: {ViewModel.UnhandledCount}";
            }
            catch { }
        }

        private void ShowError(string message, string details)
        {
            ViewModel.IsLoading = false;
            ViewModel.ErrorMessage = message;
            ViewModel.ErrorDetails = details;
            UpdateVisualState();
        }

        private void UpdateVisualState()
        {
            var hasError = !string.IsNullOrEmpty(ViewModel.ErrorMessage);

            LoadingRing.IsActive = ViewModel.IsLoading && !hasError;
            LoadingRing.Visibility = (ViewModel.IsLoading && !hasError)
                ? Visibility.Visible
                : Visibility.Collapsed;

            WebView.Visibility = hasError ? Visibility.Collapsed : Visibility.Visible;

            ErrorPanel.Visibility = hasError ? Visibility.Visible : Visibility.Collapsed;
            ErrorTitle.Text = ViewModel.ErrorMessage ?? string.Empty;
            ErrorDetailsText.Text = ViewModel.ErrorDetails ?? string.Empty;
        }

        private void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ErrorMessage = null;
            ViewModel.ErrorDetails = string.Empty;
            ViewModel.IsLoading = true;
            UpdateVisualState();

            try
            {
                if (WebView.CoreWebView2 != null)
                    WebView.CoreWebView2.Navigate(_lastBridgeUrl ?? ViewModel.Url);
            }
            catch (Exception ex)
            {
                ShowError("Не удалось перезагрузить мини-приложение", $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        private void MiniAppView_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_bridgeService != null && _eventForwardHandler != null)
                    _bridgeService.VKWebAppEvent -= _eventForwardHandler;
            }
            catch { }

            try
            {
                if (WebView?.CoreWebView2 != null)
                {
                    WebView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
                    WebView.CoreWebView2.SourceChanged -= CoreWebView2_SourceChanged;
                    WebView.CoreWebView2.FrameNavigationCompleted -= CoreWebView2_FrameNavigationCompleted;
                }
            }
            catch { }

            try
            {
                WebView.Close();
            }
            catch { }

            _initialized = false;
        }
    }

    public class StringNullOrEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var reverse = parameter?.ToString() == "reverse";
            var isEmpty = string.IsNullOrEmpty(value?.ToString());

            return (isEmpty ^ reverse) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}