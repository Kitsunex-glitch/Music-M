using System;
using System.Collections.Generic;
using System.Linq;

namespace VK_UI3.DB
{
    public static class SurveyResponseManager
    {
        public static void SaveResponse(int surveyId, string responseType, string responseValue)
        {
            var response = new SurveyResponse(surveyId, responseType, responseValue);
            DatabaseHandler.getConnect().Insert(response);
        }

        public static bool HasResponse(int surveyId)
        {
            var count = DatabaseHandler.getConnect()
                .Table<SurveyResponse>()
                .Count(r => r.SurveyId == surveyId);
            return count > 0;
        }

        public static bool HasResponse(int surveyId, string responseType)
        {
            var count = DatabaseHandler.getConnect()
                .Table<SurveyResponse>()
                .Count(r => r.SurveyId == surveyId && r.ResponseType == responseType);
            return count > 0;
        }

        public static List<SurveyResponse> GetResponses(int surveyId)
        {
            return DatabaseHandler.getConnect()
                .Table<SurveyResponse>()
                .Where(r => r.SurveyId == surveyId)
                .ToList();
        }

        public static void DeleteResponses(int surveyId)
        {
            DatabaseHandler.getConnect()
                .Table<SurveyResponse>()
                .Delete(r => r.SurveyId == surveyId);
        }
    }
}