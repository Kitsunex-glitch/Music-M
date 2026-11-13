using System;
using VK_UI3.DB;
using VK_UI3.VKs.IVK;

namespace VK_UI3.Services.Player
{
    /// <summary>
    /// Единая точка управления режимом воспроизведения (повтор одного / повтор всех / перемешивание).
    /// Используется как кнопкой плеера, так и горячими клавишами, чтобы поведение было одинаковым.
    /// </summary>
    public static class PlaybackModeService
    {
        public const string SettingKey = "playNext";

        public const string RepeatOne = "RepeatOne";
        public const string RepeatAll = "RepeatAll";
        public const string Shuffle = "Shuffle";

        /// <summary>
        /// Срабатывает после смены режима. Передаёт новый режим.
        /// </summary>
        public static event EventHandler<string> ModeChanged;

        /// <summary>
        /// Текущий режим воспроизведения (из настроек, по умолчанию «Повтор всех»).
        /// </summary>
        public static string CurrentMode
        {
            get
            {
                var setting = SettingsTable.GetSetting(SettingKey);
                if (setting == null)
                {
                    SettingsTable.SetSetting(SettingKey, RepeatAll);
                    return RepeatAll;
                }

                return string.IsNullOrEmpty(setting.settingValue) ? RepeatAll : setting.settingValue;
            }
        }

        /// <summary>
        /// Возвращает русское название режима для отображения и озвучивания.
        /// </summary>
        public static string GetModeDisplayName(string mode)
        {
            return mode switch
            {
                RepeatOne => "Повтор одного трека",
                RepeatAll => "Повтор всех треков",
                Shuffle => "Перемешать",
                _ => "Повтор всех треков"
            };
        }

        /// <summary>
        /// Переключает режим по кругу: повтор одного → повтор всех → перемешать.
        /// Возвращает новый режим.
        /// </summary>
        public static string CycleMode()
        {
            string newMode = CurrentMode switch
            {
                RepeatOne => RepeatAll,
                RepeatAll => Shuffle,
                Shuffle => RepeatOne,
                _ => RepeatOne
            };

            ApplyMode(newMode);
            return newMode;
        }

        /// <summary>
        /// Применяет режим: сохраняет настройку, обновляет список воспроизведения и уведомляет UI.
        /// </summary>
        public static void ApplyMode(string mode)
        {
            SettingsTable.SetSetting(SettingKey, mode);

            var iVKGetAudio = MediaPlayerService.iVKGetAudio;
            switch (mode)
            {
                case Shuffle:
                    iVKGetAudio?.setShuffle();
                    break;
                case RepeatOne:
                case RepeatAll:
                    iVKGetAudio?.UnShuffleList();
                    break;
            }

            ModeChanged?.Invoke(null, mode);
        }
    }
}
