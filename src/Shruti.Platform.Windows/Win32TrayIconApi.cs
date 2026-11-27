using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Shruti.Core.Platform;

namespace Shruti.Platform.Windows;

public sealed class Win32TrayIconApi(WindowsTargetFocusService? focusService = null) : IWindowsTrayIconApi, IDisposable
{
    private IntPtr _menuOwner;
    private FocusTarget? _menuTarget;
    private NotifyIcon? _notifyIcon;
    private ContextMenuStrip? _menu;
    private ToolStripMenuItem? _startMenuItem;
    private ToolStripMenuItem? _stopMenuItem;
    private ToolStripMenuItem? _cancelMenuItem;
    private ToolStripSeparator? _commandSeparator;
    private Icon? _icon;
    private bool _isDictationRunning;
    private bool _areDictationCommandsEnabled = true;
    private bool _darkTheme;
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

    public void SetDarkTheme(bool darkTheme)
    {
        _darkTheme = darkTheme;
        if (_menu is not null) ApplyMenuTheme(_menu);
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
        _startMenuItem = null;
        _stopMenuItem = null;
        _cancelMenuItem = null;
        _commandSeparator = null;
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
            ShowCheckMargin = false,
            Font = SystemFonts.MenuFont
        };
        int iconSize = Math.Min(menu.LogicalToDeviceUnits(16), menu.Font?.Height ?? menu.LogicalToDeviceUnits(16));
        menu.ImageScalingSize = new Size(iconSize, iconSize);
        menu.HandleCreated += (_, _) =>
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) && !SystemInformation.HighContrast)
            {
                int roundedSmall = 3;
                NativeMethods.DwmSetWindowAttribute(menu.Handle, 33, ref roundedSmall, sizeof(int));
            }
        };
        menu.MinimumSize = new Size(menu.LogicalToDeviceUnits(190), 0);
        menu.Padding = new Padding(menu.LogicalToDeviceUnits(3), menu.LogicalToDeviceUnits(5),
            menu.LogicalToDeviceUnits(3), menu.LogicalToDeviceUnits(5));
        ApplyMenuTheme(menu);
        menu.Opening += (_, _) =>
        {
            // WinForms has activated its hidden NotifyIcon owner on this thread.
            // The previous app may still be processing foreground deactivation.
            IntPtr active = NativeMethods.GetActiveWindow();
            _menuOwner = active != IntPtr.Zero && NativeWindow.FromHandle(active) is not null &&
                !NativeMethods.IsWindowVisible(active) ? active : IntPtr.Zero;
            _menuTarget = _menuOwner != IntPtr.Zero ? focusService?.LastExternalTarget : null;
            ApplyMenuTheme(menu);
            UpdateMenuState();
        };
        _startMenuItem = CreateMenuItem("Start dictation", WindowsTrayCommand.Start);
        _stopMenuItem = CreateMenuItem("Stop dictation", WindowsTrayCommand.Stop);
        menu.Items.Add(_startMenuItem);
        menu.Items.Add(_stopMenuItem);
        _cancelMenuItem = CreateMenuItem("Cancel dictation", WindowsTrayCommand.Cancel);
        menu.Items.Add(_cancelMenuItem);
        _commandSeparator = new ToolStripSeparator();
        menu.Items.Add(_commandSeparator);
        menu.Items.Add(CreateMenuItem("Show Shruti", WindowsTrayCommand.ShowWindow));
        menu.Items.Add(CreateMenuItem("Settings", WindowsTrayCommand.ShowSettings));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(CreateMenuItem("Exit Shruti", WindowsTrayCommand.Quit));
        UpdateMenuState();
        return menu;
    }

    private void ApplyMenuTheme(ContextMenuStrip menu)
    {
        if (SystemInformation.HighContrast)
        {
            menu.RenderMode = ToolStripRenderMode.System;
            menu.ShowImageMargin = false;
            menu.BackColor = SystemColors.Menu;
            menu.ForeColor = SystemColors.MenuText;
            return;
        }

        if (menu.Renderer is TrayMenuRenderer renderer && renderer.DarkTheme == _darkTheme) return;
        var colors = new TrayMenuColors(_darkTheme);
        menu.ShowImageMargin = true;
        menu.BackColor = colors.ToolStripDropDownBackground;
        menu.ForeColor = colors.Text;
        menu.Renderer = new TrayMenuRenderer(colors, _darkTheme);
    }

    private sealed class TrayMenuColors(bool darkTheme) : ProfessionalColorTable
    {
        public Color Text { get; } = darkTheme ? Color.FromArgb(243, 241, 238) : Color.FromArgb(30, 28, 25);
        public Color Accent { get; } = darkTheme ? Color.FromArgb(234, 141, 70) : Color.FromArgb(188, 86, 16);
        public override Color ToolStripDropDownBackground => darkTheme ? Color.FromArgb(44, 41, 37) : Color.White;
        public override Color MenuItemSelected => darkTheme ? Color.FromArgb(64, 47, 34) : Color.FromArgb(252, 233, 216);
        public override Color MenuItemBorder => MenuItemSelected;
        public override Color MenuBorder => darkTheme ? Color.FromArgb(94, 87, 79) : Color.FromArgb(212, 208, 201);
        public override Color SeparatorDark => darkTheme ? Color.FromArgb(73, 67, 61) : Color.FromArgb(231, 228, 223);
        public override Color SeparatorLight => ToolStripDropDownBackground;
        public override Color ImageMarginGradientBegin => ToolStripDropDownBackground;
        public override Color ImageMarginGradientMiddle => ToolStripDropDownBackground;
        public override Color ImageMarginGradientEnd => ToolStripDropDownBackground;
    }

    private sealed class TrayMenuRenderer(TrayMenuColors colors, bool darkTheme) : ToolStripProfessionalRenderer(colors)
    {
        public bool DarkTheme { get; } = darkTheme;

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || e.ToolStrip is not ContextMenuStrip menu)
            {
                base.OnRenderMenuItemBackground(e);
                return;
            }

            int inset = menu.LogicalToDeviceUnits(2);
            var bounds = new RectangleF(inset, inset, e.Item.Width - 2 * inset, e.Item.Height - 2 * inset);
            var prior = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var fill = new SolidBrush(ColorTable.MenuItemSelected);
            FillRoundedRectangle(e.Graphics, fill, bounds, menu.LogicalToDeviceUnits(4));
            e.Graphics.SmoothingMode = prior;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var palette = (TrayMenuColors)ColorTable;
            e.TextColor = palette.Text;
            base.OnRenderItemText(e);
            if (e.Item.Tag is not WindowsTrayCommand command || e.ToolStrip is not ContextMenuStrip menu) return;
            int inset = menu.LogicalToDeviceUnits(4);
            bool rtl = menu.RightToLeft == RightToLeft.Yes;
            int width = rtl ? e.Item.Width - e.TextRectangle.Right : e.TextRectangle.Left;
            int size = Math.Min(menu.ImageScalingSize.Width, width - 2 * inset);
            if (size < 1) return;
            int left = rtl ? e.Item.Width - inset - size : inset;
            var bounds = new Rectangle(left, e.TextRectangle.Top, size, e.TextRectangle.Height);
            Color ink = command is WindowsTrayCommand.Start or WindowsTrayCommand.Stop ? palette.Accent : palette.Text;
            using var brush = new SolidBrush(ink);
            var state = e.Graphics.Save();
            try
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                if (command == WindowsTrayCommand.Stop)
                {
                    float edge = size * .64f;
                    FillRoundedRectangle(e.Graphics, brush,
                        new RectangleF(left + (size - edge) / 2, bounds.Top + (bounds.Height - edge) / 2, edge, edge), edge * .17f);
                    return;
                }
                if (command == WindowsTrayCommand.Start)
                {
                    e.Graphics.TranslateTransform(left, bounds.Top + (bounds.Height - size) / 2f);
                    DrawMicrophone(e.Graphics, brush, size);
                    return;
                }

                string glyph = command switch
                {
                    WindowsTrayCommand.Cancel => "\uE711",
                    WindowsTrayCommand.ShowWindow => "\uE8A7",
                    WindowsTrayCommand.ShowSettings => "\uE713",
                    WindowsTrayCommand.Quit => "\uF3B1",
                    _ => ""
                };
                string iconFont = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
                using var font = new Font(iconFont, size, FontStyle.Regular, GraphicsUnit.Pixel);
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                e.Graphics.DrawString(glyph, font, brush, bounds, format);
            }
            finally
            {
                e.Graphics.Restore(state);
            }
        }
    }

    private ToolStripMenuItem CreateMenuItem(string text, WindowsTrayCommand command)
    {
        var item = new ToolStripMenuItem(text)
        {
            Tag = command,
            Padding = new Padding(8, 4, 14, 4)
        };
        item.Click += (_, _) =>
        {
            if (command is WindowsTrayCommand.Start or WindowsTrayCommand.Stop or WindowsTrayCommand.Cancel)
            {
                ReturnFocusFromMenu();
            }

            RaiseCommand(command);
        };
        return item;
    }

    private void ReturnFocusFromMenu()
    {
        if (_menuTarget is { } target && NativeMethods.IsWindowVisible(target.WindowHandle))
        {
            focusService?.ReturnForegroundFromTrayMenu(_menuOwner, target);
        }
    }

    private void UpdateIconState(string tooltip)
    {
        EnsureNotifyIcon();
        _notifyIcon!.Text = TruncateTooltip(tooltip);
        UpdateMenuState();
    }

    private void UpdateMenuState()
    {
        if (_startMenuItem is null || _stopMenuItem is null || _cancelMenuItem is null || _commandSeparator is null)
        {
            return;
        }

        _startMenuItem.Available = _areDictationCommandsEnabled && !_isDictationRunning;
        _stopMenuItem.Available = _areDictationCommandsEnabled && _isDictationRunning;
        _cancelMenuItem.Available = _areDictationCommandsEnabled && _isDictationRunning;
        _commandSeparator.Available = _areDictationCommandsEnabled;
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
        DrawMicrophone(graphics, inkBrush, scale);

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

    private static void DrawMicrophone(Graphics graphics, Brush inkBrush, float scale)
    {
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
        [DllImport("user32.dll")]
        public static extern IntPtr GetActiveWindow();

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr windowHandle);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr iconHandle);
    }
}
