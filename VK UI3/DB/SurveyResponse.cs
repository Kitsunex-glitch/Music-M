using SQLite;
using System;

namespace VK_UI3.DB
{
    public class SurveyResponse
    {
        [PrimaryKey]
        [AutoIncrement]
        public long Id { get; set; }

        public int SurveyId { get; set; }
        public string ResponseType { get; set; } // "button", "text", "dismiss"
        public string ResponseValue { get; set; } // значение кнопки или текст ответа
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public SurveyResponse() { }

        public SurveyResponse(int surveyId, string responseType, string responseValue)
        {
            SurveyId = surveyId;
            ResponseType = responseType;
            ResponseValue = responseValue;
        }
    }
}