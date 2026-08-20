using System.Windows;
using CoreRulesModern.Services;

namespace CoreRulesModern.Views;

public partial class SettingsWindow : Window
{
    public int DocumentScale { get; private set; }
    public int SpellScale { get; private set; }
    public int RecentPageLimit { get; private set; }
    public bool ReopenLastPage { get; private set; }
    public bool ClearRecentPages { get; private set; }

    public SettingsWindow(UserSettingsStore.UserSettings settings)
    {
        InitializeComponent();
        DocumentScaleBox.SelectedIndex = SettingsNormaliser.Scale(settings.Scale, 125) / 25 - 4;
        SpellScaleBox.SelectedIndex = SettingsNormaliser.Scale(settings.SpellScale, 175) / 25 - 4;
        RecentLimitBox.SelectedIndex = SettingsNormaliser.RecentPageLimit(settings.RecentPageLimit) switch
        {
            10 => 0,
            20 => 1,
            30 => 2,
            _ => 3
        };
        ReopenLastPageBox.IsChecked = settings.ReopenLastPage;
    }

    private void ClearRecent_Click(object sender, RoutedEventArgs e)
    {
        ClearRecentPages = true;
        ClearRecentStatus.Text = "Recent pages will be cleared when you save.";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DocumentScale = ReadSelectedValue(DocumentScaleBox, 125);
        SpellScale = ReadSelectedValue(SpellScaleBox, 175);
        RecentPageLimit = ReadSelectedValue(RecentLimitBox, 20);
        ReopenLastPage = ReopenLastPageBox.IsChecked == true;
        DialogResult = true;
    }

    private static int ReadSelectedValue(System.Windows.Controls.ComboBox box, int fallback) =>
        int.TryParse(Convert.ToString(box.SelectedValue), out var value) ? value : fallback;

}
