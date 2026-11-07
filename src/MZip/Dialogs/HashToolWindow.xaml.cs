using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using MZip.Services;

namespace MZip.Dialogs;

/// <summary>
/// MD5 校验工具窗口（FR-107）：拖入/选择文件计算 MD5/SHA1/SHA256/CRC32，粘贴期望值自动比对。
/// </summary>
public partial class HashToolWindow : Window
{
    private readonly HashService _hashService = new HashService();
    private CancellationTokenSource? _cts;
    private HashResult? _lastResult;
    private bool _busy;

    public HashToolWindow()
    {
        InitializeComponent();
    }

    /// <summary>打开窗口后直接带入一个文件开始计算。</summary>
    public HashToolWindow(string filePath) : this()
    {
        Loaded += async (_, _) => await StartComputeAsync(filePath);
    }

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (_busy) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            if (File.Exists(files[0]))
            {
                await StartComputeAsync(files[0]);
            }
        }
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var dialog = new OpenFileDialog { Title = "选择要校验的文件", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true)
        {
            await StartComputeAsync(dialog.FileName);
        }
    }

    private async Task StartComputeAsync(string path)
    {
        _busy = true;
        CancelButton.IsEnabled = true;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _lastResult = null;

        FilePathBox.Text = path;
        FilePathBox.Foreground = (System.Windows.Media.Brush)FindResource("TextBrush");
        Md5Text.Text = Sha1Text.Text = Sha256Text.Text = Crc32Text.Text = string.Empty;
        CompareResult.Text = string.Empty;
        HashProgress.Value = 0;
        ProgressText.Text = "0%";
        StatusText.Text = "正在计算…";

        var progress = new Progress<int>(p =>
        {
            HashProgress.Value = p;
            ProgressText.Text = $"{p}%";
        });

        try
        {
            LogHelper.Info("开始计算哈希：" + path);
            HashResult result = await _hashService.ComputeFileAsync(path, progress, _cts.Token);
            _lastResult = result;
            Md5Text.Text = result.Md5;
            Sha1Text.Text = result.Sha1;
            Sha256Text.Text = result.Sha256;
            Crc32Text.Text = result.Crc32;
            StatusText.Text = $"计算完成：{Path.GetFileName(path)}";
            LogHelper.Info($"哈希计算完成：{path} MD5={result.Md5}");
            CompareExpect();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "已取消计算";
            LogHelper.Info("哈希计算已取消：" + path);
        }
        catch (Exception ex)
        {
            StatusText.Text = "计算失败：" + ex.Message;
            LogHelper.Error("哈希计算失败：" + path, ex);
        }
        finally
        {
            _busy = false;
            CancelButton.IsEnabled = false;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
    }

    private void ExpectBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        CompareExpect();
    }

    private void CompareExpect()
    {
        string expect = ExpectBox.Text.Trim();
        if (string.IsNullOrEmpty(expect) || _lastResult == null)
        {
            CompareResult.Text = string.Empty;
            return;
        }

        string normalized = expect.Replace("-", "").ToUpperInvariant();
        bool match = normalized == _lastResult.Md5 || normalized == _lastResult.Sha1
            || normalized == _lastResult.Sha256 || normalized == _lastResult.Crc32;

        CompareResult.Text = match ? "✓ 一致" : "✗ 不一致";
        CompareResult.Foreground = match ? Brushes.ForestGreen : Brushes.Firebrick;
    }

    private void CopyText(string text, string name)
    {
        if (string.IsNullOrEmpty(text)) return;
        Clipboard.SetText(text);
        StatusText.Text = $"已复制 {name} 到剪贴板";
    }

    private void CopyMd5_Click(object sender, RoutedEventArgs e) => CopyText(Md5Text.Text, "MD5");
    private void CopySha1_Click(object sender, RoutedEventArgs e) => CopyText(Sha1Text.Text, "SHA1");
    private void CopySha256_Click(object sender, RoutedEventArgs e) => CopyText(Sha256Text.Text, "SHA256");
    private void CopyCrc32_Click(object sender, RoutedEventArgs e) => CopyText(Crc32Text.Text, "CRC32");
}
