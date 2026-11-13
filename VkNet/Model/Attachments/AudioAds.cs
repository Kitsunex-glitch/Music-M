using Newtonsoft.Json;
using VkNet.Utils;

namespace VkNet.Model.Attachments
{
	/// <summary>
	/// Рекламная информация о треке (поле <c>ads</c>).
	/// </summary>
	public class AudioAds
	{
		/// <summary>
		/// Идентификатор рекламного контента.
		/// </summary>
		[JsonProperty("content_id")]
		public string ContentId { get; set; }

		/// <summary>
		/// Длительность рекламной вставки в секундах.
		/// </summary>
		[JsonProperty("duration")]
		public string Duration { get; set; }

		/// <summary>
		/// Тип возраста аккаунта.
		/// </summary>
		[JsonProperty("account_age_type")]
		public string AccountAgeType { get; set; }

		/// <summary>
		/// Параметр puid1.
		/// </summary>
		[JsonProperty("puid1")]
		public string Puid1 { get; set; }

		/// <summary>
		/// Параметр puid22.
		/// </summary>
		[JsonProperty("puid22")]
		public string Puid22 { get; set; }

		/// <summary>
		/// Разобрать из json.
		/// </summary>
		/// <param name="response"> Ответ сервера. </param>
		/// <returns> </returns>
		public static AudioAds FromJson(VkResponse response)
		{
			return new AudioAds
			{
				ContentId = response["content_id"],
				Duration = response["duration"],
				AccountAgeType = response["account_age_type"],
				Puid1 = response["puid1"],
				Puid22 = response["puid22"]
			};
		}

		/// <summary>
		/// Преобразование <see cref="VkResponse" /> в <see cref="AudioAds" />
		/// </summary>
		/// <param name="response"> Ответ сервера. </param>
		public static implicit operator AudioAds(VkResponse response)
		{
			if (response == null)
			{
				return null;
			}

			return response.HasToken()
				? FromJson(response)
				: null;
		}
	}
}
