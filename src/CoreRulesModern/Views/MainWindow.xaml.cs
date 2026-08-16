using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoreRulesModern.Models;
using CoreRulesModern.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace CoreRulesModern.Views;

public partial class MainWindow : Window
{
    private enum ContextPanelMode
    {
        None,
        BookContents,
        Spell
    }

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
    private int _legacyFindIndex = -1;
    private string _activeFindText = string.Empty;
    private bool _initialisingFilters;
    private string? _contentsPagePath;
    private string? _coverPagePath;
    private ContextPanelMode _contextPanelMode;
    private SpellRecord? _displayedMainSpell;
    private SpellRecord? _previewedSpell;

    private bool UseLegacyDocumentBrowser =>
        _selectedDocument?.Kind == HtmlDocumentKind.Book &&
        _selectedDocument.Collection == HtmlDocumentCollection.AdndSecondEdition;

    public MainWindow()
    {
        _fontLoader.Load();
        InitializeComponent();
        LoadInterfaceTextures();
        Loaded += (_, _) => LoadSavedLibrary();
        Closed += (_, _) =>
        {
            SaveSettings();
            _fontLoader.Dispose();
        };
    }

    private static void LoadInterfaceTextures()
    {
        TrySetTextureBrush("WoodBrush", "WoodTexture.jpg", Stretch.UniformToFill);
        TrySetTextureBrush("ParchmentBrush", "ParchmentTexture.jpg", Stretch.None, true);
    }

    private static void TrySetTextureBrush(string resourceKey, string fileName, Stretch stretch, bool tile = false)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
            if (!File.Exists(path)) return;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();

