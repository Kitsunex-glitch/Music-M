using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using VK_UI3.DB;
using StatDefaultGateLib.Models;
using VK_UI3;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace VK_UI3.Views.Notification
{
    public sealed partial class NotifController : UserControl
    {
        private Notification _currentNotification;

        public NotifController()
        {
            this.InitializeComponent();
        }

        private void UserControl_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
        {
            try
            {
                if (DataContext is not Notification notification)
                    return;

                _currentNotification = notification;

                HeaderB.Text = notification.header;
                TextB.Text = notification.Message;

                if (notification.ContentPage != null)
                {
                    if (PageContent.Content != null && PageContent.Content != notification.ContentPage)
                    {
                        PageContent.Content = null;
                    }
                    PageContent.Content = notification.ContentPage;
                    PageContent.Visibility = Visibility.Visible;
                    TextB.Visibility = Visibility.Collapsed;
                    HeaderB.Visibility = Visibility.Collapsed;
                }
                else
                {
                    PageContent.Content = null;
                    PageContent.Visibility = Visibility.Collapsed;
                    if (string.IsNullOrEmpty(notification.header))
                    {
                        HeaderB.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        HeaderB.Visibility = Visibility.Visible;
                    }

                    if (string.IsNullOrEmpty(notification.Message))
                    {
                        TextB.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        TextB.Visibility = Visibility.Visible;
                    }
                }

                // Настройка поля ввода
                ConfigureInputField(notification);

                // Настройка кнопок
                ConfigureButtons(notification);

                // Кнопка "Не хочу отвечать"
                ConfigureDismissButton(notification);

                UpdateRowHeights();

                // Отправка события о показе уведомления (только для опросников)
                if (notification.Type == NotificationType.Survey)
                {
                    SendStatDefaultGateEvent("survey_shown", notification.SurveyId, null, null);
                }
            }
            catch (Exception ex) 
            {
                // Логирование ошибки
                System.Diagnostics.Debug.WriteLine($"Ошибка в UserControl_DataContextChanged: {ex.Message}");
            }
        }

        private void ConfigureInputField(Notification notification)
        {
            InputPanel.Visibility = Visibility.Collapsed;
            InputTextBox.Visibility = Visibility.Collapsed;
            SubmitInputButton.Visibility = Visibility.Collapsed;

            if (notification.InputField != null)
            {
                InputPanel.Visibility = Visibility.Visible;
                InputTextBox.Visibility = Visibility.Visible;
                SubmitInputButton.Visibility = Visibility.Visible;

                InputTextBox.PlaceholderText = notification.InputField.Placeholder ?? "Введите ответ";
                InputTextBox.Text = notification.InputField.DefaultValue ?? "";
                InputTextBox.MaxLength = notification.InputField.MaxLength ?? 0;
                // Настройка многострочности в зависимости от типа
                if (notification.InputField.Type == InputFieldType.Multiline)
                {
                    InputTextBox.AcceptsReturn = true;
                    InputTextBox.TextWrapping = TextWrapping.Wrap;
                    InputTextBox.Height = 100;
                }
                else
                {
                    InputTextBox.AcceptsReturn = false;
                    InputTextBox.TextWrapping = TextWrapping.NoWrap;
                    InputTextBox.Height = Double.NaN;
                }
            }
        }

        private void ConfigureButtons(Notification notification)
        {
            ButtonsItemsControl.Visibility = Visibility.Collapsed;
            ButtonsItemsControl.ItemsSource = null;

            if (notification.Buttons != null && notification.Buttons.Count > 0)
            {
                ButtonsItemsControl.ItemsSource = notification.Buttons;
                ButtonsItemsControl.Visibility = Visibility.Visible;
            }
        }

        private void ConfigureDismissButton(Notification notification)
        {
            DismissButton.Visibility = Visibility.Collapsed;
            if (!string.IsNullOrEmpty(notification.DismissButtonText))
            {
                DismissButton.Content = notification.DismissButtonText;
                DismissButton.Visibility = Visibility.Visible;
            }
            else if (notification.Type == NotificationType.Survey && notification.ShowUntilAnswered)
            {
                DismissButton.Content = "Не хочу отвечать";
                DismissButton.Visibility = Visibility.Visible;
            }
        }

        private void UpdateRowHeights()
        {
            // 0: HeaderB, 1: TextB, 2: PageContent, 3: InputPanel, 4: ButtonsItemsControl, 5: DismissButton, 6: CloseButton
            if (MainGrid.RowDefinitions.Count >= 7)
            {
                MainGrid.RowDefinitions[0].Height = HeaderB.Visibility == Visibility.Visible ? GridLength.Auto : new GridLength(0);
                MainGrid.RowDefinitions[1].Height = TextB.Visibility == Visibility.Visible ? GridLength.Auto : new GridLength(0);
                MainGrid.RowDefinitions[2].Height = PageContent.Visibility == Visibility.Visible ? GridLength.Auto : new GridLength(0);
                MainGrid.RowDefinitions[3].Height = InputPanel.Visibility == Visibility.Visible ? GridLength.Auto : new GridLength(0);
                MainGrid.RowDefinitions[4].Height = ButtonsItemsControl.Visibility == Visibility.Visible ? GridLength.Auto : new GridLength(0);
                MainGrid.RowDefinitions[5].Height = DismissButton.Visibility == Visibility.Visible ? GridLength.Auto : new GridLength(0);
                // Row 6 (кнопка закрытия) всегда видна, высота auto
            }
        }

        private void DynamicButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ButtonNotification btnNotification)
            {
                // Выполнить действие кнопки
                if (btnNotification.BtnAction != null)
                {
                    btnNotification.BtnAction();
                }

                // Обработка действия в зависимости от типа
                if (btnNotification.ActionType == ButtonActionType.Url && !string.IsNullOrEmpty(btnNotification.ActionValue))
                {
                    // Открыть URL (можно использовать Process.Start)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(btnNotification.ActionValue) { UseShellExecute = true });
                }
                else if (btnNotification.ActionType == ButtonActionType.Event)
                {
                    // Отправить событие в StatDefaultGate
                    SendStatDefaultGateEvent("survey_button_click", _currentNotification?.SurveyId ?? 0, btnNotification.Text, btnNotification.ActionValue);
                }

                // Сохранить ответ в БД (для опросников)
                if (_currentNotification?.Type == NotificationType.Survey)
                {
                    SurveyResponseManager.SaveResponse(_currentNotification.SurveyId, "button", btnNotification.ActionValue ?? btnNotification.Text);
                }

                // Закрыть уведомление, если closeNotification = true
                if (btnNotification.closeNotification && _currentNotification != null)
                {
                    _currentNotification.Delete();
                }
            }
        }

        private void SubmitInputButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentNotification == null) return;

            string inputText = InputTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(inputText) && _currentNotification.InputField?.Required == true)
            {
                // Можно показать ошибку
                return;
            }

            // Сохранить ответ в БД
            if (_currentNotification.Type == NotificationType.Survey)
            {
                SurveyResponseManager.SaveResponse(_currentNotification.SurveyId, "text", inputText);
                SendStatDefaultGateEvent("survey_text_submitted", _currentNotification.SurveyId, null, inputText);
            }

            // Закрыть уведомление
            _currentNotification.Delete();
        }

        private void DismissButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentNotification == null) return;

            // Сохранить отказ в БД
            if (_currentNotification.Type == NotificationType.Survey)
            {
                SurveyResponseManager.SaveResponse(_currentNotification.SurveyId, "dismiss", null);
                SendStatDefaultGateEvent("survey_dismissed", _currentNotification.SurveyId, null, null);
            }

            _currentNotification.Delete();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not Notification notification)
            {
                return;
            }
            // Отправка события о закрытии без ответа (если это опросник)
            if (notification.Type == NotificationType.Survey)
            {
                SendStatDefaultGateEvent("survey_closed", notification.SurveyId, null, null);
            }
            notification.Delete();
        }

        private void SendStatDefaultGateEvent(string eventName, int surveyId, string buttonText, string value)
        {
            try
            {
                var eventParams = new List<EventParams>();
                eventParams.Add(new EventParams("survey_id", surveyId));
                if (!string.IsNullOrEmpty(buttonText))
                    eventParams.Add(new EventParams("button_text", buttonText));
                if (!string.IsNullOrEmpty(value))
                    eventParams.Add(new EventParams("value", value));

                Event @event = new Event(eventName, DateTime.Now, eventParams: eventParams);
                _ = new MusicMStatDefaultGate().SendEvent(@event);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка отправки события StatDefaultGate: {ex.Message}");
            }
        }
    }
}
