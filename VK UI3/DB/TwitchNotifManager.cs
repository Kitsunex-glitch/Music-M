using System;
using System.Linq;

namespace VK_UI3.DB
{
    /// <summary>
    /// Управляет таблицей истории уведомлений об эфире (TwitchNotifHistory).
    /// </summary>
    public static class TwitchNotifManager
    {
        /// <summary>
        /// Проверяет, было ли уже уведомление по стримеру в указанный день.
        /// </summary>
        public static bool HasBeenNotified(string userLogin, string date)
        {
            if (string.IsNullOrWhiteSpace(userLogin) || string.IsNullOrWhiteSpace(date))
                return true;

            try
            {
                var count = DatabaseHandler.getConnect()
                    .Table<TwitchNotifHistory>()
                    .Count(h => h.UserLogin == userLogin && h.NotifDate == date);
                return count > 0;
            }
            catch
            {
                // При ошибке чтения считаем, что уведомление уже было, чтобы не спамить
                return true;
            }
        }

        /// <summary>
        /// Записывает факт показа уведомления по стримеру в указанный день.
        /// </summary>
        public static void MarkAsNotified(string userLogin, string date)
        {
            if (string.IsNullOrWhiteSpace(userLogin) || string.IsNullOrWhiteSpace(date))
                return;

            try
            {
                // Если запись уже существует для этого дня — не дублируем
                if (HasBeenNotified(userLogin, date))
                    return;

                DatabaseHandler.getConnect().Insert(new TwitchNotifHistory(userLogin, date));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка записи истории уведомлений Twitch: {ex.Message}");
            }
        }

        /// <summary>
        /// Очищает историю уведомлений. Полезно при необходимости сбросить «сегодняшние» отметки.
        /// </summary>
        public static void ClearAll()
        {
            try
            {
                DatabaseHandler.getConnect().DeleteAll<TwitchNotifHistory>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка очистки истории уведомлений Twitch: {ex.Message}");
            }
        }
    }
}