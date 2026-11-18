using Kaldir.Models;
using Kaldir.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kaldir;

// Tools sekmesi: kaldırmayla ilgili yardımcı araçlar.
public sealed partial class MainWindow
{
    IntPtr Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(this);

    void InitTools()
    {
        _tiles = new List<ToolTile>
        {
            new() { Id = "monitor",  Title = "Install Monitor",           Sub = "Log what an installer changes",        Glyph = "\uE896" },
            new() { Id = "updates",  Title = "Update outdated software",  Sub = "Silent updates through winget",        Glyph = "\uE777" },
            new() { Id = "easy",     Title = "Easy Uninstall",            Sub = "Ctrl+Alt+U on any app window",         Glyph = "\uE7C9" },
            new() { Id = "force",    Title = "Force Uninstall+",          Sub = "Remove by file or folder",             Glyph = "\uE74D" },
            new() { Id = "stubborn", Title = "Stubborn Program Remover",  Sub = "Folders with no uninstall entry",      Glyph = "\uE7BA" },
            new() { Id = "shred",    Title = "File Shredder",             Sub = "Overwrite and delete for good",        Glyph = "\uE74E" },
            new() { Id = "move",     Title = "Software Relocator",        Sub = "Move a program to another drive",      Glyph = "\uE8DE" },
            new() { Id = "bloat",    Title = "Bloatware Remover",         Sub = "Preinstalled Store apps",              Glyph = "\uE8F8" },
            new() { Id = "winupd",   Title = "Windows Updates Uninstall", Sub = "Remove installed updates",             Glyph = "\uE895" },
        };
        // Ctrl+Alt+U: pencereler oluştuktan sonra kaydet.
        Activated += (_, _) => RegisterHotKeyOnce();
    }

    List<ToolTile> _tiles = new();
    string? _hotKey;
    bool _hotKeyDone;
    void RegisterHotKeyOnce()
    {
        if (_hotKeyDone) return;
        _hotKeyDone = true;
        ApplyHotKey();   // Settings > Easy Uninstall hotkey kapalıysa kaydetmez
    }

