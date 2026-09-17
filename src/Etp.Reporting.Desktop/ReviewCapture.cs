using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Etp.Reporting.Desktop;

/// <summary>Captures the actual rendered WPF window surface for review when native screen capture is unavailable.</summary>
public static class ReviewCapture
{
    public static async Task CaptureAsync(Window window, string pngPath, int width, int height, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (width is < 400 or > 4000 || height is < 300 or > 2400) throw new ArgumentOutOfRangeException(nameof(width));
        var path = Path.GetFullPath(pngPath);
        if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a PNG output path.", nameof(pngPath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await window.Dispatcher.InvokeAsync(() =>
        {
            if (!window.IsLoaded || !window.IsVisible) throw new InvalidOperationException("Show the window before capturing it.");
            window.WindowState = WindowState.Normal;
            window.Width = width;
            window.Height = height;
            window.UpdateLayout();
        }, DispatcherPriority.Normal, token);
        await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.Render, token);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle, token);
        await window.Dispatcher.InvokeAsync(() =>
        {
            var actualWidth = checked((int)Math.Ceiling(window.ActualWidth));
            var actualHeight = checked((int)Math.Ceiling(window.ActualHeight));
            var bitmap = new RenderTargetBitmap(actualWidth, actualHeight, 96, 96, PixelFormats.Pbgra32);
            var background = new DrawingVisual();
            using (var drawing = background.RenderOpen())
                drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, actualWidth, actualHeight));
            bitmap.Render(background);
            bitmap.Render(window);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            encoder.Save(output);
        }, DispatcherPriority.Render, token);
    }

    public static async Task CaptureSizesAsync(Window window, string directory, string stem, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(stem) || stem != Path.GetFileName(stem) || stem.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Enter a simple capture name.", nameof(stem));
        await CaptureAsync(window, Path.Combine(directory, $"{stem}-1366x768.png"), 1366, 768, token);
        await CaptureAsync(window, Path.Combine(directory, $"{stem}-816x480.png"), 816, 480, token);
    }
}
