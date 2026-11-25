using StellarWatch.Controls;
using StellarWatch.Interception;
using StellarWatch.Interception.Modules;
using StellarWatch.Interception.PacketProviders;
using StellarWatch.Utility;
using StellarWatch.Windows;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

using static System.Net.Mime.MediaTypeNames;

using Path = System.Windows.Shapes.Path;

namespace StellarWatch.Windows
{
    public partial class OverlayWindow : Window
    {
        public static bool ForceShowForSettings = false;
        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            const int WS_EX_TOOLWINDOW = 0x00000080;
            const int WS_EX_TRANSPARENT = 0x00000020;
            const int GWL_EXSTYLE = -20;

            [DllImport("user32.dll")]
            static extern int GetWindowLong(IntPtr hwnd, int index);

            [DllImport("user32.dll")]
            static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

            var hwnd = new WindowInteropHelper(this).Handle;
            var extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);
        }



        DispatcherTimer timer;
        PacketModuleBase m;
        XboxProvider provider;
        TimeSpan delta = TimeSpan.FromSeconds(0.5);
        public OverlayWindow()
        {
            InitializeComponent();
            Opacity = 0;
            Visibility = Visibility.Collapsed;
            provider = InterceptionManager.GetProvider("Xbox") as XboxProvider;
            Topmost = true;
            timer = new DispatcherTimer();
            timer.Tick += Tick;
            timer.Interval = delta;
            timer.Start();
            Closed += (s, e) => timer.Stop();
            if (!CheckGameFocus(true))
            Visibility = Visibility.Collapsed;
        }

        private async void Tick(object? sender, EventArgs e)
        {
            if (!CheckGameFocus())
            {
                this.Opacity = 0;
                Visibility = Visibility.Collapsed;
                timer.Interval = TimeSpan.FromSeconds(0.3);
                return;
            }
            if (!TryFollowWindow())
            {
                this.Opacity = 0;
                Visibility = Visibility.Collapsed;
                timer.Interval = TimeSpan.FromSeconds(0.3);
                return;
            }

            CheckModules();
            CheckInstanceTimer();

            if (Visibility == Visibility.Collapsed)
            {
                D2CharacterTracker.Update();
                this.Opacity = 1;
                this.Visibility = Visibility.Visible;   // 直接显示，不走动画
                timer.Interval = delta;
            }
        }



        static Process cachedProcess;
        const string targetProcessName = "destiny2";
        static bool LastGameFocusResult = false;
        static DateTime lastCheckTime = DateTime.MinValue;
        public static bool CheckGameFocus(bool skipCheck = false)
        {
            
        // 设置时强制显示
        if (ForceShowForSettings)
            return true;
        if (!skipCheck && DateTime.Now - lastCheckTime < TimeSpan.FromSeconds(0.2))
            return LastGameFocusResult;

        lastCheckTime = DateTime.Now;

        // 刷新/获取 Destiny 2 进程
        if (cachedProcess == null || cachedProcess.HasExited)
        {
            var processes = Process.GetProcessesByName(targetProcessName);
            if (!processes.Any())
            {
                cachedProcess = null;
                return LastGameFocusResult = false;
            }
            cachedProcess = processes.First();
        }

        // 关键：检查当前前台窗口是否属于 Destiny 2
        IntPtr foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
            return LastGameFocusResult = false;

        GetWindowThreadProcessId(foregroundWindow, out uint foregroundPid);
        if (foregroundPid != (uint)cachedProcess.Id)
            return LastGameFocusResult = false;

            return LastGameFocusResult = true;
        }

        public bool TryFollowWindow()
        {
            if (cachedProcess != null && !cachedProcess.HasExited && cachedProcess.MainWindowHandle != IntPtr.Zero && GetWindowRect(cachedProcess.MainWindowHandle, out var rect))
            {
                //TODO: 4k issue

                double h_base = 1440d;
                double w_base = 2560d;
                var ratio_base = w_base / h_base;

                var dpi = VisualTreeHelper.GetDpi(this);
                double win_h = (rect.Bottom - rect.Top) / dpi.DpiScaleY;
                double win_w = (rect.Right - rect.Left) / dpi.DpiScaleX;
                var ratio = win_w / win_h;
                
                double x_multiplier = win_w / w_base;
                double y_multiplier = win_h / h_base;

                double yoffset = 0;
                double xoffset = 0;

                // widescreen
                if (ratio > ratio_base) xoffset += (win_w - win_h * ratio_base) / 2;
                if (ratio < ratio_base) yoffset += (win_h - win_w / ratio_base) / 2;
                
                var scalePercent = 1d + (Math.Clamp(Config.Instance.Settings.Overlay_ScalePercent, 0, 100) - 1) * 0.01d;

                // 345 200
                Width = ((win_w - xoffset * 2) / 4) * scalePercent;
                Height = ((win_h - yoffset * 2) / 4) * scalePercent;

                Left = rect.Left / dpi.DpiScaleX + xoffset + 118 * (win_w - xoffset * 2) / w_base * scalePercent + Config.Instance.Settings.Overlay_LeftOffset;
                Top = rect.Top / dpi.DpiScaleY + yoffset + ((win_h - yoffset * 2) * 3 / 4) - Config.Instance.Settings.Overlay_BottomOffset;

                Scale.ScaleX = ((win_w - xoffset * 2) / w_base) * scalePercent;
                Scale.ScaleY = ((win_h - yoffset * 2) / h_base) * scalePercent;
                return true;
            }
            return false;
        }



        Dictionary<PacketModuleBase, EnabledModuleTimer> modules = new();
        private void CheckModules()
        {
            foreach (var m in InterceptionManager.Modules)
            {
                var c = modules.ContainsKey(m);
                var e = m.IsEnabled;
                var a = m.IsActivated;

                // displayed
                if (c)
                {
                    var ui = modules[m];

                    if (!m.Togglable && Config.Instance.Settings.Overlay_DisplayOnlyTogglable && !a)
                    {
                        modules.Remove(m);
                        ui.ElementFadeOut();
                        Task.Run(async () =>
                        {
                            await Task.Delay(TimeSpan.FromSeconds(0.2));
                            Dispatcher.Invoke(() => Modules.Children.Remove(ui));
                        });
                        continue;
                    }

                    // disabled = disappear
                    if (!e)
                    {
                        modules.Remove(m);
                        ui.ElementFadeOut();
                        Task.Run(async () =>
                        {
                            await Task.Delay(TimeSpan.FromSeconds(0.2));
                            Dispatcher.Invoke(() => Modules.Children.Remove(ui));
                        });
                        continue;
                    }

                    // enabled = count
                    if (m.Togglable || !Config.Instance.Settings.Overlay_DisplayOnlyTogglable)
                    {
                        ui.UpdateTimer();
                        continue;
                    }
                }

                if (!m.Togglable && Config.Instance.Settings.Overlay_DisplayOnlyTogglable && !m.IsActivated)
                {
                    continue;
                }

                // Something got enabled
                if (!c && e)
                {
                    var ui = new EnabledModuleTimer(m.Name) { Visibility = Visibility.Hidden };
                    modules.Add(m, ui);
                    Modules.Children.Add(ui);
                    ui.ElementFadeIn();
                }
            }
        }
        private void CheckInstanceTimer()
        {
            if (D2CharacterTracker.RaidsCount > 0)
            {
                RaidLabel.Content = $"{D2CharacterTracker.RaidsCount}";
                Raid.ElementFadeIn();
            }
            else
            {
                Raid.ElementFadeOut();
            }


            if (Config.Instance.Settings.Overlay_ShowTime)
            {
                Timer.Content = DateTime.Now.ToString("hh':'mm':'ss");
                Timer.ElementFadeIn();
            }
            else 
            {
                if (!provider.IsEnabled || !Config.Instance.Settings.Overlay_ShowTimer)
                {
                    Timer.ElementFadeOut();
                    return;
                }

                var duration = provider.InstanceDuration();
                if (duration == TimeSpan.Zero)
                {
                    Timer.ElementFadeOut();
                    return;
                }

                Timer.Content = duration.ToString("hh':'mm':'ss");
                Timer.ElementFadeIn();

                if (duration > TimeSpan.FromSeconds(30) && (Timer.Opacity < 0.5 || Timer.Visibility != Visibility.Visible))
                {
                    Timer.Opacity = 1;
                    Timer.Visibility = Visibility.Visible;
                }
            }
        }
    }
}
