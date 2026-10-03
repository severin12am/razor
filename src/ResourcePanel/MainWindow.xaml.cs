using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ResourcePanel;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm;
    readonly MonitorLoop _monitor = new();
    readonly TrayIcon _tray;
    ListCollectionView? _view;
    bool _exiting;
    bool _pinned;
    bool _applyingDock;
    double _fullWidth = 380;
    double _fullHeight = 760;
    double _pinLeft;
    double _pinTop;
    string _dock = "Float";
    IntPtr _hwnd;
    uint _appBarMessage;
    bool _appBarRegistered;
    MachineSample? _pending;
    int _samplePosted;

    public MainWindow()
    {
        var settings = SettingsStore.Load();
        _vm = new MainViewModel(settings);
        DataContext = _vm;
        InitializeComponent();
        _tray = new TrayIcon(Dispatcher);
        _tray.OpenRequested += () => Dispatcher.Invoke(ShowFromTray);
        _tray.CheckRequested += () => Dispatcher.Invoke(() => Check_Click(this, new RoutedEventArgs()));
        _tray.FreeUpRequested += () => Dispatcher.Invoke(() => FreeUp_Click(this, new RoutedEventArgs()));
        _tray.ExitRequested += () => Dispatcher.Invoke(() => Exit_Click(this, new RoutedEventArgs()));
        Icon = IconFactory.WindowIcon();
        ApplySettings(settings);
        Loaded += OnLoaded;
        LocationChanged += OnLocationChanged;
        Closing += OnClosing;
        SystemEvents.SessionEnding += (_, _) =>
        {
            try { _vm.ResumeAll(); } catch { /* sign-out should not wait on us */ }
        };
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(_hwnd);
        source?.AddHook(WndProc);
        var viewSource = (CollectionViewSource)FindResource("ProcessView");
        viewSource.Filter += (_, args) => args.Accepted = args.Item is ProcessRow row && _vm.Accepts(row);
        _view = viewSource.View as ListCollectionView;
        ApplySort();
        SortButton.Content = "Sort: " + _vm.Sort;
        StartWithWindowsItem.IsChecked = LoginStartup.IsEnabled();
        _monitor.Updated += sample =>
        {
            _pending = sample;
            if (Interlocked.Exchange(ref _samplePosted, 1) == 1)
                return;
            Dispatcher.BeginInvoke(() =>
            {
                var latest = _pending;
                Interlocked.Exchange(ref _samplePosted, 0);
                if (latest == null)
                    return;
                _vm.Apply(latest);
                _view?.Refresh();
            });
        };
        _monitor.Start();
        if (App.SmokeTest)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _exiting = true;
                Close();
            };
            timer.Start();
        }
    }

    void ApplySettings(AppSettings settings)
    {
        Topmost = settings.Topmost;
        TopmostButton.Content = settings.Topmost ? "Top" : "Top";
        TopmostButton.Foreground = settings.Topmost
            ? (System.Windows.Media.Brush)FindResource("Brush.Accent")
            : (System.Windows.Media.Brush)FindResource("Brush.Muted");
        _pinned = settings.Pinned;
        UpdatePinVisual();
        _dock = settings.Dock;
        if (settings.Left is double left && settings.Top is double top && !double.IsNaN(left) && !double.IsNaN(top))
        {
            Left = left;
            Top = top;
        }
        else
        {
            var area = SystemParameters.WorkArea;
            Width = 380;
            Height = Math.Min(760, area.Height - 24);
            Left = area.Right - Width - 16;
            Top = area.Top + 12;
        }
        if (settings.Width >= MinWidth)
            Width = settings.Width;
        if (settings.Height >= MinHeight)
            Height = settings.Height;
        var areaNow = SystemParameters.WorkArea;
        if (Left > areaNow.Right - 40 || Top > areaNow.Bottom - 40)
        {
            Left = areaNow.Right - Width - 16;
            Top = areaNow.Top + 12;
        }
        _pinLeft = Left;
        _pinTop = Top;
        _fullWidth = Width;
        _fullHeight = Height;
        if (settings.Compact)
            ApplyCompactChrome(entering: true);
        UpdateCompactVisual();
    }

    void SaveSettings()
    {
        var settings = _vm.Settings;
        settings.Left = Left;
        settings.Top = Top;
        settings.Width = _vm.IsCompact ? _fullWidth : Width;
        settings.Height = _vm.IsCompact ? _fullHeight : Height;
        settings.Compact = _vm.IsCompact;
        settings.Topmost = Topmost;
        settings.Pinned = _pinned;
        settings.Dock = _dock;
        SettingsStore.Save(settings);
    }

    void OnLocationChanged(object? sender, EventArgs e)
    {
        if (_pinned && !_applyingDock && (Math.Abs(Left - _pinLeft) > 1 || Math.Abs(Top - _pinTop) > 1))
        {
            _applyingDock = true;
            Left = _pinLeft;
            Top = _pinTop;
            _applyingDock = false;
        }
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_exiting)
        {
            if (_vm.ShowResume)
            {
                var resume = ConfirmWindow.Show(this, "Resume paused apps?",
                    "They stay paused if you leave them, even after this panel closes.",
                    null, "Resume and quit", "Leave paused");
                if (resume)
                    _vm.ResumeAll();
            }
            SaveSettings();
            RemoveAppBar();
            _monitor.Dispose();
            _tray.Dispose();
            return;
        }
        e.Cancel = true;
        HideToTray();
    }

    void HideToTray()
    {
        SaveSettings();
        Hide();
        ShowInTaskbar = false;
        _tray.BalloonOnce();
    }

    void ShowFromTray()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Activate();
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_appBarMessage != 0 && msg == _appBarMessage && wParam == 1)
            ApplyDock(moveOnly: true);
        return IntPtr.Zero;
    }

    void Back_Click(object sender, RoutedEventArgs e) => _vm.ShowPage("Home");

    void Speed_Click(object sender, RoutedEventArgs e)
    {
        LeaveCompactForReading();
        _vm.LoadSpeed();
    }

    void Shortcut_Click(object sender, RoutedEventArgs e)
    {
        var result = DesktopLink.Create();
        _vm.Status = result.Message;
    }

    void Compact_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_vm.IsCompact)
            {
                _vm.SetCompact(false);
                ApplyCompactChrome(entering: false);
            }
            else
            {
                _fullWidth = Width;
                _fullHeight = Height;
                _vm.SetCompact(true);
                ApplyCompactChrome(entering: true);
            }
            UpdateCompactVisual();
            SaveSettings();
        }
        catch (Exception ex)
        {
            ShowProblem(ex.Message);
        }
    }

    void ApplyCompactChrome(bool entering)
    {
        _applyingDock = true;
        try
        {
            if (entering)
            {
                MinWidth = 220;
                MinHeight = 250;
                MaxWidth = 340;
                Width = 260;
                Height = 340;
            }
            else
            {
                MaxWidth = 560;
                MinWidth = 320;
                MinHeight = 520;
                Width = Math.Max(320, _fullWidth);
                Height = Math.Max(520, _fullHeight);
            }
            _pinLeft = Left;
            _pinTop = Top;
        }
        finally
        {
            _applyingDock = false;
        }
    }

    void UpdateCompactVisual() =>
        CompactButton.Foreground = _vm.IsCompact
            ? (System.Windows.Media.Brush)FindResource("Brush.Accent")
            : (System.Windows.Media.Brush)FindResource("Brush.Muted");

    void Pin_Click(object sender, RoutedEventArgs e)
    {
        _pinned = !_pinned;
        _pinLeft = Left;
        _pinTop = Top;
        UpdatePinVisual();
        SaveSettings();
    }

    void UpdatePinVisual() =>
        PinButton.Foreground = _pinned
            ? (System.Windows.Media.Brush)FindResource("Brush.Accent")
            : (System.Windows.Media.Brush)FindResource("Brush.Muted");

    void Topmost_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        TopmostButton.Foreground = Topmost
            ? (System.Windows.Media.Brush)FindResource("Brush.Accent")
            : (System.Windows.Media.Brush)FindResource("Brush.Muted");
        SaveSettings();
    }

    void Dock_Click(object sender, RoutedEventArgs e)
    {
        _dock = _dock switch
        {
            "Float" => "Right",
            "Right" => "Left",
            _ => "Float"
        };
        ApplyDock(moveOnly: false);
        SaveSettings();
        _vm.Status = _dock == "Float" ? "Floating." : "Docked to the " + _dock.ToLowerInvariant() + " edge.";
    }

    void ApplyDock(bool moveOnly)
    {
        _applyingDock = true;
        try
        {
            if (_dock == "Float")
            {
                RemoveAppBar();
                return;
            }
            var area = SystemParameters.WorkArea;
            Height = Math.Max(MinHeight, area.Height - 8);
            Top = area.Top + 4;
            Left = _dock == "Left" ? area.Left + 4 : area.Right - Width - 4;
            _pinLeft = Left;
            _pinTop = Top;
            if (!moveOnly)
                TryRegisterAppBar();
        }
        finally
        {
            _applyingDock = false;
        }
    }

    void TryRegisterAppBar()
    {
        try
        {
            if (_hwnd == IntPtr.Zero)
                return;
            RemoveAppBar();
            _appBarMessage = NativeMethods.RegisterWindowMessage(AppInfo.ProductName + ".AppBar");
            var data = NewAppBar();
            NativeMethods.SHAppBarMessage(0, ref data);
            _appBarRegistered = true;
            PositionAppBar();
        }
        catch
        {
            RemoveAppBar();
        }
    }

    void PositionAppBar()
    {
        if (!_appBarRegistered)
            return;
        var data = NewAppBar();
        data.Edge = _dock == "Left" ? 0u : 2u;
        var source = PresentationSource.FromVisual(this);
        var dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        var dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1;
        var monitor = NativeMethods.MonitorFromWindow(_hwnd, 2);
        var info = new NativeMethods.MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
            return;
        var width = (int)(Width * dpiX);
        data.Rect = _dock == "Left"
            ? new NativeMethods.Rect { Left = info.Work.Left, Top = info.Work.Top, Right = info.Work.Left + width, Bottom = info.Work.Bottom }
            : new NativeMethods.Rect { Left = info.Work.Right - width, Top = info.Work.Top, Right = info.Work.Right, Bottom = info.Work.Bottom };
        NativeMethods.SHAppBarMessage(2, ref data);
        NativeMethods.SHAppBarMessage(3, ref data);
        Left = data.Rect.Left / dpiX;
        Top = data.Rect.Top / dpiY;
        Width = Math.Max(MinWidth, (data.Rect.Right - data.Rect.Left) / dpiX);
        Height = Math.Max(MinHeight, (data.Rect.Bottom - data.Rect.Top) / dpiY);
    }

    NativeMethods.AppBarData NewAppBar() => new()
    {
        Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.AppBarData>(),
        Window = _hwnd,
        CallbackMessage = _appBarMessage
    };

    void RemoveAppBar()
    {
        if (!_appBarRegistered)
            return;
        var data = NewAppBar();
        NativeMethods.SHAppBarMessage(1, ref data);
        _appBarRegistered = false;
    }

    void Minimize_Click(object sender, RoutedEventArgs e) => HideToTray();

    void Menu_Click(object sender, RoutedEventArgs e)
    {
        MenuButton.ContextMenu.PlacementTarget = MenuButton;
        MenuButton.ContextMenu.IsOpen = true;
    }

    void RestartAdmin_Click(object sender, RoutedEventArgs e)
    {
        if (Admin.IsCurrentProcessElevated())
        {
            _vm.Status = "Already running as administrator.";
            return;
        }
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" });
            _exiting = true;
            _vm.ResumeAll();
            Close();
        }
        catch
        {
            _vm.Status = "Administrator permission was not granted.";
        }
    }

    void StartWithWindows_Click(object sender, RoutedEventArgs e)
    {
        var enabled = StartWithWindowsItem.IsChecked;
        _vm.SetStartWithWindows(enabled);
        _vm.Status = enabled ? "This panel will start with Windows." : "Removed from Windows startup.";
    }

    async void Check_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            LeaveCompactForReading();
            _vm.DismissCoach();
            await _vm.RunCheckAsync();
        }
        catch (Exception ex)
        {
            ShowProblem(ex.Message);
        }
    }

    void LeaveCompactForReading()
    {
        if (!_vm.IsCompact)
            return;
        _vm.SetCompact(false);
        ApplyCompactChrome(entering: false);
        UpdateCompactVisual();
    }

    async void FreeUp_Click(object sender, RoutedEventArgs e)
    {
        _vm.DismissCoach();
        var plan = _vm.BuildFreeUpPlan();
        var dialog = new FreeUpWindow(plan) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Request != null)
            _vm.Status = await _vm.ApplyFreeUpAsync(dialog.Request);
    }

    void Resume_Click(object sender, RoutedEventArgs e) => _vm.ResumeAll();

    void DismissCoach_Click(object sender, RoutedEventArgs e) => _vm.DismissCoach();

    void Filter_Changed(object sender, RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() => _view?.Refresh()));

    void Search_Changed(object sender, TextChangedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() => _view?.Refresh()));

    void SortMenu_Click(object sender, RoutedEventArgs e)
    {
        SortButton.ContextMenu.PlacementTarget = SortButton;
        SortButton.ContextMenu.IsOpen = true;
    }

    void SortPick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { Tag: string sort })
            return;
        _vm.Sort = sort;
        SortButton.Content = "Sort: " + sort;
        ApplySort();
    }

    public void ShowProblem(string message)
    {
        _vm.ReportProblem(message);
    }

    void ApplySort()
    {
        if (_view == null)
            return;
        _view.SortDescriptions.Clear();
        var (property, direction) = _vm.Sort switch
        {
            "Name" => ("Name", ListSortDirection.Ascending),
            "Memory" => ("RamBytes", ListSortDirection.Descending),
            "Disk" => ("DiskBytes", ListSortDirection.Descending),
            "Network" => ("NetBytes", ListSortDirection.Descending),
            _ => ("Cpu", ListSortDirection.Descending)
        };
        _view.SortDescriptions.Add(new SortDescription(property, direction));
    }

    async void PauseRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProcessRow row })
            await _vm.TogglePauseAsync(row);
        e.Handled = true;
    }

    async void EndRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProcessRow row }
            && ConfirmWindow.Show(this, "End " + row.Name + "?", "Unsaved work in that app will be lost.", ["pid " + row.Pid], "End"))
            await _vm.EndAsync(row);
        e.Handled = true;
    }

    void EndTree_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedProcess is ProcessRow row
            && ConfirmWindow.Show(this, "End " + row.Name + " and the processes it started?", "Unsaved work in those apps will be lost.", null, "End tree"))
            _vm.EndTree(row);
    }

    void Service_Click(object sender, RoutedEventArgs e)
    {
        var services = _vm.ServicesForSelected();
        if (services.Count == 0)
        {
            ConfirmWindow.Show(this, "No service here", "This process is not hosting a Windows service. Windows service hosts are left alone on purpose.", null, "OK", "Close");
            return;
        }
        var dialog = new ServiceStartWindow(services) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.ServiceName != null && dialog.StartType != null)
            _ = _vm.SetServiceStartAsync(dialog.ServiceName, dialog.StartType);
    }

    void Startup_Click(object sender, RoutedEventArgs e)
    {
        var detail = _vm.StartupDetailForSelected();
        if (detail == null)
        {
            _vm.Status = "No startup entry is tied to this app. Run Check PC to refresh the list.";
            return;
        }
        if (ConfirmWindow.Show(this, "Remove this startup entry?", detail + " can be restored from Undo saved changes.", null, "Remove"))
            _vm.DisableStartupForSelected();
    }

    async void PauseSelected_Click(object sender, RoutedEventArgs e)
    {
        var names = _vm.SafeItems.Concat(_vm.AskItems).Where(row => row.IsChecked).Select(row => row.Title).ToList();
        if (names.Count == 0)
        {
            _vm.Status = "Nothing is selected.";
            return;
        }
        if (ConfirmWindow.Show(this, "Pause these apps?", "You can resume them from the banner.", names, "Pause"))
            _vm.Status = await _vm.PauseCheckedFindingsAsync();
    }

    async void SpeedStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SpeedStepRow row })
            return;
        var yes = row.Optimized ? "Undo" : (row.HasWarning ? "I understand, do this" : "Do this");
        var message = row.HasWarning && !row.Optimized ? row.Warning + "\n\n" + row.Detail : row.Detail;
        if (!ConfirmWindow.Show(this, row.HasWarning && !row.Optimized ? "Warning" : row.Title, message, null, yes))
            return;
        await _vm.ApplySpeedAsync(row);
    }

    void DisableStartup_Click(object sender, RoutedEventArgs e)
    {
        var names = _vm.StartupItems.Where(row => row.IsChecked).Select(row => row.Title).ToList();
        if (names.Count == 0)
        {
            _vm.Status = "No startup items are selected.";
            return;
        }
        if (ConfirmWindow.Show(this, "Turn these off at startup?", "They can be put back with Undo saved changes.", names, "Turn off"))
            _vm.DisableCheckedStartup();
    }

    async void OpenOptional_Click(object sender, RoutedEventArgs e)
    {
        LeaveCompactForReading();
        await _vm.LoadOptionalAsync();
    }

    void OpenLog_Click(object sender, RoutedEventArgs e) => _vm.ShowPage("Log");

    void SelectSuggested_Click(object sender, RoutedEventArgs e) => _vm.SelectSuggested();

    async void ApplyDebloat_Click(object sender, RoutedEventArgs e)
    {
        var selected = _vm.SelectedDebloat();
        if (selected.Count == 0)
        {
            _vm.Status = "Nothing is selected.";
            return;
        }
        var bullets = selected.SelectMany(DebloatRunner.Preview).ToList();
        var required = DebloatRunner.RequiredPhrase(selected);
        string? phrase = null;
        if (required != null)
        {
            var boot = selected.Any(choice => choice.Item.Tier == DebloatTier.Boot);
            var message = boot
                ? "This includes boot services. Windows may not start, and security software may be turned off too."
                : "This turns off security software or Windows Update. The PC is easier to infect and will miss fixes.";
            phrase = ConfirmWindow.ShowForPhrase(this, "This can break the PC", message, bullets, required);
            if (phrase == null)
                return;
        }
        else if (!ConfirmWindow.Show(this, "Apply these changes?",
                     "A restore point is created first. Removed Store apps may need to be installed again. This can break apps that depended on them.",
                     bullets, "Apply"))
            return;

        var summary = await _vm.ApplyDebloatAsync(selected, phrase, allowWithoutRestore: false);
        if (summary.Contains("restore point", StringComparison.OrdinalIgnoreCase)
            && summary.Contains("Couldn't", StringComparison.OrdinalIgnoreCase)
            && ConfirmWindow.Show(this, "No restore point", summary + " Continue anyway?", null, "Continue"))
            summary = await _vm.ApplyDebloatAsync(selected, phrase, allowWithoutRestore: true);
        ConfirmWindow.Show(this, "Finished", summary, null, "OK", "Close");
    }

    void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON profile|*.json",
            FileName = AppInfo.ProductName + "-profile.json"
        };
        if (dialog.ShowDialog(this) != true)
            return;
        var document = ProfileStore.Load();
        JsonFiles.WriteAtomic(dialog.FileName, document);
        _vm.Status = "Exported " + document.Changes.Count + " saved changes.";
    }

    async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "JSON profile|*.json" };
        if (dialog.ShowDialog(this) != true)
            return;
        var document = JsonFiles.ReadOrNew<ProfileDocument>(dialog.FileName);
        if (!ConfirmWindow.Show(this, "Restore this profile?", "Saved startup items, services, and settings are put back where that is possible. Removed Store apps are not reinstalled.", null, "Restore"))
            return;
        ProfileStore.Save(document);
        var summary = await _vm.UndoProfileAsync();
        ConfirmWindow.Show(this, "Import finished", summary, null, "OK", "Close");
    }

    async void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmWindow.Show(this, "Undo saved changes?", "Services, startup items, and the Copilot button can be put back. Removed apps need to be reinstalled.", null, "Undo"))
            return;
        var summary = await _vm.UndoProfileAsync();
        ConfirmWindow.Show(this, "Undo finished", summary, null, "OK", "Close");
    }

    void Exit_Click(object sender, RoutedEventArgs e)
    {
        _exiting = true;
        Close();
    }
}

