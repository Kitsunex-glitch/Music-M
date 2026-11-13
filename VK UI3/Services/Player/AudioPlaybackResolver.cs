using System;
using System.Collections.Generic;
using System.Linq;
using VK_UI3.Models;
using VkNet.Model.Attachments;

namespace VK_UI3.Services.Player
{
    /// <summary>
    /// Результат выбора потока для воспроизведения.
    /// </summary>
    public sealed class ResolvedStream
    {
        /// <summary>
        /// Ссылка на поток.
        /// </summary>
        public Uri Url { get; init; }

        /// <summary>
        /// Тип потока из <c>audio_streams</c> (mp3, aac, flac, dash, hls_range, hls_ts).
        /// </summary>
        public string StreamType { get; init; }

        /// <summary>
        /// Запрошенный пользователем режим.
        /// </summary>
        public PlaybackType RequestedType { get; init; }

        /// <summary>
        /// Фактически использованный режим (может отличаться при fallback).
        /// </summary>
        public PlaybackType ActualType { get; init; }

        /// <summary>
        /// <c>true</c>, если выбранный режим недоступен и произошёл откат к другому.
        /// </summary>
        public bool IsFallback { get; init; }

        /// <summary>
        /// Причина fallback (для UI/логов).
        /// </summary>
        public string FallbackReason { get; init; }

        public bool IsValid => Url != null;

        /// <summary>
        /// Человекочитаемое описание режима, например «AAC (fallback: MP3)».
        /// </summary>
        public string DisplayName
        {
            get
            {
                var actual = PlaybackTypeHelper.GetInfo(ActualType).Title;

                return IsFallback
                    ? $"{actual} (откат от «{PlaybackTypeHelper.GetInfo(RequestedType).Title}»)"
                    : actual;
            }
        }
    }

    /// <summary>
    /// Выбор URL потока для воспроизведения по выбранному режиму с откатом к доступным вариантам.
    /// </summary>
    public static class AudioPlaybackResolver
    {
        /// <summary>
        /// Приоритет отката: чем раньше тип, тем он предпочтительнее.
        /// </summary>
        private static readonly string[] FallbackPriority =
        {
            "mp3",
            "aac",
            "flac",
            "hls_range",
            "dash",
            "hls_ts"
        };

        /// <summary>
        /// Есть ли у трека хоть один способ воспроизведения.
        /// </summary>
        public static bool HasPlayableSource(Audio audio)
        {
            if (audio == null)
                return false;

            if (audio.Url != null)
                return true;

            return audio.AudioStreams != null
                   && audio.AudioStreams.Any(x => x?.Url != null);
        }

