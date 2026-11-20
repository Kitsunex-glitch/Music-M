using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Newtonsoft.Json;

namespace VkNet.Model
{
	/// <summary>
	/// Ошибка при вызове метода exeсute
	/// </summary>
	[Serializable]
	public class VkError
	{
		/// <summary>
		/// Метод в котором произошла ошибка
		/// </summary>
		[JsonProperty("method")]
		public string Method { get; set; }

		/// <summary>
		/// Код ошибки
		/// </summary>
		[JsonProperty("error_code")]
		public int ErrorCode { get; set; }

		/// <summary>
		/// Сообщение об ошибке
		/// </summary>
		[JsonProperty("error_msg")]
		public string ErrorMessage { get; set; }

		/// <summary>
		/// Параметры запроса
		/// </summary>
		[JsonProperty("request_params")]
		public ReadOnlyCollection<RequestParam> RequestParams { get; set; }

		/// <summary>
		/// Идентификатор капчи
		/// </summary>
		[JsonProperty("captcha_sid", NullValueHandling = NullValueHandling.Ignore)]
		public ulong CaptchaSid { get; set; }

		/// <summary>
		/// Ссылка на изображение капчи
		/// </summary>
		[JsonProperty("captcha_img", NullValueHandling = NullValueHandling.Ignore)]
		public Uri CaptchaImg { get; set; }

		/// <summary>
		/// Адрес который необходимо открыть в браузере для подтверждения валидации.
		/// </summary>
		[JsonProperty("redirect_uri", NullValueHandling = NullValueHandling.Ignore)]
		public Uri RedirectUri { get; set; }

		/// <summary>
		/// Временный идентификатор для server-to-server капчи VK ID.
		/// <para>
		/// VK требует передавать <c>success_token</c> вместе с <c>remixstlid</c>:
		/// значение параметра и cookie должны совпадать. Если VK присылает это поле
		/// в ошибке капчи, его нужно сохранить и вернуть в повторном запросе.
		/// </para>
		/// </summary>
		[JsonProperty("remixstlid", NullValueHandling = NullValueHandling.Ignore)]
		public string Remixstlid { get; set; }

		/// <summary>
		/// Все прочие поля ошибки, которые не описаны явно.
		/// <para>
		/// Нужны, чтобы не терять новые параметры, которые VK добавляет
		/// в ответ с ошибкой капчи (например, дополнительные токены сессии).
		/// </para>
		/// </summary>
		[JsonExtensionData]
		public IDictionary<string, object> AdditionalData { get; set; }
	}
}