using System;
using System.Windows;
using System.Windows.Controls;
using QuickZip.Models;

namespace QuickZip.Dialogs
{
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _settings;

        public SettingsWindow(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings;

            FormatCombo.SelectedIndex = string.Equals(settings.LastCompressFormat, "zip", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            LevelCombo.SelectedIndex = FindLevelIndex(settings.CompressionLevel);
            OverwriteCombo.SelectedIndex = FindOverwriteIndex(settings.OverwriteMode);
        }

        private static int FindLevelIndex(int level)
        {
            int[] levels = { 0, 1, 3, 5, 7, 9 };
            int index = Array.IndexOf(levels, level);
            return index < 0 ? 3 : index;
        }

        private static int FindOverwriteIndex(string mode)
        {
            return mode switch
            {
                "Overwrite" => 1,
                "Skip" => 2,
                "Rename" => 3,
                _ => 0
            };
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            _settings.LastCompressFormat = FormatCombo.SelectedIndex == 1 ? "zip" : "7z";

            if (LevelCombo.SelectedItem is ComboBoxItem levelItem && levelItem.Tag != null
                && int.TryParse(levelItem.Tag.ToString(), out int level))
            {
                _settings.CompressionLevel = level;
            }

            if (OverwriteCombo.SelectedItem is ComboBoxItem overwriteItem && overwriteItem.Tag != null)
            {
                _settings.OverwriteMode = overwriteItem.Tag.ToString()!;
            }

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
