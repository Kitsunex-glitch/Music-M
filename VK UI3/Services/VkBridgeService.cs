using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MusicX.Core.Services;
using NLog;
using VK_UI3.Converters;
using VkNet.Abstractions;
using VkNet.Exception;
using VkNet.Utils;

namespace MusicX.Services;

[ClassInterface(ClassInterfaceType.AutoDual)]
[ComVisible(true)]
[SuppressMessage("ReSharper", "InconsistentNaming")]
public class VkBridgeService(IVkApiInvoke vkApi, Logger log, VkService vkService)
{
    #region Events

    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters =
        {
            new VkBoolJsonConverter()
        }
    };

    private static readonly AsyncLocal<string?> _currentRequestId = new();

    private string? _accessToken;

    private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcher;

    public event Action<string>? VKWebAppEvent;

    public void AttachDispatcher(Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void SetNextRequestId(string? requestId)
    {
        _currentRequestId.Value = requestId;
    }

    private void DispatchEvent<T>(string type, T data)
    {
        var json = JsonSerializer.SerializeToNode(new
        {
            type,
            data
        }, _options)!;

        var requestId = _currentRequestId.Value;
        if (!string.IsNullOrEmpty(requestId))
        {
            json["data"]!.AsObject()["request_id"] = requestId;
        }

        var payload = json.ToJsonString();

        if (_dispatcher is not null && !_dispatcher.HasThreadAccess)
        {
            _dispatcher.TryEnqueue(() => VKWebAppEvent?.Invoke(payload));
            return;
        }

        VKWebAppEvent?.Invoke(payload);
    }

    #endregion

    private string _currentAppId = "app0";
    private string _currentUrl = "https://vk.ru/";
    private string _storagePath = "";
    private readonly object _storageLock = new();

    internal void Load(string appId, string url)
    {
        _currentAppId = appId;
        _currentUrl = url;
        _storagePath = Path.Join(StaticService.UserDataFolder.FullName, $"{appId}.json");
        _accessToken ??= TryGetQueryParam(url, "access_token");
    }


    internal void UpdateCurrentUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        _currentUrl = url;
    }

    public void VKWebAppInit()
    {
        DispatchEvent(nameof(VKWebAppInit), new
        {
            Result = 1
        });
    }

    public void VKWebAppGetConfig()
    {
        var isDark = Microsoft.UI.Xaml.Application.Current?.RequestedTheme
            != Microsoft.UI.Xaml.ApplicationTheme.Light;

        const string version = "8.0.0";

        DispatchEvent(nameof(VKWebAppGetConfig), new
        {
            App = "vkclient",
            AppId = _currentAppId,
            AppVersion = version,
            ClientVersion = version,
            Appearance = isDark ? "dark" : "light",
            Scheme = isDark ? "vkcom_dark" : "vkcom_light",
            Platform = "web"
        });
    }

    private static readonly System.Net.Http.HttpClient _vkHttpClient = new();

    public async void VKWebAppCallAPIMethod(string method, string jsonParams)
    {
        try
        {
            var rootObj = string.IsNullOrWhiteSpace(jsonParams)
                ? new JsonObject()
                : JsonNode.Parse(jsonParams)?.AsObject() ?? new JsonObject();

            var actualMethod = !string.IsNullOrWhiteSpace(method)
                ? method
                : rootObj["method"]?.GetValue<string>() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(actualMethod))
                throw new ArgumentException("API method name is missing.", nameof(method));

            var parameters = rootObj.TryGetPropertyValue("params", out var paramsNode) && paramsNode is JsonObject innerParams
                ? innerParams
                : rootObj;

            var form = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var version = "5.131";

            foreach (var (key, value) in parameters)
            {
                if (value is null || string.Equals(key, "method", StringComparison.OrdinalIgnoreCase))
                    continue;

                var valueStr = value.GetValueKind() == JsonValueKind.String
                    ? value.GetValue<string>()!
                    : value.ToJsonString();

                if (string.Equals(key, "v", StringComparison.OrdinalIgnoreCase))
                {
                    version = valueStr;
                    continue;
                }

                form[key] = valueStr;
            }

            if (!form.ContainsKey("api_id")
                && _currentAppId.StartsWith("app", StringComparison.OrdinalIgnoreCase)
                && long.TryParse(_currentAppId.AsSpan(3), out var numericAppId)
                && numericAppId > 0)
            {
                form["api_id"] = numericAppId.ToString();
            }

            if (!form.ContainsKey("access_token"))
            {
                var token = _accessToken ?? TryGetQueryParam(_currentUrl, "access_token");
                if (!string.IsNullOrEmpty(token))
                    form["access_token"] = token;
            }

            var methodName = actualMethod.StartsWith("API.", StringComparison.OrdinalIgnoreCase)
                ? actualMethod[4..]
                : actualMethod;

            var url = new UriBuilder("https", "api.vk.ru")
            {
                Path = $"method/{methodName}",
                Query = $"v={Uri.EscapeDataString(version)}&client_id=6287487&vk_platform=web2"
            }.Uri.AbsoluteUri;

            var parsed = await SendVkApiRequestAsync(url, form);

            if (parsed.TryGetPropertyValue("error", out var errorNode) && errorNode is JsonObject errObj)
            {
                DispatchEvent(nameof(VKWebAppCallAPIMethod), new
                {
                    ErrorType = "api_error",
                    ErrorData = errObj
                });
                return;
            }

            DispatchEvent(nameof(VKWebAppCallAPIMethod), new
            {
                Response = parsed["response"]?.DeepClone() ?? new JsonObject()
            });
        }
        catch (Exception e)
        {
            DispatchException(nameof(VKWebAppCallAPIMethod), e);
        }
    }



    private static async Task<JsonObject> SendVkApiRequestAsync(string url, Dictionary<string, string> form)
    {
        using var content = new System.Net.Http.FormUrlEncodedContent(form);
        using var httpResponse = await _vkHttpClient.PostAsync(url, content);

        var bytes = await httpResponse.Content.ReadAsByteArrayAsync();
        var raw = System.Text.Encoding.UTF8.GetString(bytes);

        if (string.IsNullOrWhiteSpace(raw) || raw.TrimStart().StartsWith('<'))
            throw new Exception($"VK API вернул HTML вместо JSON (HTTP {(int)httpResponse.StatusCode})");

        return JsonNode.Parse(raw)?.AsObject() ?? new JsonObject();
    }





    public void VKWebAppAddToCommunity()
    {
        DispatchEvent(nameof(VKWebAppAddToCommunity), new
        {
            ErrorType = "client_error",
            ErrorData = new
            {
                ErrorCode = 4,
                ErrorReason = "Unsupported operation",
                ErrorDescription = "This operation is not supported"
            }
        });
    }

    public async void VKWebAppGetAuthToken(long appId, string scope)
    {
        try
        {
            var embeddedToken = TryGetQueryParam(_currentUrl, "access_token");

            BridgeLog($"VKWebAppGetAuthToken appId={appId} scope={scope} embedded={(!string.IsNullOrEmpty(embeddedToken) ? "yes" : "no")}");

            var token = !string.IsNullOrEmpty(embeddedToken)
                ? embeddedToken
                : await vkService.GetMiniAppTokenAsync(_currentAppId, _currentUrl, scope);

            if (string.IsNullOrEmpty(token))
            {
                BridgeLog("VKWebAppGetAuthToken: токен не получен");
                DispatchEvent(nameof(VKWebAppGetAuthToken), new
                {
                    ErrorType = "client_error",
                    ErrorData = new
                    {
                        ErrorCode = 1,
                        ErrorReason = "token_not_found",
                        ErrorDescription = "Не удалось получить access_token мини-приложения"
                    }
                });
                return;
            }

            _accessToken = token;

            BridgeLog($"VKWebAppGetAuthToken ok token_len={token.Length}");

            DispatchEvent(nameof(VKWebAppGetAuthToken), new
            {
                AccessToken = token,
                Scope = scope
            });
        }
        catch (Exception e)
        {
            BridgeLog($"VKWebAppGetAuthToken failed: {e.GetType().Name}: {e.Message}");
            DispatchException(nameof(VKWebAppGetAuthToken), e);
        }
    }

    private static string? TryGetQueryParam(string url, string key)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        try
        {
            var queryIndex = url.IndexOf('?');
            if (queryIndex < 0 || queryIndex == url.Length - 1)
                return null;

            var query = url[(queryIndex + 1)..];
            foreach (var pair in query.Split('&'))
            {
                var eq = pair.IndexOf('=');
                if (eq <= 0)
                    continue;

                var name = pair[..eq];
                if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
                    return Uri.UnescapeDataString(pair[(eq + 1)..]);
            }
        }
        catch { }

        return null;
    }

    public async void VKWebAppGetLaunchParams()
    {
        long appId = 0;

        try
        {
            appId = long.Parse(_currentAppId["app".Length..]);
        }
        catch { }

        try
        {
            var response = await vkService.GetMiniAppLaunchParams(appId);

            BridgeLog($"VKWebAppGetLaunchParams appId={appId} ok sign={(!string.IsNullOrEmpty(response?.Sign) ? "yes" : "no")}");

            DispatchEvent(nameof(VKWebAppGetLaunchParams), response);
        }
        catch (Exception e)
        {
            BridgeLog($"VKWebAppGetLaunchParams appId={appId} failed: {e.GetType().Name}: {e.Message} — fallback");

            try
            {
                var user = await vkService.GetCurrentUserAsync();

                DispatchEvent(nameof(VKWebAppGetLaunchParams), new
                {
                    VkUserId = user?.Id ?? 0,
                    VkAppId = appId,
                    VkIsAppUser = true,
                    VkAreNotificationsEnabled = false,
                    VkLanguage = "ru",
                    VkRef = (string?)null,
                    VkAccessTokenSettings = "",
                    VkGroupId = (long?)null,
                    VkViewerGroupRole = (string?)null,
                    VkPlatform = "android",
                    VkIsFavorite = false,
                    VkTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Sign = ""
                });
            }
            catch (Exception inner)
            {
                BridgeLog($"VKWebAppGetLaunchParams fallback failed: {inner.GetType().Name}: {inner.Message}");
                DispatchException(nameof(VKWebAppGetLaunchParams), e);
            }
        }
    }

    public async void VKWebAppGetUserInfo(object? userId, object? userIds)
    {
        try
        {
            var user = await vkService.GetCurrentUserAsync();

            if (user == null)
            {
                BridgeLog("VKWebAppGetUserInfo: user == null — fallback");
                DispatchEvent(nameof(VKWebAppGetUserInfo), new
                {
                    Id = 0L,
                    FirstName = "",
                    LastName = "",
                    Photo100 = (string?)null,
                    Photo200 = (string?)null
                });
                return;
            }

            BridgeLog($"VKWebAppGetUserInfo ok id={user.Id}");

            DispatchEvent(nameof(VKWebAppGetUserInfo), new
            {
                Id = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Photo100 = user.Photo100?.ToString(),
                Photo200 = user.Photo200?.ToString()
            });
        }
        catch (Exception e)
        {
            BridgeLog($"VKWebAppGetUserInfo failed: {e.GetType().Name}: {e.Message}");
            DispatchException(nameof(VKWebAppGetUserInfo), e);
        }
    }

    public async void VKWebAppStorageGet(object[] keys)
    {
        try
        {
            var storage = await ReadStorageAsync();

            DispatchEvent(nameof(VKWebAppStorageGet), new
            {
                Keys = storage.IntersectBy(keys.OfType<string>(), b => b.Key)
                    .Select(b => new { b.Key, b.Value })
                    .ToArray()
            });
        }
        catch (Exception e)
        {
            DispatchException(nameof(VKWebAppStorageGet), e);
        }
    }

    public async void VKWebAppStorageGetKeys(int count, int offset)
    {
        try
        {
            var storage = await ReadStorageAsync();

            DispatchEvent(nameof(VKWebAppStorageGetKeys), new
            {
                Keys = storage
                    .Skip(offset)
                    .Take(count)
                    .Select(b => b.Key)
                    .ToArray()
            });
        }
        catch (Exception e)
        {
            DispatchException(nameof(VKWebAppStorageGetKeys), e);
        }
    }

    public async void VKWebAppStorageSet(string key, string value)
    {
        try
        {
            await WriteStorageAsync(key, value);

            DispatchEvent(nameof(VKWebAppStorageSet), new
            {
                Result = 1
            });
        }
        catch (Exception e)
        {
            DispatchException(nameof(VKWebAppStorageSet), e);
        }
    }

    public void VKWebAppGetClientVersion()
    {
        const string version = "8.0.0";

        BridgeLog($"VKWebAppGetClientVersion -> {version} (app: {StaticService.Version})");

        DispatchEvent(nameof(VKWebAppGetClientVersion), new
        {
            Platform = "web",
            Version = version,
            App = "vkcom",
            Build = "1",
            ClientUserAgent = "WebView2",
            IsGoogleServicesAvailable = true,
            VkClientExists = true
        });
    }

    public void VKWebAppCheckAllowedScopes(string[]? scopes)
    {
        BridgeLog($"VKWebAppCheckAllowedScopes -> {string.Join(",", scopes ?? Array.Empty<string>())}");

        DispatchEvent(nameof(VKWebAppCheckAllowedScopes), new
        {
            AllowedScopes = scopes ?? Array.Empty<string>(),
            DeniedScopes = Array.Empty<string>()
        });
    }

    public void VKWebAppGetGeodata()
    {
        DispatchEvent(nameof(VKWebAppGetGeodata), new
        {
            Available = 0
        });
    }

    public void VKWebAppGetFriends(bool multi)
    {
        DispatchEvent(nameof(VKWebAppGetFriends), new
        {
            Users = Array.Empty<object>()
        });
    }

    /// <summary>
    /// VK Dating (и другие mini apps) шлют handler "SetSupportedHandlers" —
    /// без префикса VKWebApp. Ответ должен иметь type, равный handler запроса,
    /// иначе VK Bridge SDK не сопоставит ответ и приложение зависнет на splash.
    /// </summary>
    public void SetSupportedHandlers()
    {
        var supported = new[]
        {
            "VKWebAppInit",
            "VKWebAppGetConfig",
            "VKWebAppGetClientVersion",
            "VKWebAppGetLaunchParams",
            "VKWebAppGetUserInfo",
            "VKWebAppGetAuthToken",
            "VKWebAppCallAPIMethod",
            "VKWebAppStorageGet",
            "VKWebAppStorageGetKeys",
            "VKWebAppStorageSet",
            "VKWebAppCheckAllowedScopes",
            "VKWebAppGetGeodata",
            "VKWebAppGetFriends",
            "VKWebAppSetSupportedHandlers"
        };

        BridgeLog($"SetSupportedHandlers -> {string.Join(",", supported)}");

        // ВАЖНО: type должен совпадать с handler запроса ("SetSupportedHandlers").
        DispatchEvent(nameof(SetSupportedHandlers), new SetSupportedHandlersResponse(supported));
    }

    private record SetSupportedHandlersResponse([property: JsonPropertyName("supportedHandlers")] string[] SupportedHandlers);

    private void DispatchException(string methodName, Exception e)
    {
        if (e is VkApiException apiException)
        {
            DispatchEvent(methodName, new
            {
                ErrorType = "api_error",
                ErrorData = new
                {
                    apiException.ErrorCode,
                    ErrorMsg = apiException.Message,
                    RequestParams = apiException.RequestParams?.Keys
                }
            });

            return;
        }

        DispatchEvent(methodName, new
        {
            ErrorType = "client_error",
            ErrorData = new
            {
                ErrorCode = 1,
                ErrorReason = e.Message,
                ErrorDescription = $"{e.GetType()}: {e.Message}"
            }
        });

        log.Error(e, "Failed to call {Method} from bridge call", methodName);
    }

    private IReadOnlyDictionary<string, string>? _storageCache;

    private async Task<IReadOnlyDictionary<string, string>> ReadStorageAsync()
    {
        lock (_storageLock)
        {
            if (_storageCache is not null)
                return _storageCache;
        }

        if (!File.Exists(_storagePath))
        {
            lock (_storageLock)
            {
                _storageCache = ImmutableDictionary<string, string>.Empty;
            }
            return _storageCache;
        }

        try
        {
            await using var stream = File.OpenRead(_storagePath);
            var result = await JsonSerializer.DeserializeAsync<IReadOnlyDictionary<string, string>>(stream);

            lock (_storageLock)
            {
                _storageCache = result ?? ImmutableDictionary<string, string>.Empty;
            }
            return _storageCache;
        }
        catch
        {
            lock (_storageLock)
            {
                _storageCache = ImmutableDictionary<string, string>.Empty;
            }
            return _storageCache;
        }
    }

    private static void BridgeLog(string line)
    {
        try
        {
            var logPath = VK_UI3.Helpers.AppDataPaths.GetFile("miniapp-bridge.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] [bridge] {line}\n");
        }
        catch { }
    }

    private async Task WriteStorageAsync(string key, string value)
    {
        ImmutableDictionary<string, string> storage;
        lock (_storageLock)
        {
            storage = _storageCache as ImmutableDictionary<string, string>
                      ?? (_storageCache?.ToImmutableDictionary() ?? ImmutableDictionary<string, string>.Empty);
        }

        var modified = storage.SetItem(key, value);

        lock (_storageLock)
        {
            _storageCache = modified;
        }

        var tmpPath = _storagePath + ".tmp";
        try
        {
            await using (var stream = File.Create(tmpPath))
            {
                await JsonSerializer.SerializeAsync(stream, modified);
            }

            if (File.Exists(_storagePath))
                File.Replace(tmpPath, _storagePath, null);
            else
                File.Move(tmpPath, _storagePath);
        }
        catch
        {
            try { File.Delete(tmpPath); } catch { }
            throw;
        }
    }
}