    // ItemsRepeater kartları yeniden kullanır: her hazırlıkta hangi araç olduğunu Tag'e yaz, Click'i bir kez bağla.
    void OnToolElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is not Button b) return;
        b.Tag = sender.ItemsSourceView.GetAt(args.Index);
        b.Click -= OnToolButtonClick;
        b.Click += OnToolButtonClick;
    }

    void OnToolButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ToolTile t }) RunTool(t);
    }

    // Hata ve ilerleme her sayfada görünür: StatusText ve Busy sayfa başlığında/altında, hatalar ErrorBar'da.
    void ShowError(string title, string message)
    {
        ErrorBar.Title = title; ErrorBar.Message = message; ErrorBar.IsOpen = true;
        StatusText.Text = $"{title}: {message}";
    }

    async void RunTool(ToolTile t)
    {
        if (_busy) { StatusText.Text = "Busy, wait for the current task to finish"; return; }
        ErrorBar.IsOpen = false;
        try
        {
            switch (t.Id)
            {
                case "monitor": await ToolMonitorAsync(); break;
                case "updates": await ToolUpdatesAsync(); break;
                case "easy": await ToolEasyAsync(); break;
                case "force": await ToolForceAsync(); break;
                case "stubborn": await ToolStubbornAsync(); break;
                case "shred": await ToolShredAsync(); break;
                case "move": await ToolMoveAsync(); break;
                case "bloat": await ToolBloatAsync(); break;
                case "winupd": await ToolWinUpdatesAsync(); break;
            }
        }
        catch (Exception ex) { ShowError($"{t.Title} failed", ex.Message); }
        finally
        {
            _busy = false; Busy.IsActive = false; Busy.Visibility = Visibility.Collapsed;
            if (StatusText.Text == _busyStatus) StatusText.Text = "Ready";   // ilerleme yazısı takılı kalmasın
        }
    }

    string? _busyStatus;
    void BusyOn(string status) { _busy = true; Busy.IsActive = true; Busy.Visibility = Visibility.Visible; _busyStatus = status; ToolStatus(status); }
    void ToolStatus(string s) { StatusText.Text = s; }

    async Task Info(string title, string body)
    {
        await new ContentDialog { XamlRoot = Content.XamlRoot, Title = title, Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap }, CloseButtonText = "OK" }.ShowAsync();
    }

    // Onay kutulu genel liste: seçilenleri döndürür, iptalde null.
    async Task<List<CheckItem>?> ChecklistAsync(string title, string description, List<CheckItem> items, string primary)
    {
        var list = new ListView { ItemsSource = items, SelectionMode = ListViewSelectionMode.None, MaxHeight = 420 };
        list.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Grid ColumnSpacing="10" Padding="0,2">
                    <Grid.ColumnDefinitions><ColumnDefinition Width="Auto" /><ColumnDefinition Width="*" /></Grid.ColumnDefinitions>
                    <CheckBox IsChecked="{Binding IsSelected, Mode=TwoWay}" MinWidth="0" />
                    <StackPanel Grid.Column="1" VerticalAlignment="Center">
                        <TextBlock Text="{Binding Label}" TextTrimming="CharacterEllipsis" />
                        <TextBlock Text="{Binding Sub}" FontSize="11" Opacity="0.6" TextTrimming="CharacterEllipsis" />
                    </StackPanel>
                </Grid>
            </DataTemplate>
            """);
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });
        // Hepsini seç: üç durumlu, satırlar değişince kendini günceller
        var all = new CheckBox { Content = $"Select all ({items.Count})", IsThreeState = false, Margin = new Thickness(12, 0, 0, 0) };
        bool syncing = false;
        void SyncAll()
        {
            syncing = true;
            int n = items.Count(i => i.IsSelected);
            all.IsChecked = n == 0 ? false : n == items.Count ? true : null;
            syncing = false;
        }
        all.Click += (_, _) =>
        {
            if (syncing) return;
            bool on = items.Any(i => !i.IsSelected);   // biri bile seçili değilse hepsini seç, hepsi seçiliyse temizle
            foreach (var i in items) i.IsSelected = on;
            SyncAll();
        };
        foreach (var i in items) i.PropertyChanged += (_, _) => { if (!syncing) SyncAll(); };
        SyncAll();
        if (items.Count > 1) panel.Children.Add(all);
        panel.Children.Add(list);
        var dlg = new ContentDialog { XamlRoot = Content.XamlRoot, Title = title, Content = panel, PrimaryButtonText = primary, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return null;
        return items.Where(i => i.IsSelected).ToList();
    }

    async Task<string?> PickFileAsync(bool multiple = false, List<string>? many = null)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);
        if (multiple)
        {
            var files = await picker.PickMultipleFilesAsync();
            if (files == null || files.Count == 0) return null;
            many?.AddRange(files.Select(f => f.Path));
            return files[0].Path;
        }
        return (await picker.PickSingleFileAsync())?.Path;
    }

    async Task<string?> PickFolderAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);
        return (await picker.PickSingleFolderAsync())?.Path;
    }

    // ---- Install Monitor ----
    async Task ToolMonitorAsync()
    {
        bool pending = InstallMonitor.HasPending;
        var logs = InstallMonitor.Logs();
        var name = new TextBox { PlaceholderText = "Program name (for the log)", Visibility = pending ? Visibility.Visible : Visibility.Collapsed };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = pending
                ? "Monitoring is active. Install your program now, then come back and click Finish. Purge compares folders, registry, services and scheduled tasks with the snapshot taken before."
                : "Takes a snapshot of install folders, registry, services and scheduled tasks. Install your program, then click Finish here: Purge saves a log of everything the installer added, and you can remove it all later.\n\nThis compares snapshots rather than watching live, so changes outside these locations are not seen.",
        });
        panel.Children.Add(name);
        if (logs.Count > 0) panel.Children.Add(new TextBlock { Text = $"{logs.Count} saved log(s).", Opacity = 0.65 });

        var dlg = new ContentDialog
        {
            XamlRoot = Content.XamlRoot, Title = "Install Monitor", Content = panel,
            PrimaryButtonText = pending ? "Finish" : "Start monitoring",
            SecondaryButtonText = logs.Count > 0 ? "Saved logs" : "",
            CloseButtonText = pending ? "Cancel monitoring" : "Close",
            DefaultButton = ContentDialogButton.Primary,
        };
        var r = await dlg.ShowAsync();

        if (r == ContentDialogResult.Primary && !pending)
        {
            BusyOn("Taking snapshot");
            await Task.Run(InstallMonitor.Start);
            StatusText.Text = "Monitoring started: install your program, then open Install Monitor again and click Finish";
        }
        else if (r == ContentDialogResult.Primary && pending)
        {
            string n = string.IsNullOrWhiteSpace(name.Text) ? $"Install {DateTime.Now:yyyy-MM-dd HH:mm}" : name.Text.Trim();
            BusyOn("Comparing with the snapshot");
            var log = await Task.Run(() => InstallMonitor.Finish(n));
            Busy.IsActive = false; Busy.Visibility = Visibility.Collapsed;
            await ShowMonitorLogAsync(log);
        }
        else if (r == ContentDialogResult.None && pending)
        {
            InstallMonitor.Cancel();
            StatusText.Text = "Monitoring cancelled";
        }
        else if (r == ContentDialogResult.Secondary)
        {
            var pick = new ListView { ItemsSource = logs.Select(l => new CheckItem { Label = $"{l.name}", Sub = $"{l.time:yyyy-MM-dd HH:mm} · {l.count} changes", Tag = l.file }).ToList(), SelectionMode = ListViewSelectionMode.Single, MaxHeight = 380 };
            pick.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                """
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                    <StackPanel Padding="0,4"><TextBlock Text="{Binding Label}" /><TextBlock Text="{Binding Sub}" FontSize="11" Opacity="0.6" /></StackPanel>
                </DataTemplate>
                """);
            var d2 = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Saved install logs", Content = pick, PrimaryButtonText = "Open", CloseButtonText = "Close" };
            if (await d2.ShowAsync() == ContentDialogResult.Primary && pick.SelectedItem is CheckItem ci && ci.Tag is string file)
            {
                var log = InstallMonitor.Load(file);
                if (log != null) await ShowMonitorLogAsync(log);
            }
        }
    }

    async Task ShowMonitorLogAsync(MonitorLog log)
    {
        if (log.Items.Count == 0) { await Info($"{log.Name}", "No changes were detected in the watched locations."); StatusText.Text = "No changes detected"; return; }
        foreach (var it in log.Items) it.IsSelected = false;   // gözden geçirmek için; silme açık seçim ister
        await ConfirmAndCleanAsync(log.Items, $"{log.Name}: {log.Items.Count} changes");
    }

    // ---- Update outdated software ----
    async Task ToolUpdatesAsync()
    {
        BusyOn("Checking for updates (winget)");
        var (items, error) = await WingetService.ListUpgradesAsync();
        Busy.IsActive = false; Busy.Visibility = Visibility.Collapsed;
        if (error != null) { await Info("Update outdated software", error); return; }
        if (items.Count == 0) { StatusText.Text = "Everything is up to date"; await Info("Update outdated software", "No updates available."); return; }

        var rows = items.Select(u => new CheckItem { Label = u.Name, Sub = $"{u.Current}  →  {u.Available}   ({u.Id})", IsSelected = false, Tag = u }).ToList();
        var chosen = await ChecklistAsync($"{items.Count} updates available", "Selected programs are updated silently through winget. Close the program first if it is running.", rows, "Update selected");
        if (chosen == null || chosen.Count == 0) return;

        BusyOn("Updating");
        int ok = 0, i = 0; var failed = new List<string>();
        foreach (var c in chosen)
        {
            var u = (UpgradeItem)c.Tag!;
            StatusText.Text = $"({++i}/{chosen.Count}) Updating {u.Name}";
            var (good, msg) = await WingetService.UpgradeAsync(u.Id);
            LeftoverCleaner.LogHistory(u.Name, u.Available, "winget upgrade", good ? "ok" : msg);
            if (good) ok++; else failed.Add($"{u.Name} ({msg})");
        }
        StatusText.Text = failed.Count == 0 ? $"{ok} updated" : $"{ok} updated, {failed.Count} failed: {string.Join(", ", failed)}";
    }

    // ---- Easy Uninstall ----
    // Seçici: açık pencereler listesi ya da ekranda bir pencereye tıklama. Kısayol ayrıca odaktaki pencere için çalışır.
    async Task ToolEasyAsync()
    {
        var windows = await Task.Run(WindowPicker.ListWindows);
        var list = new ListView { ItemsSource = windows, SelectionMode = ListViewSelectionMode.Single, MaxHeight = 380 };
        list.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Grid ColumnSpacing="12" Padding="0,6">
                    <Grid.ColumnDefinitions><ColumnDefinition Width="24" /><ColumnDefinition Width="*" /></Grid.ColumnDefinitions>
                    <Image Source="{Binding Icon}" Width="24" Height="24" VerticalAlignment="Center" />
                    <StackPanel Grid.Column="1" VerticalAlignment="Center">
                        <TextBlock Text="{Binding Title}" TextTrimming="CharacterEllipsis" />
                        <TextBlock Text="{Binding ExeName}" FontSize="11" Opacity="0.6" TextTrimming="CharacterEllipsis" />
                    </StackPanel>
                </Grid>
            </DataTemplate>
            """);
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75,
            Text = "Pick an open window, or click Click a window and then click the program on screen (right-click or Esc cancels). " +
                   (_hotKey != null ? $"Shortcut: press {_hotKey} while the program's window is focused." : ""),
        });
        if (windows.Count == 0) panel.Children.Add(new TextBlock { Text = "No program windows are open.", Opacity = 0.6 });
        else panel.Children.Add(list);

        var dlg = new ContentDialog
        {
            XamlRoot = Content.XamlRoot, Title = "Easy Uninstall", Content = panel,
            PrimaryButtonText = "Uninstall", SecondaryButtonText = "Click a window", CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary, IsPrimaryButtonEnabled = false,
        };
        list.SelectionChanged += (_, _) => dlg.IsPrimaryButtonEnabled = list.SelectedItem != null;
        list.DoubleTapped += (_, _) => { if (list.SelectedItem != null) { dlg.Tag = "dbl"; dlg.Hide(); } };
        _ = LoadWindowIconsAsync(windows, list);

        var r = await dlg.ShowAsync();
        string? exe = null;
        if (r == ContentDialogResult.Primary || (string?)dlg.Tag == "dbl") exe = (list.SelectedItem as WindowItem)?.ExePath;
        else if (r == ContentDialogResult.Secondary) exe = await PickWindowByClickAsync();
        if (string.IsNullOrEmpty(exe)) return;
        if (WindowPicker.IsShell(exe)) { StatusText.Text = "Easy Uninstall: that window belongs to Windows itself"; return; }
        await HandlePathAsync(exe);
    }

    // Purge küçülür, imleç artı olur, bir sonraki tıklamanın altındaki pencerenin programı döner.
    async Task<string?> PickWindowByClickAsync()
    {
        StatusText.Text = "Click the program's window (right-click or Esc to cancel)";
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter op) op.Minimize();
        await Task.Delay(350);   // küçülme animasyonu bitsin, tıklama Purge'e gelmesin
        string? exe;
        try { exe = await WindowPicker.PickByClickAsync(TimeSpan.FromSeconds(30)); }
        finally
        {
            WindowPicker.RestoreCursor();
            if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter op2) op2.Restore();
            Activate();
        }
        if (exe == null) { StatusText.Text = "Easy Uninstall cancelled"; return null; }
        if (exe.Length == 0) { StatusText.Text = "Easy Uninstall: could not read that window's program"; return null; }
        StatusText.Text = $"Picked {Path.GetFileName(exe)}";
        return exe;
    }

    async Task LoadWindowIconsAsync(List<WindowItem> windows, ListView list)
    {
        foreach (var w in windows)
        {
            try
            {
                var f = await Windows.Storage.StorageFile.GetFileFromPathAsync(w.ExePath);
                using var th = await f.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.SingleItem, 32);
                if (th == null) continue;
                var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                await bmp.SetSourceAsync(th);
                w.Icon = bmp;
            }
            catch { }
        }
        var sel = list.SelectedItem;   // WindowItem bildirimsiz: listeyi yenile, seçimi koru
        list.ItemsSource = null; list.ItemsSource = windows; list.SelectedItem = sel;
    }

    async Task EasyUninstallFromForegroundAsync()
    {
        if (_busy) return;
        string? exe = HotKeyHost.ForegroundExePath();
        if (exe == null) { StatusText.Text = "Easy Uninstall: focus another program's window first"; return; }
        string file = Path.GetFileName(exe);
        if (file.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) || file.Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
        { StatusText.Text = "Easy Uninstall: that window belongs to Windows itself"; return; }
        Activate();
        await HandlePathAsync(exe);
    }

    // ---- Force Uninstall+ ----
    async Task ToolForceAsync()
    {
        var dlg = new ContentDialog
        {
            XamlRoot = Content.XamlRoot, Title = "Force Uninstall+",
            Content = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "Choose a file or folder of the program you want gone. Purge finds the program it belongs to (even if it has no uninstall entry) and removes it. You can also just drop a file, shortcut or folder on this window." },
            PrimaryButtonText = "Choose file", SecondaryButtonText = "Choose folder", CloseButtonText = "Cancel",
        };
        var r = await dlg.ShowAsync();
        string? path = r == ContentDialogResult.Primary ? await PickFileAsync() : r == ContentDialogResult.Secondary ? await PickFolderAsync() : null;
        if (path != null) await HandlePathAsync(path);
    }

    // ---- Stubborn Program Remover (kayıtsız klasörler) ----
    async Task ToolStubbornAsync()
    {
        BusyOn("Looking for folders with no uninstall entry");
        var items = await Task.Run(() => UnregisteredFinder.Find(_all));
        Busy.IsActive = false; Busy.Visibility = Visibility.Collapsed;
        if (items.Count == 0) { StatusText.Text = "No unregistered program folders found"; await Info("Stubborn Program Remover", "Every folder in Program Files and AppData\\Programs belongs to a registered program."); return; }
        await ConfirmAndCleanAsync(items, $"{items.Count} folders with no uninstall entry");
        StatusText.Text = "Done";
    }

    // ---- File Shredder ----
    async Task ToolShredAsync()
    {
        var dlg = new ContentDialog
        {
            XamlRoot = Content.XamlRoot, Title = "File Shredder",
            Content = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "Overwrites the contents of files with random data, then deletes them. There is no Recycle Bin and no undo.\n\nOn SSDs, wear levelling means overwriting is not a hard guarantee; full-disk encryption is the real protection." },
            PrimaryButtonText = "Choose files", SecondaryButtonText = "Choose folder", CloseButtonText = "Cancel",
        };
        var r = await dlg.ShowAsync();
        var targets = new List<string>();
        if (r == ContentDialogResult.Primary) await PickFileAsync(true, targets);
        else if (r == ContentDialogResult.Secondary) { var f = await PickFolderAsync(); if (f != null) targets.Add(f); }
        if (targets.Count == 0) return;

        var bad = targets.Where(Shredder.IsForbidden).ToList();
        if (bad.Count > 0) { await Info("File Shredder", "Refusing to shred protected locations:\n" + string.Join("\n", bad)); return; }

        int fileCount = Shredder.Expand(targets).Count();
        var passes = new ComboBox { ItemsSource = new[] { "1 pass (fast)", "3 passes", "7 passes (slow)" }, SelectedIndex = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = $"{fileCount} file(s) will be destroyed permanently:\n" + string.Join("\n", targets.Take(6)) + (targets.Count > 6 ? $"\n… and {targets.Count - 6} more" : ""), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(passes);
        var confirm = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Shred permanently?", Content = panel, PrimaryButtonText = "Shred", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        int n = passes.SelectedIndex switch { 0 => 1, 2 => 7, _ => 3 };
        BusyOn("Shredding");
        var (ok, errors) = await Task.Run(() => Shredder.Run(targets, n, name => DispatcherQueue.TryEnqueue(() => StatusText.Text = $"Shredding {name}")));
        LeftoverCleaner.LogHistory($"{ok} file(s)", "", "shred", errors.Count == 0 ? "ok" : $"{errors.Count} errors");
        StatusText.Text = errors.Count == 0 ? $"{ok} file(s) shredded" : $"{ok} shredded, {errors.Count} failed: {errors[0]}";
    }

    // ---- Software Relocator ----
    async Task ToolMoveAsync()
    {
        var candidates = _all.Where(a => Relocator.CanMove(a) == null).OrderByDescending(a => a.SizeBytes).ToList();
        if (candidates.Count == 0) { await Info("Software Relocator", "No movable programs found."); return; }

        var pick = new ListView { ItemsSource = candidates.Select(a => new CheckItem { Label = a.Name, Sub = $"{a.SizeText}   {a.InstallLocation}", Tag = a }).ToList(), SelectionMode = ListViewSelectionMode.Single, MaxHeight = 400 };
        pick.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <StackPanel Padding="0,4"><TextBlock Text="{Binding Label}" /><TextBlock Text="{Binding Sub}" FontSize="11" Opacity="0.6" TextTrimming="CharacterEllipsis" /></StackPanel>
            </DataTemplate>
            """);
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Pick a program. Its folder is copied to the drive you choose, verified, and the old location becomes a link, so shortcuts and the program keep working. The old copy goes to the Recycle Bin. Administrator rights are needed for Program Files.", TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });
        panel.Children.Add(pick);
        var dlg = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Software Relocator", Content = panel, PrimaryButtonText = "Choose destination", CloseButtonText = "Cancel" };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary || pick.SelectedItem is not CheckItem ci || ci.Tag is not AppEntry app) return;

        string? dest = await PickFolderAsync();
        if (dest == null) return;
        if (!await Confirm("Move program?", $"{app.Name}\n\nFrom: {app.InstallLocation}\nTo: {Path.Combine(dest, Path.GetFileName(app.InstallLocation.TrimEnd('\\')))}\n\nThe program will be closed first.", "Move")) return;

        BusyOn("Moving");
        string? err = await Relocator.MoveAsync(app.InstallLocation, dest, s => DispatcherQueue.TryEnqueue(() => StatusText.Text = $"{app.Name}: {s}"));
        LeftoverCleaner.LogHistory(app.Name, app.Version, "relocate", err ?? "ok");
        StatusText.Text = err == null ? $"{app.Name} moved" : $"Move failed: {err}";
        if (err != null) await Info("Software Relocator", err);
        else await LoadAsync();
    }

    // ---- Bloatware Remover ----
    async Task ToolBloatAsync()
    {
        var found = BloatService.Find(_all);
        if (found.Count == 0) { await Info("Bloatware Remover", "None of the known preinstalled apps are installed."); return; }
        var rows = found.Select(f => new CheckItem { Label = f.label, Sub = f.app.PackageFullName.Split('_')[0], IsSelected = false, Tag = f.app }).ToList();
        var chosen = await ChecklistAsync($"{found.Count} preinstalled apps found", "Selected apps are removed for your user account. Each one can be reinstalled from the Microsoft Store.", rows, "Remove selected");
        if (chosen == null || chosen.Count == 0) return;

        BusyOn("Removing apps");
        int ok = 0, i = 0; var failed = new List<string>();
        foreach (var c in chosen)
        {
            var a = (AppEntry)c.Tag!;
            StatusText.Text = $"({++i}/{chosen.Count}) Removing {c.Label}";
            string? err = await StoreScanner.RemoveAsync(a.PackageFullName);
            LeftoverCleaner.LogHistory(c.Label, a.Version, "bloatware", err ?? "ok");
            if (err == null) ok++; else failed.Add($"{c.Label}: {err}");
        }
        await LoadAsync();
        StatusText.Text = failed.Count == 0 ? $"{ok} app(s) removed" : $"{ok} removed, {failed.Count} failed: {failed[0]}";
    }

    // ---- Windows Updates Uninstall ----
    async Task ToolWinUpdatesAsync()
    {
        BusyOn("Reading installed updates");
        var updates = await WindowsUpdateService.ListAsync();
        Busy.IsActive = false; Busy.Visibility = Visibility.Collapsed;
        if (updates.Count == 0) { await Info("Windows Updates Uninstall", "No installed updates were reported."); return; }
        var rows = updates.Select(u => new CheckItem { Label = u.Kb, Sub = $"{u.Description}   {u.InstalledOn}", IsSelected = false, Tag = u }).ToList();
        var chosen = await ChecklistAsync($"{updates.Count} installed updates", "Selected updates are removed with Windows Update Standalone Installer (needs administrator; a UAC prompt may appear). Some cumulative updates cannot be removed this way.", rows, "Uninstall selected");
        if (chosen == null || chosen.Count == 0) return;

        BusyOn("Uninstalling updates");
        int ok = 0, i = 0; var failed = new List<string>();
        foreach (var c in chosen)
        {
            var u = (UpdateItem)c.Tag!;
            StatusText.Text = $"({++i}/{chosen.Count}) Uninstalling {u.Kb}";
            var (good, msg) = await WindowsUpdateService.UninstallAsync(u.Kb);
            LeftoverCleaner.LogHistory(u.Kb, "", "wusa", good ? (msg.Length > 0 ? msg : "ok") : msg);
            if (good) ok++; else failed.Add($"{u.Kb}: {msg}");
        }
        StatusText.Text = failed.Count == 0 ? $"{ok} update(s) uninstalled" : $"{ok} uninstalled, {failed.Count} failed: {failed[0]}";
    }

    // ---- Self test (CLI: Kaldir.exe --selftest <out>) : UI olmadan servis katmanını gerçek makinede dener, kendi oluşturduğu test verisini temizler. ----
    async Task SelfTestAsync(string outFile)
    {
        var lines = new List<string>();
        void L(string s) => lines.Add(s);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        try
        {
            var (ups, upErr) = await WingetService.ListUpgradesAsync();
            L($"winget upgrades: {ups.Count}{(upErr != null ? " (error: " + upErr + ")" : "")} {string.Join(" | ", ups.Take(3).Select(u => $"{u.Name}/{u.Id}/{u.Current}->{u.Available}"))}");
        }
        catch (Exception ex) { L("winget: EXC " + ex.Message); }

        try { var w = await WindowsUpdateService.ListAsync(); L($"windows updates: {w.Count} {string.Join(" | ", w.Take(3).Select(u => u.Kb + " " + u.InstalledOn))}"); }
        catch (Exception ex) { L("winupdates: EXC " + ex.Message); }

        var bloat = BloatService.Find(_all);
        L($"bloat found: {bloat.Count} {string.Join(", ", bloat.Take(8).Select(b => b.label))}");

        var unreg = UnregisteredFinder.Find(_all);
        L($"unregistered folders: {unreg.Count} {string.Join(" | ", unreg.Take(6).Select(u => Path.GetFileName(u.Path) + " " + u.SizeText))}");

        // Install Monitor: kendi test klasörümüz ve test anahtarımızı snapshot arasında oluştur, farkta görünmeli.
        string monDir = Path.Combine(local, "KaldirMonTest", "sub");
        string monKey = @"SOFTWARE\KaldirMonTest";
        try
        {
            InstallMonitor.Cancel();
            await Task.Run(InstallMonitor.Start);
            Directory.CreateDirectory(monDir);
            File.WriteAllText(Path.Combine(monDir, "a.txt"), "x");
            using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(monKey + @"\inner")) k.SetValue("v", 1);
            var log = await Task.Run(() => InstallMonitor.Finish("selftest"));
            bool dirSeen = log.Items.Any(i => i.Kind == "Folder" && i.Path.EndsWith("KaldirMonTest", StringComparison.OrdinalIgnoreCase));
            bool keySeen = log.Items.Any(i => i.Kind == "Registry key" && i.Path.EndsWith("KaldirMonTest", StringComparison.OrdinalIgnoreCase));
            L($"monitor: items={log.Items.Count} folderSeen={dirSeen} keySeen={keySeen} (noise: {string.Join(", ", log.Items.Where(i => !i.Path.Contains("KaldirMonTest")).Take(5).Select(i => i.Kind + ":" + Path.GetFileName(i.Path)))})");
            // Günlükten silme akışı: Cleaner aynı kayıtları kaldırmalı.
            var mine = log.Items.Where(i => i.Path.Contains("KaldirMonTest")).ToList();
            var (ok, errs, _) = await LeftoverCleaner.CleanAsync(mine);
            L($"monitor cleanup via log: removed={ok} errors={errs.Count} dirGone={!Directory.Exists(Path.Combine(local, "KaldirMonTest"))} keyGone={Microsoft.Win32.Registry.CurrentUser.OpenSubKey(monKey) == null}");
        }
        catch (Exception ex) { L("monitor: EXC " + ex); }
        finally
        {
            try { Directory.Delete(Path.Combine(local, "KaldirMonTest"), true); } catch { }
            try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(monKey, false); } catch { }
            foreach (var l in InstallMonitor.Logs().Where(l => l.name == "selftest")) try { File.Delete(l.file); } catch { }
        }

        // Shredder
        try
        {
            string tmp = Path.Combine(Path.GetTempPath(), "kaldir-shred-test.bin");
            File.WriteAllBytes(tmp, new byte[3_000_000]);
            Shredder.ShredFile(tmp, 3);
            L($"shredder: fileGone={!File.Exists(tmp)} forbidden(C:\\Windows)={Shredder.IsForbidden(@"C:\Windows\System32")} forbidden(C:\\)={Shredder.IsForbidden(@"C:\")}");
        }
        catch (Exception ex) { L("shredder: EXC " + ex.Message); }

        // Relocator: gerçek program yerine kendi test klasörü
        string rsrc = Path.Combine(local, "KaldirRelocTest");
        string rdstParent = Path.Combine(Path.GetTempPath(), "KaldirRelocDst");
        try
        {
            Directory.CreateDirectory(Path.Combine(rsrc, "data"));
            File.WriteAllText(Path.Combine(rsrc, "data", "f.txt"), "relocate-me");
            File.WriteAllText(Path.Combine(rsrc, "app.cfg"), "cfg");
            var fake = new AppEntry { Name = "Reloc Test", InstallLocation = rsrc };
            L($"relocator canMove={Relocator.CanMove(fake) ?? "yes"}");
            string? err = await Relocator.MoveAsync(rsrc, rdstParent);
            bool junction = Relocator.IsJunction(rsrc);
            string viaLink = File.Exists(Path.Combine(rsrc, "data", "f.txt")) ? File.ReadAllText(Path.Combine(rsrc, "data", "f.txt")) : "<missing>";
            bool realThere = File.Exists(Path.Combine(rdstParent, "KaldirRelocTest", "data", "f.txt"));
            L($"relocator: error={err ?? "none"} junction={junction} readViaOldPath='{viaLink}' realCopyAtDest={realThere} oldLeft={Directory.Exists(rsrc + ".kaldir-old")}");
        }
        catch (Exception ex) { L("relocator: EXC " + ex.Message); }
        finally
        {
            try { if (Relocator.IsJunction(rsrc)) Directory.Delete(rsrc); } catch { }
            try { Directory.Delete(rsrc, true); } catch { }
            try { Directory.Delete(rdstParent, true); } catch { }
            try { Directory.Delete(rsrc + ".kaldir-old", true); } catch { }
        }

        // Hotkey: kayıt sonucu
        string? hk = HotKeyHost.Register(Hwnd, () => { });
        _hotKeyDone = true;
        L($"hotkey registered as: {hk ?? "NONE"}");

        File.WriteAllLines(outFile, lines);
        Application.Current.Exit();
    }
}

