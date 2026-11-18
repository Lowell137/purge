using System.Diagnostics;
using Kaldir.Models;
using Kaldir.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kaldir;

public sealed partial class MainWindow : Window
{
    List<AppEntry> _all = new();
    bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 800));
        // Başlık çubuğu içeriğe katılır: Mica pencerenin tamamında, sürükleme alanı AppTitleBar.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        try { AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Purge.ico")); } catch { }   // görev çubuğu + Alt+Tab
        AppWindow.TitleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Standard;   // 32 px, Wintoys gibi
        Nav.DisplayModeChanged += (_, _) => ApplyLayout();
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter op) { op.PreferredMinimumWidth = 480; op.PreferredMinimumHeight = 520; }
        bool admin = Uninstaller.IsAdmin();
        ElevateBtn.Visibility = admin ? Visibility.Collapsed : Visibility.Visible;
        RestoreChk.IsEnabled = admin;
        if (admin) TitleText.Text = "Purge (admin)";
        InitTools();
        InitSettings(admin);
        _ = StartAsync();
    }

    async Task StartAsync()
    {
        await LoadAsync();
        string[] a = Environment.GetCommandLineArgs();
        if (a.Length > 2 && a[1] == "--silent-report")
        {
            // Read-only diagnostic: write the silent command we would run for every program, then exit. Nothing is executed.
            var lines = _all.Where(x => !x.IsStore).Select(x =>
            {
                var p = Uninstaller.Plan(x, true);
                return $"{p.Kind}\t{(p.Silent ? "SILENT" : "window")}\t{x.Name}\t{p.How}\t{p.Command}";
            }).OrderBy(s => s).ToList();
            File.WriteAllLines(a[2], lines);
            Application.Current.Exit();
            return;
        }
        if (a.Length > 2 && a[1] == "--selftest") { await SelfTestAsync(a[2]); return; }
        if (a.Length > 3 && a[1] == "--leftover-report")
        {
            // Read-only diagnostic: scan leftovers (install folder excluded) for every installed app whose name contains a[2]; nothing is deleted.
            var lines = new List<string>();
            foreach (var app in _all.Where(x => !x.IsStore && x.Name.Contains(a[2], StringComparison.OrdinalIgnoreCase)))
                foreach (var it in LeftoverScanner.Scan(app, includeInstallLocation: false))
                    lines.Add($"{app.Name}\t{it.Kind}\t{it.Path}\t{it.ValueName}");
            File.WriteAllLines(a[3], lines);
            Application.Current.Exit();
            return;
        }
        if (a.Length > 1 && (File.Exists(a[1]) || Directory.Exists(a[1])))
        {
            await Task.Delay(300);   // let the window root settle (ContentDialog needs XamlRoot)
            await HandlePathAsync(a[1]);
        }
    }

    async Task LoadAsync()
    {
        Busy.IsActive = true;
        Busy.Visibility = Visibility.Visible;
        SetEnabledState();
        StatusText.Text = "Scanning installed programs";
        var list = await Task.Run(() =>
        {
            var l = ProgramScanner.Scan();
            l.AddRange(StoreScanner.Scan());
            return l;
        });
        _all = list;
        Busy.IsActive = false;
        Busy.Visibility = Visibility.Collapsed;
        Apply();
        StatusText.Text = "Ready";
        _ = FillSizesAsync(list);
        _ = LoadIconsAsync(list);
    }

    // Icons come from the shell thumbnail cache (the icon inside the exe/ico), loaded four at a time on the UI thread.
    async Task LoadIconsAsync(List<AppEntry> list)
    {
        using var gate = new SemaphoreSlim(4);
        var tasks = list.Where(a => a.IconPath.Length > 0).Select(async a =>
        {
            await gate.WaitAsync();
            try
            {
                var f = await Windows.Storage.StorageFile.GetFileFromPathAsync(a.IconPath);
                using var th = await f.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.SingleItem, 32);
                if (th == null || th.Type != Windows.Storage.FileProperties.ThumbnailType.Image) return;
                var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                await bmp.SetSourceAsync(th);
                a.Icon = bmp;
            }
            catch { }
            finally { gate.Release(); }
        });
        await Task.WhenAll(tasks);
    }

    void OnSelectAll(object sender, RoutedEventArgs e)
    {
        if (SelectAllChk.IsChecked == true) AppList.SelectAll(); else AppList.SelectedItems.Clear();
    }

    // Pencere içeriğinde AdaptiveTrigger tetiklenmiyor; düzeni pencere genişliğine göre burada kur.
    // Geniş (>=900): tek satır. Orta: alt çubuk düğmeleri ikinci satıra. Dar (<640): filtreler ikinci satıra, onay kutuları alt alta.
    void OnPageSizeChanged(object sender, SizeChangedEventArgs e) => ApplyLayout();

    void ApplyLayout()
    {
        double w = Root.ActualWidth;
        bool narrow = w < 640, medium = !narrow && w < 900;
        bool minimal = Nav.DisplayMode == NavigationViewDisplayMode.Minimal;

        double side = narrow ? 16 : medium ? 24 : 32;
        PageGrid.Padding = new Thickness(side, minimal ? 44 : 8, side, 12);   // Minimal'de hamburger sayfanın sol üstünde durur
        PageTitle.Style = (Style)Application.Current.Resources[narrow ? "SubtitleTextBlockStyle" : "TitleTextBlockStyle"];
        ElevateLabel.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;

        // Arama satırı
        Grid.SetRow(SourceBox, narrow ? 1 : 0); Grid.SetColumn(SourceBox, narrow ? 0 : 1); Grid.SetColumnSpan(SourceBox, narrow ? 2 : 1);
        Grid.SetRow(SortBox, narrow ? 1 : 0); Grid.SetColumn(SortBox, 2); Grid.SetColumnSpan(SortBox, narrow ? 2 : 1);
        SourceBox.Width = SortBox.Width = narrow ? double.NaN : 160;
        SourceBox.HorizontalAlignment = SortBox.HorizontalAlignment = narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        Grid.SetColumnSpan(SearchBox, narrow ? 3 : 1);

        // Alt çubuk
        bool stack = narrow || medium;
        Grid.SetRow(BottomButtons, stack ? 1 : 0); Grid.SetColumn(BottomButtons, stack ? 0 : 1); Grid.SetColumnSpan(BottomButtons, stack ? 2 : 1);
        BottomButtons.Margin = new Thickness(0, stack ? 10 : 0, 0, 0);
        BottomButtons.HorizontalAlignment = narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        BottomChecks.Orientation = narrow ? Orientation.Vertical : Orientation.Horizontal;
        BottomChecks.Spacing = narrow ? 0 : 20;

        UiState.Current.ApplyWidth(PageGrid.ActualWidth - 2 * side);
    }

    async void OnTabChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (SearchRow == null) return;   // InitializeComponent sırasında, IsSelected="True" ile tetiklenir
        var sel = args.SelectedItem;
        bool settings = args.IsSettingsSelected;
        bool startup = !settings && sel == TabStartup, tools = !settings && sel == TabTools, programs = !settings && !startup && !tools;
        PageTitle.Text = settings ? "Settings" : startup ? "Startup" : tools ? "Tools" : "Programs";
        var v = programs ? Visibility.Visible : Visibility.Collapsed;
        SearchRow.Visibility = v; ListGrid.Visibility = v; BottomBar.Visibility = v;
        StartupView.Visibility = startup ? Visibility.Visible : Visibility.Collapsed;
        ToolsView.Visibility = tools ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        if (settings) CountText.Text = "";
        else if (startup)
        {
            StartupList.ItemsSource = await Task.Run(StartupService.Scan);
            CountText.Text = $"{((List<StartupEntry>)StartupList.ItemsSource).Count} startup items";
        }
        else if (tools) CountText.Text = $"{(ToolsGrid.ItemsSource as System.Collections.ICollection)?.Count ?? 0} tools";
        else CountText.Text = $"{(AppList.ItemsSource as List<AppEntry>)?.Count ?? 0} apps";
    }

    void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch ts || ts.DataContext is not StartupEntry en) return;
        if (en.Enabled == ts.IsOn) return;   // fired while the template loads
        string? err = StartupService.SetEnabled(en, ts.IsOn);
        if (err != null)
        {
            ts.IsOn = en.Enabled;   // revert
            StartupInfo.Severity = InfoBarSeverity.Warning;
            StartupInfo.Message = en.NeedsAdmin && !Uninstaller.IsAdmin()
                ? "This item applies to all users: restart as administrator to change it."
                : "Could not change it: " + err;
        }
    }

    // For entries without EstimatedSize, measure the install folder in the background, then refresh the sort.
    async Task FillSizesAsync(List<AppEntry> list)
    {
        var todo = list.Where(a => a.SizeBytes == 0 && a.InstallLocation.Length > 0 && Directory.Exists(a.InstallLocation)).ToList();
        if (todo.Count == 0) return;
        await Task.Run(() => Parallel.ForEach(todo, new ParallelOptions { MaxDegreeOfParallelism = 4 }, a =>
        {
            long s = ProgramScanner.DirSize(a.InstallLocation);
            if (s > 0) DispatcherQueue.TryEnqueue(() => a.SizeBytes = s);
        }));
        DispatcherQueue.TryEnqueue(() => { if (SortBox.SelectedIndex == 1) Apply(); });
    }

    void Apply()
    {
        IEnumerable<AppEntry> q = _all;
        switch (SourceBox.SelectedIndex)
        {
            case 1: q = q.Where(a => !a.IsStore); break;
            case 2: q = q.Where(a => a.IsStore); break;
        }
        string s = SearchBox.Text?.Trim() ?? "";
        if (s.Length > 0)
            q = q.Where(a => a.Name.Contains(s, StringComparison.CurrentCultureIgnoreCase)
                          || a.Publisher.Contains(s, StringComparison.CurrentCultureIgnoreCase));
        q = SortBox.SelectedIndex switch
        {
            1 => q.OrderByDescending(a => a.SizeBytes),
            2 => q.OrderByDescending(a => a.InstallDate ?? DateTime.MinValue),
            3 => q.OrderBy(a => a.Publisher, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => q.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase),
        };
        var res = q.ToList();
        AppList.ItemsSource = res;
        CountText.Text = $"{res.Count} apps";
    }

    void OnFilterChanged(object sender, object e) { if (IsLoaded()) Apply(); }
    bool IsLoaded() => AppList != null && SortBox != null && SourceBox != null && SearchBox != null;

    async void OnRefresh(object sender, RoutedEventArgs e) { if (!_busy) await LoadAsync(); }

    void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SetEnabledState();
        int n = AppList.SelectedItems.Count;
        StatusText.Text = n == 0 ? "Ready" : $"{n} selected";
    }

    void SetEnabledState()
    {
        bool any = AppList.SelectedItems.Count > 0 && !_busy;
        UninstallBtn.IsEnabled = any;
        ScanBtn.IsEnabled = any;
        ForceBtn.IsEnabled = any && AppList.SelectedItems.Cast<AppEntry>().All(a => !a.IsStore);
    }

    List<AppEntry> Selected() => AppList.SelectedItems.Cast<AppEntry>().ToList();

    void OnElevate(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Exit();
        }
        catch { StatusText.Text = "Elevation cancelled"; }
    }

    async Task<bool> Confirm(string title, string body, string primary)
    {
        var d = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await d.ShowAsync() == ContentDialogResult.Primary;
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        if (_busy || !e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)) return;
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Link;
        e.DragUIOverride.Caption = "Drop to uninstall";
        e.DragUIOverride.IsGlyphVisible = false;
    }

    async void OnDrop(object sender, DragEventArgs e)
    {
        if (_busy) return;
        var def = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            string? raw = items.FirstOrDefault()?.Path;
            if (string.IsNullOrEmpty(raw)) return;
            await HandlePathAsync(raw);
        }
        finally { def.Complete(); }
    }

    // Shared entry for drag-and-drop and the command line (Kaldir.exe "<path>").
    async Task HandlePathAsync(string raw)
    {
        string path = DropResolver.ResolveShortcut(raw);

        Nav.SelectedItem = TabPrograms;
        var hit = DropResolver.Find(path, _all);
        if (hit != null)
        {
            SearchBox.Text = ""; SourceBox.SelectedIndex = 0; Apply();
            AppList.SelectedItems.Clear();
            AppList.SelectedItem = hit;
            AppList.ScrollIntoView(hit);
            await UninstallSelectedAsync();
            return;
        }

        // No registered program: offer a name-based leftover scan.
        var syn = DropResolver.Synthetic(path);
        string where = syn.InstallLocation.Length > 0 ? $"\n\nThe install folder will be listed too:\n{syn.InstallLocation}" : "";
        if (!await Confirm("No installed program found",
            $"'{Path.GetFileName(path)}' is not linked to any installed program. Scan for leftovers by name? Nothing is deleted without your selection.{where}", "Scan")) return;
        await ShowLeftoversAsync(new List<AppEntry> { syn }, includeInstallLocation: syn.InstallLocation.Length > 0);
    }

    async void OnUninstall(object sender, RoutedEventArgs e) => await UninstallSelectedAsync();

    bool _confirming;   // only one ContentDialog may be open; a double click must not open a second one

    async Task UninstallSelectedAsync()
    {
        var sel = Selected();
        if (sel.Count == 0 || _busy || _confirming) return;
        string names = string.Join("\n", sel.Take(8).Select(a => "• " + a.Name)) + (sel.Count > 8 ? $"\n… and {sel.Count - 8} more" : "");
        _confirming = true;
        bool yes;
        try { yes = await Confirm(sel.Count == 1 ? "Uninstall this program?" : $"Uninstall {sel.Count} programs?", names, "Uninstall"); }
        finally { _confirming = false; }
        if (!yes) return;

        _busy = true; SetEnabledState();
        Busy.IsActive = true; Busy.Visibility = Visibility.Visible;
        try
        {
            if (RestoreChk.IsChecked == true)
            {
                StatusText.Text = "Creating restore point";
                bool ok = await Uninstaller.CreateRestorePointAsync(sel.Count == 1 ? sel[0].Name : $"{sel.Count} programs");
                if (!ok) StatusText.Text = "Restore point failed (Windows allows one per 24 hours), continuing";
            }

            bool silent = SilentChk.IsChecked == true;
            var removed = new List<AppEntry>();
            var failed = new List<string>();
            var noSilent = new List<AppEntry>();
            int i = 0;
            foreach (var a in sel)
            {
                StatusText.Text = $"({++i}/{sel.Count}) Uninstalling {a.Name}";
                var (res, msg, usedSilent) = await Uninstaller.RunAsync(a, silent, allowUi: !silent);
                if (res == UninstallResult.NeedsUi) { noSilent.Add(a); continue; }
                if (res != UninstallResult.Done && silent && usedSilent && !a.IsStore)
                {
                    // The guessed silent switch did not work: leave it for the user to decide (force remove or show the window).
                    noSilent.Add(a);
                    LeftoverCleaner.LogHistory(a.Name, a.Version, "silent", $"failed: {msg}");
                    continue;
                }
                LeftoverCleaner.LogHistory(a.Name, a.Version, usedSilent ? "silent" : "window", res.ToString());
                if (res == UninstallResult.Done) removed.Add(a);
                else failed.Add($"{a.Name}: {(msg.Length > 0 ? msg : res.ToString())}");
            }

            if (noSilent.Count > 0) await HandleNoSilentAsync(noSilent, removed, failed);

            if (LeftoverChk.IsChecked == true && removed.Count > 0)
                await ShowLeftoversAsync(removed, includeInstallLocation: true);

            await LoadAsync();
            StatusText.Text = failed.Count == 0
                ? $"{removed.Count} uninstalled"
                : $"{removed.Count} uninstalled, {failed.Count} failed: {string.Join(" | ", failed)}";
        }
        finally
        {
            _busy = false; Busy.IsActive = false; Busy.Visibility = Visibility.Collapsed; SetEnabledState();
        }
    }

    // Programs whose uninstaller has no silent mode: never open its window by surprise.
    async Task HandleNoSilentAsync(List<AppEntry> apps, List<AppEntry> removed, List<string> failed)
    {
        string list = string.Join("\n", apps.Take(10).Select(a => "• " + a.Name)) + (apps.Count > 10 ? $"\n… and {apps.Count - 10} more" : "");
        var dlg = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = apps.Count == 1 ? "No silent uninstall for this program" : $"No silent uninstall for {apps.Count} programs",
            Content = new TextBlock
            {
                Text = list + "\n\nForce remove closes the app, deletes its files and registry entries and shows the leftovers (files go to the Recycle Bin, registry is backed up). " +
                       "Or open the program's own uninstaller window.",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "Force remove",
            SecondaryButtonText = "Show uninstaller window",
            CloseButtonText = "Skip",
            DefaultButton = ContentDialogButton.Primary,
        };
        var r = await dlg.ShowAsync();
        if (r == ContentDialogResult.Primary)
        {
            foreach (var a in apps)
            {
                StatusText.Text = $"Force removing {a.Name}";
                Uninstaller.KillProcessesIn(a.InstallLocation);
                string? err = await LeftoverCleaner.RemoveRegistration(a);
                LeftoverCleaner.LogHistory(a.Name, a.Version, "force", err ?? "ok");
                if (err == null) removed.Add(a); else failed.Add($"{a.Name}: {err}");
            }
        }
        else if (r == ContentDialogResult.Secondary)
        {
            foreach (var a in apps)
            {
                StatusText.Text = $"Waiting for {a.Name} uninstaller";
                var (res, msg, _) = await Uninstaller.RunAsync(a, false, allowUi: true);
                LeftoverCleaner.LogHistory(a.Name, a.Version, "window", res.ToString());
                if (res == UninstallResult.Done) removed.Add(a);
                else failed.Add($"{a.Name}: {(msg.Length > 0 ? msg : res.ToString())}");
            }
        }
    }

    async void OnScanOnly(object sender, RoutedEventArgs e)
    {
        var sel = Selected();
        if (sel.Count == 0 || _busy) return;
        // If the app is still installed, its install folder must not count as a leftover.
        await ShowLeftoversAsync(sel, includeInstallLocation: false);
    }

    async void OnForce(object sender, RoutedEventArgs e)
    {
        var sel = Selected().Where(a => !a.IsStore).ToList();
        if (sel.Count == 0 || _busy) return;
        if (!await Confirm("Force uninstall",
            "The program's uninstaller is not run. Its running processes are closed, its uninstall entry is deleted (backed up as .reg) and its install folder and leftovers are listed for you to confirm. Use this for programs with a broken uninstaller.",
            "Continue")) return;

        _busy = true; SetEnabledState();
        try
        {
            var done = new List<AppEntry>();
            foreach (var a in sel)
            {
                StatusText.Text = $"Force removing {a.Name}";
                Uninstaller.KillProcessesIn(a.InstallLocation);
                string? err = await LeftoverCleaner.RemoveRegistration(a);
                LeftoverCleaner.LogHistory(a.Name, a.Version, "force", err ?? "ok");
                if (err == null) done.Add(a);
                else StatusText.Text = $"{a.Name}: {err}";
            }
            if (done.Count > 0) await ShowLeftoversAsync(done, includeInstallLocation: true);
            await LoadAsync();
        }
        finally { _busy = false; SetEnabledState(); }
    }

    async Task ShowLeftoversAsync(List<AppEntry> apps, bool includeInstallLocation)
    {
        StatusText.Text = "Scanning for leftovers";
        Busy.IsActive = true; Busy.Visibility = Visibility.Visible;
        var items = await Task.Run(() =>
        {
            var all = new List<LeftoverItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in apps)
                foreach (var it in LeftoverScanner.Scan(a, includeInstallLocation))
                    if (seen.Add(it.Kind + "|" + it.Path + "|" + it.ValueName)) all.Add(it);
            return all;
        });
        Busy.IsActive = false; Busy.Visibility = Visibility.Collapsed;

        if (items.Count == 0)
        {
            StatusText.Text = "No leftovers found";
            return;
        }

        await ConfirmAndCleanAsync(items, $"{items.Count} leftovers found");
    }

    // Shows a checklist of removable items and deletes the ones the user confirms (shared by leftovers, Install Monitor logs, unregistered folders).
    async Task ConfirmAndCleanAsync(List<LeftoverItem> items, string title)
    {
        var list = new ListView { ItemsSource = items, SelectionMode = ListViewSelectionMode.None, MaxHeight = 420 };
        list.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Grid ColumnSpacing="10" Padding="0,2">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto" /><ColumnDefinition Width="Auto" /><ColumnDefinition Width="*" /><ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <CheckBox IsChecked="{Binding IsSelected, Mode=TwoWay}" MinWidth="0" />
                    <FontIcon Grid.Column="1" Glyph="{Binding Glyph}" FontSize="14" />
                    <StackPanel Grid.Column="2">
                        <TextBlock Text="{Binding Path}" TextTrimming="CharacterEllipsis" />
                        <TextBlock Text="{Binding Kind}" FontSize="11" Opacity="0.6" />
                    </StackPanel>
                    <TextBlock Grid.Column="3" Text="{Binding SizeText}" Opacity="0.7" />
                </Grid>
            </DataTemplate>
            """);

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = "Selected items are deleted: folders and files go to the Recycle Bin, registry keys and services are backed up as .reg first.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75,
        });
        panel.Children.Add(list);

        var dlg = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = panel,
            PrimaryButtonText = "Delete selected",
            CloseButtonText = "Skip",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) { StatusText.Text = "Leftovers kept"; return; }

        var chosen = items.Where(i => i.IsSelected).ToList();
        StatusText.Text = $"Deleting {chosen.Count} leftovers";
        var (ok, errors, backup) = await LeftoverCleaner.CleanAsync(chosen);
        StatusText.Text = errors.Count == 0
            ? $"{ok} leftovers deleted" + (backup.Length > 0 ? $" · registry backup: {backup}" : "")
            : $"{ok} deleted, {errors.Count} errors: {errors[0]}";
    }
}

