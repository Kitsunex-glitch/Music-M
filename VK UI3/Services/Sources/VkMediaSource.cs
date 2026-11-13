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

        if (!resolved.IsValid)
        {
            _logger.Warn($"[Playback] No playable stream for track {track.Id}: {resolved.FallbackReason}");
            return false;
        }

        var sourceUrl = resolved.Url.ToString();

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
                    using var file = MediaFile.Open(sourceUrl, mediaOptions);

                    return CreateMediaPlaybackItem(file);
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