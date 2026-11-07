using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using MZip.Models;
using MZip.Services;

namespace MZip.Dialogs;

/// <summary>
/// 包内图片预览窗口（FR-105）：解压到内存流显示，不落盘；支持滚轮缩放、适应窗口、上一张/下一张。
/// GIF 以首帧静态显示。
/// </summary>
public partial class ImagePreviewWindow : Window
{
    public static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };

    private readonly SevenZipEngine _engine;
    private readonly string _archivePath;
    private readonly string? _password;
    private readonly List<ArchiveItem> _images;
    private int _index;
    private double _zoom = 1.0;
    private bool _fitToWindow = true;
    private CancellationTokenSource? _cts;

    public static bool IsImageEntry(ArchiveItem item)
    {
        if (item.IsFolder) return false;
        string ext = Path.GetExtension(item.FullPath);
        return ImageExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    public ImagePreviewWindow(SevenZipEngine engine, string archivePath, string? password,
        IEnumerable<ArchiveItem> entries, ArchiveItem initial)
    {
        InitializeComponent();
        _engine = engine;
        _archivePath = archivePath;
        _password = password;
        _images = entries.Where(IsImageEntry).ToList();
        _index = Math.Max(0, _images.FindIndex(i => i.FullPath == initial.FullPath));
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await ShowCurrentAsync();
    }

    private async Task ShowCurrentAsync()
    {
        if (_images.Count == 0) return;
        ArchiveItem item = _images[_index];
        FileNameText.Text = item.FullPath;
        IndexText.Text = $"{_index + 1} / {_images.Count}";
        PrevButton.IsEnabled = _index > 0;
        NextButton.IsEnabled = _index < _images.Count - 1;

        // 大图不预览（解压后超过 50MB）
        const long MaxPreviewSize = 50L * 1024 * 1024;
        if (item.Size > MaxPreviewSize)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            PreviewImage.Source = null;
            MessageBox.Show(this, "图片过大（解压后超过 50MB），不支持预览。", "图片预览",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        LoadingPanel.Visibility = Visibility.Visible;
        PreviewImage.Source = null;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        try
        {
            MemoryStream stream = await _engine.ExtractEntryToStreamAsync(
                _archivePath, item.FullPath, _password, _cts.Token);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            PreviewImage.Source = bitmap;
            ApplyZoom();
            LogHelper.Info($"图片预览：{item.FullPath}（{bitmap.PixelWidth}x{bitmap.PixelHeight}）");
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            LogHelper.Error("图片预览失败：" + item.FullPath, ex);
            MessageBox.Show(this, "无法预览该图片：" + ex.Message, "图片预览",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void ApplyZoom()
    {
        if (PreviewImage.Source is not BitmapSource bitmap) return;

        if (_fitToWindow)
        {
            double viewWidth = Math.Max(100, ImageScroll.ViewportWidth - 4);
            double viewHeight = Math.Max(100, ImageScroll.ViewportHeight - 4);
            double scale = Math.Min(viewWidth / bitmap.PixelWidth, viewHeight / bitmap.PixelHeight);
            _zoom = Math.Clamp(scale, 0.05, 8.0);
        }

        ImageScale.ScaleX = _zoom;
        ImageScale.ScaleY = _zoom;
        ZoomText.Text = $"{_zoom * 100:0}%";
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (PreviewImage.Source == null) return;
        _fitToWindow = false;
        _zoom = e.Delta > 0 ? _zoom * 1.15 : _zoom / 1.15;
        _zoom = Math.Clamp(_zoom, 0.05, 8.0);
        ApplyZoom();
        e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Left && _index > 0)
        {
            _index--;
            _ = ShowCurrentAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Right && _index < _images.Count - 1)
        {
            _index++;
            _ = ShowCurrentAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        if (_index > 0)
        {
            _index--;
            _ = ShowCurrentAsync();
        }
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_index < _images.Count - 1)
        {
            _index++;
            _ = ShowCurrentAsync();
        }
    }

    private void Fit_Click(object sender, RoutedEventArgs e)
    {
        _fitToWindow = true;
        ApplyZoom();
    }

    private void ActualSize_Click(object sender, RoutedEventArgs e)
    {
        _fitToWindow = false;
        _zoom = 1.0;
        ApplyZoom();
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        base.OnClosed(e);
    }
}