        /// <summary>
        /// Выбирает поток по режиму. Если выбранный режим недоступен — берёт первый доступный.
        /// </summary>
        /// <param name="audio">Трек.</param>
        /// <param name="requested">Выбранный пользователем режим.</param>
        /// <param name="ffmpegAvailable">Доступен ли ffmpeg (для DASH/HLS).</param>
        public static ResolvedStream Resolve(Audio audio, PlaybackType requested, bool ffmpegAvailable)
        {
            if (audio == null)
            {
                return new ResolvedStream
                {
                    RequestedType = requested,
                    ActualType = requested,
                    FallbackReason = "трек не задан"
                };
            }

            var info = PlaybackTypeHelper.GetInfo(requested);

            // 1. Режим требует ffmpeg, а его нет — сразу откатываемся к режимам без ffmpeg.
            if (info.RequiresFFmpeg && !ffmpegAvailable)
            {
                return ResolveWithoutFfmpeg(audio, requested, "FFmpeg не установлен");
            }

            // 2. Прямое совпадение по выбранному режиму.
            var direct = FindStream(audio, info.StreamTypes);

            if (direct != null)
            {
                return new ResolvedStream
                {
                    Url = direct.Url,
                    StreamType = direct.Type,
                    RequestedType = requested,
                    ActualType = requested
                };
            }

            // 3. Авто-режим: первый доступный по приоритету (без учёта запрошенного).
            if (requested == PlaybackType.Auto)
            {
                var best = FindByPriority(audio);

                if (best != null)
                {
                    return new ResolvedStream
                    {
                        Url = best.Url,
                        StreamType = best.Type,
                        RequestedType = requested,
                        ActualType = TypeForStream(best.Type),
                        IsFallback = false
                    };
                }
            }

            // 4. Откат: первый доступный поток по приоритету, но не тот, что уже проверяли.
            var fallback = FindByPriority(audio);

            if (fallback != null)
            {
                return new ResolvedStream
                {
                    Url = fallback.Url,
                    StreamType = fallback.Type,
                    RequestedType = requested,
                    ActualType = TypeForStream(fallback.Type),
                    IsFallback = true,
                    FallbackReason = $"поток «{info.Title}» недоступен, использован «{fallback.Type}»"
                };
            }

            // 5. Последний резерв — audio.Url (mp3 с CDN).
            if (audio.Url != null)
            {
                return new ResolvedStream
                {
                    Url = audio.Url,
                    StreamType = "mp3",
                    RequestedType = requested,
                    ActualType = PlaybackType.Mp3,
                    IsFallback = requested != PlaybackType.Mp3,
                    FallbackReason = $"поток «{info.Title}» недоступен, использован прямой URL трека"
                };
            }

            return new ResolvedStream
            {
                RequestedType = requested,
                ActualType = requested,
                FallbackReason = "у трека нет доступных потоков"
            };
        }

        /// <summary>
        /// Выбор режима без ffmpeg: mp3/aac/flac или прямой URL.
        /// </summary>
        private static ResolvedStream ResolveWithoutFfmpeg(Audio audio, PlaybackType requested, string reason)
        {
            var stream = FindStream(audio, new[] { "mp3", "aac", "flac" });

            if (stream != null)
            {
                return new ResolvedStream
                {
                    Url = stream.Url,
                    StreamType = stream.Type,
                    RequestedType = requested,
                    ActualType = TypeForStream(stream.Type),
                    IsFallback = true,
                    FallbackReason = $"{reason}; использован «{stream.Type}»"
                };
            }

            if (audio.Url != null)
            {
                return new ResolvedStream
                {
                    Url = audio.Url,
                    StreamType = "mp3",
                    RequestedType = requested,
                    ActualType = PlaybackType.Mp3,
                    IsFallback = true,
                    FallbackReason = $"{reason}; использован прямой URL трека"
                };
            }

            return new ResolvedStream
            {
                RequestedType = requested,
                ActualType = requested,
                FallbackReason = $"{reason}; других потоков нет"
            };
        }

        /// <summary>
        /// Находит первый поток, тип которого входит в <paramref name="types"/>.
        /// </summary>
        private static AudioStream FindStream(Audio audio, IReadOnlyList<string> types)
        {
            if (audio?.AudioStreams == null || types == null)
                return null;

            foreach (var type in types)
            {
                var stream = audio.AudioStreams
                    .FirstOrDefault(x => x?.Url != null
                                         && string.Equals(Normalize(x.Type), type, StringComparison.OrdinalIgnoreCase));

                if (stream != null)
                    return stream;
            }

            return null;
        }

        /// <summary>
        /// Находит первый доступный поток по общему приоритету.
        /// </summary>
        private static AudioStream FindByPriority(Audio audio)
        {
            return FindStream(audio, FallbackPriority);
        }

        /// <summary>
        /// Режим, соответствующий типу потока.
        /// </summary>
        public static PlaybackType TypeForStream(string streamType)
        {
            return Normalize(streamType) switch
            {
                "mp3" => PlaybackType.Mp3,
                "aac" => PlaybackType.Aac,
                "flac" => PlaybackType.Flac,
                "dash" => PlaybackType.Dash,
                "hls_range" => PlaybackType.Hls,
                "hls_ts" => PlaybackType.HlsTs,
                _ => PlaybackType.Auto
            };
        }

        private static string Normalize(string value) => value?.Trim().ToLowerInvariant() ?? string.Empty;
    }
}
