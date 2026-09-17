using System;
using Windows.Foundation.Metadata;

namespace VK_UI3
{
    internal class StaticParams
    {
        public static readonly string tokenStatDefaultGate = Environment.GetEnvironmentVariable("TOKEN_STAT_DEFAULT_GATE");
    }

    public class MusicMStatDefaultGate : StatDefaultGateLib.StatDefaultGate
    {
        public static string Token { get; set; } = StaticParams.tokenStatDefaultGate;

        public MusicMStatDefaultGate() : base(Token)
        {
        }

        /// <summary>
        /// Синхронизирует флаг IsEnabled с настройкой из БД.
        /// </summary>
        public static void SyncEnabledFromSettings()
        {
            try
            {
                var setting = DB.SettingsTable.GetSetting("StatDefaultGateEnabled");
                if (setting == null)
                {
                    // Статистика включена по умолчанию (opt-out)
                    IsEnabled = true;
                    DB.SettingsTable.SetSetting("StatDefaultGateEnabled", "1");
                }
                else
                {
                    IsEnabled = setting.settingValue.Equals("1");
                }
            }
            catch
            {
                IsEnabled = false;
            }
        }
    }
}
