# BetterLyric (локальная копия)

Локальная копия контрола `BetterLyric` из библиотеки **DevWinUI** (пакет `DevWinUI 10.1.0`),
которая, в свою очередь, портирована из проекта **BetterLyrics**:

- https://github.com/Ghost1372/DevWinUI
- https://github.com/jayfunc/BetterLyrics

## Зачем скопировано

Чтобы иметь возможность править рендер и поведение текста внутри проекта, не завися от
версии пакета `DevWinUI`. Пакет остаётся подключённым — он используется в других местах
(`MainWindow`, `ConfettiService`, `HotkeySettingControlDevWinUI` и др.).

## Что изменено относительно апстрима

| Файл | Изменение |
|---|---|
| все `.cs` | `namespace DevWinUI` → `namespace VK_UI3.Controls.BetterLyric` |
| `Common/LyricDataExtensions.cs` | синтаксис `extension(T x) { }` (C# 14) переписан в классические extension-методы |
| `Internals/Extensions/VectorExtensions.cs` | то же |
| `Internals/Extensions/RectExtensions.cs` | то же |
| `Internals/Extensions/CanvasTextLayoutExtensions.cs` | то же |
| `Internals/Dependencies/*` | добавлены зависимости: `AnimationEasingHelper`, `AnimationEasingType`, `AnimationEaseMode`, `ColorHelper` (`GetInterpolatedColor`, `WithAlpha`) |
| `Themes/BetterLyric.xaml` | `xmlns:local` указывает на новый namespace |
| все `.cs` | добавлены явные `using` вместо глобальных из `DevWinUI/GlobalUsings.cs` |

`Internals/Extensions/DispatcherQueueTimerExtension.cs` перенесён «на всякий случай» —
в текущем коде он не используется.

## Подключение стиля

Шаблон контрола подключается через [`VK UI3/Themes/Generic.xaml`](../../Themes/Generic.xaml):

```xml
<ResourceDictionary.MergedDictionaries>
    <ResourceDictionary Source="ms-appx:///Controls/BetterLyric/Themes/BetterLyric.xaml" />
</ResourceDictionary.MergedDictionaries>
```

`BetterLyric` устанавливает `DefaultStyleKey = typeof(BetterLyric)`, поэтому стиль
находится по конвенции `Themes/Generic.xaml`.

## Использование

```xml
<Page xmlns:dev="using:VK_UI3.Controls.BetterLyric">
    <dev:BetterLyric
        x:Name="LyricsView"
        CurrentLyricsData="{x:Bind LyricsData, Mode=OneWay}"
        CurrentPosition="{x:Bind Position, Mode=OneWay}"
        CurrentIsPlaying="{x:Bind IsPlaying, Mode=OneWay}"
        IsLyricsVisible="True"
        LineClicked="LyricsView_LineClicked" />
</Page>
```

Страница-потребитель: [`VK UI3/Views/ModalsPages/LyricsPage.xaml`](../../Views/ModalsPages/LyricsPage.xaml).

## Отладка

У контрола есть встроенный переключаемый логгер — [`BetterLyricDebugLog`](Internals/Common/BetterLyricDebugLog.cs).
По умолчанию он выключен: при `Enabled = false` все вызовы сводятся к проверке
булевого поля, оверхед нулевой.

### Включение

```xml
<dev:BetterLyric x:Name="LyricsView" IsDebugLoggingEnabled="True" />
```

или из кода:

```csharp
BetterLyricDebugLog.Enabled = true;
BetterLyricDebugLog.Categories = BetterLyricLogCategory.Draw | BetterLyricLogCategory.Layout;
BetterLyricDebugLog.FrameThrottleMs = 250; // троттлинг кадровых сообщений
```

На странице [`LyricsPage`](../../Views/ModalsPages/LyricsPage.xaml) переключатель находится
в панели настроек → «Эффекты» → «Диагностика». Там же кнопка «Скопировать состояние контрола»,
которая вызывает [`BetterLyric.DumpState()`](BetterLyric.cs:1) и кладёт снимок в буфер обмена.

### Категории

| Категория | Что показывает |
|---|---|
| `Lifecycle` | ApplyTemplate, CreateResources, Loaded/Unloaded, смена `IsLyricsVisible` |
| `Layout` | `RequestRelayout` / `TriggerRelayout`: причина пересоздания строк, время `MeasureAndArrange`, строки без валидного `CanvasTextLayout` |
| `Draw` | Диапазон видимых строк, сколько реально отрисовано, причины пропуска, **пустые кадры** |
| `Anim` | Старт переходов `YOffset` / `Scale` / `Blur` / `Opacity` по строкам |
| `Scroll` | Смена активной строки, `SnapToLine`, колесо, дебаунс-возврат, синхронизация позиции |
| `Data` | Загрузка текста: старт, успех/провал каждого источника, число строк, применение к контролю |

### Как читать логи

Сообщения идут в **Output → Debug** в формате:

```
[14:22:05.431] [BetterLyric/Layout] [f1834] Пересоздание строк: dirty=False, dataReplaced=True, ...
```

- `f<число>` — номер кадра отрисовки, полезно для сопоставления событий.
- Сообщение `Пустой кадр!` означает, что `IsLyricsVisible = true`, но на экран не попало ни строки —
  это главный сигнал проблем с раскладкой или диапазоном.
- `MeasureAndArrange готово: ... пустых Layout=N` — если `N > 0`, значит часть строк не получила
  текст (пустой `PrimaryText`, невалидная ширина области).

### Исправленные дефекты

В ходе отладки были устранены:

1. `RequestRelayout` досрочно выходил при совпадении сигнатуры раскладки, из-за чего смена
   `CurrentLyricsData` на объект с тем же числом строк не доезжала до `TriggerRelayout`.
   Введён явный флаг `_isContentDirty` и метод `MarkContentDirty`.
2. `IsLyricsVisible` вызывал полный relayout — заменено на `RequestAnimationRefresh`.
3. `LyricsFontStrokeWidth` не входил в `LayoutSignature` и не помечал контент устаревшим.
4. `_isNowPlayingPaletteChanged` никогда не выставлявался в `true` — добавлен
   `NotifyNowPlayingPaletteChanged()`, вызываемый при смене цветов строк.
5. `Is3DLyricsEnabled` и `Lyrics3DAutoFitLayout` не вызывали пересчёт матрицы/раскладки.
6. `OnUnloaded` падал при повторном вызове (обращение к `canvas` без проверки на `null`),
   не сбрасывал кэши строк и `LayoutSignature`.
7. При клике по строке текст сначала прыгал к прежней строке: `_songPosition` синхронизируется
   с началом выбранной строки сразу, а ожидание перемотки плеера блокирует автоподстройку
   индекса (`_pendingSnapLineIndex`). Остаток прокрутки колесом гасится анимацией, а не скачком.
8. `unplayedTextStrokeColor` использовался в обеих ветках тернарника — обводка играющей
   строки не отличалась от остальных.
9. Все `catch { return false; }` в загрузчиках `LyricsPage` заменены на логируемые.

## Зависимости

- `Microsoft.Graphics.Win2D` 1.4.0 (уже в `VK UI3.csproj`)
- `Microsoft.WindowsAppSDK` 2.2.0
- `net8.0-windows10.0.22621`

`LangVersion` поднимать не нужно — все файлы совместимы с C# 12.
