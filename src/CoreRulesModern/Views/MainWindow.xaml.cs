using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using CoreRulesModern.Models;
using CoreRulesModern.Services;
using Microsoft.Win32;

namespace CoreRulesModern.Views;

public partial class MainWindow : Window
{
    private readonly CoreRulesInstallationValidator _validator = new();
    private readonly CoreRulesInstallationLocator _locator = new();
    private readonly UserSettingsStore _settingsStore = new();
    private readonly ManualCatalogue _manualCatalogue = new();
    private readonly CharacterSheetCatalogue _characterCatalogue = new();
    private readonly SpellDatabaseParser _spellParser = new();
    private readonly PackagedFontLoader _fontLoader = new();
    private readonly List<HtmlDocumentEntry> _books = [];
    private readonly List<HtmlDocumentEntry> _characters = [];
    private readonly List<SpellRecord> _spells = [];
    private readonly List<string> _spellLoadErrors = [];
    private UserSettingsStore.UserSettings _settings = new();
    private HtmlDocumentEntry? _selectedDocument;
    private OnlineResourceEntry? _selectedOnlineResource;
    private int _scale = 125;

    public MainWindow()
    {
        InitializeComponent();
        _fontLoader.Load();
        Loaded += (_, _) => LoadSavedLibrary();
        Closed += (_, _) => _fontLoader.Dispose();
    }

    private void LoadSavedLibrary()
    {
        _settings = _settingsStore.Load();
        _scale = NormaliseScale(_settings.Scale);
        ScaleBox.SelectedIndex = _scale / 25 - 4;
        SpellScaleBox.SelectedIndex = _scale / 25 - 4;

        var libraryPath = _settings.LibraryPath;
        if (string.IsNullOrWhiteSpace(libraryPath) || !_validator.Validate(libraryPath).IsValid)
        {
            libraryPath = _locator.FindCandidates().FirstOrDefault(path => _validator.Validate(path).IsValid);
        }

        LoadLibrary(libraryPath);
        LoadCharacters(_settings.CharacterSheetsPath);
        RefreshNavigation();
    }

    private void SelectLibrary_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select the folder containing WebHelp" };
        if (dialog.ShowDialog(this) != true) return;

        var status = _validator.Validate(dialog.FolderName);
        if (!status.IsValid)
        {
            MessageBox.Show(this, "The selected folder does not contain a WebHelp HTML library.",
                "Select book library", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        LoadLibrary(status.RootPath);
        SaveSettings();
        RefreshNavigation();
    }

    private void SelectCharacters_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select the folder containing HTML character sheets" };
        if (dialog.ShowDialog(this) != true) return;

        LoadCharacters(dialog.FolderName);
        SaveSettings();
        RefreshNavigation();
    }

    private void LoadLibrary(string? root)
    {
        _books.Clear();
        _spells.Clear();
        _spellLoadErrors.Clear();
        if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
        {
            _books.AddRange(_manualCatalogue.Read(root));
            LoadSpellDatabase(Path.Combine(root, "Database", "Spells.dat"), SpellDatabaseKind.Core);
            LoadSpellDatabase(Path.Combine(root, "UserDbas", "SpellsU.dat"), SpellDatabaseKind.User);
            _settings = _settings with { LibraryPath = Path.GetFullPath(root) };
        }
    }

    private void LoadSpellDatabase(string path, SpellDatabaseKind kind)
    {
        if (!File.Exists(path)) return;

        try
        {
            _spells.AddRange(_spellParser.Parse(path, kind).Spells);
        }
        catch (SpellDatabaseFormatException exception)
        {
            _spellLoadErrors.Add($"{Path.GetFileName(path)}: {exception.Message}");
        }
    }

    private void LoadCharacters(string? folder)
    {
        _characters.Clear();
        _characters.AddRange(_characterCatalogue.Read(folder));
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
        {
            _settings = _settings with { CharacterSheetsPath = Path.GetFullPath(folder) };
        }
    }

