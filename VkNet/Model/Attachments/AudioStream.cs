using System;
using Newtonsoft.Json;
using VkNet.Utils;

namespace VkNet.Model.Attachments
{
	/// <summary>
	/// Аудиопоток трека (поле <c>audio_streams</c>).
	/// </summary>
	public class AudioStream
	{
		/// <summary>
		/// Тип потока: aac, dash, flac, hls_range, hls_ts, mp3 и т.п.
		/// </summary>
		[JsonProperty("type")]
		public string Type { get; set; }

		/// <summary>
		/// Ссылка на поток.
		/// </summary>
		[JsonProperty("url")]
		public Uri Url { get; set; }

		/// <summary>
		/// Резервная ссылка на поток.
		/// </summary>
		[JsonProperty("fallback_url")]
		public Uri FallbackUrl { get; set; }

		/// <summary>
		/// Разобрать из json.
		/// </summary>
		/// <param name="response"> Ответ сервера. </param>
		/// <returns> </returns>
		public static AudioStream FromJson(VkResponse response)
		{
			return new AudioStream
			{
				Type = response["type"],
				Url = response["url"],
				FallbackUrl = response["fallback_url"]
			};
		}

		/// <summary>
		/// Преобразование <see cref="VkResponse" /> в <see cref="AudioStream" />
		/// </summary>
		/// <param name="response"> Ответ сервера. </param>
		public static implicit operator AudioStream(VkResponse response)
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
