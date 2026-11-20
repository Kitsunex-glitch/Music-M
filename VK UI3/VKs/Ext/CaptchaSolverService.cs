using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media;
using System;
using System.Threading.Tasks;
using VK_UI3.DB;
using VK_UI3.Views.ModalsPages;
using VkNet.Extensions.DependencyInjection;
using VkNet.Model.Attachments;
using System.Collections.Generic;
using Microsoft.UI.Dispatching;

namespace VK_UI3.VKs.Ext;

public class CaptchaSolverService : IAsyncCaptchaSolver
{
    /// <summary>Сколько ждём ввода капчи, прежде чем вернуть отказ.</summary>
    private static readonly TimeSpan SolveTimeout = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Пауза после получения кода, чтобы страница капчи успела отправить
    /// <c>captchaNotRobot.endSession</c> и получить ответ, прежде чем окно закроется.
    /// </summary>
    private static readonly TimeSpan EndSessionGrace = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Последний успешно введённый/полученный ключ капчи.
    /// <para>
    /// VK иногда запрашивает подтверждение капчи повторно, присылая запрос БЕЗ ссылок
    /// на изображение и без redirect-uri. В этом случае нужно вернуть тот код, который
    /// уже был введён ранее, а не показывать пустое окно.
    /// </para>
    /// </summary>
    private static string? _lastSuccessfulKey;

    private static readonly object _cacheLock = new();

    /// <summary>
    /// Текущее показанное уведомление о непринятой капче.
    /// Хранится, чтобы повторное уведомление заменяло предыдущее, а не накладывалось на него.
    /// </summary>
    private static InfoBar? _activeToast;