            var brush = new ImageBrush(image)
            {
                Stretch = stretch,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };
            if (tile)
            {
                brush.TileMode = TileMode.Tile;
                brush.ViewportUnits = BrushMappingMode.Absolute;
                brush.Viewport = new Rect(0, 0, image.PixelWidth, image.PixelHeight);
            }
            brush.Freeze();
            Application.Current.Resources[resourceKey] = brush;
        }
        catch
        {
            // The solid-colour resource remains in use if a texture cannot load.
        }
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
        InitialiseSpellFilters();
        RefreshNavigation();
        TryRestoreLastPage();
    }

    private void InitialiseSpellFilters()
    {
        _initialisingFilters = true;
        try
        {
            SpellCasterFilterBox.SelectedValue = NormaliseChoice(
                _settings.SpellCasterFilter, ["All", "Wizard", "Priest"], "All");
            SpellLevelFilterBox.SelectedValue = Math.Clamp(_settings.SpellLevelFilter, -1, 9).ToString();
            SpellComponentFilterBox.SelectedValue = NormaliseChoice(
                _settings.SpellComponentFilter, ["All", "V", "S", "M"], "All");
            SpellSourceFilterBox.SelectedValue = NormaliseChoice(
                _settings.SpellSourceFilter, ["All", "Core", "User"], "All");

            var schoolsAndSpheres = _spells
                .SelectMany(spell => spell.Schools.Concat(spell.Spheres))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
                .Prepend("All schools and spheres")
                .ToArray();
            SpellSchoolSphereFilterBox.ItemsSource = schoolsAndSpheres;
            SpellSchoolSphereFilterBox.SelectedItem = schoolsAndSpheres.FirstOrDefault(value =>
                value.Equals(_settings.SpellSchoolSphereFilter, StringComparison.CurrentCultureIgnoreCase))
                ?? schoolsAndSpheres[0];
        }
        finally
        {
            _initialisingFilters = false;
        }
    }

    private void TryRestoreLastPage()
    {
        if (!_settings.ReopenLastPage ||
            string.IsNullOrWhiteSpace(_settings.LastDocumentStartPage) ||
            string.IsNullOrWhiteSpace(_settings.LastPagePath)) return;

        var document = FindSavedDocument(_settings.LastDocumentStartPage, _settings.LastPagePath);
        if (document is null || !File.Exists(_settings.LastPagePath)) return;

        ShowDocument(document, _settings.LastPagePath);
    }

    private static string NormaliseChoice(string? value, IReadOnlyCollection<string> choices, string fallback) =>
        choices.FirstOrDefault(choice => choice.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? fallback;

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
        PopulateSavedPages(BookmarksRoot, _settings.Bookmarks ?? [], true, filter);
        PopulateSavedPages(RecentPagesRoot, _settings.RecentPages ?? [], false, filter);
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
        var caster = Convert.ToString(SpellCasterFilterBox.SelectedValue) ?? "All";
        var level = int.TryParse(Convert.ToString(SpellLevelFilterBox.SelectedValue), out var selectedLevel)
            ? selectedLevel
            : -1;
        var schoolOrSphere = Convert.ToString(SpellSchoolSphereFilterBox.SelectedItem)
                             ?? "All schools and spheres";
        var component = Convert.ToString(SpellComponentFilterBox.SelectedValue) ?? "All";
        var source = Convert.ToString(SpellSourceFilterBox.SelectedValue) ?? "All";
        var matchingSpells = _spells
            .Where(spell => Matches(spell, filter))
            .Where(spell => caster == "All" ||
                            caster == "Wizard" && spell.WizardSpell ||
                            caster == "Priest" && spell.PriestSpell)
            .Where(spell => level < 0 || spell.Level == level)
            .Where(spell => schoolOrSphere == "All schools and spheres" ||
                            spell.Schools.Concat(spell.Spheres).Any(value =>
                                value.Equals(schoolOrSphere, StringComparison.CurrentCultureIgnoreCase)))
            .Where(spell => component == "All" || HasSpellComponent(spell, component))
            .Where(spell => source == "All" ||
                            source == "Core" && spell.DatabaseKind == SpellDatabaseKind.Core ||
                            source == "User" && spell.DatabaseKind == SpellDatabaseKind.User)
            .OrderBy(spell => spell.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(spell => spell.DatabaseKind)
            .ToArray();

        var expand = filter.Length > 0 || caster != "All" || level >= 0 ||
                     schoolOrSphere != "All schools and spheres" || component != "All" || source != "All";
        if (caster != "Priest")
            AddSpellCasterGroup(SpellsRoot, "Wizard", matchingSpells.Where(spell => spell.WizardSpell), expand);
        if (caster != "Wizard")
            AddSpellCasterGroup(SpellsRoot, "Priest", matchingSpells.Where(spell => spell.PriestSpell), expand);
        SpellsRoot.Header = matchingSpells.Length == _spells.Count
            ? $"Spells ({_spells.Count:N0})"
            : $"Spells ({matchingSpells.Length:N0} of {_spells.Count:N0})";
    }

    private static bool HasSpellComponent(SpellRecord spell, string component) =>
        spell.Components.Split([',', ';', ' ', '/', '+'], StringSplitOptions.RemoveEmptyEntries)
            .Any(value => value.Equals(component, StringComparison.OrdinalIgnoreCase));

    private static void PopulateSavedPages(
        TreeViewItem root,
        IEnumerable<SavedPage> pages,
        bool bookmark,
        string filter)
    {
        root.Items.Clear();
        var allPages = pages.ToArray();
        foreach (var page in allPages.Where(page => filter.Length == 0 ||
                     page.PageTitle.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                     page.DocumentTitle.Contains(filter, StringComparison.CurrentCultureIgnoreCase)))
        {
            root.Items.Add(new TreeViewItem
            {
                Header = page.PageTitle,
                ToolTip = page.LocationKind switch
                {
                    SavedLocationKind.Spell => $"Spell · {page.ResourceKey}",
                    SavedLocationKind.Online => $"Complete Compendium\n{page.PagePath}",
                    _ => $"{page.DocumentTitle}\n{page.PagePath}"
                },
                Tag = new SavedPageLink(page, bookmark)
            });
        }
        root.Header = bookmark ? $"Bookmarks ({allPages.Length:N0})" : $"Recent pages ({allPages.Length:N0})";
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

    private void SpellFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _initialisingFilters) return;
        SaveSettings();
        RefreshNavigation();
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
            case TreeViewItem { Tag: SavedPageLink savedPage }:
                OpenSavedPage(savedPage.Page);
                break;
        }
    }

    private void OpenSavedPage(SavedPage savedPage)
    {
        if (savedPage.LocationKind == SavedLocationKind.Spell)
        {
            var spell = _spells.FirstOrDefault(candidate =>
                SpellResourceKey(candidate).Equals(savedPage.ResourceKey, StringComparison.OrdinalIgnoreCase));
            if (spell is null)
            {
                MessageBox.Show(this, "This saved spell is no longer present in the spell databases.",
                    "Open saved spell", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            ShowSpell(spell);
            return;
        }

        if (savedPage.LocationKind == SavedLocationKind.Online)
        {
            if (!Uri.TryCreate(savedPage.PagePath, UriKind.Absolute, out var address)) return;
            ShowOnlineResource(
                new OnlineResourceEntry("Complete Compendium", "https://www.completecompendium.com/"),
                address.AbsoluteUri);
            return;
        }

        var document = FindSavedDocument(savedPage.DocumentStartPage, savedPage.PagePath);
        if (document is null || !File.Exists(savedPage.PagePath))
        {
            MessageBox.Show(this,
                "This saved page is no longer available. The book or character-sheet folder may have moved.",
                "Open saved page", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        ShowDocument(document, savedPage.PagePath);
    }

    private HtmlDocumentEntry? FindSavedDocument(string documentStartPage, string pagePath)
    {
        var documents = _books.Concat(_characters);
        var exact = documents.FirstOrDefault(document => PathsEqual(document.StartPage, documentStartPage));
        if (exact is not null) return exact;

        return documents.FirstOrDefault(document =>
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(document.StartPage));
            return folder is not null && Path.GetFullPath(pagePath).StartsWith(
                folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        });
    }

    private async void ShowDocument(HtmlDocumentEntry document, string? pagePath = null)
    {
        _selectedDocument = document;
        _displayedMainSpell = null;
        _documentPages = FindDocumentPages(document);
        var targetPage = Path.GetFullPath(pagePath ?? document.StartPage);
        _documentPageIndex = FindDocumentPageIndex(targetPage);
        UpdateSequenceNavigationButtons();
        ResetFindState();
        _selectedOnlineResource = null;
        WelcomePanel.Visibility = Visibility.Collapsed;
        SpellPanel.Visibility = Visibility.Collapsed;
        OnlinePanel.Visibility = Visibility.Collapsed;
        DocumentPanel.Visibility = Visibility.Visible;
        DocumentTitleText.Text = document.Title;
        DocumentPathText.Text = targetPage;
        UpdateBookmarkButton(targetPage);
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            if (document.Kind == HtmlDocumentKind.Book)
            {
                await ShowBookContentsAsync(document, targetPage);
            }
            else
            {
                HideBookContents();
            }
            ScaleBox.IsEnabled = true;
            var address = new Uri(targetPage);
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
                HardenWebView(DocumentBrowser.CoreWebView2);
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
        var restoreContextPanelForCompendium =
            _selectedOnlineResource is not null && _settings.BookContentsVisible;
        if (BookContentsPanel.Visibility == Visibility.Visible || restoreContextPanelForCompendium)
        {
            SetBookContentsPanelVisible(true);
            await ShowSpellInContextPanelAsync(spell);
            return;
        }

        await ShowSpellInMainViewerAsync(spell);
    }

    private async Task ShowSpellInMainViewerAsync(SpellRecord spell)
    {
        _selectedDocument = null;
        _selectedOnlineResource = null;
        _displayedMainSpell = spell;
        HideBookContents();
        WelcomePanel.Visibility = Visibility.Collapsed;
        DocumentPanel.Visibility = Visibility.Collapsed;
        OnlinePanel.Visibility = Visibility.Collapsed;
        SpellPanel.Visibility = Visibility.Visible;
        SpellTitleText.Text = spell.Name;
        SpellSourceText.Text = spell.DatabaseKind == SpellDatabaseKind.Core
            ? "Database\\Spells.dat · original Core Rules record"
            : "UserDbas\\SpellsU.dat · imported or user-created record";
        UpdateSpellBookmarkButtons();
        try
        {
            await SpellBrowser.EnsureCoreWebView2Async();
            HardenWebView(SpellBrowser.CoreWebView2);
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

    private async Task ShowSpellInContextPanelAsync(SpellRecord spell)
    {
        _contextPanelMode = ContextPanelMode.Spell;
        _contentsPagePath = null;
        _coverPagePath = null;
        _previewedSpell = spell;
        BookContentsTitleText.Text = $"Spell — {spell.Name}";
        ContentsToggleButton.Visibility = Visibility.Visible;
        ContentsToggleButton.Content = "Hide spell preview";
        ContextBookmarkButton.Visibility = Visibility.Visible;
        BookReferenceSplitGrid.Visibility = Visibility.Collapsed;
        SpellContextBrowser.Visibility = Visibility.Visible;
        try
        {
            await SpellContextBrowser.EnsureCoreWebView2Async();
            HardenWebView(SpellContextBrowser.CoreWebView2);
            SpellContextBrowser.ZoomFactor = 0.9;
            SpellContextBrowser.NavigateToString(CreateSpellHtml(spell));
            UpdateSpellBookmarkButtons();
            FooterStatus.Text = $"Previewing {spell.Name} · main viewer retained";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"This spell could not be previewed.\n\n{exception.Message}",
                "Preview spell", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ShowOnlineResource(OnlineResourceEntry resource, string? pageAddress = null)
    {
        _selectedDocument = null;
        _displayedMainSpell = null;
        _selectedOnlineResource = resource;
        HideBookContents();
        WelcomePanel.Visibility = Visibility.Collapsed;
        SpellPanel.Visibility = Visibility.Collapsed;
        DocumentPanel.Visibility = Visibility.Collapsed;
        OnlinePanel.Visibility = Visibility.Visible;
        OnlineTitleText.Text = resource.Title;
        var targetAddress = pageAddress ?? resource.Address;
        OnlineAddressText.Text = targetAddress;
        OnlineBookmarkButton.IsEnabled = false;

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            await OnlineBrowser.EnsureCoreWebView2Async();
            HardenWebView(OnlineBrowser.CoreWebView2);
            OnlineBrowser.Source = new Uri(targetAddress);
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
        html.Append("body{margin:22px;color:#17212b;background-color:#f5e8c8;background-image:")
            .Append(CreateParchmentBackgroundImage())
            .Append(";background-repeat:repeat;background-size:768px 768px;font-size:16px;line-height:1.45}");
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
                html.Append("<p class=\"muted\">")
                    .Append(Encode(spell.Name)).Append(" — ")
                    .Append(Encode(GetSpellHelpBookName(helpTopic.PagePath))).Append("</p>");
            }
        }
        else
        {
            html.Append("<div class=\"description\">").Append(Encode(spell.Description)).Append("</div>");
        }

        html.Append("</body></html>");
        return html.ToString();
    }

    private static string GetSpellHelpBookName(string pagePath)
    {
        var folder = Path.GetFileName(Path.GetDirectoryName(pagePath)) ?? string.Empty;
        return folder.ToUpperInvariant() switch
        {
            "PHB" => "Player's Handbook",
            "DMG" => "Dungeon Master Guide",
            "MM" => "Monstrous Manual",
            "AEG" => "The Complete Book of Arms and Equipment",
            "CBH" => "The Complete Bard's Handbook",
            "HLC" => "Dungeon Master Option: High-Level Campaigns",
            "CDH" => "The Complete Druid's Handbook",
            "CBD" => "The Complete Book of Dwarves",
            "CBE" => "The Complete Book of Elves",
            "CFH" => "The Complete Fighter's Handbook",
            "CBGH" => "The Complete Book of Gnomes & Halflings",
            "CBN" => "The Complete Book of Necromancers",
            "CPAH" => "The Complete Paladin's Handbook",
            "CT" => "Player's Option: Combat & Tactics",
            "SM" => "Player's Option: Spells & Magic",
            "SP" => "Player's Option: Skills & Powers",
            "CPRH" => "The Complete Priest's Handbook",
            "CRH" => "The Complete Ranger's Handbook",
            "CTH" => "The Complete Thief's Handbook",
            "TM" => "Tome of Magic",
            "CWH" => "The Complete Wizard's Handbook",
            _ => string.IsNullOrWhiteSpace(folder) ? "Core Rules" : folder.Replace('_', ' ')
        };
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
            DocumentPathText.Text = source.LocalPath;
        }
        UpdateSequenceNavigationButtons();
        ApplyDocumentScale();
        await ApplyDocumentStyleAsync();
        if (DocumentBrowser.Source is { IsFile: true } currentSource)
        {
            var title = await ReadWebView2PageTitleAsync();
            TrackCurrentPage(currentSource.LocalPath, title);
            await UpdateBookContentsForPageAsync(currentSource.LocalPath);
        }
    }

    private void DocumentBrowser_NavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs e)
    {
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var address) &&
            BrowserSecurityPolicy.IsLocalPageWithin(address,
                [_settings.LibraryPath, _settings.CharacterSheetsPath])) return;

        e.Cancel = true;
        FooterStatus.Text = "Blocked navigation outside the selected local library.";
    }

    private static void HardenWebView(CoreWebView2? webView)
    {
        if (webView is null) return;
        webView.Settings.AreDevToolsEnabled = false;
        webView.Settings.AreDefaultContextMenusEnabled = false;
        webView.Settings.IsStatusBarEnabled = false;
        webView.Settings.IsPasswordAutosaveEnabled = false;
        webView.Settings.IsGeneralAutofillEnabled = false;
        webView.Settings.AreHostObjectsAllowed = false;
    }

    private async void LegacyDocumentBrowser_LoadCompleted(object sender, System.Windows.Navigation.NavigationEventArgs e)
    {
        if (e.Uri is { IsFile: true })
        {
            var pageIndex = FindDocumentPageIndex(e.Uri.LocalPath);
            if (pageIndex >= 0) _documentPageIndex = pageIndex;
            DocumentPathText.Text = e.Uri.LocalPath;
        }
        UpdateSequenceNavigationButtons();
        ApplyLegacyParagraphStyle(LegacyDocumentBrowser);
        ApplyDocumentScale();
        if (e.Uri is { IsFile: true } currentUri)
        {
            TrackCurrentPage(currentUri.LocalPath, ReadLegacyPageTitle());
            await UpdateBookContentsForPageAsync(currentUri.LocalPath);
        }
    }

    private void LegacyDocumentBrowser_Navigating(
        object sender,
        System.Windows.Navigation.NavigatingCancelEventArgs e)
    {
        if (BrowserSecurityPolicy.IsLocalPageWithin(e.Uri, [_settings.LibraryPath])) return;
        e.Cancel = true;
        FooterStatus.Text = "Blocked navigation outside the selected local library.";
    }

    private async Task<string> ReadWebView2PageTitleAsync()
    {
        try
        {
            var json = await DocumentBrowser.CoreWebView2.ExecuteScriptAsync("document.title||''");
            return JsonSerializer.Deserialize<string>(json) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private string ReadLegacyPageTitle()
    {
        try
        {
            dynamic? document = LegacyDocumentBrowser.Document;
            if (document is null) return string.Empty;
            return Convert.ToString(document.title) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private void ApplyLegacyParagraphStyle(WebBrowser browser)
    {
        try
        {
            dynamic? document = browser.Document;
            if (document is null) return;

            dynamic? head = document.getElementsByTagName("head")?.item(0);
            if (head is null) return;

            dynamic style = document.createElement("style");
            style.type = "text/css";
            style.styleSheet.cssText =
                CreateLegacyFontCss() +
                "html,body{background-color:#f5e8c8!important;background-image:" +
                CreateParchmentBackgroundImage() +
                "!important;background-repeat:repeat!important;}" +
                "html,body,body *{font-family:'Core Rules Book Antiqua','Book Antiqua',Palatino,Georgia,serif!important;}" +
                "h1,h1 *,h2,h2 *,h3,h3 *,h4,h4 *,h5,h5 *,h6,h6 *," +
                "font[size='+1'],font[size='+1'] *,font[size='4'],font[size='4'] *," +
                "font[size='+2'],font[size='+2'] *,font[size='5'],font[size='5'] *," +
                "font[size='+3'],font[size='+3'] *,font[size='6'],font[size='6'] *," +
                "font[size='+4'],font[size='+4'] *,font[size='7'],font[size='7'] *{" +
                "font-family:'Core Rules University Roman','University Roman Std','University Roman',serif!important;}" +
                "p.core-rules-body-paragraph{margin-top:0!important;margin-bottom:0!important;text-indent:1.5em!important;}" +
                "p.core-rules-heading-paragraph{margin-top:1em!important;margin-bottom:0!important;text-indent:0!important;}" +
                "span.core-rules-paragraph-indent{display:inline-block!important;width:1.5em!important;height:0!important;" +
                "margin:0!important;padding:0!important;}";
            head.appendChild(style);

            dynamic paragraphs = document.getElementsByTagName("p");
            var paragraphCount = (int)paragraphs.length;
            for (var index = 0; index < paragraphCount; index++)
            {
                dynamic paragraph = paragraphs.item(index);
                var className = IsLegacyHeadingParagraph(paragraph)
                    ? "core-rules-heading-paragraph"
                    : "core-rules-body-paragraph";
                var existingClass = (Convert.ToString(paragraph.className) ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(existingClass))
                {
                    paragraph.className = className;
                }
                else if (!existingClass.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                             .Contains(className, StringComparer.OrdinalIgnoreCase))
                {
                    paragraph.className = existingClass + " " + className;
                }

                // Core Rules WebHelp exports use an empty <P></P> as a
                // paragraph delimiter, leaving the prose as a following text
                // node. text-indent therefore has no visible target. Preserve
                // the block boundary and place a small inline spacer before
                // the following prose instead.
                var paragraphText = (Convert.ToString(paragraph.innerText) ?? string.Empty).Trim();
                var alreadyStyled = string.Equals(
                    Convert.ToString(paragraph.getAttribute("data-core-rules-styled")),
                    "1",
                    StringComparison.Ordinal);
                if (!alreadyStyled &&
                    className == "core-rules-body-paragraph" &&
                    paragraphText.Length == 0 &&
                    !IsInsideLegacyElement(paragraph, "TABLE"))
                {
                    paragraph.insertAdjacentHTML(
                        "afterEnd",
                        "<span class=\"core-rules-paragraph-indent\" aria-hidden=\"true\">&nbsp;</span>");
                }
                paragraph.setAttribute("data-core-rules-styled", "1");
            }
        }
        catch
        {
            // The original page remains usable if its legacy DOM rejects styling.
        }
    }

    private static bool IsLegacyHeadingParagraph(dynamic paragraph)
    {
        foreach (var tagName in new[] { "h1", "h2", "h3", "h4", "h5", "h6" })
        {
            if ((int)paragraph.getElementsByTagName(tagName).length > 0) return true;
        }

        dynamic? ancestor = paragraph.parentElement;
        while (ancestor is not null)
        {
            var tagName = (Convert.ToString(ancestor.tagName) ?? string.Empty).Trim();
            if (tagName.Equals("H1", StringComparison.OrdinalIgnoreCase) ||
                tagName.Equals("H2", StringComparison.OrdinalIgnoreCase) ||
                tagName.Equals("H3", StringComparison.OrdinalIgnoreCase) ||
                tagName.Equals("H4", StringComparison.OrdinalIgnoreCase) ||
                tagName.Equals("H5", StringComparison.OrdinalIgnoreCase) ||
                tagName.Equals("H6", StringComparison.OrdinalIgnoreCase))
                return true;

            if (tagName.Equals("FONT", StringComparison.OrdinalIgnoreCase))
            {
                object? ancestorSizeValue = ancestor.getAttribute("size");
                if (IsLegacyHeadingFontSize(Convert.ToString(ancestorSizeValue))) return true;
            }

            if (tagName.Equals("BODY", StringComparison.OrdinalIgnoreCase)) break;
            ancestor = ancestor.parentElement;
        }

        dynamic fonts = paragraph.getElementsByTagName("font");
        var fontCount = (int)fonts.length;
        for (var index = 0; index < fontCount; index++)
        {
            object? rawSizeValue = fonts.item(index).getAttribute("size");
            var rawSize = (Convert.ToString(rawSizeValue) ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(rawSize)) continue;

            if (IsLegacyHeadingFontSize(rawSize)) return true;
        }

        return false;
    }

    private static bool IsLegacyHeadingFontSize(string? value)
    {
        var rawSize = value?.Trim() ?? string.Empty;
        if (rawSize.StartsWith('+') && int.TryParse(rawSize[1..], out int relativeSize))
            return relativeSize >= 1;
        return int.TryParse(rawSize, out int absoluteSize) && absoluteSize >= 4;
    }

    private static bool IsInsideLegacyElement(dynamic element, string tagName)
    {
        dynamic? ancestor = element.parentElement;
        while (ancestor is not null)
        {
            var ancestorTag = Convert.ToString(ancestor.tagName);
            if (string.Equals(ancestorTag, tagName, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(ancestorTag, "BODY", StringComparison.OrdinalIgnoreCase)) return false;
            ancestor = ancestor.parentElement;
        }

        return false;
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
        await ApplyDocumentStyleAsync(DocumentBrowser.CoreWebView2);
    }

    private async Task ApplyDocumentStyleAsync(CoreWebView2? webView)
    {
        if (webView is null) return;
        var css = CreatePackagedFontCss() + CreateDocumentFontCss() +
                  CreateDocumentSurfaceCss() +
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
                     "const path=decodeURIComponent(location.pathname).replace(/\\\\/g,'/');" +
                     "const page=path.substring(path.lastIndexOf('/')+1).toLowerCase();" +
                     "const vanRichtenFolder=/\\/van[_ ]richten[_ ]guides(?:_v\\d+)?\\//i.test(path);" +
                     "const vanRichtenCover=vanRichtenFolder&&(page==='index.htm'||page==='index.html'||/^vr0[1-9]_00\\.html?$/.test(page));" +
                     "const domainsFolder=path.toLowerCase().replace(/[^a-z0-9]/g,'').includes('domainsofdread');" +
                     "const largeCoverImages=[...images].filter(img=>Math.max(Number(img.getAttribute('width')),img.naturalWidth)>=500&&Math.max(Number(img.getAttribute('height')),img.naturalHeight)>=700);" +
                     "const domainsCover=domainsFolder&&largeCoverImages.length===1;" +
                     "const simpleCover=text.length===0&&(images.length===1||background);" +
                     "body.classList.toggle('core-rules-cover-page',simpleCover);" +
                     "body.classList.toggle('core-rules-van-richten-cover',vanRichtenCover||domainsCover);" +
                     "document.documentElement.classList.toggle('core-rules-cover-document',simpleCover||vanRichtenCover||domainsCover);" +
                     "}" +
                     "})()";
        try
        {
            await webView.ExecuteScriptAsync(script);
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
            var weight = name.Contains("bold", StringComparison.OrdinalIgnoreCase) ? "bold" : "normal";
            var style = name.Contains("italic", StringComparison.OrdinalIgnoreCase) ? "italic" : "normal";
            css.Append("@font-face{font-family:'").Append(family).Append("';src:url(data:")
                .Append(mime).Append(";base64,").Append(Convert.ToBase64String(File.ReadAllBytes(path)))
                .Append(") format('").Append(format).Append("');font-style:").Append(style)
                .Append(";font-weight:").Append(weight).Append(";}");
        }

        return css.ToString();
    }

    private static string CreateLegacyFontCss()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
        if (!Directory.Exists(folder)) return string.Empty;

        var css = new StringBuilder();
        foreach (var path in Directory.EnumerateFiles(folder)
                     .Where(path => path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) ||
                                    path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var family = name.Contains("university", StringComparison.OrdinalIgnoreCase)
                ? "Core Rules University Roman"
                : name.Contains("antiqua", StringComparison.OrdinalIgnoreCase)
                    ? "Core Rules Book Antiqua"
                    : null;
            if (family is null) continue;

            var format = path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) ? "opentype" : "truetype";
            var weight = name.Contains("bold", StringComparison.OrdinalIgnoreCase) ? "bold" : "normal";
            var style = name.Contains("italic", StringComparison.OrdinalIgnoreCase) ? "italic" : "normal";
            css.Append("@font-face{font-family:'").Append(family).Append("';src:url('")
                .Append(new Uri(path).AbsoluteUri).Append("') format('").Append(format)
                .Append("');font-style:").Append(style).Append(";font-weight:").Append(weight).Append(";}");
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

    private string CreateDocumentSurfaceCss()
    {
        if (_selectedDocument?.Kind != HtmlDocumentKind.Book) return string.Empty;

        return "html,body{background-color:#f5e8c8!important;background-image:" +
               CreateParchmentBackgroundImage() +
               "!important;background-repeat:repeat!important;background-size:768px 768px!important;}";
    }

    private static string CreateParchmentBackgroundImage()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "ParchmentTexture.jpg");
        return File.Exists(path) ? $"url('{new Uri(path).AbsoluteUri}')" : "none";
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

        var background = CreateCharacterSheetBackground();

        // The top-level WIDTH=100% tables in the Core Rules 2 export can
        // otherwise exceed the padded body by a few pixels and create a
        // redundant horizontal scrollbar.
        return "html{overflow-x:hidden!important;overflow-y:auto!important;background-color:#2b190f!important;" +
               $"background-image:{background}!important;background-position:center top!important;background-size:cover!important;" +
               "background-repeat:no-repeat!important;background-attachment:fixed!important;}" +
               "body{margin:0!important;padding:24px 28px 40px!important;box-sizing:border-box!important;" +
               "width:100%!important;max-width:100vw!important;height:auto!important;min-height:0!important;" +
               "position:relative!important;overflow-x:clip!important;overflow-y:visible!important;" +
               "color:#282521!important;background:transparent!important;font-size:15px!important;line-height:1.38!important;}" +
               "body>table{background:transparent!important;}" +
               "body>table,body>table[width]{width:calc(100% - 24px)!important;max-width:1440px!important;margin:0 auto 16px!important;" +
               "box-sizing:border-box!important;border-collapse:separate!important;border-spacing:12px 0!important;}" +
               "body>table>tbody>tr>td{padding:0!important;}" +
               "body>table>tbody>tr>td>table[border='1']{border:1px solid #b79a61!important;border-radius:9px!important;" +
               "border-spacing:0!important;background:rgba(255,253,247,.88)!important;box-shadow:0 3px 11px rgba(55,40,23,.22)!important;overflow:hidden!important;}" +
               "body>table>tbody>tr>td>table[border='1']>tbody>tr>td{padding:0 12px 10px!important;background:transparent!important;}" +
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
               "body>table>tbody>tr>td>table[border='1']{background:#fff!important;box-shadow:none!important;}" +
               "body>p:last-of-type{break-before:avoid-page;}" +
               "body::-webkit-scrollbar,html::-webkit-scrollbar{display:none!important;}}";
    }

    private static string CreateCharacterSheetBackground()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "CharacterSheetTabletop.jpg");
        return File.Exists(path)
            ? $"url('{new Uri(path).AbsoluteUri}')"
            : "radial-gradient(circle at 12% 8%,rgba(255,255,255,.52),transparent 28%)," +
              "linear-gradient(135deg,#eee7da 0%,#ded3c1 100%)";
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
        "html.core-rules-cover-document{margin:0!important;min-height:100vh;background:#000!important;}" +
        "html.core-rules-cover-document body.core-rules-van-richten-cover{" +
        "background-color:#000!important;background-image:none!important;}" +
        "body.core-rules-van-richten-cover table[width='700']{" +
        "width:700px!important;max-width:100%!important;margin-left:auto!important;margin-right:auto!important;}" +
        "html.core-rules-cover-document body.core-rules-cover-page{" +
        "margin:0 !important;min-height:100vh;background-color:#000 !important;" +
        "background-image:none !important;" +
        "background-repeat:no-repeat !important;background-position:top center !important;" +
        "background-size:contain !important;}" +
        "body.core-rules-cover-page img{" +
        "display:block;margin:0 auto !important;max-width:100%;height:auto;" +
        "border:2px solid #000;box-sizing:border-box;}";

    private void TrackCurrentPage(string pagePath, string pageTitle)
    {
        if (_selectedDocument is null || !File.Exists(pagePath)) return;

        var fullPath = Path.GetFullPath(pagePath);
        var displayTitle = string.IsNullOrWhiteSpace(pageTitle)
            ? $"{_selectedDocument.Title} — {Path.GetFileNameWithoutExtension(fullPath)}"
            : pageTitle.Trim();
        var savedPage = new SavedPage(
            _selectedDocument.Title,
            Path.GetFullPath(_selectedDocument.StartPage),
            fullPath,
            displayTitle,
            _selectedDocument.Kind,
            _selectedDocument.Collection,
            DateTimeOffset.Now);

        var recentLimit = NormaliseRecentLimit(_settings.RecentPageLimit);
        var recent = (_settings.RecentPages ?? [])
            .Where(page => page.LocationKind != SavedLocationKind.Document ||
                           !PathsEqual(page.PagePath, fullPath))
            .Prepend(savedPage)
            .Take(recentLimit)
            .ToArray();
        _settings = _settings with
        {
            RecentPages = recent,
            LastDocumentStartPage = savedPage.DocumentStartPage,
            LastPagePath = fullPath
        };
        UpdateBookmarkButton(fullPath);
        PopulateSavedPages(BookmarksRoot, _settings.Bookmarks ?? [], true, SearchBox.Text.Trim());
        PopulateSavedPages(RecentPagesRoot, recent, false, SearchBox.Text.Trim());
        SaveSettings();
    }

    private void BookmarkPage_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDocument is null) return;
        var path = GetCurrentPagePath();
        if (path is null || !File.Exists(path)) return;

        var bookmarks = (_settings.Bookmarks ?? []).ToList();
        var existingIndex = bookmarks.FindIndex(page =>
            page.LocationKind == SavedLocationKind.Document && PathsEqual(page.PagePath, path));
        if (existingIndex >= 0)
        {
            bookmarks.RemoveAt(existingIndex);
        }
        else
        {
            var recentTitle = (_settings.RecentPages ?? [])
                .FirstOrDefault(page => PathsEqual(page.PagePath, path))?.PageTitle;
            var title = recentTitle ?? (UseLegacyDocumentBrowser ? ReadLegacyPageTitle() : DocumentTitleText.Text);
            bookmarks.Add(new SavedPage(
                _selectedDocument.Title,
                Path.GetFullPath(_selectedDocument.StartPage),
                Path.GetFullPath(path),
                string.IsNullOrWhiteSpace(title)
                    ? $"{_selectedDocument.Title} — {Path.GetFileNameWithoutExtension(path)}"
                    : title.Trim(),
                _selectedDocument.Kind,
                _selectedDocument.Collection,
                DateTimeOffset.Now));
        }

        _settings = _settings with { Bookmarks = bookmarks.ToArray() };
        SaveSettings();
        UpdateBookmarkButton(path);
        PopulateSavedPages(BookmarksRoot, bookmarks, true, SearchBox.Text.Trim());
    }

    private string? GetCurrentPagePath()
    {
        if (UseLegacyDocumentBrowser)
        {
            return LegacyDocumentBrowser.Source is { IsFile: true } source ? source.LocalPath : null;
        }
        return DocumentBrowser.Source is { IsFile: true } webViewSource ? webViewSource.LocalPath : null;
    }

    private void UpdateBookmarkButton(string pagePath)
    {
        var bookmarked = (_settings.Bookmarks ?? []).Any(page =>
            page.LocationKind == SavedLocationKind.Document && PathsEqual(page.PagePath, pagePath));
        BookmarkPageButton.Content = bookmarked ? "Remove bookmark" : "Bookmark page";
    }

    private void SpellBookmark_Click(object sender, RoutedEventArgs e)
    {
        if (_displayedMainSpell is not null) ToggleSpellBookmark(_displayedMainSpell);
    }

    private void ContextBookmark_Click(object sender, RoutedEventArgs e)
    {
        if (_previewedSpell is not null) ToggleSpellBookmark(_previewedSpell);
    }

    private void ToggleSpellBookmark(SpellRecord spell)
    {
        ToggleBookmark(CreateSpellSavedPage(spell));
        UpdateSpellBookmarkButtons();
    }

    private static SavedPage CreateSpellSavedPage(SpellRecord spell) => new(
        "Spells",
        string.Empty,
        SpellResourceKey(spell),
        spell.Name,
        HtmlDocumentKind.Book,
        HtmlDocumentCollection.None,
        DateTimeOffset.Now,
        SavedLocationKind.Spell,
        SpellResourceKey(spell));

    private static string SpellResourceKey(SpellRecord spell) =>
        $"{spell.DatabaseKind}|{spell.Level}|{spell.Name}";

    private void UpdateSpellBookmarkButtons()
    {
        if (_displayedMainSpell is not null)
        {
            SpellBookmarkButton.Content = IsSpellBookmarked(_displayedMainSpell)
                ? "Remove bookmark"
                : "Bookmark spell";
        }
        if (_previewedSpell is not null)
        {
            ContextBookmarkButton.Content = IsSpellBookmarked(_previewedSpell)
                ? "Remove bookmark"
                : "Bookmark spell";
        }
    }

    private bool IsSpellBookmarked(SpellRecord spell)
    {
        var key = SpellResourceKey(spell);
        return (_settings.Bookmarks ?? []).Any(page =>
            page.LocationKind == SavedLocationKind.Spell &&
            string.Equals(page.ResourceKey, key, StringComparison.OrdinalIgnoreCase));
    }

    private void OnlineBookmark_Click(object sender, RoutedEventArgs e)
    {
        if (OnlineBrowser.Source is not { } address || GetCompendiumPageName(address) is not { } title) return;
        ToggleBookmark(CreateOnlineSavedPage(address, title));
        UpdateOnlineBookmarkButton(address);
    }

    private void ToggleBookmark(SavedPage target)
    {
        var bookmarks = (_settings.Bookmarks ?? []).ToList();
        var existingIndex = bookmarks.FindIndex(page => SavedLocationsEqual(page, target));
        if (existingIndex >= 0)
        {
            bookmarks.RemoveAt(existingIndex);
        }
        else
        {
            bookmarks.Add(target);
        }

        _settings = _settings with { Bookmarks = bookmarks.ToArray() };
        SaveSettings();
        PopulateSavedPages(BookmarksRoot, bookmarks, true, SearchBox.Text.Trim());
    }

    private static bool SavedLocationsEqual(SavedPage first, SavedPage second)
    {
        if (first.LocationKind != second.LocationKind) return false;
        return first.LocationKind switch
        {
            SavedLocationKind.Document => PathsEqual(first.PagePath, second.PagePath),
            SavedLocationKind.Spell => string.Equals(
                first.ResourceKey, second.ResourceKey, StringComparison.OrdinalIgnoreCase),
            SavedLocationKind.Online => NormaliseOnlineAddress(first.PagePath).Equals(
                NormaliseOnlineAddress(second.PagePath), StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static string NormaliseOnlineAddress(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) return address.TrimEnd('/');
        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static bool PathsEqual(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second)) return false;
        try
        {
            return Path.GetFullPath(first).Equals(Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return first.Equals(second, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        _scale = NormaliseScale(dialog.DocumentScale, 125);
        _spellScale = NormaliseScale(dialog.SpellScale, 175);
        var recentLimit = NormaliseRecentLimit(dialog.RecentPageLimit);
        IReadOnlyList<SavedPage> recentPages = dialog.ClearRecentPages
            ? []
            : (_settings.RecentPages ?? []).Take(recentLimit).ToArray();
        _settings = _settings with
        {
            Scale = _scale,
            SpellScale = _spellScale,
            ReopenLastPage = dialog.ReopenLastPage,
            RecentPageLimit = recentLimit,
            RecentPages = recentPages
        };
        ScaleBox.SelectedIndex = _scale / 25 - 4;
        SpellScaleBox.SelectedIndex = _spellScale / 25 - 4;
        ApplyDocumentScale();
        ApplySpellScale();
        SaveSettings();
        PopulateSavedPages(RecentPagesRoot, recentPages, false, SearchBox.Text.Trim());
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key == Key.F &&
            DocumentPanel.Visibility == Visibility.Visible)
        {
            FindTextBox.Focus();
            FindTextBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.F3 && DocumentPanel.Visibility == Visibility.Visible)
        {
            _ = FindInCurrentDocumentAsync((Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            e.Handled = true;
        }
    }

    private void FindTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        _ = FindInCurrentDocumentAsync((Keyboard.Modifiers & ModifierKeys.Shift) != 0);
        e.Handled = true;
    }

    private void FindTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _legacyFindIndex = -1;
        _activeFindText = string.Empty;
        FindStatusText.Text = string.Empty;
    }

    private void FindPrevious_Click(object sender, RoutedEventArgs e) => _ = FindInCurrentDocumentAsync(true);

    private void FindNext_Click(object sender, RoutedEventArgs e) => _ = FindInCurrentDocumentAsync(false);

    private async Task FindInCurrentDocumentAsync(bool backwards)
    {
        var query = FindTextBox.Text.Trim();
        if (query.Length == 0)
        {
            FindStatusText.Text = "Enter text";
            FindTextBox.Focus();
            return;
        }

        if (UseLegacyDocumentBrowser)
        {
            FindInLegacyDocument(query, backwards);
            return;
        }

        if (DocumentBrowser.CoreWebView2 is null) return;
        try
        {
            var script = $"window.find({JsonSerializer.Serialize(query)},false,{backwards.ToString().ToLowerInvariant()},true,false,false,false)";
            var result = await DocumentBrowser.CoreWebView2.ExecuteScriptAsync(script);
            FindStatusText.Text = result.Equals("true", StringComparison.OrdinalIgnoreCase)
                ? "Match selected"
                : "No matches";
        }
        catch
        {
            FindStatusText.Text = "Search unavailable";
        }
    }

    private void FindInLegacyDocument(string query, bool backwards)
    {
        try
        {
            dynamic? document = LegacyDocumentBrowser.Document;
            if (document is null) return;
            dynamic? body = document.body;
            if (body is null) return;

            var matches = new List<object>();
            dynamic range = body.createTextRange();
            while ((bool)range.findText(query, int.MaxValue, 0))
            {
                matches.Add(range.duplicate());
                range.collapse(false);
            }

            if (matches.Count == 0)
            {
                FindStatusText.Text = "No matches";
                _legacyFindIndex = -1;
                return;
            }

            if (!_activeFindText.Equals(query, StringComparison.CurrentCultureIgnoreCase))
            {
                _activeFindText = query;
                _legacyFindIndex = backwards ? matches.Count : -1;
            }
            _legacyFindIndex = backwards
                ? (_legacyFindIndex - 1 + matches.Count) % matches.Count
                : (_legacyFindIndex + 1) % matches.Count;
            dynamic selectedRange = matches[_legacyFindIndex];
            selectedRange.select();
            selectedRange.scrollIntoView(true);
            FindStatusText.Text = $"{_legacyFindIndex + 1} of {matches.Count}";
        }
        catch
        {
            FindStatusText.Text = "Search unavailable";
        }
    }

    private void ResetFindState()
    {
        _legacyFindIndex = -1;
        _activeFindText = string.Empty;
        FindStatusText.Text = string.Empty;
    }

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

    private async Task ShowBookContentsAsync(HtmlDocumentEntry document, string? currentPage = null)
    {
        _contextPanelMode = ContextPanelMode.BookContents;
        var contents = BookContentsResolver.Resolve(document.StartPage, currentPage);
        _contentsPagePath = contents.PagePath;
        _coverPagePath = contents.CoverPagePath;
        _previewedSpell = null;
        BookContentsTitleText.Text = contents.SectionTitle is null
            ? $"Contents — {document.Title}"
            : $"Contents — {contents.SectionTitle}";
        ContextBookmarkButton.Visibility = Visibility.Collapsed;
        ContentsToggleButton.Visibility = Visibility.Visible;
        SpellContextBrowser.Visibility = Visibility.Collapsed;
        BookReferenceSplitGrid.Visibility = Visibility.Visible;
        SetCoverPaneVisible(_coverPagePath is not null);
        SetBookContentsPanelVisible(_settings.BookContentsVisible);

        var address = new Uri(_contentsPagePath);
        if (UseLegacyDocumentBrowser)
        {
            ContentsBrowser.Visibility = Visibility.Collapsed;
            LegacyContentsBrowser.Visibility = Visibility.Visible;
            if (LegacyContentsBrowser.Source is null ||
                !PathsEqual(LegacyContentsBrowser.Source.LocalPath, _contentsPagePath))
            {
                LegacyContentsBrowser.Navigate(address);
            }
        }
        else
        {
            LegacyContentsBrowser.Visibility = Visibility.Collapsed;
            ContentsBrowser.Visibility = Visibility.Visible;
            await ContentsBrowser.EnsureCoreWebView2Async();
            HardenWebView(ContentsBrowser.CoreWebView2);
            ContentsBrowser.ZoomFactor = 0.9;
            if (ContentsBrowser.Source is null ||
                !PathsEqual(ContentsBrowser.Source.LocalPath, _contentsPagePath))
            {
                ContentsBrowser.Source = address;
            }
        }

        if (_coverPagePath is not null)
        {
            await ShowCoverPageAsync(new Uri(_coverPagePath));
        }
    }

    private async Task UpdateBookContentsForPageAsync(string currentPage)
    {
        if (_selectedDocument?.Kind != HtmlDocumentKind.Book ||
            _contextPanelMode != ContextPanelMode.BookContents) return;

        var contents = BookContentsResolver.Resolve(_selectedDocument.StartPage, currentPage);
        if (!string.IsNullOrWhiteSpace(_contentsPagePath) &&
            PathsEqual(_contentsPagePath, contents.PagePath) &&
            ((string.IsNullOrWhiteSpace(_coverPagePath) &&
              string.IsNullOrWhiteSpace(contents.CoverPagePath)) ||
             (!string.IsNullOrWhiteSpace(_coverPagePath) &&
              !string.IsNullOrWhiteSpace(contents.CoverPagePath) &&
              PathsEqual(_coverPagePath, contents.CoverPagePath)))) return;

        await ShowBookContentsAsync(_selectedDocument, currentPage);
    }

    private void HideBookContents()
    {
        _contextPanelMode = ContextPanelMode.None;
        _contentsPagePath = null;
        _coverPagePath = null;
        _previewedSpell = null;
        ContextBookmarkButton.Visibility = Visibility.Collapsed;
        ContentsToggleButton.Visibility = Visibility.Collapsed;
        SetBookContentsPanelVisible(false);
    }

    private async void ToggleBookContents_Click(object sender, RoutedEventArgs e)
    {
        var show = BookContentsPanel.Visibility != Visibility.Visible;
        _settings = _settings with { BookContentsVisible = show };
        if (_contextPanelMode == ContextPanelMode.Spell)
        {
            SetBookContentsPanelVisible(show);
            SaveSettings();
            return;
        }

        if (_selectedDocument?.Kind != HtmlDocumentKind.Book) return;
        if (show)
        {
            await ShowBookContentsAsync(_selectedDocument, GetCurrentPagePath());
        }
        else
        {
            SetBookContentsPanelVisible(false);
        }
        SaveSettings();
    }

    private void SetBookContentsPanelVisible(bool visible)
    {
        var retainedWidth = BookContentsPanel.Visibility == Visibility.Visible &&
                            BookContentsColumn.ActualWidth >= 220
            ? BookContentsColumn.ActualWidth
            : _settings.BookContentsWidth;
        if (BookContentsPanel.Visibility == Visibility.Visible &&
            BookContentsColumn.ActualWidth >= 220)
        {
            _settings = _settings with { BookContentsWidth = BookContentsColumn.ActualWidth };
        }
        BookContentsColumn.MinWidth = visible ? 220 : 0;
        BookContentsColumn.Width = visible ? new GridLength(retainedWidth) : new GridLength(0);
        BookContentsSplitterColumn.Width = visible ? new GridLength(5) : new GridLength(0);
        BookContentsPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        BookContentsSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ContentsToggleButton.Content = visible ? "Hide contents" : "Show contents";
    }

    private void SetCoverPaneVisible(bool visible)
    {
        CoverLabelRow.Height = visible ? GridLength.Auto : new GridLength(0);
        CoverViewerRow.MinHeight = visible ? 120 : 0;
        CoverViewerRow.Height = visible
            ? new GridLength(_settings.BookReferenceCoverHeight)
            : new GridLength(0);
        ReferenceSplitterRow.Height = visible ? new GridLength(6) : new GridLength(0);
        ReferenceGridSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        LegacyCoverBrowser.Visibility = Visibility.Collapsed;
        CoverBrowser.Visibility = Visibility.Collapsed;
    }

    private async Task ShowCoverPageAsync(Uri address)
    {
        if (UseLegacyDocumentBrowser)
        {
            LegacyCoverBrowser.Visibility = Visibility.Visible;
            if (LegacyCoverBrowser.Source is null ||
                !PathsEqual(LegacyCoverBrowser.Source.LocalPath, address.LocalPath))
            {
                LegacyCoverBrowser.Navigate(address);
            }
            return;
        }

        CoverBrowser.Visibility = Visibility.Visible;
        await CoverBrowser.EnsureCoreWebView2Async();
        HardenWebView(CoverBrowser.CoreWebView2);
        CoverBrowser.ZoomFactor = 0.9;
        if (CoverBrowser.Source is null ||
            !PathsEqual(CoverBrowser.Source.LocalPath, address.LocalPath))
        {
            CoverBrowser.Source = address;
        }
    }

    private bool IsAllowedReferenceNavigation(Uri? address, string? referencePath)
    {
        return address is { IsFile: true } &&
               BrowserSecurityPolicy.IsLocalPageWithin(address, [_settings.LibraryPath]) &&
               !string.IsNullOrWhiteSpace(referencePath);
    }

    private void LegacyCoverBrowser_Navigating(
        object sender,
        System.Windows.Navigation.NavigatingCancelEventArgs e)
    {
        if (_contextPanelMode != ContextPanelMode.BookContents) return;
        if (!IsAllowedReferenceNavigation(e.Uri, _coverPagePath))
        {
            e.Cancel = true;
            FooterStatus.Text = "Blocked navigation outside the selected local library.";
            return;
        }
        if (PathsEqual(e.Uri.LocalPath, _coverPagePath!)) return;

        e.Cancel = true;
        NavigateDocument(e.Uri);
    }

    private void LegacyCoverBrowser_LoadCompleted(
        object sender,
        System.Windows.Navigation.NavigationEventArgs e)
    {
        ApplyLegacyParagraphStyle(LegacyCoverBrowser);
        try
        {
            dynamic? document = LegacyCoverBrowser.Document;
            dynamic? body = document?.body;
            if (body is not null) body.style.zoom = "90%";
        }
        catch
        {
            // The cover remains usable when a legacy DOM rejects zoom.
        }
    }

    private void CoverBrowser_NavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs e)
    {
        if (_contextPanelMode != ContextPanelMode.BookContents) return;
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var address) ||
            !IsAllowedReferenceNavigation(address, _coverPagePath))
        {
            e.Cancel = true;
            FooterStatus.Text = "Blocked navigation outside the selected local library.";
            return;
        }
        if (PathsEqual(address.LocalPath, _coverPagePath!)) return;

        e.Cancel = true;
        NavigateDocument(address);
    }

    private async void CoverBrowser_NavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess) return;
        CoverBrowser.ZoomFactor = 0.9;
        await ApplyDocumentStyleAsync(CoverBrowser.CoreWebView2);
    }

    private void LegacyContentsBrowser_Navigating(
        object sender,
        System.Windows.Navigation.NavigatingCancelEventArgs e)
    {
        if (_contextPanelMode != ContextPanelMode.BookContents) return;
        if (e.Uri is not { IsFile: true } address ||
            !BrowserSecurityPolicy.IsLocalPageWithin(address, [_settings.LibraryPath]))
        {
            e.Cancel = true;
            FooterStatus.Text = "Blocked navigation outside the selected local library.";
            return;
        }
        if (string.IsNullOrWhiteSpace(_contentsPagePath) ||
            PathsEqual(address.LocalPath, _contentsPagePath)) return;

        e.Cancel = true;
        NavigateDocument(address);
    }

    private void LegacyContentsBrowser_LoadCompleted(
        object sender,
        System.Windows.Navigation.NavigationEventArgs e)
    {
        ApplyLegacyParagraphStyle(LegacyContentsBrowser);
        try
        {
            dynamic? document = LegacyContentsBrowser.Document;
            if (document is null) return;
            dynamic? body = document.body;
            if (body is not null) body.style.zoom = "90%";
        }
        catch
        {
            // The contents page remains usable when a legacy DOM rejects zoom.
        }
    }

    private void ContentsBrowser_NavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs e)
    {
        if (_contextPanelMode != ContextPanelMode.BookContents) return;
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var address) ||
            !BrowserSecurityPolicy.IsLocalPageWithin(address, [_settings.LibraryPath]))
        {
            e.Cancel = true;
            FooterStatus.Text = "Blocked navigation outside the selected local library.";
            return;
        }
        if (string.IsNullOrWhiteSpace(_contentsPagePath) ||
            PathsEqual(address.LocalPath, _contentsPagePath)) return;

        e.Cancel = true;
        NavigateDocument(address);
    }

    private async void ContentsBrowser_NavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess) return;
        ContentsBrowser.ZoomFactor = 0.9;
        if (_contextPanelMode == ContextPanelMode.BookContents)
        {
            await ApplyDocumentStyleAsync(ContentsBrowser.CoreWebView2);
        }
    }

    private void OnlineBrowser_NavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess) return;
        UpdateOnlinePageState();
    }

    private void OnlineBrowser_NavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs e)
    {
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var address) &&
            BrowserSecurityPolicy.IsAllowedOnlineAddress(address)) return;

        e.Cancel = true;
        FooterStatus.Text = "Blocked navigation outside Complete Compendium.";
    }

    private void OnlineBrowser_SourceChanged(
        object? sender,
        CoreWebView2SourceChangedEventArgs e)
    {
        UpdateOnlinePageState();
    }

    private void UpdateOnlinePageState()
    {
        if (OnlineBrowser.Source is not { } address) return;

        OnlineAddressText.Text = address.AbsoluteUri;
        var pageTitle = GetCompendiumPageName(address);
        OnlineBookmarkButton.IsEnabled = pageTitle is not null;
        UpdateOnlineBookmarkButton(address);
        if (pageTitle is not null) TrackOnlinePage(address, pageTitle);
    }

    private void TrackOnlinePage(Uri address, string pageTitle)
    {
        var savedPage = CreateOnlineSavedPage(address, pageTitle);
        var recentLimit = NormaliseRecentLimit(_settings.RecentPageLimit);
        var recent = (_settings.RecentPages ?? [])
            .Where(page => !SavedLocationsEqual(page, savedPage))
            .Prepend(savedPage)
            .Take(recentLimit)
            .ToArray();
        _settings = _settings with { RecentPages = recent };
        PopulateSavedPages(RecentPagesRoot, recent, false, SearchBox.Text.Trim());
        SaveSettings();
    }

    private static SavedPage CreateOnlineSavedPage(Uri address, string pageTitle) => new(
        "Complete Compendium",
        "https://www.completecompendium.com/",
        address.AbsoluteUri,
        pageTitle,
        HtmlDocumentKind.Book,
        HtmlDocumentCollection.None,
        DateTimeOffset.Now,
        SavedLocationKind.Online,
        NormaliseOnlineAddress(address.AbsoluteUri));

    private static string? GetCompendiumPageName(Uri address)
    {
        if (!address.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !address.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return null;
        if (!address.Host.Equals("completecompendium.com", StringComparison.OrdinalIgnoreCase) &&
            !address.Host.EndsWith(".completecompendium.com", StringComparison.OrdinalIgnoreCase)) return null;

        var segments = address.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return null;
        var name = Uri.UnescapeDataString(segments[^1]).Replace('-', ' ').Replace('_', ' ').Trim();
        if (name.Length == 0) return null;
        if (name.Equals("index", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("index.htm", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("index.html", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("home", StringComparison.OrdinalIgnoreCase)) return null;
        return char.ToUpper(name[0], System.Globalization.CultureInfo.CurrentCulture) + name[1..];
    }

    private void UpdateOnlineBookmarkButton(Uri address)
    {
        var pageTitle = GetCompendiumPageName(address);
        if (pageTitle is null)
        {
            OnlineBookmarkButton.Content = "Bookmark page";
            return;
        }

        var target = CreateOnlineSavedPage(address, pageTitle);
        var bookmarked = (_settings.Bookmarks ?? []).Any(page => SavedLocationsEqual(page, target));
        OnlineBookmarkButton.Content = bookmarked ? "Remove bookmark" : "Bookmark page";
    }

    private void OpenDocument_Click(object sender, RoutedEventArgs e)
    {
        var address = GetCurrentPagePath() ?? _selectedDocument?.StartPage;
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
            var address = OnlineBrowser.Source?.AbsoluteUri ?? _selectedOnlineResource.Address;
            Process.Start(new ProcessStartInfo(address) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Windows could not open this website.\n\n{exception.Message}",
                "Open online resource", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveSettings()
    {
        _settings = _settings with
        {
            Scale = _scale,
            SpellScale = _spellScale,
            SpellCasterFilter = Convert.ToString(SpellCasterFilterBox.SelectedValue) ?? "All",
            SpellLevelFilter = int.TryParse(Convert.ToString(SpellLevelFilterBox.SelectedValue), out var level)
                ? level
                : -1,
            SpellSchoolSphereFilter = Convert.ToString(SpellSchoolSphereFilterBox.SelectedItem)
                                      ?? "All schools and spheres",
            SpellComponentFilter = Convert.ToString(SpellComponentFilterBox.SelectedValue) ?? "All",
            SpellSourceFilter = Convert.ToString(SpellSourceFilterBox.SelectedValue) ?? "All",
            BookReferenceCoverHeight = CoverViewerRow.ActualHeight >= 120
                ? CoverViewerRow.ActualHeight
                : _settings.BookReferenceCoverHeight,
            BookContentsWidth = BookContentsColumn.ActualWidth >= 220
                ? BookContentsColumn.ActualWidth
                : _settings.BookContentsWidth
        };
        _settingsStore.Save(_settings);
    }

    private static int NormaliseScale(int scale, int fallback) =>
        scale is >= 100 and <= 300 && scale % 25 == 0 ? scale : fallback;

    private static int NormaliseRecentLimit(int value) => value switch
    {
        10 or 20 or 30 or 50 => value,
        _ => 20
    };
}