    private void RefreshNavigation()
    {
        var filter = SearchBox.Text.Trim();
        Populate(CharactersRoot, _characters.Where(item => Matches(item, filter)), "Characters", _characters.Count);
        PopulateBooks(filter);
        PopulateSpells(filter);
        PopulateOnlineResources(filter);
        LibraryPathText.Text = _settings.LibraryPath ?? "Not selected";
        CharacterPathText.Text = _settings.CharacterSheetsPath ?? "Not selected";
        WelcomeSummary.Text = $"{_books.Count:N0} books, {_characters.Count:N0} character sheets and {_spells.Count:N0} spell records are available.";
        FooterStatus.Text = _spellLoadErrors.Count == 0
            ? $"{_books.Count:N0} books · {_characters.Count:N0} characters · {_spells.Count:N0} spells"
            : $"{_books.Count:N0} books · {_characters.Count:N0} characters · {_spells.Count:N0} spells · {_spellLoadErrors.Count} database warning(s)";
    }

    private void PopulateBooks(string filter)
    {
        BooksRoot.Items.Clear();
        var matchingBooks = _books.Where(item => Matches(item, filter)).ToArray();
        AddDocumentGroup(
            BooksRoot,
            "AD&D 2nd Edition",
            matchingBooks.Where(item => item.Collection == HtmlDocumentCollection.AdndSecondEdition),
            filter.Length > 0);
        AddDocumentGroup(
            BooksRoot,
            "Ravenloft",
            matchingBooks.Where(item => item.Collection == HtmlDocumentCollection.Ravenloft),
            filter.Length > 0);
        BooksRoot.Header = $"Books ({_books.Count:N0})";
    }

    private void PopulateSpells(string filter)
    {
        SpellsRoot.Items.Clear();
        var matchingSpells = _spells
            .Where(spell => Matches(spell, filter))
            .OrderBy(spell => spell.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(spell => spell.DatabaseKind)
            .ToArray();

        AddSpellCasterGroup(SpellsRoot, "Wizard", matchingSpells.Where(spell => spell.WizardSpell), filter.Length > 0);
        AddSpellCasterGroup(SpellsRoot, "Priest", matchingSpells.Where(spell => spell.PriestSpell), filter.Length > 0);
        SpellsRoot.Header = $"Spells ({_spells.Count:N0})";
    }

    private void PopulateOnlineResources(string filter)
    {
        OnlineResourcesRoot.Items.Clear();
        var resource = new OnlineResourceEntry("Complete Compendium", "https://www.completecompendium.com/");
        if (filter.Length == 0 || resource.Title.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
        {
            OnlineResourcesRoot.Items.Add(new TreeViewItem
            {
                Header = resource.Title,
                ToolTip = resource.Address,
                Tag = resource
            });
        }
    }

    private static void AddDocumentGroup(
        ItemsControl parent,
        string title,
        IEnumerable<HtmlDocumentEntry> entries,
        bool expand)
    {
        var group = new TreeViewItem { Header = title, IsExpanded = expand };
        foreach (var entry in entries)
        {
            group.Items.Add(new TreeViewItem { Header = entry.Title, ToolTip = entry.StartPage, Tag = entry });
        }

        if (group.Items.Count > 0) parent.Items.Add(group);
    }

    private static void AddSpellCasterGroup(
        ItemsControl parent,
        string title,
        IEnumerable<SpellRecord> spells,
        bool expand)
    {
        var spellsByLevel = spells.GroupBy(spell => spell.Level).OrderBy(group => group.Key).ToArray();
        if (spellsByLevel.Length == 0) return;

        var casterGroup = new TreeViewItem { Header = title, IsExpanded = expand };
        foreach (var levelGroup in spellsByLevel)
        {
            var levelNode = new TreeViewItem
            {
                Header = $"Level {levelGroup.Key} ({levelGroup.Count():N0})",
                IsExpanded = expand
            };

            foreach (var spell in levelGroup)
            {
                var source = spell.DatabaseKind == SpellDatabaseKind.Core ? "Core Rules" : "User database";
                levelNode.Items.Add(new TreeViewItem
                {
                    Header = spell.Name,
                    ToolTip = $"{source} · {title} level {spell.Level}",
                    Tag = spell
                });
            }

            casterGroup.Items.Add(levelNode);
        }

        parent.Items.Add(casterGroup);
    }

    private static void Populate(TreeViewItem root, IEnumerable<HtmlDocumentEntry> entries, string label, int total)
    {
        root.Items.Clear();
        foreach (var entry in entries)
        {
            root.Items.Add(new TreeViewItem { Header = entry.Title, ToolTip = entry.StartPage, Tag = entry });
        }
        root.Header = $"{label} ({total:N0})";
    }

    private static bool Matches(HtmlDocumentEntry entry, string filter) =>
        filter.Length == 0 || entry.Title.Contains(filter, StringComparison.CurrentCultureIgnoreCase);

    private static bool Matches(SpellRecord spell, string filter) =>
        filter.Length == 0 ||
        spell.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
        spell.Schools.Any(school => school.Contains(filter, StringComparison.CurrentCultureIgnoreCase)) ||
        spell.Spheres.Any(sphere => sphere.Contains(filter, StringComparison.CurrentCultureIgnoreCase));

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) RefreshNavigation();
    }

