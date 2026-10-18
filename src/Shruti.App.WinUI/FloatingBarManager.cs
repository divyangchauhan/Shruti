using Microsoft.UI.Xaml;
using Shruti.Platform.Windows;
using Shruti.Workflow.Dictation;

namespace Shruti.App.WinUI;

internal sealed class FloatingBarManager : IDisposable
{
    private readonly IWindowsWindowVisibility _visibility;
    private readonly Dictionary<string, FloatingMicWindow> _windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _displayRefresh = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private DictationShellState _state = DictationShellState.Initial;
    private string _shortcut = "";
    private string? _preferredMonitorId;
    private bool _enabled;
    private bool _allMonitors;
    private bool _disposed;

    public FloatingBarManager(IWindowsWindowVisibility visibility)
    {
        _visibility = visibility;
        RefreshDisplays();
        _displayRefresh.Tick += (_, _) => { _displayRefresh.Stop(); RefreshDisplays(); };
    }

    public IReadOnlyList<WindowsDisplayMonitor> Monitors { get; private set; } = [];
    public event EventHandler? DisplaysChanged;
    public event EventHandler? TriggerRequested;
    public event EventHandler? CancelRequested;
    public event EventHandler? DismissRequested;
    public event EventHandler? SettingsRequested;

    public void ScheduleDisplayRefresh()
    {
        if (_disposed) return;
        _displayRefresh.Stop();
        _displayRefresh.Start();
    }

    private void RefreshDisplays()
    {
        if (_disposed) return;
        try { Monitors = WindowsDisplayMonitors.List(); }
        catch (System.ComponentModel.Win32Exception) { return; }
        Reconcile();
        DisplaysChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Update(DictationShellState state, string shortcut, bool enabled, bool allMonitors, string? preferredMonitorId)
    {
        if (_disposed) return;
        _state = state;
        _shortcut = shortcut;
        _enabled = enabled;
        _allMonitors = allMonitors;
        _preferredMonitorId = preferredMonitorId;
        Reconcile();
    }

    public void UpdateAudioLevel(float peak)
    {
        foreach (var window in _windows.Values) window.UpdateAudioLevel(peak);
    }

    private void Reconcile()
    {
        var targets = _enabled ? WindowsDisplayMonitors.Select(Monitors, _allMonitors, _preferredMonitorId) : [];
        var ids = targets.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string id in _windows.Keys.Where(id => !ids.Contains(id)).ToArray())
        {
            _windows[id].CloseForApplicationExit();
            _windows.Remove(id);
        }
        foreach (var monitor in targets)
        {
            if (!_windows.TryGetValue(monitor.Id, out var window))
            {
                window = new FloatingMicWindow(_visibility, monitor.Handle);
                window.TriggerRequested += (_, _) => TriggerRequested?.Invoke(this, EventArgs.Empty);
                window.CancelRequested += (_, _) => CancelRequested?.Invoke(this, EventArgs.Empty);
                window.DismissRequested += (_, _) => DismissRequested?.Invoke(this, EventArgs.Empty);
                window.SettingsRequested += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
                _windows.Add(monitor.Id, window);
            }
            window.SetMonitor(monitor.Handle);
            window.Show(_state, _shortcut);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _displayRefresh.Stop();
        foreach (var window in _windows.Values) window.CloseForApplicationExit();
        _windows.Clear();
    }
}
