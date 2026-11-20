using FFMediaToolkit.Audio;
using FFMediaToolkit.Decoding;
using MusicX.Shared.Player;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TagLib.Mpeg;
using VK_UI3;
using VK_UI3.Services.Player;
using VkNet.Model.Attachments;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace MusicX.Services.Player.Sources;

public class VkMediaSource : MediaSourceBase
{
    private readonly Logger _logger;

    public VkMediaSource(Logger logger)
    {
        _logger = logger;
    }
    public override async Task<bool> OpenWithMediaPlayerAsync(MediaPlayer player, Audio track,
        CancellationToken cancellationToken = default, AudioEqualizer equalizer = null)
    {
        if (track == null || !AudioPlaybackResolver.HasPlayableSource(track)) return false;

        // Выбираем поток согласно настройке «Тип воспроизведения».
        // Если выбранного формата у трека нет — резолвер вернёт первый доступный.
        var resolved = Resolve(track);

        // Последний резерв — прямой URL трека (раньше проверялось только `track.Url != null`,
        // и любой такой трек открывался; сохраняем это поведение, чтобы не «терять» треки).
        if (!resolved.IsValid && track.Url != null)
        {
            resolved = new ResolvedStream
            {
                Url = track.Url,
                StreamType = "mp3",
                RequestedType = VK_UI3.Models.PlaybackTypeHelper.GetSelected(),
                ActualType = VK_UI3.Models.PlaybackType.Mp3,
                IsFallback = true,
                FallbackReason = "резолвер не нашёл поток, использован прямой URL трека"
            };
        }

        if (!resolved.IsValid)
        {
            _logger.Warn($"[Playback] No playable stream for track {track.Id}: {resolved.FallbackReason}");
            return false;
        }

        var sourceUrl = resolved.Url.ToString();

        // Для TagLib берём ТОЛЬКО прямой URL трека (обычно mp3). sourceUrl может быть
        // m3u8/mpd (HLS/DASH), которые TagLib не открывает и падает с исключением,
        // из-за чего трек не открывался.
        var tagUrl = track.Url?.ToString() ?? sourceUrl;

        try
        {
            var rtMediaSource = await CreateWinRtMediaSource(sourceUrl, cancellationToken: cancellationToken);

            var eq = "";
            if (equalizer != null)
            {
                string filterString = equalizer.GetFFmpegFilterString();
                if (!string.IsNullOrEmpty(filterString))
                {
                    eq = $"{filterString}";
                }
            }   

            

            await rtMediaSource.OpenWithMediaPlayerAsync(player).AsTask(cancellationToken);

            var playbackItem = player.Source as MediaPlaybackItem;

                        MediaItemDisplayProperties props = playbackItem.GetDisplayProperties();
            props.Type = Windows.Media.MediaPlaybackType.Music;
            props.MusicProperties.Title = track.Title;
            props.MusicProperties.AlbumArtist = track.Artist;


            if (track.Album != null && track.Album.Thumb != null)
            {
                RandomAccessStreamReference imageStreamRef = RandomAccessStreamReference.CreateFromUri(new Uri(
                    track.Album.Thumb.Photo600 ??
                    track.Album.Thumb.Photo270 ??
                    track.Album.Thumb.Photo300
                    ));

                props.Thumbnail = imageStreamRef;
                playbackItem.ApplyDisplayProperties(props);

                props.Thumbnail = imageStreamRef;

            }
            else
                props.Thumbnail = null;
            playbackItem.ApplyDisplayProperties(props);


            RegisterSourceObjectReference(player, rtMediaSource);

            if (equalizer == null)
            {
                System.Diagnostics.Debug.WriteLine("[FFMedia] Equalizer is null, clearing audio filters");
                rtMediaSource.ClearFFmpegAudioFilters();
            }
            else
            {
                string filterString = equalizer.GetFFmpegFilterString();
                System.Diagnostics.Debug.WriteLine($"[FFMedia] Equalizer filter string: '{filterString}'");
                if (!string.IsNullOrEmpty(filterString))
                {
                    System.Diagnostics.Debug.WriteLine($"[FFMedia] Setting audio filters: {filterString}");
                    rtMediaSource.SetFFmpegAudioFilters(filterString, rtMediaSource.AudioStreams.First());
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[FFMedia] Filter string empty, clearing audio filters");
                    rtMediaSource.ClearFFmpegAudioFilters();
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to use winrt decoder for vk media source");

            try
            {
                var baseMediaOptions = GetMediaOptions();
                var mediaOptions = new MediaOptions
                {
                    StreamsToLoad = baseMediaOptions.StreamsToLoad,
                    AudioSampleFormat = baseMediaOptions.AudioSampleFormat,
                };

                mediaOptions.DemuxerOptions.FlagDiscardCorrupt = baseMediaOptions.DemuxerOptions.FlagDiscardCorrupt;
                mediaOptions.DemuxerOptions.FlagEnableFastSeek = baseMediaOptions.DemuxerOptions.FlagEnableFastSeek;
                mediaOptions.DemuxerOptions.SeekToAny = baseMediaOptions.DemuxerOptions.SeekToAny;
                mediaOptions.DemuxerOptions.PrivateOptions = new Dictionary<string, string>(baseMediaOptions.DemuxerOptions.PrivateOptions);


                // Добавляем эквалайзер, если он передан
                if (equalizer != null)
                {
                    string filterString = equalizer.GetFFmpegFilterString();
                    System.Diagnostics.Debug.WriteLine($"[FFMedia] FFMediaToolkit equalizer filter string: '{filterString}'");
                    if (!string.IsNullOrEmpty(filterString))
                    {
                        // Добавляем loudnorm после эквалайзера
                        mediaOptions.EqualizerArgs = $"{filterString}";
                        System.Diagnostics.Debug.WriteLine($"[FFMedia] Set EqualizerArgs: {mediaOptions.EqualizerArgs}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[FFMedia] Filter string empty, EqualizerArgs remains null");
                    }
                }

                // i think its better to use task.run over task.yield because we aren't doing async with ffmpeg
                var playbackItem = await Task.Run(() =>
                {
                    // TagLib/FFMediaToolkit надёжно открывают прямой файл (mp3/aac/flac).
                    // Если tagUrl — это HLS/DASH (m3u8/mpd), сначала пробуем его,
                    // а при ошибке откатываемся на sourceUrl, чтобы не терять трек.
                    MediaFile file = null;
                    try
                    {
                        file = MediaFile.Open(tagUrl, mediaOptions);
                    }
                    catch (Exception tagEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[FFMedia] Open(tagUrl) failed: {tagEx.Message}; trying sourceUrl");
                        if (!string.Equals(tagUrl, sourceUrl, StringComparison.OrdinalIgnoreCase))
                        {
                            file = MediaFile.Open(sourceUrl, mediaOptions);
                        }
                        else
                        {
                            throw;
                        }
                    }

                    using (file)
                    {
                        return CreateMediaPlaybackItem(file);
                    }
                }, cancellationToken);

                MediaItemDisplayProperties props = playbackItem.GetDisplayProperties();
                props.Type = Windows.Media.MediaPlaybackType.Music;
                props.MusicProperties.Title = track.Title;
                props.MusicProperties.AlbumArtist = track.Artist;


                if (track.Album != null && track.Album.Thumb != null)
                {
                    RandomAccessStreamReference imageStreamRef = RandomAccessStreamReference.CreateFromUri(new Uri(
                        track.Album.Thumb.Photo600 ??
                        track.Album.Thumb.Photo270 ??
                        track.Album.Thumb.Photo300
                        ));

                    props.Thumbnail = imageStreamRef;
                    playbackItem.ApplyDisplayProperties(props);

                    props.Thumbnail = imageStreamRef;

                }
                else
                    props.Thumbnail = null;
                playbackItem.ApplyDisplayProperties(props);



                player.Source = playbackItem;
            }
            catch (Exception fallbackEx)
            {
                _logger.Error(fallbackEx, "Fallback FFmpeg decoder also failed for vk media source");
                System.Diagnostics.Debug.WriteLine($"[FFMedia] Fallback decoder failed: {fallbackEx.Message}");
                return false;
            }
        }

        return true;
    }
}