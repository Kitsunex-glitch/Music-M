using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Data.Json;
using VK_UI3.Views.Notification;
using VK_UI3.DB;

namespace VK_UI3.Helpers
{
    public class NotificationItem
    {
        public int Id { get; set; }
        public string Header { get; set; }
        public string Message { get; set; }
        public List<NotificationLink> Links { get; set; } = new List<NotificationLink>();
        // Новые поля для расширенного формата
        public NotificationType Type { get; set; } = NotificationType.Standard;
        public List<NotificationButton> Buttons { get; set; } = new List<NotificationButton>();
        public NotificationInput Input { get; set; }
        public bool ShowUntilAnswered { get; set; }
        public string DismissButton { get; set; }
    }

    public class NotificationLink
    {
        public string Name { get; set; }
        public string Url { get; set; }
    }

    public class NotificationButton
    {
        public string Text { get; set; }
        public ButtonActionType Action { get; set; }
        public string Value { get; set; }
    }

    public class NotificationInput
    {
        public InputFieldType Type { get; set; }
        public string Placeholder { get; set; }
        public bool Required { get; set; }
        public int? MaxLength { get; set; }
    }

    internal class NotificationsGetter
    {
        private const string NotificationsUrl = "https://vkm.example.com/notifications.json";
        private HttpClient _client;

        public NotificationsGetter()
        {
            _client = new HttpClient();
            // Можно добавить таймаут при необходимости
            // _client.Timeout = TimeSpan.FromSeconds(10);
        }

        public async Task<List<NotificationItem>> GetNotificationsAsync()
        {
            try
            {
                HttpResponseMessage response = await _client.GetAsync(NotificationsUrl);
                response.EnsureSuccessStatusCode();

                string jsonString = await response.Content.ReadAsStringAsync();
                return ParseNotifications(jsonString);
            }
            catch (HttpRequestException ex)
            {
                // Логирование ошибки сети
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки уведомлений: {ex.Message}");
                return new List<NotificationItem>();
            }
            catch (Exception ex)
            {
                // Логирование других ошибок
                System.Diagnostics.Debug.WriteLine($"Ошибка обработки уведомлений: {ex.Message}");
                return new List<NotificationItem>();
            }
        }

