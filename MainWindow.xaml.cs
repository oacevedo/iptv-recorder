using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace IptvRecorder;

public partial class MainWindow : Window
{
    private AppSettings _settings;
    private readonly ObservableCollection<Recording> _recordings;
    private readonly RecordingScheduler _scheduler;
    private readonly PreviewPlayer _external = new();
    private EmbeddedPreview? _preview;
    private List<Channel> _channels = new();
    private HashSet<string> _favorites = new(StringComparer.OrdinalIgnoreCase);
    private Channel? _selected;
    private string _previewUrl = "";
    private WinForms.NotifyIcon? _tray;
    private WinForms.ToolStripMenuItem? _trayOpen;
    private WinForms.ToolStripMenuItem? _trayExit;
    private Recording? _lastNotified;
    private bool _exiting;

    /// <summary>Entrada "todos los grupos" del desplegable. Es un objeto propio para no confundirla con un grupo real.</summary>
    private sealed class AllGroupsItem
    {
        public override string ToString() => Loc.Get("AllGroups");
    }
    private AllGroupsItem _allGroups = new();

    public MainWindow()
    {
        InitializeComponent();

        _settings = Store.LoadSettings();
        _favorites = Store.LoadFavorites();
        _recordings = new ObservableCollection<Recording>(Store.LoadRecordings().OrderBy(r => r.Start));
        _scheduler = new RecordingScheduler(_recordings, () => _settings);
        _scheduler.Log += msg => SetStatus(msg);
        _scheduler.Changed += () => Store.SaveRecordings(_recordings);
        _scheduler.Starting += r =>
        {
            // Si se estaba viendo justo ese canal, se sigue viendo desde la propia
            // grabación en cuanto ffmpeg empieza a emitir al puerto local.
            var sameChannel = (_preview?.IsPlaying ?? false) && _previewUrl == r.ChannelUrl;
            var wasOpen = _external.IsRunning || (_preview?.IsPlaying ?? false);
            _external.Stop();
            StopEmbeddedPreview();

            if (sameChannel) ScheduleLiveSwitch(r);
            else if (wasOpen) SetStatus(Loc.Get("Status_PreviewClosedForRecording", r.Title));
        };

        _scheduler.Finished += NotifyFinished;

        _preview = new EmbeddedPreview(VideoView, _settings.UserAgent);
        _preview.Error += msg => Dispatcher.BeginInvoke(() =>
        {
            SetStatus(msg);
            PreviewChannelText.Text = msg;
        });

        RecordingsGrid.ItemsSource = _recordings;
        ApplyColumnHeaders();
        M3uUrlBox.Text = _settings.M3uUrl;
        DateBox.SelectedDate = DateTime.Today;
        TimeBox.Text = DateTime.Now.AddMinutes(5).ToString("HH:mm");

        SetupTray();

        var cached = Store.LoadChannelsCache();
        if (cached != null) ApplyChannels(M3uParser.Parse(cached), fromCache: true);

        if (RecordingScheduler.ResolveFfmpeg(_settings.FfmpegPath) == null)
            SetStatus(Loc.Get("Status_FfmpegMissing"));

        if (Environment.GetCommandLineArgs().Contains("--tray") && _settings.MinimizeToTray)
        {
            Loaded += (_, _) => HideToTray();
        }
    }

    private static void Info(Window owner, string key)
        => MessageBox.Show(owner, Loc.Get(key), Loc.Get("App_Title"), MessageBoxButton.OK, MessageBoxImage.Information);

    // ---------- Canales ----------

