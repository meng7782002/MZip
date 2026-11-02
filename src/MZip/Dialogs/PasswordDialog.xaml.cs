using System.Windows;
using System.Windows.Input;

namespace MZip.Dialogs
{
    public partial class PasswordDialog : Window
    {
        public string Password { get; private set; } = string.Empty;

        public PasswordDialog(string archiveName, bool previousFailed = false)
        {
            InitializeComponent();
            ArchiveNameText.Text = archiveName;
            if (previousFailed)
            {
                ErrorText.Text = "密码错误，请重新输入。";
            }
            Loaded += (_, _) => PwdBox.Focus();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(PwdBox.Password))
            {
                ErrorText.Text = "请输入密码。";
                PwdBox.Focus();
                return;
            }
            Password = PwdBox.Password;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void PwdBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Ok_Click(sender, e);
            }
        }
    }
}
