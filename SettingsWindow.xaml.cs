using System.IO;
using System.Windows;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace IptvRecorder;

public partial class SettingsWindow : Window
{
    public AppSettings Result { get; private set; }

    private sealed record LanguageOption(string Code, string Name)
    {
        // Nombre que exponen los lectores de pantalla y la automatización de la interfaz.
        public override string ToString() => Name;
    }

    public SettingsWindow(AppSettings current)
    {
        InitializeComponent();
        Result = current;

        var languages = new List<LanguageOption> { new("", Loc.Get("Lang_Auto")) };
        languages.AddRange(Loc.Available.Select(l => new LanguageOption(l.Code, l.Name)));
        LanguageBox.ItemsSource = languages;
        LanguageBox.SelectedValue = languages.Any(l => l.Code == current.Language) ? current.Language : "";

        FfmpegBox.Text = current.FfmpegPath;
        PlayerBox.Text = current.PlayerPath;
        OutputBox.Text = current.OutputFolder;
        UserAgentBox.Text = current.UserAgent;
        LeadBox.Text = current.LeadSeconds.ToString();
        ConvertBox.IsChecked = current.ConvertToMp4;
        TrayBox.IsChecked = current.MinimizeToTray;
        AutostartBox.IsChecked = current.StartWithWindows;
        PreventSleepBox.IsChecked = current.PreventSleep;

        // Una ruta guardada que ya no existe (por ejemplo tras actualizar ffmpeg) se vacía
        // para volver a la detección automática en lugar de bloquear el guardado.
        if (current.FfmpegPath.Length > 0 && !File.Exists(current.FfmpegPath))
            FfmpegBox.Text = "";

        if (FfmpegBox.Text.Length == 0)
        {
            var found = RecordingScheduler.ResolveFfmpeg("");
            if (found != null) FfmpegBox.ToolTip = Loc.Get("Set_Detected", found);
        }

        if (current.PlayerPath.Length > 0 && !File.Exists(current.PlayerPath))
            PlayerBox.Text = "";

        if (PlayerBox.Text.Length == 0)
        {
            var player = PreviewPlayer.ResolvePlayer("", current.FfmpegPath);
            PlayerBox.ToolTip = player != null ? Loc.Get("Set_Detected", player) : Loc.Get("Set_PlayerNotFound");
        }
    }

    private void Warn(string key)
        => MessageBox.Show(this, Loc.Get(key), Loc.Get("Settings_Title"), MessageBoxButton.OK, MessageBoxImage.Warning);

    private void BrowseFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = Loc.Get("Filter_Ffmpeg"), Title = Loc.Get("Dlg_SelectFfmpeg") };
        if (dlg.ShowDialog(this) == true) FfmpegBox.Text = dlg.FileName;
    }

    private void BrowsePlayer_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = Loc.Get("Filter_Player"), Title = Loc.Get("Dlg_SelectPlayer") };
        if (dlg.ShowDialog(this) == true) PlayerBox.Text = dlg.FileName;
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new WinForms.FolderBrowserDialog
        {
            Description = Loc.Get("Dlg_OutputFolder"),
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(OutputBox.Text) ? OutputBox.Text : "",
        };
        if (dlg.ShowDialog() == WinForms.DialogResult.OK) OutputBox.Text = dlg.SelectedPath;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var ffmpeg = FfmpegBox.Text.Trim();
        if (ffmpeg.Length > 0 && !File.Exists(ffmpeg)) { Warn("Msg_FfmpegMissingFile"); return; }
        if (ffmpeg.Length == 0 && RecordingScheduler.ResolveFfmpeg("") == null) { Warn("Msg_FfmpegNotInPath"); return; }

        var player = PlayerBox.Text.Trim();
        if (player.Length > 0 && !File.Exists(player)) { Warn("Msg_PlayerMissingFile"); return; }

        var output = OutputBox.Text.Trim();
        if (output.Length == 0) { Warn("Msg_OutputRequired"); return; }

        if (!int.TryParse(LeadBox.Text.Trim(), out var lead) || lead < 0 || lead > 600) { Warn("Msg_LeadRange"); return; }

        Result = new AppSettings
        {
            M3uUrl = Result.M3uUrl,
            FfmpegPath = ffmpeg,
            PlayerPath = player,
            OutputFolder = output,
            UserAgent = UserAgentBox.Text.Trim().Length > 0 ? UserAgentBox.Text.Trim() : new AppSettings().UserAgent,
            LeadSeconds = lead,
            ConvertToMp4 = ConvertBox.IsChecked == true,
            MinimizeToTray = TrayBox.IsChecked == true,
            StartWithWindows = AutostartBox.IsChecked == true,
            PreventSleep = PreventSleepBox.IsChecked == true,
            Language = LanguageBox.SelectedValue as string ?? "",
        };
        DialogResult = true;
    }
}
