using System.Windows;
using System.Windows.Controls;
using CoreRulesModern.Models;

namespace CoreRulesModern.Views;

public partial class CharacterPrintOptionsWindow : Window
{
    public CharacterPrintOptions? Options { get; private set; }

    public CharacterPrintOptionsWindow(
        IReadOnlyList<string> sectionHeadings,
        CharacterPrintOptions initial)
    {
        InitializeComponent();
        SelectByValue(PaperSizeBox, initial.PaperSize);
        SelectByValue(OrientationBox, initial.Landscape ? "Landscape" : "Portrait");
        SelectByValue(MarginBox, initial.MarginMm.ToString());
        SelectByValue(SectionsPerPageBox, initial.SectionsPerPage.ToString());
        KeepTogetherBox.IsChecked = initial.KeepSectionsTogether;
        PrintBackgroundsBox.IsChecked = initial.PrintBackgrounds;

        var selectedBreaks = initial.BreakAfterSections.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var heading in sectionHeadings.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            SectionBreaksPanel.Children.Add(new CheckBox
            {
                Content = heading,
                Tag = heading,
                IsChecked = selectedBreaks.Contains(heading),
                Margin = new Thickness(2, 3, 2, 3)
            });
        }
    }

    private static void SelectByValue(ComboBox comboBox, string value)
    {
        foreach (ComboBoxItem item in comboBox.Items)
        {
            var candidate = Convert.ToString(item.Tag ?? item.Content);
            if (!string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase)) continue;
            comboBox.SelectedItem = item;
            return;
        }
        comboBox.SelectedIndex = 0;
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        var breaks = SectionBreaksPanel.Children.OfType<CheckBox>()
            .Where(checkBox => checkBox.IsChecked == true)
            .Select(checkBox => Convert.ToString(checkBox.Tag) ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        Options = new CharacterPrintOptions(
            Convert.ToString(PaperSizeBox.SelectedValue) ?? "A4",
            string.Equals(Convert.ToString(OrientationBox.SelectedValue), "Landscape", StringComparison.OrdinalIgnoreCase),
            int.TryParse(Convert.ToString(MarginBox.SelectedValue), out var margin) ? margin : 12,
            int.TryParse(Convert.ToString(SectionsPerPageBox.SelectedValue), out var count) ? count : 0,
            KeepTogetherBox.IsChecked == true,
            PrintBackgroundsBox.IsChecked == true,
            breaks);
        DialogResult = true;
    }
}
