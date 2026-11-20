using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using MusicX.Core.Models;
using MusicX.Core.Services;
using MvvmHelpers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VK_UI3.Controllers;
using VK_UI3.Controls.BetterLyric;
using VK_UI3.VKs;
using Windows.ApplicationModel.DataTransfer;

namespace VK_UI3.Views.ModalsPages
{
    public class ArgsSeconds : EventArgs
    {
        public ArgsSeconds(int mssecond)
        {
            this.mssecond = mssecond;
        }

        public int mssecond { get; set; }
    }

    /// <summary>
    /// Страница текста песни на основе DevWinUI <see cref="BetterLyric"/>.
    /// Источники текста: VK, Genius, LRCLib. Синхронизированные источники (VK timestamps, LRC)
    /// конвертируются в <see cref="LyricData"/> с посимвольными слогами для эффекта «слово за словом».
    /// </summary>
    public sealed partial class LyricsPage : Page, INotifyPropertyChanged
    {
        /// <summary>Длительность строки по умолчанию, если источник не дал явный End.</summary>
        private const int DefaultLineDurationMs = 5000;

        /// <summary>Минимальная длительность слога, чтобы эффекты (glow/scale) выглядели осмысленно.</summary>
        private const int MinSyllableDurationMs = 120;

        public event PropertyChangedEventHandler PropertyChanged;

        private readonly GeniusService _geniusService;
        private readonly HttpClient _httpClient;
        private VkNet.Model.Attachments.Audio _currentTrack;
        private DispatcherTimer _timer;
        private bool _disposed;

        private readonly List<string> _plainTexts = new();
        private LyricData _lyricsData;

        public static event EventHandler timerTickCangeCheck;

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }
        private bool _isLoading;

        public string Credits
        {
            get => _credits;
            set => SetProperty(ref _credits, value);
        }
        private string _credits;

        /// <summary>Текущий набор строк (используется для копирования в буфер обмена).</summary>
        public ObservableRangeCollection<object> Texts { get; } = new();

        public VkNet.Model.Attachments.Audio Track =>
            VK_UI3.Services.MediaPlayerService.PlayingTrack?.audio;

        public LyricsPage()
        {
            InitializeComponent();
            _geniusService = App._host.Services.GetRequiredService<GeniusService>();
            _httpClient = new HttpClient();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            MoveBackDownStoryboard.Completed += MoveBackDownStoryboard_Completed;

            RestoreDebugLoggingSetting();

            // Источник текста
            GeniusToggle.Items.Add("Auto");
            GeniusToggle.Items.Add("VK");
            GeniusToggle.Items.Add("Genius");
            GeniusToggle.Items.Add("LRCLib");
            var savedSource = int.Parse(DB.SettingsTable.GetSetting("lyricsSource", "0").settingValue);
            GeniusToggle.SelectedIndex = savedSource >= 0 && savedSource < GeniusToggle.Items.Count ? savedSource : 0;

            // Визуальный эффект
            EffectsToggle.Items.Add("Классика");
            EffectsToggle.Items.Add("Неон (glow)");
            EffectsToggle.Items.Add("Спокойный");
            EffectsToggle.Items.Add("3D");
            var savedEffect = int.Parse(DB.SettingsTable.GetSetting("lyricsEffect", "1").settingValue);
            EffectsToggle.SelectedIndex = savedEffect >= 0 && savedEffect < EffectsToggle.Items.Count ? savedEffect : 1;
            ApplyEffectPreset(EffectsToggle.SelectedIndex);

            InitSettingsPane();
        }

        #region Lifecycle

        private void MoveBackDownStoryboard_Completed(object sender, object e)
        {
            if (GridBRing.Opacity != 0)
                return;

            GridBRing.Visibility = Visibility.Collapsed;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // LoadSettingsToControls теперь всегда пишет в LyricsView — и при первом
            // запуске (дефолты), и при последующих (сохранённые значения).
            ApplySavedSettings(force: true);

            InitializeTimer();
            _currentTrack = Track;
            LyricsView.CurrentIsPlaying = IsPlayerPlaying();
            _ = LoadLyricsAsync();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            DisposeTimer();
            _disposed = true;
        }

        private static bool IsPlayerPlaying()
        {
            try
            {
                return VK_UI3.Services.MediaPlayerService.MediaPlayer?.PlaybackSession?.PlaybackState
                    == Windows.Media.Playback.MediaPlaybackState.Playing;
            }
            catch
            {
                return false;
            }
        }

