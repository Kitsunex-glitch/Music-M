using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Threading.Tasks;
using VK_UI3.DB;
using VK_UI3.VKs;
using VkNet.AudioBypassService.Models.Auth;
using Page = Microsoft.UI.Xaml.Controls.Page;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace VK_UI3.Views.LoginWindow
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public partial class OtpCode : Page, NavigateArgsSender
    {
        internal VKAuth vk;

        public int CodeLength { get; set; }
        public string? Info { get; set; }
        public LoginWay loginWay { get; set; } = LoginWay.None;
        public bool HasAnotherVerificationMethods { get; set; }







        public OtpCode()
        {
            this.InitializeComponent();
            this.Loaded += OtpCode_Loaded;
        }

        private void OtpCode_Loaded(object sender, RoutedEventArgs e)
        {
            passpey.Focus(FocusState.Pointer);
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // Параметр передается как объект, поэтому его нужно привести к нужному типу
            var viewModel = e.Parameter as OtpCode;

            if (viewModel != null)
            {


                CodeLength = (int)viewModel.CodeLength;
                Info = viewModel.Info;
                loginWay = viewModel.loginWay;
                var txt = "Вам был отправлен код авторизации.";

                if (loginWay == LoginWay.Codegen)
                    txt = "Ваш код был сгенерирован приложением для генерации";

                if (loginWay == LoginWay.CallReset)
                    txt = "Вам сейчас позвонят и продиктуют код авторизации";

                if (loginWay == LoginWay.Email)
                    txt = "Ваш код авторизации был отправлен на ваш Mail";
                if (this.Info != "" && this.Info != null) txt += $" ({Info})";

                if (loginWay == LoginWay.Sms)
                {
                    txt = "Ваш код авторизации был отправлен в смс сообщении";
                    if (this.Info != "" && this.Info != null) txt += " на номер " + Info;
                }
                if (loginWay == LoginWay.Push)
                    txt = "Ваш код авторизации был отправлен в приложение ВК на Вашем мобильном устройстве.";

                if (loginWay == LoginWay.ReserveCode)
                    txt = "Используйте резервный код из сохранённого Вами списка.";



                passpey.Text = txt;

                HasAnotherVerificationMethods = viewModel.HasAnotherVerificationMethods;

                if (!HasAnotherVerificationMethods) goAnotherBTN.Visibility = Visibility.Collapsed;



                this.vk = viewModel.vk;


            }


        }

        private async void BackButton(object sender, RoutedEventArgs e)
        {
            // _ = await InputTextDialogAsync("hello!", this.XamlRoot);
            AccountsDB.activeAccount = new AccountsDB.Accounts();
            this.Frame.Navigate(typeof(Login));
        }
        private void SubmitButton_Click(object sender, RoutedEventArgs e)
        {
            sumbit();
        }

        private void sumbit()
        {
            // Код проверяется в фоне: ошибка вернётся сюда через SendArgs,
            // а не потеряется в необработанном исключении.
            _ = vk.Vk2FaCompleteAsync(CodeBox.Text);

        }

        private void ShowAnotherVerificationMethodsButton_Click(object sender, RoutedEventArgs e)
        {
            vk.ShowAnotherVerificationMethodsAsync();
        }

        private void passpey_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {

                sumbit();
            }

        }

        public void SendArgs(ArgSender argSender)
        {
            // Раньше метод был пустым: ошибка входа на этой странице никак не показывалась.
            if (argSender == null || string.IsNullOrEmpty(argSender.ErrorText))
                return;

            System.Diagnostics.Debug.WriteLine($"[OtpCode] Ошибка: {argSender.ErrorText}");

            // Своего блока ошибки на странице нет — показываем диалог,
            // чтобы пользователь не остался без обратной связи.
            _ = ShowErrorAsync(argSender.ErrorText);
        }

        private async Task ShowErrorAsync(string message)
        {
            try
            {
                var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
                {
                    XamlRoot = this.XamlRoot,
                    Title = "Ошибка входа",
                    Content = message,
                    CloseButtonText = "Ок"
                };

                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OtpCode] Не удалось показать ошибку: {ex.Message}");
            }
        }
    }
}
