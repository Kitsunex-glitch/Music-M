using SQLite;
using System;

namespace VK_UI3.DB
{
    /// <summary>
    /// Хранит информацию о том, по каким стримерам уже было создано уведомление об эфире
    /// и в какой день (чтобы не показывать повторно в один и тот же день).
    /// </summary>
    public class TwitchNotifHistory
    {
        [PrimaryKey]
        [AutoIncrement]
        public long Id { get; set; }

        /// <summary>Логин стримера (user_login).</summary>
        public string UserLogin { get; set; }

        /// <summary>Дата показа уведомления в формате yyyy-MM-dd.</summary>
        public string NotifDate { get; set; }

        public TwitchNotifHistory() { }

        public TwitchNotifHistory(string userLogin, string notifDate)
        {
            UserLogin = userLogin;
            NotifDate = notifDate;
        }
    }
}