using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VK_UI3.Views.Notification
{
    internal class Notification
    {
        public static ObservableCollection<Notification> Notifications = new ObservableCollection<Notification>();

        public string header { get; set; }
        public string Message { get; set; }

        public NotificationType Type { get; set; } = NotificationType.Standard;
        public List<ButtonNotification> Buttons { get; set; } = new List<ButtonNotification>();
        public InputField InputField { get; set; }
        public bool ShowUntilAnswered { get; set; }
        public string DismissButtonText { get; set; }
        public int SurveyId { get; set; }

        public object ContentPage { get; set; } // Может быть Page или UserControl

        // Конструктор для обратной совместимости (старые уведомления с двумя кнопками)
        public Notification(string header = null, string message = null, ButtonNotification button1 = null, ButtonNotification button2 = null)
        {
            this.header = header;
            Message = message;
            if (button1 != null) Buttons.Add(button1);
            if (button2 != null) Buttons.Add(button2);
            Notifications.Add(this);
        }

        // Конструктор для опросников с списком кнопок
        public Notification(string header, string message, List<ButtonNotification> buttons, InputField inputField = null, bool showUntilAnswered = false, string dismissButtonText = null, int surveyId = 0)
        {
            this.header = header;
            Message = message;
            Buttons = buttons ?? new List<ButtonNotification>();
            InputField = inputField;
            ShowUntilAnswered = showUntilAnswered;
            DismissButtonText = dismissButtonText;
            SurveyId = surveyId;
            Type = NotificationType.Survey;
            Notifications.Add(this);
        }

        // Конструктор для контентной страницы
        public Notification(object contentPage, string header = null, string message = null, ButtonNotification button1 = null, ButtonNotification button2 = null)
        {
            this.ContentPage = contentPage;
            this.header = header;
            Message = message;
            if (button1 != null) Buttons.Add(button1);
            if (button2 != null) Buttons.Add(button2);
            Notifications.Add(this);
        }

        internal void Delete()
        {
            Notifications.Remove(this);
        }
    }

    public class ButtonNotification
    {
        public ButtonNotification(string text, Action btnAction, bool closeNotification = false)
        {
            Text = text;
            BtnAction = btnAction;
            this.closeNotification = closeNotification;
            ActionType = ButtonActionType.Close; // По умолчанию
        }

        public ButtonNotification(string text, ButtonActionType actionType, string actionValue = null, bool closeNotification = false)
        {
            Text = text;
            ActionType = actionType;
            ActionValue = actionValue;
            this.closeNotification = closeNotification;
        }

        public bool closeNotification { get; set; }
        public string Text { get; set; }
        public Action BtnAction { get; set; } // Устаревшее, но оставлено для совместимости
        public ButtonActionType ActionType { get; set; }
        public string ActionValue { get; set; }
    }
}
