using System;
using System.Collections.Generic;
using System.Linq;

namespace VK_UI3.Models
{
    /// <summary>
    /// Тип (режим) воспроизведения аудио.
    /// Значение хранится в настройках под ключом <see cref="PlaybackTypeHelper.SettingsKey"/>.
    /// </summary>
    public enum PlaybackType
    {
        /// <summary>
        /// Автоматический выбор: берётся лучший доступный поток трека.
        /// </summary>
        Auto = 0,

        /// <summary>
        /// MP3 (прямой поток с CDN).
        /// </summary>
        Mp3 = 1,

        /// <summary>
        /// AAC (прямой поток с CDN).
        /// </summary>
        Aac = 2,

        /// <summary>
        /// FLAC (lossless, прямой поток).
        /// </summary>
        Flac = 3,

        /// <summary>
        /// DASH (адаптивный сегментированный поток).
        /// </summary>
        Dash = 4,

        /// <summary>
        /// HLS (playlist .m3u8, тип hls_range).
        /// </summary>
        Hls = 5,

        /// <summary>
        /// HLS TS (устаревший вариант HLS, тип hls_ts).
        /// </summary>
        HlsTs = 6
    }

    /// <summary>
    /// Описание режима воспроизведения для UI и логики выбора.
    /// </summary>
    public sealed class PlaybackTypeInfo
    {
        public PlaybackType Type { get; init; }

        /// <summary>
        /// Короткое название для списка настроек.
        /// </summary>
        public string Title { get; init; }

        /// <summary>
        /// Подробное описание: что даёт режим, плюсы и минусы.
        /// </summary>
        public string Description { get; init; }

        /// <summary>
        /// Имена типов в <c>audio_streams</c>, которые подходят этому режиму.
        /// </summary>
        public IReadOnlyList<string> StreamTypes { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Требуется ли ffmpeg (библиотеки/утилита).
        /// </summary>
        public bool RequiresFFmpeg { get; init; }

        public override string ToString() => Title;
    }

    /// <summary>
    /// Метаданные режимов воспроизведения и работа с настройкой.
    /// </summary>
    public static class PlaybackTypeHelper
    {
        /// <summary>
        /// Ключ настройки в <c>SettingsTable</c>.
        /// </summary>
        public const string SettingsKey = "playbackType";

        /// <summary>
        /// Режим по умолчанию.
        /// </summary>
        public const PlaybackType Default = PlaybackType.Auto;

        private static readonly IReadOnlyList<PlaybackTypeInfo> _all = new List<PlaybackTypeInfo>
        {
            new PlaybackTypeInfo
            {
                Type = PlaybackType.Auto,
                Title = "Авто (рекомендуется)",
                Description = "Автоматически выбирает лучший доступный поток: сначала AAC, затем MP3, FLAC, HLS, DASH. " +
                              "Если выбранного формата в треке нет — используется тот, что доступен. Надёжный вариант без настройки.",
                StreamTypes = new[] { "aac", "mp3", "flac", "hls_range", "dash", "hls_ts" },
                RequiresFFmpeg = false
            },
            new PlaybackTypeInfo
            {
                Type = PlaybackType.Mp3,
                Title = "MP3",
                Description = "Классический MP3-поток с CDN. Максимальная совместимость и минимальная нагрузка на CPU. " +
                              "Качество ниже, чем у AAC/FLAC, задержка обычная. Хорошо работает даже без FFmpeg.",
                StreamTypes = new[] { "mp3" },
                RequiresFFmpeg = false
            },
            new PlaybackTypeInfo
            {
                Type = PlaybackType.Aac,
                Title = "AAC",
                Description = "Поток AAC: лучшее качество при небольшом размере, чем у MP3. Небольшая нагрузка на декодер. " +
                              "Рекомендуется для повседневного прослушивания.",
                StreamTypes = new[] { "aac" },
                RequiresFFmpeg = false
            },
            new PlaybackTypeInfo
            {
                Type = PlaybackType.Flac,
                Title = "FLAC (lossless)",
                Description = "Поток без сжатия с потерями: максимальное качество звука. Больше трафик и нагрузка на CPU/сеть. " +
                              "Имеет смысл только на хорошей акустике и быстром интернете.",
                StreamTypes = new[] { "flac" },
                RequiresFFmpeg = false
            },
            new PlaybackTypeInfo
            {
                Type = PlaybackType.Dash,
                Title = "DASH",
                Description = "Адаптивный сегментированный поток (MPEG-DASH): плеер сам подбирает качество под скорость сети, " +
                              "устойчив к просадкам. Задержка выше, чем у прямого потока, нужен FFmpeg.",
                StreamTypes = new[] { "dash" },
                RequiresFFmpeg = true
            },
            new PlaybackTypeInfo
            {
                Type = PlaybackType.Hls,
                Title = "HLS",
                Description = "HLS-поток (.m3u8, тип hls_range): сегментация с нарезкой по диапазонам, устойчив к обрывам связи, " +
                              "легко переподключается. Задержка в пределах длины сегмента, нужен FFmpeg.",
                StreamTypes = new[] { "hls_range" },
                RequiresFFmpeg = true
            },
            new PlaybackTypeInfo
            {
                Type = PlaybackType.HlsTs,
                Title = "HLS TS",
                Description = "Устаревший вариант HLS с транспортными потоками (.ts). Самая широкая совместимость, " +
                              "но наибольшая задержка и лишний трафик. Нужен FFmpeg.",
                StreamTypes = new[] { "hls_ts" },
                RequiresFFmpeg = true
            }
        };

        /// <summary>
        /// Все режимы в порядке отображения.
        /// </summary>
        public static IReadOnlyList<PlaybackTypeInfo> All => _all;

        /// <summary>
        /// Метаданные режима.
        /// </summary>
        public static PlaybackTypeInfo GetInfo(PlaybackType type)
        {
            return _all.FirstOrDefault(x => x.Type == type) ?? _all[0];
        }

        /// <summary>
        /// Читает выбранный режим из настроек.
        /// </summary>
        public static PlaybackType GetSelected()
        {
            try
            {
                var setting = DB.SettingsTable.GetSetting(SettingsKey);

                if (setting == null || string.IsNullOrWhiteSpace(setting.settingValue))
                    return Default;

                if (Enum.TryParse<PlaybackType>(setting.settingValue, true, out var parsed)
                    && Enum.IsDefined(typeof(PlaybackType), parsed))
                    return parsed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PlaybackType] Failed to read setting: {ex.Message}");
            }

            return Default;
        }

        /// <summary>
        /// Сохраняет выбранный режим в настройки.
        /// </summary>
        public static void SetSelected(PlaybackType type)
        {
            try
            {
                DB.SettingsTable.SetSetting(SettingsKey, type.ToString());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PlaybackType] Failed to save setting: {ex.Message}");
            }
        }
    }
}
