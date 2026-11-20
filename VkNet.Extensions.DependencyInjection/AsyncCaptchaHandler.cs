using System.Reflection;
using Microsoft.Extensions.Logging;
using VkNet.Abstractions.Core;
using VkNet.Exception;
using VkNet.Utils;
using System;

namespace VkNet.Extensions.DependencyInjection;

/// <inheritdoc />
public class AsyncCaptchaHandler : ICaptchaHandler
{
    private static readonly MethodInfo PerformAsyncMethod = typeof(AsyncCaptchaHandler).GetMethod(nameof(PerformAsync), BindingFlags.Instance | BindingFlags.NonPublic)!;
    
    private readonly IAsyncCaptchaSolver? _captchaSolver;
    private readonly ILogger<CaptchaHandler>? _logger;

    /// <inheritdoc cref="T:CaptchaHandler" />
    public AsyncCaptchaHandler(ILogger<CaptchaHandler>? logger, IAsyncCaptchaSolver? captchaSolver)
    {
        _logger = logger;
        _captchaSolver = captchaSolver;
    }

    /// <inheritdoc />
    public int MaxCaptchaRecognitionCount { get; set; } = 3;

    /// <inheritdoc />
    public T? Perform<T>(Func<ulong?, string?, T> action)
    {
        if (typeof(T).IsAssignableTo(typeof(Task)))
        {
            return (T?)PerformAsyncMethod.MakeGenericMethod(typeof(T).GenericTypeArguments[0], typeof(T))
                .Invoke(this, new object[] {action});
        }
        
        var context = new CaptchaSolveContext(MaxCaptchaRecognitionCount);
        var flag = false;
        var obj = default (T);
        do
        {
            try
            {
                obj = action(context.CaptchaSidTemp, context.CaptchaKeyTemp);
                flag = true;
            }
            catch (CaptchaNeededException ex)
            {
                if (!RepeatSolveCaptchaAsync(ex, context).Result)
                    break;
            }
        }
        while (context.RemainingSolveAttempts > 0 && !flag);
        if (flag || !context.CaptchaSidTemp.HasValue)
            return obj;

        throw new CaptchaNeededException(new()
        {
            CaptchaSid = context.CaptchaSidTemp.Value
        });
    }

    private async Task<T?> PerformAsync<T, T2>(Func<ulong?, string?, T2> action) where T2 : Task<T>
    {
        var context = new CaptchaSolveContext(MaxCaptchaRecognitionCount);
        var flag = false;
        var obj = default (T);
        do
        {
            // КЛЮЧЕВОЕ: передаём sid и key ТОЛЬКО вместе.
            // Если sid поменялся (VK выдал новую капчу), старый key к ней не подходит —
            // VK его отвергает, и мы получаем бесконечный цикл «капча → отказ → капча».
            if (context.CaptchaSidTemp.HasValue && context.SolvedForSid != context.CaptchaSidTemp)
            {
                _logger?.LogInformation(
                    "[Captcha] sid изменился ({OldSid} -> {NewSid}) — сбрасываем старый ключ",
                    context.SolvedForSid,
                    context.CaptchaSidTemp);

                context.CaptchaKeyTemp = null;
            }

            try
            {
                _logger?.LogInformation(
                    "[Captcha] Вызов API: sid={Sid}, key={Key}, осталось попыток={Remaining}",
                    context.CaptchaSidTemp,
                    string.IsNullOrEmpty(context.CaptchaKeyTemp) ? "<нет>" : "<есть>",
                    context.RemainingSolveAttempts);

                obj = await action(context.CaptchaSidTemp, context.CaptchaKeyTemp);
                flag = true;

                _logger?.LogInformation("[Captcha] Вызов API успешен");
            }
            catch (CaptchaNeededException ex)
            {
                _logger?.LogInformation(
                    "[Captcha] VK запросил капчу: sid={Sid} (было {OldSid}), img={Img}, redirect={Redirect}",
                    ex.Sid,
                    context.CaptchaSidTemp,
                    ex.Img?.AbsoluteUri ?? "<нет>",
                    ex.RedirectUri?.ToString() ?? "<нет>");

                // Ключ был передан, но VK выдал капчу снова с ДРУГИМ sid —
                // значит введённый код не принят. Сбрасываем ключ, чтобы он не ушёл
                // в следующий вызов вместе с чужим sid. Уведомление пользователю
                // показывает RepeatSolveCaptchaAsync (единая точка, без дублей).
                bool keyWasRejected = context.CaptchaKeyTemp is not null
                    && context.CaptchaSidTemp.HasValue
                    && context.CaptchaSidTemp != ex.Sid;

                if (keyWasRejected)
                {
                    _logger?.LogWarning(
                        "[Captcha] Код не принят: sid {OldSid} -> {NewSid}",
                        context.CaptchaSidTemp,
                        ex.Sid);

                    context.CaptchaKeyTemp = null;
                    context.SolvedForSid = null;
                }

                if (!await RepeatSolveCaptchaAsync(ex, context))
                    break;
            }
        }
        while (context.RemainingSolveAttempts > 0 && !flag);

        if (flag || !context.CaptchaSidTemp.HasValue)
            return obj;

        _logger?.LogWarning(
            "[Captcha] Попытки исчерпаны, пробрасываем CaptchaNeededException (sid={Sid})",
            context.CaptchaSidTemp.Value);

        throw new CaptchaNeededException(new()
        {
            CaptchaSid = context.CaptchaSidTemp.Value
        });
    }
    