        private void InitializeTimer()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _timer.Tick += OnTimerTick;
        }

        private void DisposeTimer()
        {
            if (_timer == null)
                return;

            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _timer = null;
        }

        private async void OnTimerTick(object sender, object e)
        {
            if (_disposed)
                return;

            if (Track == null)
            {
                LyricsView.CurrentIsPlaying = false;
                return;
            }

            if (_currentTrack?.Id != Track.Id || _currentTrack?.OwnerId != Track.OwnerId)
            {
                BetterLyricDebugLog.WriteAlways(
                    BetterLyricLogCategory.Data,
                    $"Смена трека: '{Track.Title}' ({Track.OwnerId}_{Track.Id}) — перезагрузка текста");

                _currentTrack = Track;
                await LoadLyricsAsync();
            }

            // Текст без таймингов: позиция плеера не подаётся в контрол,
            // иначе полотно уедет к случайной строке.
            if (IsUntimedMode)
            {
                LyricsView.CurrentIsPlaying = false;
                return;
            }

            var mediaPlayer = VK_UI3.Services.MediaPlayerService.MediaPlayer;
            if (mediaPlayer == null)
                return;

            var position = mediaPlayer.Position;
            LyricsView.CurrentPosition = position;
            LyricsView.CurrentIsPlaying = mediaPlayer.PlaybackSession?.PlaybackState
                == Windows.Media.Playback.MediaPlaybackState.Playing;

            timerTickCangeCheck?.Invoke(this, new ArgsSeconds((int)position.TotalMilliseconds));
        }

        public void Enable()
        {
            _timer?.Start();
            LyricsView.CurrentIsPlaying = IsPlayerPlaying();
        }

        public void Disable()
        {
            _timer?.Stop();
            LyricsView.CurrentIsPlaying = false;
        }

        #endregion

        #region Effects

        private void EffectsToggle_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var index = EffectsToggle.SelectedIndex;
            if (index < 0)
                return;

            DB.SettingsTable.SetSetting("lyricsEffect", index.ToString());
            ApplyEffectPreset(index);
        }

        /// <summary>Пресеты визуального оформления <see cref="BetterLyric"/>.</summary>
        private void ApplyEffectPreset(int index)
        {
            switch (index)
            {
                case 0: // Классика — только fade, без glow/scale
                    LyricsView.WordByWordEffectMode = LyricsWordByWordEffectMode.Never;
                    LyricsView.IsLyricsGlowEffectEnabled = false;
                    LyricsView.IsLyricsScaleEffectEnabled = false;
                    LyricsView.IsLyricsFloatAnimationEnabled = false;
                    LyricsView.IsLyricsBlurEffectEnabled = false;
                    LyricsView.IsLyricsFadeOutEffectEnabled = true;
                    LyricsView.IsLyricsOutOfSightEffectEnabled = true;
                    LyricsView.Is3DLyricsEnabled = false;
                    break;

                case 1: // Неон (glow) — рекомендованный
                    LyricsView.WordByWordEffectMode = LyricsWordByWordEffectMode.Auto;
                    LyricsView.IsLyricsGlowEffectEnabled = true;
                    LyricsView.LyricsGlowEffectScope = LyricEffectScope.LongDurationSyllable;
                    LyricsView.IsLyricsGlowEffectAmountAutoAdjust = true;
                    LyricsView.IsLyricsScaleEffectEnabled = true;
                    LyricsView.IsLyricsScaleEffectAmountAutoAdjust = true;
                    LyricsView.IsLyricsFloatAnimationEnabled = true;
                    LyricsView.IsLyricsBlurEffectEnabled = true;
                    LyricsView.IsLyricsFadeOutEffectEnabled = true;
                    LyricsView.IsLyricsOutOfSightEffectEnabled = true;
                    LyricsView.Is3DLyricsEnabled = false;
                    break;

                case 2: // Спокойный — без float и blur
                    LyricsView.WordByWordEffectMode = LyricsWordByWordEffectMode.Auto;
                    LyricsView.IsLyricsGlowEffectEnabled = true;
                    LyricsView.LyricsGlowEffectAmount = 5;
                    LyricsView.IsLyricsScaleEffectEnabled = true;
                    LyricsView.IsLyricsFloatAnimationEnabled = false;
                    LyricsView.IsLyricsBlurEffectEnabled = false;
                    LyricsView.IsLyricsFadeOutEffectEnabled = true;
                    LyricsView.IsLyricsOutOfSightEffectEnabled = true;
                    LyricsView.Is3DLyricsEnabled = false;
                    break;

                case 3: // 3D
                    LyricsView.WordByWordEffectMode = LyricsWordByWordEffectMode.Auto;
                    LyricsView.IsLyricsGlowEffectEnabled = true;
                    LyricsView.IsLyricsScaleEffectEnabled = true;
                    LyricsView.IsLyricsFloatAnimationEnabled = true;
                    LyricsView.IsLyricsBlurEffectEnabled = true;
                    LyricsView.IsLyricsFadeOutEffectEnabled = true;
                    LyricsView.IsLyricsOutOfSightEffectEnabled = true;
                    LyricsView.Is3DLyricsEnabled = true;
                    LyricsView.Lyrics3DXAngle = 18;
                    LyricsView.Lyrics3DYAngle = 0;
                    LyricsView.Lyrics3DZAngle = 0;
                    LyricsView.Lyrics3DAutoFitLayout = false;
                    LyricsView.Lyrics3DDepth = 1000;
                    break;
            }
        }

        #endregion

        #region Loading

        private Guid _currentLoadId;

        public async Task LoadLyricsAsync()
        {
            ShowLoadRing();
            if (Track == null)
            {
                BetterLyricDebugLog.WriteAlways(BetterLyricLogCategory.Data, "LoadLyricsAsync: трек отсутствует — загрузка пропущена");
                hideLoadRing();
                return;
            }

            var loadId = Guid.NewGuid();
            _currentLoadId = loadId;

            var sourceName = GeniusToggle.SelectedIndex switch
            {
                0 => "Auto",
                1 => "VK",
                2 => "Genius",
                3 => "LRCLib",
                _ => $"Unknown({GeniusToggle.SelectedIndex})"
            };

            BetterLyricDebugLog.WriteAlways(
                BetterLyricLogCategory.Data,
                $"LoadLyricsAsync: старт (loadId={loadId.ToString("N")[..8]}), трек='{Track.Title}', источник={sourceName}");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                IsLoading = true;
                Credits = string.Empty;
                _plainTexts.Clear();
                _lyricsData = null;

                bool result = false;

                switch (GeniusToggle.SelectedIndex)
                {
                    case 0: // Auto
                        result = await TryLoadVkLyrics(Track, loadId)
                            || await TryLoadLrcLibLyrics(Track, loadId)
                            || await TryLoadGeniusLyrics(Track, loadId);
                        break;
                    case 1: // VK
                        result = await TryLoadVkLyrics(Track, loadId);
                        break;
                    case 2: // Genius
                        result = await TryLoadGeniusLyrics(Track, loadId);
                        break;
                    case 3: // LRCLib
                        result = await TryLoadLrcLibLyrics(Track, loadId);
                        break;
                }

                if (!result && _currentLoadId == loadId)
                {
                    BetterLyricDebugLog.WriteAlways(
                        BetterLyricLogCategory.Data,
                        $"LoadLyricsAsync: текст не найден за {stopwatch.ElapsedMilliseconds} мс — установлена заглушка");

                    SetPlaceholder("Текст песни не найден");
                }
            }
            catch (Exception ex)
            {
                BetterLyricDebugLog.WriteAlways(
                    BetterLyricLogCategory.Data,
                    $"LoadLyricsAsync: исключение — {ex.GetType().Name}: {ex.Message}");

                Debug.WriteLine($"Ошибка загрузки: {ex}");
                if (_currentLoadId == loadId)
                    SetPlaceholder("Ошибка загрузки текста");
            }
            finally
            {
                if (_currentLoadId == loadId)
                {
                    IsLoading = false;
                    hideLoadRing();
                }

                BetterLyricDebugLog.WriteAlways(
                    BetterLyricLogCategory.Data,
                    $"LoadLyricsAsync: завершено за {stopwatch.ElapsedMilliseconds} мс (актуальна={_currentLoadId == loadId})");
            }
        }

        private void hideLoadRing()
        {
            MoveBackUpStoryboard.Pause();
            MoveBackDownStoryboard.Begin();
        }

        private void ShowLoadRing()
        {
            GridBRing.Visibility = Visibility.Visible;
            MoveBackDownStoryboard.Pause();
            MoveBackUpStoryboard.Begin();
        }

        /// <summary>Показать единственную строку-заглушку (например, «не найдено»).</summary>
        private void SetPlaceholder(string text)
        {
            _plainTexts.Clear();
            _plainTexts.Add(text);
            Texts.Clear();
            Texts.Add(text);

            var line = new LyricLine
            {
                StartMs = 0,
                EndMs = (int)TimeSpan.FromMinutes(99).TotalMilliseconds,
                PrimaryText = text,
                PrimarySyllables = new List<BaseLyric>
                {
                    new BaseLyric
                    {
                        Text = text,
                        StartMs = 0,
                        EndMs = (int)TimeSpan.FromMinutes(99).TotalMilliseconds
                    }
                },
                IsPrimaryHasRealSyllableInfo = false
            };

            ApplyLyricsData(new LyricData(new List<LyricLine> { line }));
        }

        /// <summary>Установить данные в контрол (в UI-потоке).</summary>
        private void ApplyLyricsData(LyricData data)
        {
            void Apply()
            {
                _lyricsData = data;

                BetterLyricDebugLog.WriteAlways(
                    BetterLyricLogCategory.Data,
                    $"ApplyLyricsData: строк={data.LyricsLines.Count}, wordByWord={data.IsWordByWord}, " +
                    $"untimed={data.IsUntimed}, duration={data.Duration.TotalSeconds:0.##} с, " +
                    $"UI-поток={DispatcherQueue.HasThreadAccess}");

                if (data.IsUntimed)
                {
                    // Текст без таймингов: позиция плеера не подаётся в контрол, иначе
                    // синхронизатор подсветит случайную строку и полотно уедет к ней.
                    ApplyUntimedEffectProfile();
                    LyricsView.CurrentLyricsData = data;
                    LyricsView.CurrentPosition = TimeSpan.Zero;
                    LyricsView.CurrentIsPlaying = false;
                }
                else
                {
                    RestoreTimedEffectProfile();
                    LyricsView.CurrentLyricsData = data;
                    LyricsView.CurrentPosition = VK_UI3.Services.MediaPlayerService.MediaPlayer?.Position ?? TimeSpan.Zero;
                }
            }

            if (DispatcherQueue.HasThreadAccess)
                Apply();
            else
                DispatcherQueue.TryEnqueue(Apply);
        }

        /// <summary>
        /// Статичный профиль для текста без таймингов: текст просто читается,
        /// без посимвольных эффектов и без подсветки «играющей» строки.
        /// </summary>
        private void ApplyUntimedEffectProfile()
        {
            if (_isUntimedProfileApplied)
                return;

            _isUntimedProfileApplied = true;

            var v = LyricsView;
            v.WordByWordEffectMode = LyricsWordByWordEffectMode.Never;
            v.IsLyricsGlowEffectEnabled = false;
            v.IsLyricsScaleEffectEnabled = false;
            v.IsLyricsFloatAnimationEnabled = false;
            v.IsLyricsBlurEffectEnabled = false;
            v.IsLyricsOutOfSightEffectEnabled = false;
            v.IsLyricsBrethingEffectEnabled = false;
            v.Is3DLyricsEnabled = false;
            v.IsFanLyricsEnabled = false;
            // Плавное появление/исчезание строк оставляем — оно не зависит от таймингов.
            v.IsLyricsFadeOutEffectEnabled = true;

            BetterLyricDebugLog.WriteAlways(
                BetterLyricLogCategory.Data,
                "Untimed: применён статичный профиль эффектов");
        }

        /// <summary>
        /// Возврат к пользовательским настройкам оформления после статичного текста.
        /// Настройки в БД не трогаются — они применяются заново из сохранённых значений.
        /// </summary>
        private void RestoreTimedEffectProfile()
        {
            if (!_isUntimedProfileApplied)
                return;

            _isUntimedProfileApplied = false;

            ApplySavedSettings(force: true);

            BetterLyricDebugLog.WriteAlways(
                BetterLyricLogCategory.Data,
                "Untimed: восстановлен пользовательский профиль эффектов");
        }

        /// <summary>Признак того, что к контролу применён статичный (untimed) профиль.</summary>
        private bool _isUntimedProfileApplied;

        /// <summary>Текущий текст отображается без таймингов.</summary>
        private bool IsUntimedMode => _lyricsData?.IsUntimed == true;

        #endregion

        #region Sources

        private async Task<bool> TryLoadVkLyrics(VkNet.Model.Attachments.Audio track, Guid loadId)
        {
            if (!track.HasLyrics || _currentLoadId != loadId)
            {
                BetterLyricDebugLog.WriteAlways(
                    BetterLyricLogCategory.Data,
                    $"VK: пропуск (hasLyrics={track.HasLyrics}, actualLoad={_currentLoadId == loadId})");
                return false;
            }

            try
            {
                var vkLyrics = await VK.vkService.GetLyrics($"{track.OwnerId}_{track.Id}");

                if (vkLyrics?.LyricsInfo == null || _currentLoadId != loadId)
                {
                    BetterLyricDebugLog.WriteAlways(
                        BetterLyricLogCategory.Data,
                        $"VK: нет LyricsInfo или устаревший запрос (actualLoad={_currentLoadId == loadId})");
                    return false;
                }

                var data = BuildLyricDataFromVk(vkLyrics.LyricsInfo);

                if (data == null || data.LyricsLines.Count == 0 || _currentLoadId != loadId)
                {
                    BetterLyricDebugLog.WriteAlways(
                        BetterLyricLogCategory.Data,
                        $"VK: данные пусты (lines={data?.LyricsLines.Count.ToString() ?? "null"}, actualLoad={_currentLoadId == loadId})");
                    return false;
                }

                BetterLyricDebugLog.WriteAlways(
                    BetterLyricLogCategory.Data,
                    $"VK: успех — {data.LyricsLines.Count} строк, timestamps={(vkLyrics.LyricsInfo.Timestamps?.Count ?? 0)}");

                Credits = vkLyrics.Credits;
                ApplyLyricsData(data);
                return true;
            }
            catch (Exception ex)
            {
                BetterLyricDebugLog.WriteAlways(
                    BetterLyricLogCategory.Data,
                    $"VK: ошибка — {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> TryLoadGeniusLyrics(VkNet.Model.Attachments.Audio track, Guid loadId)
        {
            if (_currentLoadId != loadId)
                return false;

            try
            {
                var artist = track.MainArtists?.FirstOrDefault()?.Name ?? "";
                var results = await _geniusService.SearchAsync($"{track.Title} {artist}");
                var song = results.FirstOrDefault()?.Result;

                if (song == null || _currentLoadId != loadId)
                {
                    var resultCount = results?.Count() ?? 0;

                    BetterLyricDebugLog.WriteAlways(
                        BetterLyricLogCategory.Data,
                        $"Genius: песня не найдена (results={resultCount}, actualLoad={_currentLoadId == loadId})");
                    return false;
                }

                var lyrics = await _geniusService.GetSongAsync(song.Id);
                var lines = lyrics.Lyrics.Plain.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0)
                    .ToList();

                if (lines.Count == 0 || _currentLoadId != loadId)
                {
                    BetterLyricDebugLog.WriteAlways(
                        BetterLyricLogCategory.Data,
                        $"Genius: пустой текст (lines={lines.Count}, actualLoad={_currentLoadId == loadId})");
                    return false;
                }

                BetterLyricDebugLog.WriteAlways(
                    BetterLyricLogCategory.Data,
                    $"Genius: успех — {lines.Count} строк без тайминга (songId={song.Id})");

                Credits = "Текст предоставлен Genius (genius.com)";
                ApplyLyricsData(BuildUntimedLyricData(lines));
                return true;
            }
            catch (Exception ex)
            {
                BetterLyricDebugLog.WriteAlways(
                    BetterLyricLogCategory.Data,
                    $"Genius: ошибка — {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> TryLoadLrcLibLyrics(VkNet.Model.Attachments.Audio track, Guid loadId)
        {
            if (_currentLoadId != loadId)
                return false;

            try
            {
                var artist = track.MainArtists?.FirstOrDefault()?.Name ?? "";
                var title = track.Title;

                // Remove (feat. ...) parts from title if present
                var featIndex = title.IndexOf("(feat.");
                if (featIndex > 0)
                {
                    title = title.Substring(0, featIndex).Trim();
                }

                var url = $"https://lrclib.net/api/search?track_name={WebUtility.UrlEncode(title)}&artist_name={WebUtility.UrlEncode(artist)}";

                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    BetterLyricDebugLog.WriteAlways(
                        BetterLyricLogCategory.Data,
                        $"LRCLib: HTTP {(int)response.StatusCode} для '{title}' / '{artist}'");
                    return false;
                }

                var content = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var results = JsonSerializer.Deserialize<List<LrcLibResponse>>(content, options);

                var bestMatch = results?.FirstOrDefault();
                if (bestMatch == null || _currentLoadId != loadId)
                {
                    var resultCount = results?.Count ?? 0;

                    BetterLyricDebugLog.WriteAlways(
                        BetterLyricLogCategory.Data,
                        $"LRCLib: нет совпадений (results={resultCount}, actualLoad={_currentLoadId == loadId})");
                    return false;
                }

                LyricData data = null;

                if (!string.IsNullOrWhiteSpace(bestMatch.SyncedLyrics))
                    data = BuildLyricDataFromLrc(bestMatch.SyncedLyrics);

                if (data == null || data.LyricsLines.Count == 0)
                {
                    var plain = ParsePlainLyrics(bestMatch.PlainLyrics);
                    if (plain.Count == 0)
                    {
                        BetterLyricDebugLog.WriteAlways(
                            BetterLyricLogCategory.Data,
                            $"LRCLib: нет ни synced, ни plain текста у '{bestMatch.TrackName}' / '{bestMatch.ArtistName}'");
                        return false;
                    }

                    data = BuildUntimedLyricData(plain);
                }

                if (_currentLoadId != loadId)
                {
                    BetterLyricDebugLog.WriteAlways(
                        BetterLyricLogCategory.Data,
                        "LRCLib: результат устарел (загружен новый трек)");
                    return false;
                }

                BetterLyricDebugLog.WriteAlways(
                    BetterLyricLogCategory.Data,
                    $"LRCLib: успех — {data.LyricsLines.Count} строк, synced={!string.IsNullOrWhiteSpace(bestMatch.SyncedLyrics)}, " +
                    $"трек='{bestMatch.TrackName}' / '{bestMatch.ArtistName}'");

                Credits = "Текст предоставлен LRCLib (lrclib.net)";
                ApplyLyricsData(data);
                return true;
            }
            catch (Exception ex)
            {
                BetterLyricDebugLog.WriteAlways(
                    BetterLyricLogCategory.Data,
                    $"LRCLib: ошибка — {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region LyricData building

        /// <summary>
        /// VK: timestamps (строка + begin/end) или plain-текст.
        /// Строки без тайминга раскладываются последовательно по длительности трека.
        /// </summary>
        private LyricData BuildLyricDataFromVk(LyricsInfo lyricsInfo)
        {
            var lines = new List<LyricLine>();
            _plainTexts.Clear();

            var timestamps = lyricsInfo.Timestamps?.Where(t => t != null && !string.IsNullOrWhiteSpace(t.Line)).ToList();
            if (timestamps != null && timestamps.Count > 0)
            {
                for (int i = 0; i < timestamps.Count; i++)
                {
                    var ts = timestamps[i];
                    var start = Math.Max(ts.Begin, 0);
                    var end = ts.End > start ? ts.End : GetNextLineEnd(timestamps, i, start);

                    var line = CreateTimedLine(ts.Line, start, end);
                    lines.Add(line);
                    _plainTexts.Add(line.PrimaryText);
                }
            }
            else
            {
                var plain = lyricsInfo.Text?
                    .Select(t => t is string s ? s : (t?.ToString()))
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .ToList();

                if (plain == null || plain.Count == 0)
                    return null;

                return BuildUntimedLyricData(plain);
            }

            return new LyricData(lines);
        }

        private static int GetNextLineEnd(List<LyricsTimestamp> timestamps, int index, int start)
        {
            var next = index + 1 < timestamps.Count ? timestamps[index + 1].Begin : 0;
            if (next > start)
                return next;

            var duration = GetTrackDurationMs();
            if (duration > start)
                return duration;

            return start + DefaultLineDurationMs;
        }

        private static int GetTrackDurationMs()
        {
            try
            {
                var naturalDuration = VK_UI3.Services.MediaPlayerService.MediaPlayer?.NaturalDuration;
                if (naturalDuration.HasValue && naturalDuration.Value.TotalMilliseconds > 0)
                    return (int)naturalDuration.Value.TotalMilliseconds;
            }
            catch
            {
                // ignore
            }

            return 0;
        }

        /// <summary>Синхронизированные строки из LRC ([mm:ss.xx]text).</summary>
        private LyricData BuildLyricDataFromLrc(string lrcText)
        {
            var entries = new List<(int Start, string Text)>();
            _plainTexts.Clear();

            foreach (var raw in lrcText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var line = raw.Trim();
                if (line.Length == 0)
                    continue;

                // Пропускаем метатеги [ar:], [ti:], [offset:] и т.п.
                if (!TryParseLrcLine(line, out var timestamp, out var text))
                    continue;

                entries.Add(((int)timestamp.TotalMilliseconds, text ?? string.Empty));
            }

            if (entries.Count == 0)
                return null;

            entries.Sort((a, b) => a.Start.CompareTo(b.Start));

            var lines = new List<LyricLine>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                var start = Math.Max(entries[i].Start, 0);
                var end = i + 1 < entries.Count && entries[i + 1].Start > start
                    ? entries[i + 1].Start
                    : start + DefaultLineDurationMs;

                var line = CreateTimedLine(entries[i].Text, start, end);
                lines.Add(line);
                _plainTexts.Add(line.PrimaryText);
            }

            return new LyricData(lines);
        }

        /// <summary>Строка с синхронизацией и посимвольными слогами (эффект «слово за словом»).</summary>
        private static LyricLine CreateTimedLine(string text, int startMs, int endMs)
        {
            text ??= string.Empty;

            var safeEnd = Math.Max(endMs, startMs + MinSyllableDurationMs);

            var syllables = BuildWordSyllables(text, startMs, safeEnd);

            return new LyricLine
            {
                StartMs = startMs,
                EndMs = safeEnd,
                PrimaryText = text,
                PrimarySyllables = syllables,
                IsPrimaryHasRealSyllableInfo = syllables.Count > 0
            };
        }

        /// <summary>
        /// Разбивает строку на «слоги» (слова с учётом пробелов) и распределяет время пропорционально длине.
        /// </summary>
        private static List<BaseLyric> BuildWordSyllables(string text, int startMs, int endMs)
        {
            var result = new List<BaseLyric>();

            if (string.IsNullOrEmpty(text))
            {
                result.Add(new BaseLyric { Text = " ", StartMs = startMs, EndMs = endMs, StartIndex = 0 });
                return result;
            }

            var segments = SplitIntoSegments(text);
            if (segments.Count == 0)
                return result;

            var totalChars = segments.Sum(s => s.Length);
            if (totalChars == 0)
                totalChars = 1;

            var duration = Math.Max(endMs - startMs, MinSyllableDurationMs);
            var cursorMs = (double)startMs;
            var charIndex = 0;

            foreach (var segment in segments)
            {
                var segDuration = duration * ((double)segment.Length / totalChars);
                var segStart = (int)Math.Round(cursorMs);
                cursorMs += segDuration;
                var segEnd = (int)Math.Round(cursorMs);

                if (segEnd <= segStart)
                    segEnd = segStart + MinSyllableDurationMs;

                result.Add(new BaseLyric
                {
                    Text = segment,
                    StartMs = segStart,
                    EndMs = segEnd,
                    StartIndex = charIndex
                });

                charIndex += segment.Length;
            }

            return result;
        }

        /// <summary>Делит строку на сегменты «слово + следующий пробел», чтобы сохранить исходную длину текста.</summary>
        private static List<string> SplitIntoSegments(string text)
        {
            var segments = new List<string>();
            var sb = new StringBuilder();

            foreach (var ch in text)
            {
                sb.Append(ch);

                if (char.IsWhiteSpace(ch))
                {
                    segments.Add(sb.ToString());
                    sb.Clear();
                }
            }

            if (sb.Length > 0)
                segments.Add(sb.ToString());

            return segments;
        }

        /// <summary>
        /// Текст без таймингов: строки отображаются статично (без синхронизации с плеером).
        /// <para>
        /// Каждая строка получает ровно один слог на весь текст строки. Без слога
        /// <see cref="RenderLyricsLine"/> не создаёт ни одного символа: строка не попадает
        /// в раскладку и не рисуется вовсе — именно из-за этого раньше «пропадал» текст
        /// из Genius/LRCLib без LRC.
        /// </para>
        /// <para>
        /// Тайминги строк условные (нужны только как границы для раскладки) — режим
        /// <see cref="LyricData.IsUntimed"/> отключает привязку к позиции воспроизведения.
        /// </para>
        /// </summary>
        private static LyricData BuildUntimedLyricData(IReadOnlyList<string> lines)
        {
            var result = new List<LyricLine>(lines.Count);

            var trackDuration = GetTrackDurationMs();
            if (trackDuration <= 0)
                trackDuration = lines.Count * DefaultLineDurationMs;

            var perLine = Math.Max(trackDuration / Math.Max(lines.Count, 1), MinSyllableDurationMs);

            for (int i = 0; i < lines.Count; i++)
            {
                var start = i * perLine;
                var end = start + perLine;
                var text = lines[i] ?? string.Empty;

                result.Add(new LyricLine
                {
                    StartMs = start,
                    EndMs = end,
                    PrimaryText = text,
                    // Один слог на всю строку: контрол раскладывает символы только внутри слога.
                    PrimarySyllables = new List<BaseLyric>
                    {
                        new BaseLyric
                        {
                            Text = text,
                            StartMs = start,
                            EndMs = end,
                            StartIndex = 0
                        }
                    },
                    // Реального посимвольного тайминга нет — эффект «слово за словом» отключается.
                    IsPrimaryHasRealSyllableInfo = false
                });
            }

            return LyricData.CreateUntimed(result);
        }

        private static List<string> ParsePlainLyrics(string plainText)
        {
            if (string.IsNullOrWhiteSpace(plainText))
                return new List<string>();

            return plainText
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();
        }

        private static bool TryParseLrcLine(string line, out TimeSpan timestamp, out string text)
        {
            timestamp = TimeSpan.Zero;
            text = null;

            if (!line.StartsWith("[") || line.IndexOf(']') <= 1)
                return false;

            var closeBracketIndex = line.IndexOf(']');
            var timePart = line.Substring(1, closeBracketIndex - 1);

            // Поддержка форматов: mm:ss.ff, mm:ss.fff, m:ss.ff, m:ss.fff
            string[] formats = { @"m\:ss\.ff", @"m\:ss\.fff", @"mm\:ss\.ff", @"mm\:ss\.fff" };

            bool parsed = false;
            foreach (var format in formats)
            {
                if (TimeSpan.TryParseExact(timePart, format, null, out timestamp))
                {
                    parsed = true;
                    break;
                }
            }

            if (!parsed)
                return false;

            text = closeBracketIndex + 1 < line.Length
                ? line.Substring(closeBracketIndex + 1).Trim()
                : string.Empty;

            return true;
        }

        #endregion

        #region Settings pane

        private const string SettingsPrefix = "lyricsView";
        private bool _settingsInitialized;

        /// <summary>
        /// Подавляет обработку изменения контролов при программном применении значений.
        /// Запись в БД при этом НЕ подавляется: снимок уже содержит применённые значения.
        /// </summary>
        private bool _isApplyingSettings;

        /// <summary>Набор настроек, уже разложенный по контролам — повторно не применяем.</summary>
        private string _appliedSettingsSnapshot;

        /// <summary>
        /// Снимок, который нужно разложить по контролам при следующей загрузке страницы.
        /// Нужен для возврата к сохранённым настройкам после «Сброса», когда строк ещё нет.
        /// </summary>
        private string _pendingSettingsSnapshotToApply;

        // Свойства для x:Bind (обновляются при изменении зависимых переключателей)
        public bool IsGlowAmountEnabled => !LyricsView.IsLyricsGlowEffectAmountAutoAdjust;
        public bool IsScaleAmountEnabled => !LyricsView.IsLyricsScaleEffectAmountAutoAdjust;
        public bool IsFloatAmountEnabled => !LyricsView.IsLyricsFloatAnimationAmountAutoAdjust;
        public bool IsFanEnabled => LyricsView.IsFanLyricsEnabled;
        public bool Is3DEnabled => LyricsView.Is3DLyricsEnabled;
        public bool IsBreathingEnabled => LyricsView.IsLyricsBrethingEffectEnabled;

        private void RefreshDependentBindings()
        {
            Bindings.Update();
        }

        private static double ApplyNumber(string key, double fallback, Action<double> apply)
        {
            var setting = DB.SettingsTable.GetSetting(SettingsPrefix + key, null);

            var value = fallback;
            if (setting != null
                && double.TryParse(setting.settingValue, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var parsed)
                && !double.IsNaN(parsed) && !double.IsInfinity(parsed))
            {
                value = parsed;
            }

            apply(value);
            return value;
        }

        private static bool ApplyBool(string key, bool fallback, Action<bool> apply)
        {
            var setting = DB.SettingsTable.GetSetting(SettingsPrefix + key, null);
            var value = setting != null && bool.TryParse(setting.settingValue, out var parsed) ? parsed : fallback;

            apply(value);
            return value;
        }

        private static T ApplyEnum<T>(string key, T fallback, Action<T> apply) where T : struct, Enum
        {
            var setting = DB.SettingsTable.GetSetting(SettingsPrefix + key, null);
            var value = setting != null && Enum.TryParse<T>(setting.settingValue, out var parsed) ? parsed : fallback;

            apply(value);
            return value;
        }

        private void InitSettingsPane()
        {
            AlignmentCombo.ItemsSource = Enum.GetValues<LyricsTextAlignmentType>();
            OrientationCombo.ItemsSource = Enum.GetValues<LyricsContentOrientation>();
            FontWeightCombo.ItemsSource = Enum.GetValues<LyricFontWeight>();
            WordByWordCombo.ItemsSource = Enum.GetValues<LyricsWordByWordEffectMode>();
            GlowScopeCombo.ItemsSource = Enum.GetValues<LyricEffectScope>();
            EasingTypeCombo.ItemsSource = Enum.GetValues<AnimationEasingType>();
            EasingModeCombo.ItemsSource = Enum.GetValues<AnimationEaseMode>();

            // Пока страница не загружена, шаблон контрола ещё не применён (нет Canvas),
            // поэтому настройки раскладываются в OnLoaded — иначе изменения теряются.
            if (IsLoaded)
                ApplySavedSettings(force: true);
        }

        /// <summary>
        /// Применяет сохранённые настройки к контролам и тексту песни.
        /// Вызывается при загрузке страницы, при открытии панели настроек и после сброса.
        /// Повторное применение тех же значений пропускается, чтобы не вызывать лишний релейаут.
        /// </summary>
        /// <param name="force">Применить даже если настройки не менялись.</param>
        private void ApplySavedSettings(bool force = false)
        {
            var snapshot = BuildSettingsSnapshot();

            if (!force && snapshot == _appliedSettingsSnapshot)
                return;

            _settingsInitialized = false;
            _isApplyingSettings = true;
            try
            {
                LoadSettingsToControls();
            }
            finally
            {
                _isApplyingSettings = false;
                _settingsInitialized = true;
            }

            _appliedSettingsSnapshot = snapshot;
        }

        /// <summary>
        /// Восстанавливает значения по умолчанию из XAML для всех контролов настроек
        /// и переносит их в текст песни. Используется, когда сохранённых значений нет.
        /// </summary>
        private void ApplyDefaultSettings()
        {
            _settingsInitialized = false;
            _isApplyingSettings = true;
            try
            {
                var v = LyricsView;

                v.LyricsAlignmentType = LyricsTextAlignmentType.Left;
                v.LyricsLineContentOrientation = LyricsContentOrientation.Vertical;
                v.AutoWrap = true;
                v.PlayingLineTopOffset = 45;
                v.LyricsLineOverallSpacingFactor = 0.6;
                v.LyricsLineInnerSpacingFactor = 0.1;

                v.LyricsFontWeight = LyricFontWeight.SemiBold;
                v.OriginalLyricsFontSize = 24;
                v.TranslatedLyricsFontSize = 12;
                v.IsDynamicLyricsFontSize = true;
                v.LyricsFontStrokeWidth = 0;
                v.IsRightToLeftLyric = false;

                v.PlayedOriginalLyricsOpacity = 100;
                v.UnplayedOriginalLyricsOpacity = 35;
                v.TranslatedLyricsOpacity = 70;

                v.WordByWordEffectMode = LyricsWordByWordEffectMode.Auto;
                v.IsLyricsBlurEffectEnabled = true;
                v.IsLyricsFadeOutEffectEnabled = true;
                v.IsLyricsOutOfSightEffectEnabled = true;

                v.IsLyricsGlowEffectEnabled = true;
                v.LyricsGlowEffectScope = LyricEffectScope.LongDurationSyllable;
                v.LyricsGlowEffectLongSyllableDuration = 700;
                v.IsLyricsGlowEffectAmountAutoAdjust = true;
                v.LyricsGlowEffectAmount = 8;

                v.IsLyricsScaleEffectEnabled = true;
                v.LyricsScaleEffectLongSyllableDuration = 700;
                v.IsLyricsScaleEffectAmountAutoAdjust = true;
                v.LyricsScaleEffectAmount = 115;

                v.IsLyricsFloatAnimationEnabled = true;
                v.IsLyricsFloatAnimationAmountAutoAdjust = true;
                v.LyricsFloatAnimationAmount = 8;
                v.LyricsFloatAnimationDuration = 450;

                v.LyricsScrollEasingType = AnimationEasingType.Quad;
                v.LyricsScrollEasingMode = AnimationEaseMode.Out;
                v.LyricsScrollDuration = 500;
                v.TimelineSyncThreshold = 150;

                v.IsFanLyricsEnabled = false;
                v.FanLyricsAngle = 30;

                v.Is3DLyricsEnabled = false;
                v.Lyrics3DXAngle = 30;
                v.Lyrics3DYAngle = 0;
                v.Lyrics3DZAngle = 0;
                v.Lyrics3DDepth = 1000;
                v.Lyrics3DAutoFitLayout = false;

                v.IsLyricsBrethingEffectEnabled = false;
                v.LyricsBreathingIntensity = 80;

                v.IsLyricsShadowEffectEnabled = false;
                v.PositionOffset = 0;
                v.IsScrollViewerEnabled = true;
            }
            finally
            {
                _isApplyingSettings = false;
                _settingsInitialized = true;
            }

            RefreshDependentBindings();
            _appliedSettingsSnapshot = null;

            // Панель настроек могла быть ещё не открыта — при следующем открытии
            // она разложит значения по контролам.
            _pendingSettingsSnapshotToApply = BuildSettingsSnapshot();
        }

        /// <summary>Снимок сохранённых настроек для быстрого сравнения.</summary>
        private static string BuildSettingsSnapshot()
        {
            var builder = new StringBuilder();
            foreach (var key in SettingsKeys)
                builder.Append(key).Append('=').Append(DB.SettingsTable.GetSetting(SettingsPrefix + key, null)?.settingValue).Append(';');

            return builder.ToString();
        }

        /// <summary>Читает сохранённые значения и раскладывает по контролам.</summary>
        /// <summary>
        /// Единственная точка применения настроек: читает сохранённые значения (или дефолты)
        /// и раскладывает их ОДНОВРЕМЕННО в контролы панели и в <see cref="LyricsView"/>.
        /// </summary>
        private void LoadSettingsToControls()
        {
            var v = LyricsView;

            // ---- Разметка ----
            AlignmentCombo.SelectedItem = ApplyEnum("alignment", LyricSettingsDefaults.Alignment,
                x => v.LyricsAlignmentType = x);

            OrientationCombo.SelectedItem = ApplyEnum("orientation", LyricSettingsDefaults.Orientation,
                x => v.LyricsLineContentOrientation = x);

            AutoWrapToggle.IsOn = ApplyBool("autowrap", LyricSettingsDefaults.AutoWrap,
                x => v.AutoWrap = x);

            TopOffsetSlider.Value = ApplyNumber("topOffset", LyricSettingsDefaults.TopOffset,
                x => v.PlayingLineTopOffset = (int)x);

            OverallSpacingSlider.Value = ApplyNumber("overallSpacing", LyricSettingsDefaults.OverallSpacing,
                x => v.LyricsLineOverallSpacingFactor = x);

            InnerSpacingSlider.Value = ApplyNumber("innerSpacing", LyricSettingsDefaults.InnerSpacing,
                x => v.LyricsLineInnerSpacingFactor = x);

            // ---- Шрифт ----
            FontWeightCombo.SelectedItem = ApplyEnum("fontWeight", LyricSettingsDefaults.FontWeight,
                x => v.LyricsFontWeight = x);

            FontSizeSlider.Value = ApplyNumber("fontSize", LyricSettingsDefaults.FontSize,
                x => v.OriginalLyricsFontSize = (int)x);

            TranslatedFontSizeSlider.Value = ApplyNumber("translatedFontSize", LyricSettingsDefaults.TranslatedFontSize,
                x => v.TranslatedLyricsFontSize = (int)x);

            DynamicFontSizeToggle.IsOn = ApplyBool("dynamicFontSize", LyricSettingsDefaults.DynamicFontSize,
                x => v.IsDynamicLyricsFontSize = x);

            StrokeWidthSlider.Value = ApplyNumber("strokeWidth", LyricSettingsDefaults.StrokeWidth,
                x => v.LyricsFontStrokeWidth = (int)x);

            RtlToggle.IsOn = ApplyBool("rtl", LyricSettingsDefaults.Rtl,
                x => v.IsRightToLeftLyric = x);

            // ---- Прозрачность ----
            PlayedOpacitySlider.Value = ApplyNumber("playedOpacity", LyricSettingsDefaults.PlayedOpacity,
                x => v.PlayedOriginalLyricsOpacity = (int)x);

            UnplayedOpacitySlider.Value = ApplyNumber("unplayedOpacity", LyricSettingsDefaults.UnplayedOpacity,
                x => v.UnplayedOriginalLyricsOpacity = (int)x);

            TranslatedOpacitySlider.Value = ApplyNumber("translatedOpacity", LyricSettingsDefaults.TranslatedOpacity,
                x => v.TranslatedLyricsOpacity = (int)x);

            // ---- Прокрутка ----
            EasingTypeCombo.SelectedItem = ApplyEnum("easingType", LyricSettingsDefaults.EasingType,
                x => v.LyricsScrollEasingType = x);

            EasingModeCombo.SelectedItem = ApplyEnum("easingMode", LyricSettingsDefaults.EasingMode,
                x => v.LyricsScrollEasingMode = x);

            ScrollDurationSlider.Value = ApplyNumber("scrollDuration", LyricSettingsDefaults.ScrollDuration,
                x => v.LyricsScrollDuration = (int)x);

            SyncThresholdSlider.Value = ApplyNumber("syncThreshold", LyricSettingsDefaults.SyncThreshold,
                x => v.TimelineSyncThreshold = (int)x);

            // ---- Прочее ----
            ShadowToggle.IsOn = ApplyBool("shadow", LyricSettingsDefaults.Shadow,
                x => v.IsLyricsShadowEffectEnabled = x);

            PositionOffsetSlider.Value = ApplyNumber("positionOffset", LyricSettingsDefaults.PositionOffset,
                x => v.PositionOffset = (int)x);

            ScrollViewerToggle.IsOn = ApplyBool("scrollViewer", LyricSettingsDefaults.ScrollViewer,
                x => v.IsScrollViewerEnabled = x);

            // ---- Эффекты ----
            WordByWordCombo.SelectedItem = ApplyEnum("wordByWord", LyricSettingsDefaults.WordByWord,
                x => v.WordByWordEffectMode = x);

            BlurToggle.IsOn = ApplyBool("blur", LyricSettingsDefaults.Blur,
                x => v.IsLyricsBlurEffectEnabled = x);

            FadeOutToggle.IsOn = ApplyBool("fadeOut", LyricSettingsDefaults.FadeOut,
                x => v.IsLyricsFadeOutEffectEnabled = x);

            OutOfSightToggle.IsOn = ApplyBool("outOfSight", LyricSettingsDefaults.OutOfSight,
                x => v.IsLyricsOutOfSightEffectEnabled = x);

            // ---- Свечение ----
            GlowToggle.IsOn = ApplyBool("glow", LyricSettingsDefaults.Glow,
                x => v.IsLyricsGlowEffectEnabled = x);

            GlowScopeCombo.SelectedItem = ApplyEnum("glowScope", LyricSettingsDefaults.GlowScope,
                x => v.LyricsGlowEffectScope = x);

            GlowLongSyllableSlider.Value = ApplyNumber("glowLongSyllable", LyricSettingsDefaults.GlowLongSyllable,
                x => v.LyricsGlowEffectLongSyllableDuration = (int)x);

            GlowAutoAmountToggle.IsOn = ApplyBool("glowAutoAmount", LyricSettingsDefaults.GlowAutoAmount,
                x => v.IsLyricsGlowEffectAmountAutoAdjust = x);

            GlowAmountSlider.Value = ApplyNumber("glowAmount", LyricSettingsDefaults.GlowAmount,
                x => v.LyricsGlowEffectAmount = (int)x);

            // ---- Масштаб ----
            ScaleToggle.IsOn = ApplyBool("scale", LyricSettingsDefaults.Scale,
                x => v.IsLyricsScaleEffectEnabled = x);

            ScaleLongSyllableSlider.Value = ApplyNumber("scaleLongSyllable", LyricSettingsDefaults.ScaleLongSyllable,
                x => v.LyricsScaleEffectLongSyllableDuration = (int)x);

            ScaleAutoAmountToggle.IsOn = ApplyBool("scaleAutoAmount", LyricSettingsDefaults.ScaleAutoAmount,
                x => v.IsLyricsScaleEffectAmountAutoAdjust = x);

            ScaleAmountSlider.Value = ApplyNumber("scaleAmount", LyricSettingsDefaults.ScaleAmount,
                x => v.LyricsScaleEffectAmount = (int)x);

            // ---- Плавающая анимация ----
            FloatToggle.IsOn = ApplyBool("float", LyricSettingsDefaults.Float,
                x => v.IsLyricsFloatAnimationEnabled = x);

            FloatAutoAmountToggle.IsOn = ApplyBool("floatAutoAmount", LyricSettingsDefaults.FloatAutoAmount,
                x => v.IsLyricsFloatAnimationAmountAutoAdjust = x);

            FloatAmountSlider.Value = ApplyNumber("floatAmount", LyricSettingsDefaults.FloatAmount,
                x => v.LyricsFloatAnimationAmount = (int)x);

            FloatDurationSlider.Value = ApplyNumber("floatDuration", LyricSettingsDefaults.FloatDuration,
                x => v.LyricsFloatAnimationDuration = (int)x);

            // ---- Веер ----
            FanToggle.IsOn = ApplyBool("fan", LyricSettingsDefaults.Fan,
                x => v.IsFanLyricsEnabled = x);

            FanAngleSlider.Value = ApplyNumber("fanAngle", LyricSettingsDefaults.FanAngle,
                x => v.FanLyricsAngle = (int)x);

            // ---- 3D ----
            ThreeDToggle.IsOn = ApplyBool("threeD", LyricSettingsDefaults.ThreeD,
                x => v.Is3DLyricsEnabled = x);

            ThreeDXSlider.Value = ApplyNumber("threeDX", LyricSettingsDefaults.ThreeDX,
                x => v.Lyrics3DXAngle = (int)x);

            ThreeDYSlider.Value = ApplyNumber("threeDY", LyricSettingsDefaults.ThreeDY,
                x => v.Lyrics3DYAngle = (int)x);

            ThreeDZSlider.Value = ApplyNumber("threeDZ", LyricSettingsDefaults.ThreeDZ,
                x => v.Lyrics3DZAngle = (int)x);

            ThreeDDepthSlider.Value = ApplyNumber("threeDDepth", LyricSettingsDefaults.ThreeDDepth,
                x => v.Lyrics3DDepth = (int)x);

            ThreeDAutoFitToggle.IsOn = ApplyBool("threeDAutoFit", LyricSettingsDefaults.ThreeDAutoFit,
                x => v.Lyrics3DAutoFitLayout = x);

            // ---- Дыхание ----
            BreathingToggle.IsOn = ApplyBool("breathing", LyricSettingsDefaults.Breathing,
                x => v.IsLyricsBrethingEffectEnabled = x);

            BreathingIntensitySlider.Value = ApplyNumber("breathingIntensity", LyricSettingsDefaults.BreathingIntensity,
                x => v.LyricsBreathingIntensity = (int)x);

            RefreshDependentBindings();
        }

        // ---- Хелперы чтения/записи настроек ----

        private static double ReadNumber(string key, double fallback)
        {
            var setting = DB.SettingsTable.GetSetting(SettingsPrefix + key, null);

            if (setting != null
                && double.TryParse(setting.settingValue, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                && !double.IsNaN(parsed)
                && !double.IsInfinity(parsed))
            {
                return parsed;
            }

            return fallback;
        }

        private static bool ReadBool(string key, bool fallback)
        {
            var setting = DB.SettingsTable.GetSetting(SettingsPrefix + key, null);
            return setting != null && bool.TryParse(setting.settingValue, out var parsed) ? parsed : fallback;
        }

        private static T ReadEnum<T>(string key, T fallback) where T : struct, Enum
        {
            var setting = DB.SettingsTable.GetSetting(SettingsPrefix + key, null);
            return setting != null && Enum.TryParse<T>(setting.settingValue, out var parsed) ? parsed : fallback;
        }

        private void SaveSetting(string key, object value)
        {
            // Запись не подавляется при программном применении: значения пришли
            // из настроек/дефолтов и должны сохраниться, иначе панель и текст
            // начинают жить разными настройками. Дедупликация — забота SettingsTable.

            var text = value switch
            {
                double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
                bool b => b.ToString(),
                _ => value?.ToString()
            };

            if (text != null)
                DB.SettingsTable.SetSetting(SettingsPrefix + key, text);
        }

        // ---- Открытие/закрытие панели ----

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (SettingsPane.Visibility == Visibility.Visible)
                CloseSettings();
            else
                OpenSettings();
        }

        private void CloseSettingsButton_Click(object sender, RoutedEventArgs e) => CloseSettings();

        private void OpenSettings()
        {
            CloseSettingsStoryboard.Pause();
            SettingsPane.Visibility = Visibility.Visible;
            SettingsPane.Width = 0;
            OpenSettingsStoryboard.Begin();

            // Контролы и LyricsView уже синхронизированы в OnLoaded/сбросе,
            // повторное применение не требуется. Оставляем force-вызов на случай,
            // если настройки изменились вне этой страницы.
            ApplySavedSettings();
        }

        private void CloseSettings()
        {
            OpenSettingsStoryboard.Pause();
            CloseSettingsStoryboard.Completed -= CloseSettingsStoryboard_Completed;
            CloseSettingsStoryboard.Completed += CloseSettingsStoryboard_Completed;
            CloseSettingsStoryboard.Begin();
        }

        private void CloseSettingsStoryboard_Completed(object sender, object e)
        {
            CloseSettingsStoryboard.Completed -= CloseSettingsStoryboard_Completed;
            SettingsPane.Visibility = Visibility.Collapsed;
            SettingsPane.Width = 380;
        }

        /// <summary>Переключение между меню «Стиль» и «Эффекты».</summary>
        private void SettingsSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
        {
            var isEffects = SettingsSelector.SelectedItem == SettingsSelector.Items[1];

            StyleScroll.Visibility = isEffects ? Visibility.Collapsed : Visibility.Visible;
            EffectsScroll.Visibility = isEffects ? Visibility.Visible : Visibility.Collapsed;

            RefreshDependentBindings();
        }

        // ---- Обработчики: Стиль ----

        private void AlignmentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_settingsInitialized || AlignmentCombo.SelectedItem is not LyricsTextAlignmentType value)
                return;

            LyricsView.LyricsAlignmentType = value;
            SaveSetting("alignment", value);
        }

        private void OrientationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_settingsInitialized || OrientationCombo.SelectedItem is not LyricsContentOrientation value)
                return;

            LyricsView.LyricsLineContentOrientation = value;
            SaveSetting("orientation", value);
        }

        private void AutoWrapToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.AutoWrap = AutoWrapToggle.IsOn;
            SaveSetting("autowrap", AutoWrapToggle.IsOn);
        }

        private void TopOffsetSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.PlayingLineTopOffset = (int)e.NewValue;
            SaveSetting("topOffset", e.NewValue);
        }

        private void OverallSpacingSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.LyricsLineOverallSpacingFactor = e.NewValue;
            SaveSetting("overallSpacing", e.NewValue);
        }

        private void InnerSpacingSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.LyricsLineInnerSpacingFactor = e.NewValue;
            SaveSetting("innerSpacing", e.NewValue);
        }

        private void FontWeightCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_settingsInitialized || FontWeightCombo.SelectedItem is not LyricFontWeight value)
                return;

            LyricsView.LyricsFontWeight = value;
            SaveSetting("fontWeight", value);
        }

        private void FontSizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.OriginalLyricsFontSize = (int)e.NewValue;
            SaveSetting("fontSize", e.NewValue);
        }

        private void TranslatedFontSizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.TranslatedLyricsFontSize = (int)e.NewValue;
            SaveSetting("translatedFontSize", e.NewValue);
        }

        private void DynamicFontSizeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.IsDynamicLyricsFontSize = DynamicFontSizeToggle.IsOn;
            SaveSetting("dynamicFontSize", DynamicFontSizeToggle.IsOn);
        }

        private void StrokeWidthSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.LyricsFontStrokeWidth = (int)e.NewValue;
            SaveSetting("strokeWidth", e.NewValue);
        }

        private void RtlToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.IsRightToLeftLyric = RtlToggle.IsOn;
            SaveSetting("rtl", RtlToggle.IsOn);
        }

        private void PlayedOpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.PlayedOriginalLyricsOpacity = (int)e.NewValue;
            SaveSetting("playedOpacity", e.NewValue);
        }

        private void UnplayedOpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.UnplayedOriginalLyricsOpacity = (int)e.NewValue;
            SaveSetting("unplayedOpacity", e.NewValue);
        }

        private void TranslatedOpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.TranslatedLyricsOpacity = (int)e.NewValue;
            SaveSetting("translatedOpacity", e.NewValue);
        }

        private void EasingTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_settingsInitialized || EasingTypeCombo.SelectedItem is not AnimationEasingType value)
                return;

            LyricsView.LyricsScrollEasingType = value;
            SaveSetting("easingType", value);
        }

        private void EasingModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_settingsInitialized || EasingModeCombo.SelectedItem is not AnimationEaseMode value)
                return;

            LyricsView.LyricsScrollEasingMode = value;
            SaveSetting("easingMode", value);
        }

        private void ScrollDurationSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.LyricsScrollDuration = (int)e.NewValue;
            SaveSetting("scrollDuration", e.NewValue);
        }

        private void SyncThresholdSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.TimelineSyncThreshold = (int)e.NewValue;
            SaveSetting("syncThreshold", e.NewValue);
        }

        private void ShadowToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.IsLyricsShadowEffectEnabled = ShadowToggle.IsOn;
            SaveSetting("shadow", ShadowToggle.IsOn);
        }

        private void PositionOffsetSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.PositionOffset = (int)e.NewValue;
            SaveSetting("positionOffset", e.NewValue);
        }

        private void ScrollViewerToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.IsScrollViewerEnabled = ScrollViewerToggle.IsOn;
            SaveSetting("scrollViewer", ScrollViewerToggle.IsOn);
        }

        // ---- Обработчики: Эффекты ----

        private void WordByWordCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_settingsInitialized || WordByWordCombo.SelectedItem is not LyricsWordByWordEffectMode value)
                return;

            LyricsView.WordByWordEffectMode = value;
            SaveSetting("wordByWord", value);
        }

        private void BlurToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.IsLyricsBlurEffectEnabled = BlurToggle.IsOn;
            SaveSetting("blur", BlurToggle.IsOn);
        }

        private void FadeOutToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.IsLyricsFadeOutEffectEnabled = FadeOutToggle.IsOn;
            SaveSetting("fadeOut", FadeOutToggle.IsOn);
        }

        private void OutOfSightToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.IsLyricsOutOfSightEffectEnabled = OutOfSightToggle.IsOn;
            SaveSetting("outOfSight", OutOfSightToggle.IsOn);
        }

        private void GlowToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.IsLyricsGlowEffectEnabled = GlowToggle.IsOn;
            SaveSetting("glow", GlowToggle.IsOn);
        }

        private void GlowScopeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_settingsInitialized || GlowScopeCombo.SelectedItem is not LyricEffectScope value)
                return;

            LyricsView.LyricsGlowEffectScope = value;
            SaveSetting("glowScope", value);
        }

        private void GlowLongSyllableSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.LyricsGlowEffectLongSyllableDuration = (int)e.NewValue;
            SaveSetting("glowLongSyllable", e.NewValue);
        }

        private void GlowAutoAmountToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.IsLyricsGlowEffectAmountAutoAdjust = GlowAutoAmountToggle.IsOn;
            SaveSetting("glowAutoAmount", GlowAutoAmountToggle.IsOn);
            RefreshDependentBindings();
        }

        private void GlowAmountSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.LyricsGlowEffectAmount = (int)e.NewValue;
            SaveSetting("glowAmount", e.NewValue);
        }

        private void ScaleToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.IsLyricsScaleEffectEnabled = ScaleToggle.IsOn;
            SaveSetting("scale", ScaleToggle.IsOn);
        }

        private void ScaleLongSyllableSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.LyricsScaleEffectLongSyllableDuration = (int)e.NewValue;
            SaveSetting("scaleLongSyllable", e.NewValue);
        }

        private void ScaleAutoAmountToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.IsLyricsScaleEffectAmountAutoAdjust = ScaleAutoAmountToggle.IsOn;
            SaveSetting("scaleAutoAmount", ScaleAutoAmountToggle.IsOn);
            RefreshDependentBindings();
        }

        private void ScaleAmountSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.LyricsScaleEffectAmount = (int)e.NewValue;
            SaveSetting("scaleAmount", e.NewValue);
        }

        private void FloatToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.IsLyricsFloatAnimationEnabled = FloatToggle.IsOn;
            SaveSetting("float", FloatToggle.IsOn);
        }

        private void FloatAutoAmountToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.IsLyricsFloatAnimationAmountAutoAdjust = FloatAutoAmountToggle.IsOn;
            SaveSetting("floatAutoAmount", FloatAutoAmountToggle.IsOn);
            RefreshDependentBindings();
        }

        private void FloatAmountSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.LyricsFloatAnimationAmount = (int)e.NewValue;
            SaveSetting("floatAmount", e.NewValue);
        }

        private void FloatDurationSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.LyricsFloatAnimationDuration = (int)e.NewValue;
            SaveSetting("floatDuration", e.NewValue);
        }

        private void FanToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.IsFanLyricsEnabled = FanToggle.IsOn;
            SaveSetting("fan", FanToggle.IsOn);
            RefreshDependentBindings();
        }

        private void FanAngleSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.FanLyricsAngle = (int)e.NewValue;
            SaveSetting("fanAngle", e.NewValue);
        }

        private void ThreeDToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.Is3DLyricsEnabled = ThreeDToggle.IsOn;
            SaveSetting("threeD", ThreeDToggle.IsOn);
            RefreshDependentBindings();
        }

        private void ThreeDXSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.Lyrics3DXAngle = (int)e.NewValue;
            SaveSetting("threeDX", e.NewValue);
        }

        private void ThreeDYSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.Lyrics3DYAngle = (int)e.NewValue;
            SaveSetting("threeDY", e.NewValue);
        }

        private void ThreeDZSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.Lyrics3DZAngle = (int)e.NewValue;
            SaveSetting("threeDZ", e.NewValue);
        }

        private void ThreeDDepthSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.Lyrics3DDepth = (int)e.NewValue;
            SaveSetting("threeDDepth", e.NewValue);
        }

        private void ThreeDAutoFitToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.Lyrics3DAutoFitLayout = ThreeDAutoFitToggle.IsOn;
            SaveSetting("threeDAutoFit", ThreeDAutoFitToggle.IsOn);
        }

        private void BreathingToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.IsLyricsBrethingEffectEnabled = BreathingToggle.IsOn;
            SaveSetting("breathing", BreathingToggle.IsOn);
            RefreshDependentBindings();
        }

        private void BreathingIntensitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_settingsInitialized)
                return;

            LyricsView.LyricsBreathingIntensity = (int)e.NewValue;
            SaveSetting("breathingIntensity", e.NewValue);
        }

        private void ResetSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var key in SettingsKeys)
                DB.SettingsTable.RemoveSetting(SettingsPrefix + key);

            _appliedSettingsSnapshot = null;   // чтобы сравнение не пропустило применение
            ApplySavedSettings(force: true);   // теперь применит дефолты через LoadSettingsToControls
        }

        // ---- Диагностика ----

        private const string DebugLoggingSettingKey = "lyricsDebugLogging";

        /// <summary>
        /// Включает/выключает отладочные логи контрола и сохраняет выбор между запусками.
        /// </summary>
        private void DebugLoggingToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var isOn = DebugLoggingToggle.IsOn;

            LyricsView.IsDebugLoggingEnabled = isOn;

            if (!_isApplyingSettings)
                DB.SettingsTable.SetSetting(DebugLoggingSettingKey, isOn.ToString());

            Debug.WriteLine($"[LyricsPage] Отладочные логи {(isOn ? "включены" : "выключены")}");
        }

        /// <summary>
        /// Снимает снимок состояния <see cref="BetterLyric"/> и копирует его в буфер обмена.
        /// </summary>
        private void DumpStateButton_Click(object sender, RoutedEventArgs e)
        {
            string dump;

            try
            {
                dump = LyricsView.DumpState();
            }
            catch (Exception ex)
            {
                dump = $"Ошибка получения состояния: {ex.GetType().Name}: {ex.Message}";
            }

            var package = new DataPackage();
            package.SetText(dump);
            Clipboard.SetContent(package);

            Debug.WriteLine($"[LyricsPage] DumpState скопирован в буфер обмена ({dump.Length} символов)");
        }

        /// <summary>
        /// Восстанавливает сохранённое значение переключателя отладочных логов.
        /// </summary>
        private void RestoreDebugLoggingSetting()
        {
            var saved = DB.SettingsTable.GetSetting(DebugLoggingSettingKey, null);
            var isOn = saved != null && bool.TryParse(saved.settingValue, out var parsed) && parsed;

            DebugLoggingToggle.IsOn = isOn;
            LyricsView.IsDebugLoggingEnabled = isOn;
        }

        private static readonly string[] SettingsKeys =
        {
            "alignment", "orientation", "autowrap", "topOffset", "overallSpacing", "innerSpacing",
            "fontWeight", "fontSize", "translatedFontSize", "dynamicFontSize", "strokeWidth", "rtl",
            "playedOpacity", "unplayedOpacity", "translatedOpacity",
            "wordByWord", "blur", "fadeOut", "outOfSight",
            "float", "floatAutoAmount", "floatAmount", "floatDuration",
            "glow", "glowScope", "glowLongSyllable", "glowAutoAmount", "glowAmount",
            "scale", "scaleLongSyllable", "scaleAutoAmount", "scaleAmount",
            "easingType", "easingMode", "scrollDuration", "syncThreshold",
            "fan", "fanAngle", "threeD", "threeDX", "threeDY", "threeDZ", "threeDDepth", "threeDAutoFit",
            "breathing", "breathingIntensity", "shadow", "positionOffset", "scrollViewer"
        };

        #endregion

        #region UI events

        private void SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void GeniusToggle_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GeniusToggle.SelectedIndex < 0)
                return;

            DB.SettingsTable.SetSetting("lyricsSource", GeniusToggle.SelectedIndex.ToString());
            _ = LoadLyricsAsync();
        }

        /// <summary>
        /// Клик по строке — перемотка на начало строки.
        /// Примагничивание текста к выбранной строке выполняет сам контрол.
        /// <para>
        /// Перемотка выполняется ДО снапа: контрол синхронизирует свою локальную позицию
        /// с началом строки, и плеер должен двигаться к той же точке. Иначе следующий тик
        /// таймера вернёт старую позицию плеера и текст прыгнет к прежней строке.
        /// </para>
        /// </summary>
        private void LyricsView_LineClicked(object sender, int lineIndex)
        {
            if (_lyricsData == null || lineIndex < 0 || lineIndex >= _lyricsData.LyricsLines.Count)
                return;

            // Текст без таймингов: строки не привязаны к времени — перематывать некуда.
            if (_lyricsData.IsUntimed)
                return;

            var line = _lyricsData.LyricsLines[lineIndex];
            var mediaPlayer = VK_UI3.Services.MediaPlayerService.MediaPlayer;
            if (mediaPlayer == null)
                return;

            var target = TimeSpan.FromMilliseconds(line.StartMs);

            BetterLyricDebugLog.WriteAlways(
                BetterLyricLogCategory.Scroll,
                $"LineClicked: строка {lineIndex} ('{line.PrimaryText}'), перемотка на {target.TotalMilliseconds:0} мс");

            try
            {
                mediaPlayer.Position = target;
            }
            catch (Exception ex)
            {
                BetterLyricDebugLog.WriteAlways(
                    BetterLyricLogCategory.Scroll,
                    $"LineClicked: перемотка не удалась — {ex.GetType().Name}: {ex.Message}");
            }

            // Контрол не должен ждать подтверждения от плеера: синхронизация
            // позиции уже выполнена внутри SnapToLine.
            LyricsView.CurrentPosition = target;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            MainView.mainView.ToggleLyricsPanel();
        }

        private void CopyClipboard_Click(object sender, RoutedEventArgs e)
        {
            var textToClip = new StringBuilder();

            if (_plainTexts.Count > 0)
            {
                foreach (var textLine in _plainTexts)
                    textToClip.AppendLine(textLine);
            }
            else
            {
                foreach (var item in Texts)
                {
                    if (item is string textString)
                        textToClip.AppendLine(textString);
                    else if (item is LyricsTimestamp timeText)
                        textToClip.AppendLine(timeText.Line);
                }
            }

            var package = new DataPackage();
            package.SetText(textToClip.ToString());
            Clipboard.SetContent(package);
        }

        #endregion

    }

    public class LrcLibResponse
    {
        public string TrackName { get; set; }
        public string ArtistName { get; set; }
        public string AlbumName { get; set; }
        public string PlainLyrics { get; set; }
        public string SyncedLyrics { get; set; }
    }

    /// <summary>
    /// Единственный источник дефолтов для настроек текста песни.
    /// Используется и при первом запуске (нет записей в БД), и при сбросе.
    /// </summary>
    public static class LyricSettingsDefaults
    {
        public const LyricsTextAlignmentType Alignment = LyricsTextAlignmentType.Left;
        public const LyricsContentOrientation Orientation = LyricsContentOrientation.Vertical;
        public const bool AutoWrap = true;
        public const double TopOffset = 45;
        public const double OverallSpacing = 0.6;
        public const double InnerSpacing = 0.1;

        public const LyricFontWeight FontWeight = LyricFontWeight.SemiBold;
        public const double FontSize = 24;
        public const double TranslatedFontSize = 12;
        public const bool DynamicFontSize = true;
        public const double StrokeWidth = 0;
        public const bool Rtl = false;

        public const double PlayedOpacity = 100;
        public const double UnplayedOpacity = 35;
        public const double TranslatedOpacity = 70;

        public const AnimationEasingType EasingType = AnimationEasingType.Quad;
        public const AnimationEaseMode EasingMode = AnimationEaseMode.Out;
        public const double ScrollDuration = 500;
        public const double SyncThreshold = 150;

        public const bool Shadow = false;
        public const double PositionOffset = 0;
        public const bool ScrollViewer = true;

        public const LyricsWordByWordEffectMode WordByWord = LyricsWordByWordEffectMode.Auto;
        public const bool Blur = true;
        public const bool FadeOut = true;
        public const bool OutOfSight = true;

        public const bool Glow = true;
        public const LyricEffectScope GlowScope = LyricEffectScope.LongDurationSyllable;
        public const double GlowLongSyllable = 700;
        public const bool GlowAutoAmount = true;
        public const double GlowAmount = 8;

        public const bool Scale = true;
        public const double ScaleLongSyllable = 700;
        public const bool ScaleAutoAmount = true;
        public const double ScaleAmount = 115;

        public const bool Float = true;
        public const bool FloatAutoAmount = true;
        public const double FloatAmount = 8;
        public const double FloatDuration = 450;

        public const bool Fan = false;
        public const double FanAngle = 30;

        public const bool ThreeD = false;
        public const double ThreeDX = 30;
        public const double ThreeDY = 0;
        public const double ThreeDZ = 0;
        public const double ThreeDDepth = 1000;
        public const bool ThreeDAutoFit = false;

        public const bool Breathing = false;
        public const double BreathingIntensity = 80;
    }
}
