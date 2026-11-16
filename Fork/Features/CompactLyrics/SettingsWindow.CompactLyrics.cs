using System;
using System.Windows;
using System.Windows.Controls;
using VNotch.Fork;
using VNotch.Models;
using VNotch.Services;

namespace VNotch;

// faridfox fork: the "Show lyrics in the collapsed notch" switch, placed right below upstream's "Show Spotify lyrics"
// (Settings > Advanced). Built at runtime like upstream's own runtime rows (SettingsWindow.Privacy.cs), so the
// settings search indexes it. Fork-owned part of upstream's partial SettingsWindow (compile-checked at each sync);
// attached by Fork/Core/ForkUi.cs.
//
// Behaves like the other switches: toggling previews live in the notch, Save/Apply keeps it, Cancel/close reverts.
public partial class SettingsWindow
{
    private CheckBox? _forkCompactLyricsCheck;

    internal CheckBox? ForkCompactLyricsCheck => _forkCompactLyricsCheck;

    internal void ForkAttachCompactLyricsRow(ForkSettingsService settings)
    {
        if (_forkCompactLyricsCheck != null || SpotifyLyricsRow.Parent is not Panel panel) return;

        var check = new CheckBox { IsChecked = settings.ShowLyricsInCompactNotch };
        var hint = new TextBlock
        {
            Style = (Style)FindResource("SettingHintText"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        };
        var keywords = new TextBlock { Visibility = Visibility.Collapsed };
        var content = new StackPanel();
        content.Children.Add(check);
        content.Children.Add(hint);
        content.Children.Add(keywords);
        var row = new Border { Style = (Style)FindResource("SettingRowBorder"), Child = content };
        panel.Children.Insert(panel.Children.IndexOf(SpotifyLyricsRow) + 1, row);
        _forkCompactLyricsCheck = check;

        void Localize()
        {
            check.Content = ForkText.T("compactLyrics.setting");
            hint.Text = ForkText.T("compactLyrics.hint");
            keywords.Text = ForkText.T("compactLyrics.keywords");
        }
        void OnLanguageChanged()
        {
            if (Dispatcher.CheckAccess()) Localize();
            else Dispatcher.BeginInvoke(new Action(Localize));
        }
        void OnToggled(object sender, RoutedEventArgs e) => settings.PreviewShowLyricsInCompactNotch(check.IsChecked == true);
        // Raised by Save and Apply after upstream wrote its settings (possibly off the UI thread; the service is thread-safe).
        void OnApplied(object? sender, NotchSettings applied) => settings.CommitPreview();

        Localize();
        check.Checked += OnToggled;
        check.Unchecked += OnToggled;
        Loc.LanguageChanged += OnLanguageChanged;
        _settingsAppService.SettingsApplied += OnApplied;
        Closed += (_, _) =>
        {
            Loc.LanguageChanged -= OnLanguageChanged;
            _settingsAppService.SettingsApplied -= OnApplied;
            settings.RevertPreview();
        };
    }
}