    private async Task<bool> RepeatSolveCaptchaAsync(
        CaptchaNeededException captchaNeededException,
        CaptchaSolveContext context)
    {
        _logger?.LogWarning("Повторная обработка капчи");

        try
        {
            if (context.RemainingSolveAttempts < MaxCaptchaRecognitionCount && _captchaSolver is not null)
                await _captchaSolver.SolveFailedAsync();

            if (context.RemainingSolveAttempts <= 0)
            {
                _logger?.LogWarning("[Captcha] Попытки ввода исчерпаны (0) — капча не решена");
                return false;
            }

            context.CaptchaSidTemp = captchaNeededException.Sid;

            // VK может прислать запрос капчи БЕЗ картинки (Img == null) и без RedirectUri.
            // Раньше здесь был оператор `!` — он превращал это в NullReferenceException,
            // и вход падал вместо того, чтобы вернуть ранее введённый код.
            string? imageUrl = captchaNeededException.Img?.AbsoluteUri;
            string? redirectUri = captchaNeededException.RedirectUri?.ToString();

            _logger?.LogInformation(
                "[Captcha] Решаем: sid={Sid}, image={Image}, redirect={Redirect}, solver={Solver}",
                context.CaptchaSidTemp,
                imageUrl ?? "<нет>",
                redirectUri ?? "<нет>",
                _captchaSolver is null ? "<не зарегистрирован>" : _captchaSolver.GetType().Name);

            if (_captchaSolver is null)
            {
                _logger?.LogError("[Captcha] IAsyncCaptchaSolver не зарегистрирован в DI — решить капчу нельзя");
                context.RemainingSolveAttempts--;
                return false;
            }

            // Без оператора `!`: при капче без картинки imageUrl == null,
            // и солвер должен получить именно null, чтобы сработала ветка
            // «нет ссылок» (а не пустая строка, которую он примет за валидный url).
            context.CaptchaKeyTemp = await _captchaSolver.SolveAsync(imageUrl, redirectUri);
            context.RemainingSolveAttempts--;

            // Запоминаем, для какого sid получен ключ: следующий вызов API
            // должен использовать ключ только вместе с этим sid.
            context.SolvedForSid = context.CaptchaKeyTemp is not null
                ? context.CaptchaSidTemp
                : null;

            _logger?.LogInformation(
                "[Captcha] Результат ввода: {Result}, sid={Sid}, осталось попыток={Remaining}",
                string.IsNullOrEmpty(context.CaptchaKeyTemp) ? "<пусто/отказ>" : "<код получен>",
                context.CaptchaSidTemp,
                context.RemainingSolveAttempts);

            return context.CaptchaKeyTemp is not null;
        }
        catch (System.OperationCanceledException ex)
        {
            _logger?.LogWarning(ex, "[Captcha] Ввод капчи отменён");
            context.RemainingSolveAttempts--;
            context.CaptchaKeyTemp = null;
            return false;
        }
        catch (System.TimeoutException ex)
        {
            // Именно этот случай виден в логах как TimeoutException из System.IO.Pipes:
            // WebView2/диалог не ответил вовремя. Авторизация не должна зависать.
            _logger?.LogWarning(ex, "[Captcha] Таймаут ожидания ответа капчи");
            context.RemainingSolveAttempts--;
            context.CaptchaKeyTemp = null;
            return false;
        }
        catch (System.Exception ex)
        {
            // Любая ошибка решения капчи не должна ронять авторизацию:
            // считаем попытку неудачной и отдаём управление вызывающему коду.
            _logger?.LogError(ex, "[Captcha] Ошибка обработки капчи: {Type}", ex.GetType().FullName);
            context.RemainingSolveAttempts--;
            context.CaptchaKeyTemp = null;
            return false;
        }
    }

    private class CaptchaSolveContext
    {
        public int RemainingSolveAttempts;
        public ulong? CaptchaSidTemp;
        public string? CaptchaKeyTemp;

        /// <summary>
        /// sid, для которого был получен <see cref="CaptchaKeyTemp"/>.
        /// Ключ и sid передаются в API только парой: если VK выдал новую капчу,
        /// старый ключ к ней не подходит и его нужно сбросить.
        /// </summary>
        public ulong? SolvedForSid;

        public CaptchaSolveContext(int maxSolveAttempts)
        {
            RemainingSolveAttempts = maxSolveAttempts;
        }
    }
}