    private void NavigationTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        switch (e.NewValue)
        {
            case TreeViewItem { Tag: HtmlDocumentEntry document }:
                ShowDocument(document);
                break;
            case TreeViewItem { Tag: SpellRecord spell }:
                ShowSpell(spell);
                break;
            case TreeViewItem { Tag: OnlineResourceEntry resource }:
                ShowOnlineResource(resource);
                break;
        }
    }

    private void ShowDocument(HtmlDocumentEntry document)
    {
        _selectedDocument = document;
        _selectedOnlineResource = null;
        WelcomePanel.Visibility = Visibility.Collapsed;
        SpellPanel.Visibility = Visibility.Collapsed;
        OnlinePanel.Visibility = Visibility.Collapsed;
        DocumentPanel.Visibility = Visibility.Visible;
        ScaleBox.IsEnabled = true;
        DocumentTitleText.Text = document.Title;
        DocumentPathText.Text = document.StartPage;

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            DocumentBrowser.Navigate(new Uri(document.StartPage));
            FooterStatus.Text = $"Displaying {document.Title} · read-only";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"This HTML document could not be displayed.\n\n{exception.Message}",
                "Display document", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void ShowSpell(SpellRecord spell)
    {
        _selectedDocument = null;
        _selectedOnlineResource = null;
        WelcomePanel.Visibility = Visibility.Collapsed;
        DocumentPanel.Visibility = Visibility.Collapsed;
        OnlinePanel.Visibility = Visibility.Collapsed;
        SpellPanel.Visibility = Visibility.Visible;
        SpellTitleText.Text = spell.Name;
        SpellSourceText.Text = spell.DatabaseKind == SpellDatabaseKind.Core
            ? "Database\\Spells.dat · original Core Rules record"
            : "UserDbas\\SpellsU.dat · imported or user-created record";
        SpellBrowser.NavigateToString(CreateSpellHtml(spell));
        FooterStatus.Text = $"Displaying {spell.Name} · read-only";
    }

    private async void ShowOnlineResource(OnlineResourceEntry resource)
    {
        _selectedDocument = null;
        _selectedOnlineResource = resource;
        WelcomePanel.Visibility = Visibility.Collapsed;
        SpellPanel.Visibility = Visibility.Collapsed;
        DocumentPanel.Visibility = Visibility.Collapsed;
        OnlinePanel.Visibility = Visibility.Visible;
        OnlineTitleText.Text = resource.Title;
        OnlineAddressText.Text = resource.Address;

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            await OnlineBrowser.EnsureCoreWebView2Async();
            OnlineBrowser.Source = new Uri(resource.Address);
            FooterStatus.Text = $"Displaying {resource.Title} · online resource";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"This online resource could not be displayed.\n\n{exception.Message}",
                "Display online resource", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private string CreateSpellHtml(SpellRecord spell)
    {
        var html = new StringBuilder();
        html.Append("<!doctype html><html><head><meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\">");
        html.Append("<style>");
        html.Append("html,body,body *{font-family:'ITC Korinna','Korinna',Georgia,serif;box-sizing:border-box}");
        html.Append($"body{{zoom:{_scale}%;margin:22px;color:#17212b;background:#fff;font-size:16px;line-height:1.45}}");
        html.Append(".badges{margin:0 0 18px}.badge{display:inline-block;background:#8d2f23;color:#fff;padding:4px 9px;margin:0 6px 6px 0;border-radius:3px}");
        html.Append("table{border-collapse:collapse;width:100%;max-width:980px;margin-bottom:24px}th,td{border-bottom:1px solid #d8d2c6;padding:9px 12px;text-align:left;vertical-align:top}");
        html.Append("th{width:190px;background:#f4f0e7}.description{max-width:980px;white-space:pre-wrap}.muted{color:#657078;font-style:italic}h2{color:#8d2f23;margin-top:22px}");
        html.Append("</style></head><body>");
        html.Append("<div class=\"badges\">");
        if (spell.WizardSpell) AppendBadge(html, "Wizard");
        if (spell.PriestSpell) AppendBadge(html, "Priest");
        AppendBadge(html, $"Level {spell.Level}");
        AppendBadge(html, spell.DatabaseKind == SpellDatabaseKind.Core ? "Core Rules" : "User database");
        html.Append("</div><table>");
        AppendRow(html, "Schools", Join(spell.Schools));
        AppendRow(html, "Spheres", Join(spell.Spheres));
        AppendRow(html, "Range", spell.Range);
        AppendRow(html, "Duration", spell.Duration);
        AppendRow(html, "Area of effect", spell.AreaOfEffect);
        AppendRow(html, "Casting time", spell.CastingTime);
        AppendRow(html, "Components", spell.Components);
        AppendRow(html, "Saving throw", spell.SavingThrow);
        AppendRow(html, "Critical", spell.Critical);
        AppendRow(html, "Knockdown", spell.Knockdown);
        AppendRow(html, "Sensory", spell.Sensory);
        AppendRow(html, "Subtlety", spell.Subtlety);
        AppendRow(html, "Reversible", spell.Reversible ? "Yes" : "No");
        AppendRow(html, "Never Ban Cantrip", spell.NeverBanCantrip ? "Yes" : "No");
        if (spell.HelpTopicId > 0) AppendRow(html, "Help topic", spell.HelpTopicId.ToString());
        html.Append("</table><h2>Description</h2>");
        if (string.IsNullOrWhiteSpace(spell.Description))
        {
            html.Append("<p class=\"muted\">No description is stored in this database record.</p>");
        }
        else
        {
            html.Append("<div class=\"description\">").Append(Encode(spell.Description)).Append("</div>");
        }

        html.Append("</body></html>");
        return html.ToString();
    }

    private static void AppendBadge(StringBuilder html, string value) =>
        html.Append("<span class=\"badge\">").Append(Encode(value)).Append("</span>");

    private static void AppendRow(StringBuilder html, string label, string? value)
    {
        html.Append("<tr><th>").Append(Encode(label)).Append("</th><td>")
            .Append(Encode(string.IsNullOrWhiteSpace(value) ? "—" : value))
            .Append("</td></tr>");
    }

    private static string Join(IReadOnlyList<string> values) => values.Count == 0 ? "—" : string.Join(", ", values);

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private void DocumentBrowser_LoadCompleted(object sender, NavigationEventArgs e) => ApplyDisplayStyle();

    private void ScaleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: ComboBoxItem { Tag: string value } } && int.TryParse(value, out var scale))
        {
            _scale = scale;
            var selectedIndex = _scale / 25 - 4;
            if (ScaleBox.SelectedIndex != selectedIndex) ScaleBox.SelectedIndex = selectedIndex;
            if (SpellScaleBox.SelectedIndex != selectedIndex) SpellScaleBox.SelectedIndex = selectedIndex;
            if (IsLoaded)
            {
                SaveSettings();
                ApplyDisplayStyle();
                if (SpellPanel.Visibility == Visibility.Visible &&
                    NavigationTree.SelectedItem is TreeViewItem { Tag: SpellRecord spell })
                {
                    SpellBrowser.NavigateToString(CreateSpellHtml(spell));
                }
            }
        }
    }

    private void ApplyDisplayStyle()
    {
        try
        {
            dynamic document = DocumentBrowser.Document;
            if (document is null) return;
            dynamic oldStyle = document.getElementById("core-rules-library-style");
            if (oldStyle is not null) oldStyle.parentNode.removeChild(oldStyle);
            dynamic style = document.createElement("style");
            style.id = "core-rules-library-style";
            style.type = "text/css";
            style.styleSheet.cssText = CreateDocumentFontCss() +
                $"body{{zoom:{_scale}%;margin:18px;}}" +
                "a{color:#7b241c;} img{max-width:100%;height:auto;}";
            document.getElementsByTagName("head").item(0).appendChild(style);
        }
        catch
        {
            // Legacy help remains readable if a particular page rejects injected styling.
        }
    }

    private string CreateDocumentFontCss()
    {
        if (_selectedDocument?.Kind != HtmlDocumentKind.Book)
        {
            return "html,body,body *{font-family:'ITC Korinna','Korinna',Georgia,serif !important;}";
        }

        const string headings =
            "h1,h1 *,h2,h2 *,h3,h3 *,h4,h4 *,h5,h5 *,h6,h6 *," +
            "font[size='4'],font[size='4'] *,font[size='5'],font[size='5'] *," +
            "font[size='6'],font[size='6'] *,font[size='7'],font[size='7'] *";

        return _selectedDocument.Collection == HtmlDocumentCollection.Ravenloft
            ? "html,body,body *{font-family:'ITC Korinna','Korinna',Georgia,serif !important;}" +
              $"{headings}{{font-family:'Honda','ITC Honda','ITC Korinna',serif !important;font-weight:normal !important;}}"
            : "html,body,body *{font-family:'Book Antiqua',Palatino,Georgia,serif !important;}" +
              $"{headings}{{font-family:'University Roman Std','University Roman',serif !important;font-weight:bold !important;}}";
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (DocumentBrowser.CanGoBack) DocumentBrowser.GoBack();
    }

    private void Forward_Click(object sender, RoutedEventArgs e)
    {
        if (DocumentBrowser.CanGoForward) DocumentBrowser.GoForward();
    }

    private void StartPage_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDocument is not null) DocumentBrowser.Navigate(new Uri(_selectedDocument.StartPage));
    }

    private void OpenDocument_Click(object sender, RoutedEventArgs e)
    {
        var address = _selectedDocument?.StartPage;
        if (address is null) return;
        try
        {
            Process.Start(new ProcessStartInfo(address) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Windows could not open this document.\n\n{exception.Message}",
                "Open document", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnlineBack_Click(object sender, RoutedEventArgs e)
    {
        if (OnlineBrowser.CanGoBack) OnlineBrowser.GoBack();
    }

    private void OnlineForward_Click(object sender, RoutedEventArgs e)
    {
        if (OnlineBrowser.CanGoForward) OnlineBrowser.GoForward();
    }

    private void OnlineStartPage_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedOnlineResource is not null) OnlineBrowser.Source = new Uri(_selectedOnlineResource.Address);
    }

    private void OnlineOpen_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedOnlineResource is null) return;
        try
        {
            Process.Start(new ProcessStartInfo(_selectedOnlineResource.Address) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Windows could not open this website.\n\n{exception.Message}",
                "Open online resource", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveSettings()
    {
        _settings = _settings with { Scale = _scale };
        _settingsStore.Save(_settings);
    }

    private static int NormaliseScale(int scale) => scale is >= 100 and <= 200 && scale % 25 == 0 ? scale : 125;
}
