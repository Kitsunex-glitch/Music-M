namespace VkNet.Extensions.DependencyInjection;

/// <summary>
/// Определяет интерфейс взаимодействия с сервисом-распознавателем капчи
/// </summary>
public interface IAsyncCaptchaSolver
{
    /// <summary>Распознает текст капчи.</summary>
    /// <param name="url">
    /// Ссылка на изображение капчи. Может быть <c>null</c>: VK иногда запрашивает
    /// подтверждение капчи, присылая запрос без картинки и без redirect-uri.
    /// </param>
    /// <param name="redirectUri"> Ссылка на веб-капчу VK ID (если VK её прислал). </param>
    /// <returns> Строка, содержащая текст, который был закодирован в капче. </returns>
    ValueTask<string?> SolveAsync(string? url, string? redirectUri = null);
    /// <summary>
    /// Сообщает сервису, что последняя капча была распознана неверно.
    /// </summary>
    ValueTask SolveFailedAsync();
}