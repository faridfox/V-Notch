using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using VNotch.Fork;
using VNotch.Models;
using VNotch.Services;

namespace VNotch;

// faridfox fork: "Tabs position" and "Tabs size" (Settings > Appearance, right after upstream's "Navigation Tabs" row),
// for Fork/Features/NavTabs/MainWindow.TabsPill.cs. Built at runtime like upstream's own runtime rows, with upstream's
// dropdown style. Behaves like the other settings: a choice previews live, Save/Apply keeps it, Cancel/close reverts.
public partial class SettingsWindow
{
    private ComboBox? _forkTabsPositionCombo;
    private ComboBox? _forkTabsSizeCombo;

    internal ComboBox? ForkTabsPositionCombo => _forkTabsPositionCombo;
    internal ComboBox? ForkTabsSizeCombo => _forkTabsSizeCombo;

    internal void ForkAttachTabsOptions(ForkSettingsService settings)
    {
        if (_forkTabsPositionCombo != null) return;
        if (NavTabsLabel.Parent is not FrameworkElement { Parent: Border anchor } || anchor.Parent is not Panel panel) return;

        var current = settings.Current;
        _forkTabsPositionCombo = new ComboBox();
        foreach (var position in Enum.GetValues<ForkTabsPosition>())
            _forkTabsPositionCombo.Items.Add(new ComboBoxItem { Tag = position });
        _forkTabsPositionCombo.SelectedIndex = (int)current.TabsPosition;
        _forkTabsSizeCombo = new ComboBox();
        foreach (var size in Enum.GetValues<ForkTabsSize>())
            _forkTabsSizeCombo.Items.Add(new ComboBoxItem { Tag = size });
        _forkTabsSizeCombo.SelectedIndex = (int)current.TabsSize;

        var (positionRow, positionTitle, positionHint, positionKeywords) = ForkSettingsRow(_forkTabsPositionCombo);
        var (sizeRow, sizeTitle, sizeHint, sizeKeywords) = ForkSettingsRow(_forkTabsSizeCombo);
        int at = panel.Children.IndexOf(anchor) + 1;
        panel.Children.Insert(at, positionRow);
        panel.Children.Insert(at + 1, sizeRow);

        void Localize()
        {
            positionTitle.Text = ForkText.T("tabs.position");
            positionHint.Text = ForkText.T("tabs.position.hint");
            positionKeywords.Text = ForkText.T("tabs.keywords");
            sizeTitle.Text = ForkText.T("tabs.size");
            sizeHint.Text = ForkText.T("tabs.size.hint");
            sizeKeywords.Text = ForkText.T("tabs.keywords");
            foreach (var item in _forkTabsPositionCombo.Items.OfType<ComboBoxItem>())
                item.Content = ForkText.T("tabs.position." + item.Tag.ToString()!.ToLowerInvariant());
            foreach (var item in _forkTabsSizeCombo.Items.OfType<ComboBoxItem>())
                item.Content = ForkText.T("tabs.size." + item.Tag.ToString()!.ToLowerInvariant());
        }
        void UpdateSizeEnabled() =>
            _forkTabsSizeCombo.IsEnabled = _forkTabsPositionCombo.SelectedIndex != (int)ForkTabsPosition.Top;
        void OnLanguageChanged()
        {
            if (Dispatcher.CheckAccess()) Localize();
            else Dispatcher.BeginInvoke(new Action(Localize));
        }
        void OnChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSizeEnabled();
            if (_forkTabsPositionCombo.SelectedItem is ComboBoxItem { Tag: ForkTabsPosition position } &&
                _forkTabsSizeCombo.SelectedItem is ComboBoxItem { Tag: ForkTabsSize size })
                settings.PreviewTabs(position, size);
        }
        void OnApplied(object? sender, NotchSettings applied) => settings.CommitPreview();

        Localize();
        UpdateSizeEnabled();
        _forkTabsPositionCombo.SelectionChanged += OnChanged;
        _forkTabsSizeCombo.SelectionChanged += OnChanged;
        Loc.LanguageChanged += OnLanguageChanged;
        _settingsAppService.SettingsApplied += OnApplied;
        Closed += (_, _) =>
        {
            Loc.LanguageChanged -= OnLanguageChanged;
            _settingsAppService.SettingsApplied -= OnApplied;
            settings.RevertPreview();
        };
    }

    // A settings row like upstream's: title, hint, the control, and collapsed keywords for upstream's settings search.
    private (Border Row, TextBlock Title, TextBlock Hint, TextBlock Keywords) ForkSettingsRow(FrameworkElement control)
    {
        var title = new TextBlock { Style = (Style)FindResource("SettingTitleText") };
        var hint = new TextBlock
        {
            Style = (Style)FindResource("SettingHintText"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 8),
        };
        var keywords = new TextBlock { Visibility = Visibility.Collapsed };
        var content = new StackPanel();
        content.Children.Add(title);
        content.Children.Add(hint);
        content.Children.Add(control);
        content.Children.Add(keywords);
        var row = new Border { Style = (Style)FindResource("SettingRowBorder"), Child = content };
        return (row, title, hint, keywords);
    }
}