public sealed class ServiceStartWindow : Window
{
    readonly ComboBox _services = new() { Margin = new Thickness(0, 8, 0, 8) };

    public string? ServiceName { get; private set; }
    public string? StartType { get; private set; }

    public ServiceStartWindow(IReadOnlyList<string> services)
    {
        Title = "Service start";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1C, 0x1F, 0x27));
        Foreground = System.Windows.Media.Brushes.White;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        ShowInTaskbar = false;
        foreach (var service in services)
            _services.Items.Add(service);
        _services.SelectedIndex = 0;
        var manual = new Button { Content = "Manual", Height = 32, Margin = new Thickness(0, 0, 6, 0) };
        var disabled = new Button { Content = "Disabled", Height = 32, Margin = new Thickness(6, 0, 0, 0) };
        var cancel = new Button { Content = "Cancel", Height = 32, Margin = new Thickness(0, 8, 0, 0) };
        manual.Click += (_, _) => Choose("Manual");
        disabled.Click += (_, _) => Choose("Disabled");
        cancel.Click += (_, _) => DialogResult = false;
        Content = new Border
        {
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2C, 0x31, 0x3C)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "Change service start type", FontWeight = FontWeights.SemiBold, FontSize = 15 },
                    new TextBlock { Text = "The previous start type is saved so Undo can put it back. This does not stop the service until the next boot, unless Windows stops it itself.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9A, 0xA3, 0xB2)) },
                    _services,
                    new UniformGrid { Columns = 2, Children = { manual, disabled } },
                    cancel
                }
            }
        };
    }

    void Choose(string start)
    {
        ServiceName = _services.SelectedItem as string;
        StartType = start;
        DialogResult = true;
    }
}