    /// <summary>
    /// Кэширует ключ, который пользователь ВВЁЛ.
    /// <para>
    /// ВНИМАНИЕ: это НЕ подтверждённый ключ — VK ещё не проверил его.
    /// Используется только как «черновик» на случай запроса без ссылок.
    /// Инвалидируется при первом же отказе VK (см. <see cref="SolveFailedAsync"/>).
    /// </para>
    /// </summary>
    public static void CacheSuccessfulKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        lock (_cacheLock)
            _lastSuccessfulKey = key;
    }

    /// <summary>Сбрасывает кэш введённого ключа (он оказался неверным).</summary>
    private static void InvalidateCache()
    {
        lock (_cacheLock)
            _lastSuccessfulKey = null;
    }

    private readonly IServiceProvider _serviceProvider;

    List<ContentDialog> contentDialogs = new List<ContentDialog>();

    public CaptchaSolverService(
        IServiceProvider serviceProvider)
    {

        _serviceProvider = serviceProvider;
    }

    public async ValueTask<string?> SolveAsync(string? url, string? redirectUri = null)
    {
        bool hasImage = !string.IsNullOrWhiteSpace(url);
        bool hasWeb = !string.IsNullOrWhiteSpace(redirectUri);

        System.Diagnostics.Debug.WriteLine(
            $"[Captcha] SolveAsync: image={(hasImage ? url : "<нет>")}, web={(hasWeb ? redirectUri : "<нет>")}, " +
            $"UI-поток={MainWindow.dispatcherQueue?.HasThreadAccess}");

        // Нет ни картинки, ни веб-капчи — показывать нечего.
        //
        // Раньше здесь возвращался кэшированный ключ. Это создавало цикл:
        // пользователь вводил код → он кэшировался ДО проверки VK → VK отвергал →
        // повторный запрос без ссылок получал тот же НЕВЕРНЫЙ код → отказ снова.
        //
        // Теперь кэш одноразовый: его можно отдать ровно один раз (на случай, если
        // VK в рамках той же итерации запросит подтверждение повторно), но сразу
        // после выдачи он удаляется. Повторно тот же код не уйдёт никогда.
        // Отказ VK дополнительно чистит кэш через SolveFailedAsync.
        if (!hasImage && !hasWeb)
        {
            string? cached;
            lock (_cacheLock)
            {
                cached = _lastSuccessfulKey;
                _lastSuccessfulKey = null;
            }

            System.Diagnostics.Debug.WriteLine(
                $"[Captcha] Запрос без ссылок — кэш: {(cached == null ? "пуст, возвращаем отказ" : "выдаём одноразово и очищаем")}");

            return cached;
        }

        foreach (var item in contentDialogs)
        {
            if (item != null)
                item.Hide();
        }
        contentDialogs.Clear();

        TaskCompletionSource<string?> Submitted = new();
        CaptchaEnter captchaEnter = new CaptchaEnter();
        captchaEnter.Submitted = Submitted;
        captchaEnter.CaptchaUri = url;
        captchaEnter.RedirectUri = redirectUri;

        TaskCompletionSource<string?> dialogResult = new();

        bool enqueued = MainWindow.dispatcherQueue.TryEnqueue(async () =>
        {
            ContentDialog? dialog = null;
            try
            {
                dialog = new CustomDialog();
                dialog.Transitions = new TransitionCollection
                {
                    new PopupThemeTransition()
                };
                dialog.XamlRoot = MainWindow.contentFrame.XamlRoot;

                var a = new CaptchaEnter(captchaEnter);
                a.ParentDialog = dialog;
                dialog.Content = a;
                dialog.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                contentDialogs.Add(dialog);

                var showOperation = dialog.ShowAsync();

                // Пользователь мог просто закрыть окно — тогда ShowAsync завершится,
                // а Submitted останется незавершённым. Не даём запросу зависнуть.
                var completed = await Task.WhenAny(Submitted.Task, showOperation.AsTask(), Task.Delay(SolveTimeout));

                string? result = null;

                if (completed == Submitted.Task)
                {
                    result = await Submitted.Task;

                    // Код получен — страница капчи ещё должна отправить
                    // captchaNotRobot.endSession и дождаться {status:OK}.
                    // Если закрыть диалог сразу, WebView2 уничтожится вместе с окном,
                    // и запрос завершения сессии не успеет уйти.
                    if (!string.IsNullOrEmpty(result))
                        await Task.Delay(EndSessionGrace);
                }
                else if (completed == showOperation.AsTask())
                {
                    System.Diagnostics.Debug.WriteLine("[Captcha] Диалог закрыт без ввода — возвращаем null");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[Captcha] Таймаут ожидания ({SolveTimeout.TotalSeconds:0} с) — возвращаем null");
                }

                if (result == null)
                    Submitted.TrySetResult(null);

                dialogResult.TrySetResult(result);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Captcha] Ошибка окна капчи: {ex.GetType().Name}: {ex.Message}");
                Submitted.TrySetResult(null);
                dialogResult.TrySetResult(null);
            }
            finally
            {
                try
                {
                    if (dialog != null)
                    {
                        dialog.Hide();
                        contentDialogs.Remove(dialog);
                    }
                }
                catch
                {
                    // Диалог мог быть уже закрыт.
                }
            }
        });

        // Если UI-поток недоступен (окно закрывается), очередь может отказать
        // в постановке задачи. Без этого запрос капчи повис бы навсегда.
        if (!enqueued)
        {
            System.Diagnostics.Debug.WriteLine("[Captcha] Не удалось поставить окно капчи в очередь UI — возвращаем отказ");
            return null;
        }

        var result = await dialogResult.Task;

        System.Diagnostics.Debug.WriteLine(
            $"[Captcha] SolveAsync завершён: {(string.IsNullOrEmpty(result) ? "<отказ>" : "<код получен>")}");

        // Запоминаем ВВЕДЁННЫЙ ключ (ещё не проверенный VK).
        // Пустой результат CacheSuccessfulKey игнорирует, поэтому отказ не затрёт кэш.
        // При первом же отказе VK кэш сбрасывается в SolveFailedAsync.
        CacheSuccessfulKey(result);

        return result;
    }

    /// <summary>
    /// VK не принял введённый код капчи. Раньше метод был пустым —
    /// пользователь видел только повторно открывшееся окно без объяснения.
    /// </summary>
    public ValueTask SolveFailedAsync()
    {
        // КЛЮЧЕВОЕ: сбрасываем кэш введённого ключа.
        //
        // Без этого отвергнутый код оставался в _lastSuccessfulKey и уходил
        // в следующий запрос без ссылок — VK снова его отвергал, и цикл не прерывался.
        InvalidateCache();

        System.Diagnostics.Debug.WriteLine("[Captcha] VK не принял код — кэш сброшен, показываем уведомление");

        try
        {
            MainWindow.dispatcherQueue?.TryEnqueue(() =>
            {
                try
                {
                    ShowToast("Код капчи не принят", "Введите код ещё раз — VK выдал новую капчу.");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Captcha] Не удалось показать уведомление: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Captcha] Ошибка постановки уведомления в очередь: {ex.Message}");
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Короткое всплывающее уведомление поверх окна приложения.
    /// <para>
    /// Вставляется в корневую панель окна (XamlRoot.Content), а не в Frame —
    /// Frame не является Panel, и раньше уведомление просто не показывалось.
    /// </para>
    /// </summary>
    private static void ShowToast(string title, string message)
    {
        var root = MainWindow.contentFrame?.XamlRoot;
        var panel = root?.Content as Panel;

        if (panel == null)
        {
            System.Diagnostics.Debug.WriteLine(
                "[Captcha] Не удалось показать уведомление: корневой элемент окна не является Panel");
            return;
        }

        // Капчу могут отвергнуть несколько раз подряд: старое уведомление
        // убираем, чтобы они не накладывались друг на друга.
        if (_activeToast is not null)
        {
            panel.Children.Remove(_activeToast);
            _activeToast = null;
        }

        var infoBar = new InfoBar
        {
            Severity = InfoBarSeverity.Warning,
            Title = title,
            Message = message,
            IsOpen = true,
            IsClosable = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 24, 0, 0),
            MinWidth = 320
        };

        panel.Children.Add(infoBar);
        _activeToast = infoBar;

        infoBar.CloseButtonClick += (_, _) =>
        {
            panel.Children.Remove(infoBar);
            if (ReferenceEquals(_activeToast, infoBar))
            {
                _activeToast = null;
            }
        };

        // Автоматически убираем через 8 секунд, чтобы не засорять интерфейс.
        _ = HideAfterDelayAsync(panel, infoBar, TimeSpan.FromSeconds(8));
    }

    private static async Task HideAfterDelayAsync(Panel panel, InfoBar infoBar, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay);

            infoBar.IsOpen = false;
            panel.Children.Remove(infoBar);

            if (ReferenceEquals(_activeToast, infoBar))
            {
                _activeToast = null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Captcha] Ошибка скрытия уведомления: {ex.Message}");
        }
    }
}
