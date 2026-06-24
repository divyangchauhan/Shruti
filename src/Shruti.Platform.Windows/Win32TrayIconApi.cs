using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Shruti.Platform.Windows;

public sealed class Win32TrayIconApi : IWindowsTrayIconApi, IDisposable
{
    private NotifyIcon? _notifyIcon;
    private ContextMenuStrip? _menu;
    private ToolStripMenuItem? _startMenuItem;
    private ToolStripMenuItem? _stopMenuItem;
    private ToolStripMenuItem? _cancelMenuItem;
    private Icon? _icon;
    private bool _isDictationRunning;
    private bool _areDictationCommandsEnabled = true;
    private bool _isDisposed;

    public event Action<WindowsTrayCommand>? CommandInvoked;

    public bool AddIcon(IntPtr windowHandle, uint iconId, uint callbackMessage, string tooltip)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        try
        {
            EnsureNotifyIcon();
            UpdateIconState(tooltip);
            _notifyIcon!.Visible = true;
            return true;
        }
        catch (ExternalException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public bool UpdateIcon(IntPtr windowHandle, uint iconId, uint callbackMessage, string tooltip)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_notifyIcon is null)
        {
            return false;
        }

        try
        {
            UpdateIconState(tooltip);
            _notifyIcon.Visible = true;
            return true;
        }
        catch (ExternalException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public void RemoveIcon(IntPtr windowHandle, uint iconId)
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
        }
    }

    public void SetCommandState(bool isDictationRunning, bool areDictationCommandsEnabled)
    {
        _isDictationRunning = isDictationRunning;
        _areDictationCommandsEnabled = areDictationCommandsEnabled;
        UpdateMenuState();
    }

    public WindowsTrayCommand? ShowMenu(
        IntPtr windowHandle,
        bool isDictationRunning,
        bool areDictationCommandsEnabled)
    {
        SetCommandState(isDictationRunning, areDictationCommandsEnabled);
        return null;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.MouseClick -= NotifyIcon_MouseClick;
            _notifyIcon.MouseDoubleClick -= NotifyIcon_MouseDoubleClick;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _menu?.Dispose();
        _menu = null;
        _icon?.Dispose();
        _icon = null;
        GC.SuppressFinalize(this);
    }

    private void EnsureNotifyIcon()
    {
        if (_notifyIcon is not null)
        {
            return;
        }

        _icon = CreateTrayIcon();
        _menu = CreateMenu();
        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            ContextMenuStrip = _menu,
            Text = "Shruti - Ready"
        };
        _notifyIcon.MouseClick += NotifyIcon_MouseClick;
        _notifyIcon.MouseDoubleClick += NotifyIcon_MouseDoubleClick;
    }

    private ContextMenuStrip CreateMenu()
    {
        var menu = new ContextMenuStrip
        {
            ShowImageMargin = false
        };
        menu.Opening += (_, _) => UpdateMenuState();
        menu.Items.Add(CreateMenuItem("Show Shruti", WindowsTrayCommand.ShowWindow));
        menu.Items.Add(new ToolStripSeparator());
        _startMenuItem = CreateMenuItem("Start dictation", WindowsTrayCommand.Start);
        _stopMenuItem = CreateMenuItem("Stop dictation", WindowsTrayCommand.Stop);
        menu.Items.Add(_startMenuItem);
        menu.Items.Add(_stopMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        _cancelMenuItem = CreateMenuItem("Cancel dictation", WindowsTrayCommand.Cancel);
        menu.Items.Add(_cancelMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(CreateMenuItem("Settings", WindowsTrayCommand.ShowSettings));
        menu.Items.Add(CreateMenuItem("Exit Shruti", WindowsTrayCommand.Quit));
        UpdateMenuState();
        return menu;
    }

    private ToolStripMenuItem CreateMenuItem(string text, WindowsTrayCommand command)
    {
        var item = new ToolStripMenuItem(text)
        {
            Tag = command
        };
        item.Click += (_, _) => RaiseCommand(command);
        return item;
    }

    private void UpdateIconState(string tooltip)
    {
        EnsureNotifyIcon();
        _notifyIcon!.Text = TruncateTooltip(tooltip);
        UpdateMenuState();
    }

    private void UpdateMenuState()
    {
        if (_startMenuItem is null || _stopMenuItem is null || _cancelMenuItem is null)
        {
            return;
        }

        _startMenuItem.Enabled = _areDictationCommandsEnabled && !_isDictationRunning;
        _stopMenuItem.Enabled = _areDictationCommandsEnabled && _isDictationRunning;
        _cancelMenuItem.Enabled = _areDictationCommandsEnabled && _isDictationRunning;
    }

    private void NotifyIcon_MouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            RaiseCommand(_areDictationCommandsEnabled
                ? WindowsTrayCommand.Toggle
                : WindowsTrayCommand.ShowWindow);
        }
    }

    private void NotifyIcon_MouseDoubleClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            RaiseCommand(WindowsTrayCommand.ShowSettings);
        }
    }

    private void RaiseCommand(WindowsTrayCommand command)
    {
        CommandInvoked?.Invoke(command);
    }

    private static string TruncateTooltip(string tooltip)
    {
        const int maxTooltipLength = 63;
        return tooltip.Length <= maxTooltipLength
            ? tooltip
            : tooltip[..maxTooltipLength];
    }

    private Icon CreateTrayIcon()
    {
        Size iconSize = SystemInformation.SmallIconSize;
        int width = Math.Max(iconSize.Width, 16);
        int height = Math.Max(iconSize.Height, 16);
        using var bitmap = new Bitmap(width, height);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        float circleDiameter = Math.Min(width, height) - 2;
        float circleX = (width - circleDiameter) / 2;
        float circleY = (height - circleDiameter) / 2;
        using var accentBrush = new SolidBrush(Color.FromArgb(245, 185, 79));
        graphics.FillEllipse(accentBrush, circleX, circleY, circleDiameter, circleDiameter);

        using var inkBrush = new SolidBrush(Color.FromArgb(43, 29, 5));
        float scale = Math.Min(width, height);
        FillRoundedRectangle(
            graphics,
            inkBrush,
            new RectangleF(scale * 0.39f, scale * 0.20f, scale * 0.22f, scale * 0.38f),
            scale * 0.09f);
        graphics.FillRectangle(inkBrush, scale * 0.47f, scale * 0.62f, scale * 0.06f, scale * 0.18f);
        FillRoundedRectangle(
            graphics,
            inkBrush,
            new RectangleF(scale * 0.31f, scale * 0.77f, scale * 0.38f, scale * 0.10f),
            scale * 0.04f);
        using var pen = new Pen(inkBrush, Math.Max(2, scale * 0.07f));
        graphics.DrawArc(pen, scale * 0.27f, scale * 0.43f, scale * 0.46f, scale * 0.30f, 0, 180);

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using Icon icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    private static void FillRoundedRectangle(
        Graphics graphics,
        Brush brush,
        RectangleF bounds,
        float radius)
    {
        using GraphicsPath path = CreateRoundedRectanglePath(bounds, radius);
        graphics.FillPath(brush, path);
    }

    private static GraphicsPath CreateRoundedRectanglePath(RectangleF bounds, float radius)
    {
        float diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr iconHandle);
    }
}
