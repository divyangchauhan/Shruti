using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Shruti.App.WinUI;
using Shruti.Platform.Windows;
using Shruti.Core;
using Shruti.Core.Dictation;
using Shruti.Workflow.Dictation;
using WinRT.Interop;

namespace Shruti.PopupChecks;

internal static class WindowOrderChecks
{
    public static async Task RunAsync(string output)
    {
        var results = new List<string>();
        var target = new Window { Title = "Shruti window order check" };
        IntPtr targetHandle = WindowNative.GetWindowHandle(target);
        IntPtr monitor = Native.MonitorFromWindow(targetHandle, 2);
        var pill = new FloatingMicWindow(new Win32WindowVisibility(), monitor);
        IntPtr pillHandle = WindowNative.GetWindowHandle(pill);
        try
        {
            target.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(-20000, -20000, 200, 100));
            target.AppWindow.Show(activateWindow: false);
            pill.Show(DictationShellState.Initial, "Ctrl+Win+Space");
            await Task.Delay(1200);
            CheckTopmost();
            var capturing = DictationShellState.Initial with
            {
                SessionState = DictationSessionState.Recording,
                IsRunning = true,
                CanStop = true
            };
            pill.UpdateState(capturing);
            var recording = pill.AppWindow;
            var position = recording.Position;
            var size = recording.Size;
            results.Add($"Recording: {position.X},{position.Y} {size.Width}x{size.Height}");
            CheckSameBounds(capturing with { SessionState = DictationSessionState.TranscribingFinalAudio, CanStop = false });
            CheckSameBounds(DictationShellState.Initial with { LastOutcome = DictationRunOutcome.Inserted });

            void CheckSameBounds(DictationShellState state)
            {
                pill.UpdateState(state);
                var current = pill.AppWindow;
                string measurement = $"{state.LastOutcome?.ToString() ?? state.SessionState.ToString()}: {current.Position.X},{current.Position.Y} {current.Size.Width}x{current.Size.Height}";
                results.Add(measurement);
                if (current.Position.X != position.X || current.Position.Y != position.Y ||
                    current.Size.Width != size.Width || current.Size.Height != size.Height)
                    throw new InvalidOperationException($"The active floating bar moved or resized. {measurement}");
            }

            pill.UpdateState(DictationShellState.Initial);
            // A fullscreen game can sit above other topmost windows. The real
            // floating bar must leave that order alone on its next timer tick.
            target.Activate();
            PutTargetAbovePill();
            await Task.Delay(1500);
            CheckTopmost();
            Check("Idle timer preserves the other window's position above the bar");

            pill.SetMonitor(monitor);
            Check("Display reconciliation preserves window order");

            pill.UpdateState(DictationShellState.Initial with { LastOutcome = Shruti.Core.Dictation.DictationRunOutcome.Failed });
            Check("A status resize preserves window order");

            results.Add("PASS: all floating window order checks");
        }
        finally
        {
            await File.WriteAllLinesAsync(Path.Combine(output, "window-order.txt"), results);
            pill.CloseForApplicationExit();
            target.Close();
        }

        void PutTargetAbovePill()
        {
            if (!Native.SetWindowPos(targetHandle, (IntPtr)(-1), 0, 0, 0, 0, 0x0013))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            Check("Setup places the other topmost window above the bar");
        }

        void Check(string name)
        {
            for (IntPtr window = Native.GetWindow(pillHandle, 3); window != IntPtr.Zero; window = Native.GetWindow(window, 3))
            {
                if (window != targetHandle) continue;
                results.Add($"PASS: {name}");
                return;
            }
            results.Add($"FAIL: {name}");
            throw new InvalidOperationException(name);
        }

        void CheckTopmost()
        {
            if ((Native.GetWindowLongPtr(pillHandle, -20) & 8) != 0)
            {
                results.Add("PASS: The floating bar remains topmost for desktop use");
                return;
            }
            results.Add("FAIL: The floating bar lost its topmost style");
            throw new InvalidOperationException("The floating bar lost its topmost style");
        }
    }

    private static class Native
    {
        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern nint GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    }
}
