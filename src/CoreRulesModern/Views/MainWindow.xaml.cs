using System.Diagnostics;
using System.IO;
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
    private readonly PackagedFontLoader _fontLoader = new();
    private readonly List<HtmlDocumentEntry> _books = [];
    private readonly List<HtmlDocumentEntry> _characters = [];
    private UserSettingsStore.UserSettings _settings = new();
    private HtmlDocumentEntry? _selectedDocument;
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

        var libraryPath = _settings.LibraryPath;
        if (string.IsNullOrWhiteSpace(libraryPath) || !_validator.Validate(libraryPath).IsValid)
        {
            libraryPath = _locator.FindCandidates().FirstOrDefault(path => _validator.Validate(path).IsValid);
        }

        LoadBooks(libraryPath);
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

        LoadBooks(status.RootPath);
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

    private void LoadBooks(string? root)
    {
        _books.Clear();
        if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
        {
            _books.AddRange(_manualCatalogue.Read(root));
            _settings = _settings with { LibraryPath = Path.GetFullPath(root) };
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
        Populate(BooksRoot, _books.Where(item => Matches(item, filter)), "Books", _books.Count);
        LibraryPathText.Text = _settings.LibraryPath ?? "Not selected";
        CharacterPathText.Text = _settings.CharacterSheetsPath ?? "Not selected";
        WelcomeSummary.Text = $"{_books.Count:N0} books and {_characters.Count:N0} character sheets are available.";
        FooterStatus.Text = $"{_books.Count:N0} books · {_characters.Count:N0} characters";
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

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) RefreshNavigation();
    }

    private void NavigationTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem { Tag: HtmlDocumentEntry document }) ShowDocument(document);
    }

    private void ShowDocument(HtmlDocumentEntry document)
    {
        _selectedDocument = document;
        WelcomePanel.Visibility = Visibility.Collapsed;
        DocumentPanel.Visibility = Visibility.Visible;
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

    private void DocumentBrowser_LoadCompleted(object sender, NavigationEventArgs e) => ApplyDisplayStyle();

    private void ScaleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ScaleBox.SelectedItem is ComboBoxItem { Tag: string value } && int.TryParse(value, out var scale))
        {
            _scale = scale;
            if (IsLoaded)
            {
                SaveSettings();
                ApplyDisplayStyle();
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
            style.styleSheet.cssText =
                "html,body,body *{font-family:'ITC Korinna','Korinna',Georgia,serif !important;}" +
                $"body{{zoom:{_scale}%;margin:18px;}}" +
                "a{color:#7b241c;} img{max-width:100%;height:auto;}";
            document.getElementsByTagName("head").item(0).appendChild(style);
        }
        catch
        {
            // Legacy help remains readable if a particular page rejects injected styling.
        }
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
        if (_selectedDocument is null) return;
        try
        {
            Process.Start(new ProcessStartInfo(_selectedDocument.StartPage) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Windows could not open this document.\n\n{exception.Message}",
                "Open document", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveSettings()
    {
        _settings = _settings with { Scale = _scale };
        _settingsStore.Save(_settings);
    }

    private static int NormaliseScale(int scale) => scale is >= 100 and <= 200 && scale % 25 == 0 ? scale : 125;
}
