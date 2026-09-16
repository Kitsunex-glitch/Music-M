using System;

namespace VK_UI3.Views.Notification
{
    public enum NotificationType
    {
        Standard,
        Survey
    }

    public enum ButtonActionType
    {
        Url,        // Открыть URL
        Event,      // Отправить событие
        Close       // Закрыть уведомление
    }

    public enum InputFieldType
    {
        Text,
        Number,
        Multiline
    }
}