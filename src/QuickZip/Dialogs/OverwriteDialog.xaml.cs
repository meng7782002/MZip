using System.Windows;

namespace QuickZip.Dialogs
{
    public partial class OverwriteDialog : Window
    {
        public string Mode { get; private set; } = "Overwrite";

        public OverwriteDialog()
        {
            InitializeComponent();
        }

        private void Overwrite_Click(object sender, RoutedEventArgs e)
        {
            Mode = "Overwrite";
            DialogResult = true;
        }

        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            Mode = "Skip";
            DialogResult = true;
        }

        private void Rename_Click(object sender, RoutedEventArgs e)
        {
            Mode = "Rename";
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
