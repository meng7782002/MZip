using System.Windows;
using System.Windows.Controls;
using MZip.Services;

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

            var vault = new PasswordVault();
            if (vault.Passwords.Count > 0)
            {
                HistoryCombo.Items.Add(new ComboBoxItem { Content = "历史密码…" });
                foreach (string pwd in vault.Passwords)
                {
                    HistoryCombo.Items.Add(new ComboBoxItem { Content = pwd });
                }
                HistoryCombo.SelectedIndex = 0;
            }
            else
            {
                HistoryCombo.Visibility = Visibility.Collapsed;
            }

            Loaded += (_, _) => PwdBox.Focus();
        }

        private void HistoryCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            // 第 0 项是占位提示
            if (HistoryCombo.SelectedIndex <= 0) return;
            if (HistoryCombo.SelectedItem is ComboBoxItem item && item.Content is string pwd)
            {
                PwdBox.Password = pwd;
                PwdBox.Focus();
            }
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

        private void PwdBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                Ok_Click(sender, e);
            }
        }
    }
}
