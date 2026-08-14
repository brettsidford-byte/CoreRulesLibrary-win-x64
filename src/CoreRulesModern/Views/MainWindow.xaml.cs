using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CoreRulesModern.Models;
using CoreRulesModern.Services;
using Microsoft.Web.WebView2.Core;
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
    private readonly SpellHelpTopicCatalogue _spellHelpTopics = new();
    private readonly PackagedFontLoader _fontLoader = new();
    private readonly List<HtmlDocumentEntry> _books = [];
    private readonly List<HtmlDocumentEntry> _characters = [];
    private readonly List<SpellRecord> _spells = [];
    private readonly List<string> _spellLoadErrors = [];
    private UserSettingsStore.UserSettings _settings = new();
    private HtmlDocumentEntry? _selectedDocument;
    private OnlineResourceEntry? _selectedOnlineResource;
    private IReadOnlyList<string> _documentPages = [];
    private int _documentPageIndex = -1;
    private int _scale = 125;
    private int _spellScale = 175;

    private bool UseLegacyDocumentBrowser =>
        _selectedDocument?.Kind == HtmlDocumentKind.Book &&
        _selectedDocument.Collection == HtmlDocumentCollection.AdndSecondEdition;

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
        _scale = NormaliseScale(_settings.Scale, 125);
        _spellScale = NormaliseScale(_settings.SpellScale, 175);
        ScaleBox.SelectedIndex = _scale / 25 - 4;
        SpellScaleBox.SelectedIndex = _spellScale / 25 - 4;

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
            _spellHelpTopics.Load(root);
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
            var levelLabel = title == "Priest" && levelGroup.Key == 0
                ? "Quest Spells"
                : $"Level {levelGroup.Key}";
            var levelNode = new TreeViewItem
            {
                Header = $"{levelLabel} ({levelGroup.Count():N0})",
                IsExpanded = expand
            };

            foreach (var spell in levelGroup)
            {
                var source = spell.DatabaseKind == SpellDatabaseKind.Core ? "Core Rules" : "User database";
                levelNode.Items.Add(new TreeViewItem
                {
                    Header = spell.Name,
                    ToolTip = title == "Priest" && spell.Level == 0
                        ? $"{source} · Priest Quest Spell"
                        : $"{source} · {title} level {spell.Level}",
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

    private async void ShowDocument(HtmlDocumentEntry document)
    {
        _selectedDocument = document;
        _documentPages = FindDocumentPages(document);
        _documentPageIndex = FindDocumentPageIndex(document.StartPage);
        UpdateSequenceNavigationButtons();
        _selectedOnlineResource = null;
        WelcomePanel.Visibility = Visibility.Collapsed;
        SpellPanel.Visibility = Visibility.Collapsed;
        OnlinePanel.Visibility = Visibility.Collapsed;
        DocumentPanel.Visibility = Visibility.Visible;
        DocumentTitleText.Text = document.Title;
        DocumentPathText.Text = document.StartPage;

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            ScaleBox.IsEnabled = true;
            var address = new Uri(Path.GetFullPath(document.StartPage));
            if (UseLegacyDocumentBrowser)
            {
                DocumentBrowser.Visibility = Visibility.Collapsed;
                LegacyDocumentBrowser.Visibility = Visibility.Visible;
                LegacyDocumentBrowser.Navigate(address);
                FooterStatus.Text = $"Displaying {document.Title} · WebView1 · read-only";
            }
            else
            {
                LegacyDocumentBrowser.Visibility = Visibility.Collapsed;
                DocumentBrowser.Visibility = Visibility.Visible;
                await DocumentBrowser.EnsureCoreWebView2Async();
                ApplyDocumentScale();
                DocumentBrowser.Source = address;
                FooterStatus.Text = $"Displaying {document.Title} · WebView2 · read-only";
            }
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

    private async void ShowSpell(SpellRecord spell)
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
        try
        {
            await SpellBrowser.EnsureCoreWebView2Async();
            ApplySpellScale();
            SpellBrowser.NavigateToString(CreateSpellHtml(spell));
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"This spell could not be displayed.\n\n{exception.Message}",
                "Display spell", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
        var helpTopic = string.IsNullOrWhiteSpace(spell.Description) ? _spellHelpTopics.Find(spell) : null;
        var html = new StringBuilder();
        html.Append("<!doctype html><html><head><meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\">");
        if (helpTopic is not null)
        {
            html.Append("<base href=\"").Append(Encode(new Uri(helpTopic.PagePath).AbsoluteUri)).Append("\">");
        }
        html.Append("<style>").Append(CreatePackagedFontCss());
        html.Append("html,body,body *{font-family:'Core Rules Korinna','ITC Korinna','Korinna',Georgia,serif;box-sizing:border-box}");
        html.Append("body{margin:22px;color:#17212b;background:#fff;font-size:16px;line-height:1.45}");
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
            if (helpTopic is null)
            {
                html.Append("<p class=\"muted\">No description is stored in this database record and no matching WebHelp topic was found.</p>");
            }
            else
            {
                html.Append("<div class=\"description\">").Append(helpTopic.DescriptionHtml).Append("</div>");
                html.Append("<p class=\"muted\">Description recovered from ")
                    .Append(Encode(helpTopic.Title)).Append(".</p>");
            }
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
            .Append(Encode(NormaliseSpellValue(value)))
            .Append("</td></tr>");
    }

    private static string Join(IReadOnlyList<string> values) =>
        values.Count == 0 ? "N/A" : NormaliseSpellValue(string.Join(", ", values));

    private static string NormaliseSpellValue(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed is "—" or "â€”" ? "N/A" : trimmed;
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private async void DocumentBrowser_NavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess) return;
        if (DocumentBrowser.Source is { IsFile: true } source)
        {
            var pageIndex = FindDocumentPageIndex(source.LocalPath);
            if (pageIndex >= 0) _documentPageIndex = pageIndex;
        }
        UpdateSequenceNavigationButtons();
        ApplyDocumentScale();
        await ApplyDocumentStyleAsync();
    }

    private void LegacyDocumentBrowser_LoadCompleted(object sender, System.Windows.Navigation.NavigationEventArgs e)
    {
        if (e.Uri is { IsFile: true })
        {
            var pageIndex = FindDocumentPageIndex(e.Uri.LocalPath);
            if (pageIndex >= 0) _documentPageIndex = pageIndex;
        }
        UpdateSequenceNavigationButtons();
        ApplyLegacyParagraphStyle();
        ApplyDocumentScale();
    }

    private void ApplyLegacyParagraphStyle()
    {
        try
        {
            dynamic? document = LegacyDocumentBrowser.Document;
            if (document is null) return;

            dynamic? head = document.getElementsByTagName("head")?.item(0);
            if (head is null) return;

            dynamic style = document.createElement("style");
            style.type = "text/css";
            style.styleSheet.cssText =
                "p{margin-top:0;margin-bottom:0;text-indent:1.5em;}";
            head.appendChild(style);
        }
        catch
        {
            // The original page remains usable if its legacy DOM rejects styling.
        }
    }

    private void ScaleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: ComboBoxItem { Tag: string value } } && int.TryParse(value, out var scale))
        {
            if (ReferenceEquals(sender, SpellScaleBox))
            {
                _spellScale = scale;
            }
            else
            {
                _scale = scale;
            }

            if (IsLoaded)
            {
                SaveSettings();
                if (ReferenceEquals(sender, ScaleBox)) ApplyDocumentScale();
                if (ReferenceEquals(sender, SpellScaleBox)) ApplySpellScale();
            }
        }
    }

    private void ApplyDocumentScale()
    {
        if (UseLegacyDocumentBrowser)
        {
            try
            {
                dynamic document = LegacyDocumentBrowser.Document;
                if (document?.body is not null) document.body.style.zoom = $"{_scale}%";
            }
            catch
            {
                // Some legacy pages are still loading or do not expose a body element.
            }
        }
        else if (DocumentBrowser.CoreWebView2 is not null)
        {
            DocumentBrowser.ZoomFactor = _scale / 100d;
        }
    }

    private void ApplySpellScale()
    {
        if (SpellBrowser.CoreWebView2 is not null) SpellBrowser.ZoomFactor = _spellScale / 100d;
    }

    private async Task ApplyDocumentStyleAsync()
    {
        if (DocumentBrowser.CoreWebView2 is null) return;
        var css = CreatePackagedFontCss() + CreateDocumentFontCss() +
                  CreateDocumentParagraphCss() +
                  CreateCharacterSheetCss() +
                  CreateRulesBoxCss() +
                  CreateResponsiveDocumentCss() +
                  CreateCoverPageCss() +
                  "a{color:#7b241c;}font[color] a{color:inherit;}img{max-width:100%;height:auto;}";
        var encodedCss = JsonSerializer.Serialize(css);
        var characterPrintScript = CreateCharacterPrintScript();
        var script = "(() => {" +
                     "const id='core-rules-library-style';" +
                     "document.getElementById(id)?.remove();" +
                     "const style=document.createElement('style');" +
                     "style.id=id;style.textContent=" + encodedCss + ";" +
                     "(document.head||document.documentElement).appendChild(style);" +
                     characterPrintScript +
                     "const body=document.body;" +
                     "if(body){" +
                     "const text=(body.innerText||'').replace(/[\\s\\u00a0]/g,'');" +
                     "const images=body.querySelectorAll('img');" +
                     "const background=body.hasAttribute('background')||getComputedStyle(body).backgroundImage!=='none';" +
                     "const cover=text.length===0&&(images.length===1||background);" +
                     "body.classList.toggle('core-rules-cover-page',cover);" +
                     "}" +
                     "})()";
        try
        {
            await DocumentBrowser.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch
        {
            // The source document remains readable if it rejects injected styling.
        }
    }

    private static string CreatePackagedFontCss()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
        if (!Directory.Exists(folder)) return string.Empty;

        var css = new StringBuilder();
        foreach (var path in Directory.EnumerateFiles(folder)
                     .Where(path => path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) ||
                                    path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var family = name.Contains("korinna", StringComparison.OrdinalIgnoreCase) ? "Core Rules Korinna" :
                name.Contains("honda", StringComparison.OrdinalIgnoreCase) ? "Core Rules Honda" :
                name.Contains("university", StringComparison.OrdinalIgnoreCase) ? "Core Rules University Roman" :
                name.Contains("antiqua", StringComparison.OrdinalIgnoreCase) ? "Core Rules Book Antiqua" : null;
            if (family is null) continue;

            var format = path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) ? "opentype" : "truetype";
            var mime = path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) ? "font/otf" : "font/ttf";
            css.Append("@font-face{font-family:'").Append(family).Append("';src:url(data:")
                .Append(mime).Append(";base64,").Append(Convert.ToBase64String(File.ReadAllBytes(path)))
                .Append(") format('").Append(format).Append("');font-style:normal;font-weight:normal;}");
        }

        return css.ToString();
    }

    private string CreateDocumentFontCss()
    {
        if (_selectedDocument?.Kind != HtmlDocumentKind.Book)
        {
            return "html,body,body *{font-family:'Core Rules Korinna','ITC Korinna','Korinna',Georgia,serif !important;}";
        }

        // Ravenloft's legacy HTML uses relative FONT sizes while newer books
        // may use semantic headings. Only the upper three levels use Honda;
        // minor headings deliberately inherit Korinna with the body text.
        const string majorHeadings =
            "h1,h1 *,h2,h2 *,h3,h3 *," +
            "font[size='+2'],font[size='+2'] *,font[size='5'],font[size='5'] *," +
            "font[size='+3'],font[size='+3'] *,font[size='6'],font[size='6'] *," +
            "font[size='+4'],font[size='+4'] *,font[size='7'],font[size='7'] *";

        const string allAdndHeadings =
            "h1,h1 *,h2,h2 *,h3,h3 *,h4,h4 *,h5,h5 *,h6,h6 *," +
            "font[size='+1'],font[size='+1'] *,font[size='4'],font[size='4'] *," +
            "font[size='+2'],font[size='+2'] *,font[size='5'],font[size='5'] *," +
            "font[size='+3'],font[size='+3'] *,font[size='6'],font[size='6'] *," +
            "font[size='+4'],font[size='+4'] *,font[size='7'],font[size='7'] *";

        return _selectedDocument.Collection == HtmlDocumentCollection.Ravenloft
            ? "html,body,body *{font-family:'Core Rules Korinna','ITC Korinna','Korinna',Georgia,serif !important;}" +
              $"{majorHeadings}{{font-family:'Core Rules Honda','Honda','ITC Honda','Core Rules Korinna',serif !important;font-weight:normal !important;}}"
            : "html,body,body *{font-family:'Core Rules Book Antiqua','Book Antiqua',Palatino,Georgia,serif !important;}" +
              $"{allAdndHeadings}{{font-family:'Core Rules University Roman','University Roman Std','University Roman',serif !important;font-weight:bold !important;}}";
    }

    private string CreateDocumentParagraphCss()
    {
        if (_selectedDocument?.Kind != HtmlDocumentKind.Book) return string.Empty;

        // Consecutive body paragraphs read as a continuous typeset passage:
        // the first line is indented and no blank line is inserted between
        // paragraphs. Heading and explicit BR boundaries retain a one-line
        // separation and begin without an indent.
        const string headingParagraph =
            "p:has(h1),p:has(h2),p:has(h3),p:has(h4),p:has(h5),p:has(h6)," +
            "p:has(font[size='+1']),p:has(font[size='4'])," +
            "p:has(font[size='+2']),p:has(font[size='5'])," +
            "p:has(font[size='+3']),p:has(font[size='6'])," +
            "p:has(font[size='+4']),p:has(font[size='7'])";
        const string paragraphAfterHeading =
            "h1+p,h2+p,h3+p,h4+p,h5+p,h6+p," +
            "p:has(h1)+p,p:has(h2)+p,p:has(h3)+p,p:has(h4)+p,p:has(h5)+p,p:has(h6)+p," +
            "p:has(font[size='+1'])+p,p:has(font[size='4'])+p," +
            "p:has(font[size='+2'])+p,p:has(font[size='5'])+p," +
            "p:has(font[size='+3'])+p,p:has(font[size='6'])+p," +
            "p:has(font[size='+4'])+p,p:has(font[size='7'])+p";

        return "p{margin-top:0;margin-bottom:0;text-indent:1.5em;}" +
               $"{headingParagraph}{{margin-top:1em;text-indent:0;}}" +
               $"{paragraphAfterHeading},br+p,p:has(>br:last-child)+p{{margin-top:1em;text-indent:0;}}";
    }

    private string CreateCharacterSheetCss()
    {
        if (_selectedDocument?.Kind != HtmlDocumentKind.Character) return string.Empty;

        // The top-level WIDTH=100% tables in the Core Rules 2 export can
        // otherwise exceed the padded body by a few pixels and create a
        // redundant horizontal scrollbar.
        return "html{overflow-x:hidden!important;overflow-y:auto!important;background:#d8cebc;}" +
               "body{margin:0!important;padding:24px 28px 40px!important;box-sizing:border-box!important;" +
               "width:100%!important;max-width:100vw!important;height:auto!important;min-height:0!important;" +
               "overflow-x:clip!important;overflow-y:visible!important;color:#282521!important;" +
               "background:radial-gradient(circle at 12% 8%,rgba(255,255,255,.52),transparent 28%)," +
               "linear-gradient(135deg,#eee7da 0%,#ded3c1 100%)!important;font-size:15px!important;line-height:1.38!important;}" +
               "body>table,body>table[width]{width:calc(100% - 24px)!important;max-width:1440px!important;margin:0 auto 16px!important;" +
               "box-sizing:border-box!important;border-collapse:separate!important;border-spacing:12px 0!important;}" +
               "body>table>tbody>tr>td{padding:0!important;}" +
               "body>table>tbody>tr>td>table[border='1']{border:1px solid #b79a61!important;border-radius:9px!important;" +
               "border-spacing:0!important;background:#fffdf7!important;box-shadow:0 3px 11px rgba(55,40,23,.16)!important;overflow:hidden!important;}" +
               "body>table>tbody>tr>td>table[border='1']>tbody>tr>td{padding:0 12px 10px!important;background:#fffdf7!important;}" +
               "body>table>tbody>tr>td>table[border='1']>tbody>tr>td>font:first-child{display:block!important;margin:0 -12px 10px!important;" +
               "padding:9px 13px 8px!important;color:#f7e9c5!important;background:linear-gradient(90deg,#4c211d,#6b3028)!important;" +
               "border-bottom:2px solid #a88445!important;letter-spacing:.035em!important;font-family:'Core Rules Honda','Honda','ITC Honda','Core Rules Korinna',serif!important;" +
               "font-size:18px!important;font-weight:normal!important;text-shadow:0 1px 1px #24100d!important;}" +
               "body>table>tbody>tr>td>table[border='1']>tbody>tr>td>br:first-of-type{display:none!important;}" +
               "body>table>tbody>tr>td>table[border='1'] table{border-collapse:separate!important;border-spacing:0!important;background:transparent!important;}" +
               "body>table>tbody>tr>td>table[border='1'] table td{padding:6px 8px!important;border-bottom:1px solid rgba(168,132,69,.18)!important;vertical-align:top!important;}" +
               "body>table>tbody>tr>td>table[border='1'] table tr:last-child>td{border-bottom:0!important;}" +
               "body>table>tbody>tr>td>table[border='1'] table tr:nth-child(even)>td{background:rgba(232,224,208,.27)!important;}" +
               "body>table>tbody>tr>td>table[border='1'] font[size='+2']{color:#632b24!important;font-weight:bold!important;}" +
               "a{color:#7b241c!important;text-decoration-color:#b79a61!important;}" +
               "@media(max-width:900px){body{padding:14px 10px 28px!important;}body>table,body>table[width]{width:calc(100% - 10px)!important;border-spacing:5px 0!important;margin-bottom:10px!important;}" +
               "body>table>tbody>tr>td>table[border='1'] table td{padding:5px 6px!important;}}" +
               "@media print{@page{margin:12mm;}html,body{overflow:visible!important;background:#fff!important;}" +
               "body{padding:0!important;width:auto!important;max-width:none!important;-webkit-print-color-adjust:exact!important;print-color-adjust:exact!important;}" +
               "body>table,body>table[width]{width:100%!important;max-width:none!important;margin:0 0 7mm!important;border-spacing:0!important;}" +
               "body>table{break-inside:avoid-page;page-break-inside:avoid;}" +
               "body>table.cr-print-break{break-before:page;page-break-before:always;}" +
               "body>table>tbody>tr>td>table[border='1']{box-shadow:none!important;}" +
               "body>p:last-of-type{break-before:avoid-page;}" +
               "body::-webkit-scrollbar,html::-webkit-scrollbar{display:none!important;}}";
    }

    private string CreateCharacterPrintScript()
    {
        if (_selectedDocument?.Kind != HtmlDocumentKind.Character) return string.Empty;

        // These classes have no screen styling. They mark natural boundaries
        // in Core Rules 2 exports for WebView2's print pagination.
        return """
            const printBreakHeadings=new Set([
              'Combat','Weapons','Racial Abilities','Spells','Inventory',
              'Spells Memorized','Spells Known','Character History'
            ]);
            for(const card of document.querySelectorAll("table[border='1']")){
              if(card.parentElement?.closest("table[border='1']"))continue;
              const heading=card.querySelector(':scope>tbody>tr>td>font:first-child strong')?.textContent?.trim()||'';
              if(printBreakHeadings.has(heading))card.closest('body>table')?.classList.add('cr-print-break');
            }
            """;
    }

    private static string CreateRulesBoxCss() =>
        ".rules-box{" +
        "background-color:#e0e0e0;border:1px solid #777;padding:12px 16px;margin:16px 20px;" +
        "box-sizing:border-box;max-width:calc(100% - 40px);overflow-x:auto;overflow-wrap:anywhere;}" +
        ".rules-box table{max-width:100%;}" +
        ".rules-box img{max-width:100%;height:auto;}";

    private static string CreateResponsiveDocumentCss() =>
        "html,body{max-width:100%;overflow-x:hidden;}" +
        "body{box-sizing:border-box;overflow-wrap:break-word;word-wrap:break-word;}" +
        "p,div,blockquote,li,td,th{max-width:100%;overflow-wrap:break-word;word-wrap:break-word;}" +
        "table{max-width:100%;table-layout:auto;}" +
        "table[width]{width:100% !important;}" +
        "td[width],th[width]{width:auto !important;}" +
        "[nowrap]{white-space:normal !important;}" +
        "pre{max-width:100%;overflow-x:auto;}";

    private static string CreateCoverPageCss() =>
        "body.core-rules-cover-page{" +
        "margin:0 !important;min-height:100vh;background-color:#000 !important;" +
        "background-repeat:no-repeat !important;background-position:top center !important;" +
        "background-size:contain !important;}" +
        "body.core-rules-cover-page img{" +
        "display:block;margin:0 auto !important;max-width:100%;height:auto;" +
        "border:2px solid #000;box-sizing:border-box;}";

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (UseLegacyDocumentBrowser)
        {
            if (LegacyDocumentBrowser.CanGoBack) LegacyDocumentBrowser.GoBack();
        }
        else if (DocumentBrowser.CanGoBack)
        {
            DocumentBrowser.GoBack();
        }
    }

    private void Forward_Click(object sender, RoutedEventArgs e)
    {
        if (UseLegacyDocumentBrowser)
        {
            if (LegacyDocumentBrowser.CanGoForward) LegacyDocumentBrowser.GoForward();
        }
        else if (DocumentBrowser.CanGoForward)
        {
            DocumentBrowser.GoForward();
        }
    }

    private void PreviousPage_Click(object sender, RoutedEventArgs e) => NavigateDocumentSequence(-1);

    private void NextPage_Click(object sender, RoutedEventArgs e) => NavigateDocumentSequence(1);

    private void NavigateDocumentSequence(int offset)
    {
        var nextIndex = _documentPageIndex + offset;
        if (nextIndex < 0 || nextIndex >= _documentPages.Count) return;

        _documentPageIndex = nextIndex;
        NavigateDocument(new Uri(_documentPages[nextIndex]));
        UpdateSequenceNavigationButtons();
    }

    private static IReadOnlyList<string> FindDocumentPages(HtmlDocumentEntry document)
    {
        if (document.Kind != HtmlDocumentKind.Book) return [Path.GetFullPath(document.StartPage)];

        var folder = Path.GetDirectoryName(Path.GetFullPath(document.StartPage));
        if (folder is null || !Directory.Exists(folder)) return [Path.GetFullPath(document.StartPage)];

        return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => Path.GetRelativePath(folder, path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private int FindDocumentPageIndex(string path)
    {
        var fullPath = Path.GetFullPath(path);
        for (var index = 0; index < _documentPages.Count; index++)
        {
            if (_documentPages[index].Equals(fullPath, StringComparison.OrdinalIgnoreCase)) return index;
        }

        return -1;
    }

    private void UpdateSequenceNavigationButtons()
    {
        PreviousPageButton.IsEnabled = _documentPageIndex > 0;
        NextPageButton.IsEnabled = _documentPageIndex >= 0 && _documentPageIndex < _documentPages.Count - 1;
    }

    private void StartPage_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDocument is null) return;
        NavigateDocument(new Uri(Path.GetFullPath(_selectedDocument.StartPage)));
    }

    private void NavigateDocument(Uri address)
    {
        if (UseLegacyDocumentBrowser)
        {
            LegacyDocumentBrowser.Navigate(address);
        }
        else
        {
            DocumentBrowser.Source = address;
        }
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
        _settings = _settings with { Scale = _scale, SpellScale = _spellScale };
        _settingsStore.Save(_settings);
    }

    private static int NormaliseScale(int scale, int fallback) =>
        scale is >= 100 and <= 300 && scale % 25 == 0 ? scale : fallback;
}
