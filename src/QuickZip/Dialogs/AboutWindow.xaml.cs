using System.Windows;

namespace QuickZip.Dialogs
{
    public partial class AboutWindow : Window
    {
        public AboutWindow(string engineVersion)
        {
            InitializeComponent();
            EngineText.Text = $"内置压缩引擎：7-Zip {engineVersion}";
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