    private async void LoadList_Click(object sender, RoutedEventArgs e)
    {
        var url = M3uUrlBox.Text.Trim();
        if (url.Length == 0)
        {
            Info(this, "Msg_PasteM3u");
            return;
        }

        LoadButton.IsEnabled = false;
        SetStatus(Loc.Get("Status_Downloading"));
        try
        {
            string content;
            if (File.Exists(url))
            {
                content = await File.ReadAllTextAsync(url);
            }
            else
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd(_settings.UserAgent);
                content = await http.GetStringAsync(url);
            }

            var channels = M3uParser.Parse(content);
            if (channels.Count == 0)
            {
                MessageBox.Show(this, Loc.Get("Msg_NoChannels"), Loc.Get("App_Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Store.SaveChannelsCache(content);
            _settings.M3uUrl = url;
            Store.SaveSettings(_settings);
            ApplyChannels(channels, fromCache: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Loc.Get("Msg_DownloadFailed", ex.Message), Loc.Get("App_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus(Loc.Get("Status_LoadError"));
        }
        finally
        {
            LoadButton.IsEnabled = true;
        }
    }

    private void ApplyChannels(List<Channel> channels, bool fromCache)
    {
        _channels = channels;
        foreach (var c in _channels) c.IsFavorite = _favorites.Contains(c.Key);
        RebuildGroupFilter();

        var view = CollectionViewSource.GetDefaultView(_channels);
        ChannelList.ItemsSource = _channels;
        view.Filter = FilterChannel;

        ChannelCountText.Text = Loc.Get("Channels_Count", channels.Count);
        SetStatus(fromCache ? Loc.Get("Status_CacheLoaded") : Loc.Get("Status_ListLoaded", channels.Count));
    }

    private void RebuildGroupFilter()
    {
        var previous = GroupFilter.SelectedItem as string;
        // Instancia nueva para que el desplegable vuelva a pedir el texto (cambia con el idioma).
        _allGroups = new AllGroupsItem();
        var groups = new List<object> { _allGroups };
        groups.AddRange(_channels.Select(c => c.Group).Distinct().OrderBy(g => g, StringComparer.CurrentCultureIgnoreCase));
        GroupFilter.ItemsSource = groups;
        GroupFilter.SelectedItem = previous != null && groups.Contains(previous) ? previous : _allGroups;
    }

    private bool FilterChannel(object obj)
    {
        if (obj is not Channel c) return false;
        if (FavFilter.IsChecked == true && !c.IsFavorite) return false;
        if (GroupFilter.SelectedItem is string group && c.Group != group) return false;

        var q = SearchBox.Text.Trim();
        if (q.Length == 0) return true;
        foreach (var word in q.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (!c.Name.Contains(word, StringComparison.CurrentCultureIgnoreCase)) return false;
        return true;
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (ChannelList.ItemsSource == null) return;
        CollectionViewSource.GetDefaultView(ChannelList.ItemsSource).Refresh();
    }

    /// <summary>Marca o desmarca un canal como favorito. Se recuerda por el identificador
    /// de la lista, así que sobrevive a que el proveedor renombre el canal.</summary>
    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Channel channel }) return;

        channel.IsFavorite = !channel.IsFavorite;
        if (channel.IsFavorite) _favorites.Add(channel.Key);
        else _favorites.Remove(channel.Key);
        Store.SaveFavorites(_favorites);

        // Al quitar un favorito mientras se filtra por favoritos, la fila debe irse.
        if (FavFilter.IsChecked == true) Filter_Changed(sender, e);
    }

    /// <summary>Cada fila pide su logotipo al hacerse visible, de modo que con miles de
    /// canales solo se descargan los que se están mirando.</summary>
    private void ChannelLogo_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Channel channel }) LogoCache.Request(channel);
    }

    private void ChannelList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = ChannelList.SelectedItem as Channel;
        UpdateSelectedChannelText();
        if (_selected != null && TitleBox.Text.Trim().Length == 0) TitleBox.Text = _selected.Name;
    }

    private void UpdateSelectedChannelText()
    {
        SelectedChannelText.Text = _selected?.Name ?? Loc.Get("SelectChannel_Hint");
        SelectedChannelText.Foreground = _selected == null ? System.Windows.Media.Brushes.Gray : System.Windows.Media.Brushes.Black;
    }

    private void ChannelList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_selected != null) OpenPreview(_selected);
    }

    // ---------- Vista previa ----------

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null)
        {
            Info(this, "Msg_SelectChannel");
            return;
        }
        OpenPreview(_selected);
    }

    private void OpenExternal_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null)
        {
            Info(this, "Msg_SelectChannel");
            return;
        }
        if (!ConfirmPreviewWhileRecording()) return;

        StopEmbeddedPreview();
        try
        {
            var player = _external.Start(_selected, _settings);
            SetStatus(Loc.Get("Status_ExternalOpened", _selected.Name, player));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Loc.Get("App_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StopPreview_Click(object sender, RoutedEventArgs e)
    {
        StopEmbeddedPreview();
        _external.Stop();
        SetStatus(Loc.Get("Status_PreviewStopped"));
    }

    private void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _preview?.SetVolume((int)e.NewValue);
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        _preview?.SetMute(MuteButton.IsChecked == true);
    }

    private bool ConfirmPreviewWhileRecording()
    {
        if (!_scheduler.AnyActive) return true;
        var ok = MessageBox.Show(this, Loc.Get("Msg_PreviewWhileRecording"), Loc.Get("App_Title"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return ok == MessageBoxResult.Yes;
    }

    private void OpenPreview(Channel channel)
    {
        if (_preview == null) return;
        if (!ConfirmPreviewWhileRecording()) return;

        _external.Stop();
        try
        {
            _preview.SetUserAgent(_settings.UserAgent);
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
            VideoView.Visibility = Visibility.Visible;
            _previewUrl = channel.Url;
            _preview.Play(channel, (int)VolumeSlider.Value, MuteButton.IsChecked == true);
            PreviewChannelText.Text = channel.Name;
            PreviewChannelText.Foreground = System.Windows.Media.Brushes.Black;
            SetStatus(Loc.Get("Status_PreviewStarted", channel.Name));
        }
        catch (Exception ex)
        {
            StopEmbeddedPreview();
            MessageBox.Show(this, Loc.Get("Msg_PreviewFailed", ex.Message), Loc.Get("App_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Ver una grabación en curso: el flujo llega del puerto local que alimenta
    /// ffmpeg, así que no se abre ninguna conexión adicional al proveedor.</summary>
    private void WatchRecording_Click(object sender, RoutedEventArgs e)
    {
        if (RecordingsGrid.SelectedItem is not Recording r || r.Status != RecordingStatus.Recording)
        {
            Info(this, "Msg_SelectActiveRecording");
            return;
        }
        if (r.LiveUrl.Length == 0)
        {
            Info(this, "Msg_LiveUnavailable");
            return;
        }
        WatchLive(r);
    }

    private void WatchLive(Recording r)
    {
        if (_preview == null || r.LiveUrl.Length == 0) return;
        _external.Stop();
        try
        {
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
            VideoView.Visibility = Visibility.Visible;
            _previewUrl = r.LiveUrl;
            _preview.Play(r.LiveUrl, (int)VolumeSlider.Value, MuteButton.IsChecked == true);
            PreviewChannelText.Text = Loc.Get("Preview_Live", r.ChannelName);
            PreviewChannelText.Foreground = System.Windows.Media.Brushes.Black;
            SetStatus(Loc.Get("Status_WatchingRecording", r.Title));
        }
        catch (Exception ex)
        {
            StopEmbeddedPreview();
            MessageBox.Show(this, Loc.Get("Msg_PreviewFailed", ex.Message), Loc.Get("App_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>ffmpeg tarda unos segundos en emitir los primeros paquetes, así que el
    /// cambio a la grabación se hace con un pequeño retardo.</summary>
    private void ScheduleLiveSwitch(Recording r)
    {
        SetStatus(Loc.Get("Status_SwitchingToRecording", r.Title));
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (r.Status == RecordingStatus.Recording && r.LiveUrl.Length > 0) WatchLive(r);
        };
        timer.Start();
    }

    private void StopEmbeddedPreview()
    {
        _previewUrl = "";
        _preview?.Stop();
        VideoView.Visibility = Visibility.Collapsed;
        PreviewPlaceholder.Visibility = Visibility.Visible;
        PreviewChannelText.Text = Loc.Get("Preview_None");
        PreviewChannelText.Foreground = System.Windows.Media.Brushes.Gray;
    }

    // ---------- Grabaciones ----------

    private Recording? BuildRecording(bool now)
    {
        if (_selected == null)
        {
            Info(this, "Msg_SelectChannel");
            return null;
        }

        if (!int.TryParse(DurationBox.Text.Trim(), out var minutes) || minutes < 1 || minutes > 24 * 60)
        {
            Info(this, "Msg_BadDuration");
            return null;
        }

        DateTime start;
        if (now)
        {
            start = DateTime.Now;
        }
        else
        {
            if (DateBox.SelectedDate == null || !TimeSpan.TryParse(TimeBox.Text.Trim(), out var time))
            {
                Info(this, "Msg_BadDateTime");
                return null;
            }
            start = DateBox.SelectedDate.Value.Date + time;
            if (start.AddMinutes(minutes) <= DateTime.Now)
            {
                Info(this, "Msg_TimePassed");
                return null;
            }
        }

        if (!ConfirmDiskSpace(minutes)) return null;

        var title = TitleBox.Text.Trim();
        return new Recording
        {
            Title = title.Length > 0 ? title : _selected.Name,
            ChannelName = _selected.Name,
            ChannelUrl = _selected.Url,
            Start = start,
            DurationMinutes = minutes,
            Status = RecordingStatus.Pending,
        };
    }

    /// <summary>Avisa antes de programar si el disco puede quedarse corto, que es el
    /// momento en que aún se puede hacer sitio.</summary>
    private bool ConfirmDiskSpace(int minutes)
    {
        var free = DiskSpace.Free(_settings.OutputFolder);
        if (free is not long bytes) return true;

        var tail = Math.Clamp(_settings.TailMinutes, 0, RecordingScheduler.MaxTailMinutes);
        var needed = DiskSpace.Estimate(minutes + tail, _settings.ConvertToMp4);
        if (bytes >= needed) return true;

        return MessageBox.Show(this,
            Loc.Get("Msg_LowDiskSpace", DiskSpace.Format(bytes), DiskSpace.Format(needed)),
            Loc.Get("App_Title"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private void Schedule_Click(object sender, RoutedEventArgs e)
    {
        var r = BuildRecording(now: false);
        if (r == null) return;
        InsertSorted(r);
        Store.SaveRecordings(_recordings);
        var stopAt = RecordingScheduler.EndWithTail(r, _settings);
        SetStatus(stopAt > r.End
            ? Loc.Get("Status_ScheduledTail", r.Title, r.Start.ToString("d"), r.Start.ToString("HH:mm"), stopAt.ToString("HH:mm"))
            : Loc.Get("Status_Scheduled", r.Title, r.Start.ToString("d"), r.Start.ToString("HH:mm")));
        TitleBox.Clear();
        _scheduler.Tick();
    }

    private void RecordNow_Click(object sender, RoutedEventArgs e)
    {
        var r = BuildRecording(now: true);
        if (r == null) return;
        InsertSorted(r);
        Store.SaveRecordings(_recordings);
        TitleBox.Clear();
        _scheduler.Start(r);
    }

    private void InsertSorted(Recording r)
    {
        var idx = 0;
        while (idx < _recordings.Count && _recordings[idx].Start <= r.Start) idx++;
        _recordings.Insert(idx, r);
        RecordingsGrid.SelectedItem = r;
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (RecordingsGrid.SelectedItem is not Recording r) return;
        if (r.Status == RecordingStatus.Recording)
        {
            _scheduler.Stop(r);
            SetStatus(Loc.Get("Status_Stopping"));
        }
        else if (r.Status == RecordingStatus.Pending)
        {
            r.Status = RecordingStatus.Cancelled;
            r.LastLog = Loc.Get("Log_CancelledByUser");
            Store.SaveRecordings(_recordings);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (RecordingsGrid.SelectedItem is not Recording r) return;
        if (r.IsActive)
        {
            Info(this, "Msg_StopBeforeDelete");
            return;
        }
        _recordings.Remove(r);
        Store.SaveRecordings(_recordings);
    }

    private void ClearFinished_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _recordings.Where(r => r.Status is RecordingStatus.Completed or RecordingStatus.Failed or RecordingStatus.Cancelled).ToList())
            _recordings.Remove(r);
        Store.SaveRecordings(_recordings);
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (RecordingsGrid.SelectedItem is not Recording r || r.OutputFile.Length == 0 || !File.Exists(r.OutputFile)) return;
        Process.Start(new ProcessStartInfo(r.OutputFile) { UseShellExecute = true });
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_settings.OutputFolder);
            if (RecordingsGrid.SelectedItem is Recording r && File.Exists(r.OutputFile))
                Process.Start("explorer.exe", $"/select,\"{r.OutputFile}\"");
            else
                Process.Start(new ProcessStartInfo(_settings.OutputFolder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus(Loc.Get("Status_FolderError", ex.Message));
        }
    }

    // ---------- Ajustes ----------

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow(_settings) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            var languageChanged = Loc.Resolve(dlg.Result.Language) != Loc.Current;
            _settings = dlg.Result;
            _settings.M3uUrl = M3uUrlBox.Text.Trim();
            Store.SaveSettings(_settings);
            Autostart.Apply(_settings.StartWithWindows);
            if (languageChanged) ApplyLanguage(_settings.Language);
            SetStatus(Loc.Get("Status_SettingsSaved"));
        }
    }

    /// <summary>Cambia el idioma en caliente. Los textos ligados por DynamicResource se actualizan solos;
    /// aquí se refrescan los que se asignan desde código.</summary>
    private void ApplyLanguage(string code)
    {
        Loc.Apply(code);
        UpdateSelectedChannelText();
        if (!(_preview?.IsPlaying ?? false)) PreviewChannelText.Text = Loc.Get("Preview_None");
        if (_channels.Count > 0)
        {
            ChannelCountText.Text = Loc.Get("Channels_Count", _channels.Count);
            RebuildGroupFilter();
        }
        ApplyColumnHeaders();
        RecordingsGrid.Items.Refresh();
        if (_tray != null) _tray.Text = Loc.Get("App_Title");
        if (_trayOpen != null) _trayOpen.Text = Loc.Get("Tray_Open");
        if (_trayExit != null) _trayExit.Text = Loc.Get("Tray_Exit");
    }

    /// <summary>Las columnas del DataGrid no están en el árbol visual, así que DynamicResource
    /// no las actualiza al cambiar de idioma. Se asignan desde código.</summary>
    private void ApplyColumnHeaders()
    {
        var keys = new[] { "Col_Title", "Col_Channel", "Col_Start", "Col_Min", "Col_Status", "Col_Detail" };
        for (var i = 0; i < keys.Length && i < RecordingsGrid.Columns.Count; i++)
            RecordingsGrid.Columns[i].Header = Loc.Get(keys[i]);
    }

    private void SetStatus(string msg)
    {
        StatusText.Text = $"{DateTime.Now:HH:mm:ss}  {msg}";
    }

    // ---------- Bandeja del sistema ----------

    private void SetupTray()
    {
        System.Drawing.Icon? icon = null;
        try { icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? ""); } catch { }

        _tray = new WinForms.NotifyIcon
        {
            Icon = icon ?? System.Drawing.SystemIcons.Application,
            Text = Loc.Get("App_Title"),
            Visible = true,
        };
        var menu = new WinForms.ContextMenuStrip();
        _trayOpen = new WinForms.ToolStripMenuItem(Loc.Get("Tray_Open"), null, (_, _) => ShowFromTray());
        _trayExit = new WinForms.ToolStripMenuItem(Loc.Get("Tray_Exit"), null, (_, _) => ExitApp());
        menu.Items.Add(_trayOpen);
        menu.Items.Add(_trayExit);
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowFromTray();
        _tray.BalloonTipClicked += (_, _) => OpenNotified();
    }

    /// <summary>Aviso en la bandeja al acabar una grabación, para no descubrir un fallo
    /// al día siguiente. Al pulsar el aviso se abre la carpeta con el archivo.</summary>
    private void NotifyFinished(Recording r)
    {
        if (!_settings.Notifications || _tray == null) return;

        _lastNotified = r;
        var ok = r.Status == RecordingStatus.Completed;
        _tray.ShowBalloonTip(
            8000,
            Loc.Get(ok ? "Notify_Done" : "Notify_Failed"),
            $"{r.Title}\n{r.LastLog}",
            ok ? WinForms.ToolTipIcon.Info : WinForms.ToolTipIcon.Error);
    }

    private void OpenNotified()
    {
        var r = _lastNotified;
        if (r != null && r.Status == RecordingStatus.Completed && r.OutputFile.Length > 0 && File.Exists(r.OutputFile))
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{r.OutputFile}\"");
                return;
            }
            catch { }
        }
        ShowFromTray();
        if (r != null) RecordingsGrid.SelectedItem = r;
    }

    private void HideToTray()
    {
        StopEmbeddedPreview();
        Hide();
        _tray?.ShowBalloonTip(2000, Loc.Get("App_Title"), Loc.Get("Tray_Balloon"), WinForms.ToolTipIcon.Info);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _settings.MinimizeToTray) HideToTray();
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (_exiting) return;

        if (_settings.MinimizeToTray)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        if (!ConfirmExit()) { e.Cancel = true; return; }
        Shutdown();
    }

    private bool ConfirmExit()
    {
        if (!_scheduler.AnyActive && !_recordings.Any(r => r.Status == RecordingStatus.Pending)) return true;
        var msg = Loc.Get(_scheduler.AnyActive ? "Msg_ExitRecording" : "Msg_ExitPending");
        return MessageBox.Show(this, msg, Loc.Get("App_Title"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private void ExitApp()
    {
        ShowFromTray();
        if (!ConfirmExit()) return;
        Shutdown();
    }

    private void Shutdown()
    {
        _exiting = true;
        _external.Stop();
        _preview?.Dispose();
        _scheduler.Dispose();
        Store.SaveRecordings(_recordings);
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        Application.Current.Shutdown();
    }
}
