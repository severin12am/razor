using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ResourcePanel;

static class IconFactory
{
    public static ImageSource WindowIcon()
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var green = new SolidColorBrush(Color.FromRgb(0x6F, 0xDB, 0xA2));
            var ink = new SolidColorBrush(Color.FromRgb(0x14, 0x22, 0x1A));
            green.Freeze();
            ink.Freeze();
            context.DrawRoundedRectangle(green, null, new Rect(0, 0, 32, 32), 7, 7);
            context.DrawRectangle(ink, null, new Rect(7, 18, 4, 8));
            context.DrawRectangle(ink, null, new Rect(14, 12, 4, 14));
            context.DrawRectangle(ink, null, new Rect(21, 7, 4, 19));
        }
        var bitmap = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static System.Drawing.Icon TrayIcon()
    {
        using var bitmap = new System.Drawing.Bitmap(32, 32);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.Clear(System.Drawing.Color.FromArgb(111, 219, 162));
            using var ink = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(20, 34, 26));
            graphics.FillRectangle(ink, 7, 18, 4, 8);
            graphics.FillRectangle(ink, 14, 12, 4, 14);
            graphics.FillRectangle(ink, 21, 7, 4, 19);
        }
        var handle = bitmap.GetHicon();
        try
        {
            using var icon = System.Drawing.Icon.FromHandle(handle);
            return (System.Drawing.Icon)icon.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }
}

sealed class TrayIcon : IDisposable
{
    readonly System.Windows.Forms.NotifyIcon _icon;
    bool _balloonShown;

    public event Action? OpenRequested;
    public event Action? CheckRequested;
    public event Action? FreeUpRequested;
    public event Action? ExitRequested;

    public TrayIcon(System.Windows.Threading.Dispatcher dispatcher)
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => dispatcher.BeginInvoke(() => OpenRequested?.Invoke()));
        menu.Items.Add("Check PC", null, (_, _) => dispatcher.BeginInvoke(() => CheckRequested?.Invoke()));
        menu.Items.Add("Free up now", null, (_, _) => dispatcher.BeginInvoke(() => FreeUpRequested?.Invoke()));
        menu.Items.Add("Exit", null, (_, _) => dispatcher.BeginInvoke(() => ExitRequested?.Invoke()));
        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = IconFactory.TrayIcon(),
            Text = AppInfo.ProductName,
            Visible = true,
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => dispatcher.BeginInvoke(() => OpenRequested?.Invoke());
    }

    public void BalloonOnce()
    {
        if (_balloonShown)
            return;
        _balloonShown = true;
        _icon.BalloonTipTitle = AppInfo.ProductName;
        _icon.BalloonTipText = "Still running. Open it from this icon.";
        _icon.ShowBalloonTip(2500);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
