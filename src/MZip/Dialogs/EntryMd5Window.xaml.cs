using System.Windows;

namespace MZip.Dialogs;

/// <summary>
/// 包内条目 MD5 结果弹窗（FR-108）：展示哈希值并可一键复制。
/// </summary>
public partial class EntryMd5Window : Window
{
    public EntryMd5Window(string entryName, string md5)
    {
        InitializeComponent();
        EntryNameText.Text = entryName;
        Md5Text.Text = md5;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(Md5Text.Text))
        {
            Clipboard.SetText(Md5Text.Text);
        }
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
