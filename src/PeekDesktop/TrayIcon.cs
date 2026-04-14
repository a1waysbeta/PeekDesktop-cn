using System;
using System.Drawing;
using System.Windows.Forms;

namespace PeekDesktop;

/// <summary>
/// Manages the system tray (notification area) icon and its context menu.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly DesktopPeek _desktopPeek;
    private readonly Settings _settings;
    private readonly Action _exitAction;
    private readonly ToolStripMenuItem _enabledItem;

    public TrayIcon(DesktopPeek desktopPeek, Settings settings, Action exitAction)
    {
        _desktopPeek = desktopPeek;
        _settings = settings;
        _exitAction = exitAction;

        _enabledItem = new ToolStripMenuItem("Enabled")
        {
            Checked = _settings.Enabled,
            CheckOnClick = true
        };

        _notifyIcon = new NotifyIcon
        {
            Text = "PeekDesktop — click desktop to peek",
            Icon = CreateIcon(),
            Visible = true,
            ContextMenuStrip = CreateContextMenu()
        };
    }

    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();

        _enabledItem.CheckedChanged += (_, _) =>
        {
            _settings.Enabled = _enabledItem.Checked;
            _desktopPeek.IsEnabled = _enabledItem.Checked;

            if (_enabledItem.Checked)
                _desktopPeek.Start();
            else
                _desktopPeek.Stop();

            _settings.Save();
        };

        var startupItem = new ToolStripMenuItem("Start with Windows")
        {
            Checked = _settings.StartWithWindows,
            CheckOnClick = true
        };
        startupItem.CheckedChanged += (_, _) =>
        {
            _settings.StartWithWindows = startupItem.Checked;
            _settings.Save();
            Settings.SetAutoStart(startupItem.Checked);
        };

        var aboutItem = new ToolStripMenuItem("About PeekDesktop");
        aboutItem.Click += (_, _) =>
        {
            string version = Application.ProductVersion;
            MessageBox.Show(
                $"PeekDesktop v{version}\n\n" +
                "Click your desktop wallpaper to peek at your desktop,\n" +
                "just like macOS Sonoma.\n\n" +
                "Click any window or the taskbar to restore.\n\n" +
                "github.com/shanselman/PeekDesktop",
                "About PeekDesktop",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        };

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) =>
        {
            _notifyIcon.Visible = false;
            _exitAction();
        };

        menu.Items.Add(_enabledItem);
        menu.Items.Add(startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(aboutItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        return menu;
    }

    /// <summary>
    /// Creates a simple programmatic icon (blue monitor with white stand).
    /// Replace with a proper .ico in production.
    /// </summary>
    private static Icon CreateIcon()
    {
        var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // Monitor screen
            using var screenBrush = new SolidBrush(Color.FromArgb(30, 144, 255));
            g.FillRectangle(screenBrush, 3, 3, 26, 18);

            // Monitor bezel
            using var bezelPen = new Pen(Color.White, 1.5f);
            g.DrawRectangle(bezelPen, 3, 3, 26, 18);

            // Stand
            g.FillRectangle(Brushes.White, 13, 21, 6, 4);
            g.FillRectangle(Brushes.White, 10, 25, 12, 2);

            // "Peek" eye on the screen
            using var eyePen = new Pen(Color.White, 1.5f);
            g.DrawEllipse(eyePen, 10, 8, 12, 8);
            g.FillEllipse(Brushes.White, 14, 10, 4, 4);
        }

        IntPtr hIcon = bitmap.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