        private List<NotificationItem> ParseNotifications(string jsonString)
        {
            var notifications = new List<NotificationItem>();
            var lastIdStr = DB.SettingsTable.GetSetting("noteLastId");
            var lastID = 0;
            if (lastIdStr != null && int.TryParse(lastIdStr.settingValue, out int id))
            {
                lastID = id;
            }
               
            try
            {
                JsonArray jsonArray = JsonArray.Parse(jsonString);

                foreach (IJsonValue item in jsonArray)
                {
                    if (item.ValueType == JsonValueType.Object)
                    {
                        JsonObject jsonObject = item.GetObject();
                        var notification = new NotificationItem();

                        // Парсинг ID (сохраняем во временную переменную, пока не знаем тип)
                        int notificationId = 0;
                        if (jsonObject.ContainsKey("id"))
                        {
                            notificationId = (int)jsonObject["id"].GetNumber();
                            // Пока не устанавливаем notification.Id, сделаем позже после проверок
                        }

                        if (jsonObject.ContainsKey("Header"))
                            notification.Header = jsonObject["Header"].GetString();

                        if (jsonObject.ContainsKey("Message"))
                            notification.Message = jsonObject["Message"].GetString();

                        // Определение типа уведомления
                        if (jsonObject.ContainsKey("type"))
                        {
                            string typeStr = jsonObject["type"].GetString();
                            if (Enum.TryParse(typeStr, true, out NotificationType type))
                                notification.Type = type;
                        }

                        // Парсинг кнопок (новый формат)
                        if (jsonObject.ContainsKey("buttons"))
                        {
                            JsonArray buttonsArray = jsonObject["buttons"].GetArray();
                            foreach (IJsonValue buttonValue in buttonsArray)
                            {
                                if (buttonValue.ValueType == JsonValueType.Object)
                                {
                                    JsonObject buttonObject = buttonValue.GetObject();
                                    var button = new NotificationButton();

                                    if (buttonObject.ContainsKey("text"))
                                        button.Text = buttonObject["text"].GetString();

                                    if (buttonObject.ContainsKey("action"))
                                    {
                                        string actionStr = buttonObject["action"].GetString();
                                        if (Enum.TryParse(actionStr, true, out ButtonActionType action))
                                            button.Action = action;
                                    }

                                    if (buttonObject.ContainsKey("value"))
                                        button.Value = buttonObject["value"].GetString();

                                    notification.Buttons.Add(button);
                                }
                            }
                        }
                        // Парсинг ссылок (старый формат) - преобразуем в кнопки с действием Url
                        else if (jsonObject.ContainsKey("links"))
                        {
                            JsonArray linksArray = jsonObject["links"].GetArray();
                            foreach (IJsonValue linkValue in linksArray)
                            {
                                if (linkValue.ValueType == JsonValueType.Object)
                                {
                                    JsonObject linkObject = linkValue.GetObject();
                                    var button = new NotificationButton();

                                    if (linkObject.ContainsKey("name"))
                                        button.Text = linkObject["name"].GetString();

                                    if (linkObject.ContainsKey("url"))
                                    {
                                        button.Action = ButtonActionType.Url;
                                        button.Value = linkObject["url"].GetString();
                                    }

                                    notification.Buttons.Add(button);
                                }
                            }
                        }

                        // Парсинг поля ввода
                        if (jsonObject.ContainsKey("input"))
                        {
                            JsonObject inputObject = jsonObject["input"].GetObject();
                            var input = new NotificationInput();

                            if (inputObject.ContainsKey("type"))
                            {
                                string typeStr = inputObject["type"].GetString();
                                if (Enum.TryParse(typeStr, true, out InputFieldType type))
                                    input.Type = type;
                            }

                            if (inputObject.ContainsKey("placeholder"))
                                input.Placeholder = inputObject["placeholder"].GetString();

                            if (inputObject.ContainsKey("required"))
                                input.Required = inputObject["required"].GetBoolean();

                            if (inputObject.ContainsKey("maxLength"))
                                input.MaxLength = (int)inputObject["maxLength"].GetNumber();

                            notification.Input = input;
                        }

                        if (jsonObject.ContainsKey("showUntilAnswered"))
                            notification.ShowUntilAnswered = jsonObject["showUntilAnswered"].GetBoolean();

                        if (jsonObject.ContainsKey("dismissButton"))
                            notification.DismissButton = jsonObject["dismissButton"].GetString();

                        // Теперь, когда известны Type и ShowUntilAnswered, применяем фильтрацию по lastID
                        bool isSurveyWithShowUntil = notification.Type == NotificationType.Survey && notification.ShowUntilAnswered;

                        // Для не-опросников (или опросников без ShowUntilAnswered) применяем фильтр lastID
                        if (!isSurveyWithShowUntil && lastID >= notificationId)
                            continue;

                        // Устанавливаем ID
                        notification.Id = notificationId;

                        // Обновляем noteLastId только для не-опросников (чтобы опросники показывались до ответа)
                        if (!isSurveyWithShowUntil)
                        {
                            DB.SettingsTable.SetSetting("noteLastId", notificationId.ToString());
                        }

                        // Проверка, был ли уже ответ на опрос (если ShowUntilAnswered = true)
                        if (notification.Type == NotificationType.Survey && notification.ShowUntilAnswered)
                        {
                            // TODO: Проверить в БД, отвечен ли уже этот опрос
                            // Если отвечен, пропустить уведомление
                            bool answered = SurveyResponseManager.HasResponse(notification.Id);
                            if (answered)
                                continue;
                        }

                        notifications.Add(notification);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка парсинга JSON: {ex.Message}");
            }

            return notifications;
        }

        

        // Метод для проверки наличия новых уведомлений
        public async Task<bool> HasNewNotificationsAsync(HashSet<int> knownNotificationIds)
        {
            var notifications = await GetNotificationsAsync();

            foreach (var notification in notifications)
            {
                if (!knownNotificationIds.Contains(notification.Id))
                {
                    return true;
                }
            }

            return false;
        }

        // Очистка ресурсов
        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}