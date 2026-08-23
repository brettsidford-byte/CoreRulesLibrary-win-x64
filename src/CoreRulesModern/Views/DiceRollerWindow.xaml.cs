using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CoreRulesModern.Models;
using CoreRulesModern.Services;

namespace CoreRulesModern.Views;

public partial class DiceRollerWindow : Window
{
    private readonly DiceRollerStore _store = new();
    private readonly List<DicePool> _pools;
    private DicePool? _pool;
    private DieSpec? _selectedDie;
    private bool _loading;
    private bool _scaleLoading;
    private string _interfaceScalePreference = "Auto";
    private string _lastResult = string.Empty;
    private static readonly string[] Colours = ["#E9D8AE", "#A72222", "#235DA8", "#2C7A43", "#6D3B91", "#C28C24", "#222222", "#EFEFEF", "#168A91", "#B04470"];

    public DiceRollerWindow()
    {
        InitializeComponent();
        _pools = _store.LoadPools();
        _interfaceScalePreference = _store.LoadInterfaceScale();
        BuildColourButtons();
        RefreshRecentHistory();
        RefreshPoolList();
        if (_pools.Count > 0) PoolList.SelectedIndex = 0;
        SelectScalePreference();
        Loaded += (_, _) => ApplyInterfaceScale();
        SizeChanged += (_, _) => { if (_interfaceScalePreference == "Auto" && IsLoaded) ApplyInterfaceScale(); };
        Closed += (_, _) => _store.SavePools(_pools);
    }

    private void SelectScalePreference()
    {
        _scaleLoading = true;
        try
        {
            for (var index = 0; index < ScaleBox.Items.Count; index++)
                if (ScaleBox.Items[index] is ComboBoxItem item && string.Equals(item.Tag?.ToString(), _interfaceScalePreference, StringComparison.Ordinal))
                { ScaleBox.SelectedIndex = index; return; }
            ScaleBox.SelectedIndex = 0;
        }
        finally { _scaleLoading = false; }
    }

    private void ScaleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_scaleLoading || ScaleBox.SelectedItem is not ComboBoxItem item) return;
        _interfaceScalePreference = item.Tag?.ToString() ?? "Auto";
        _store.SaveInterfaceScale(_interfaceScalePreference);
        ApplyInterfaceScale();
    }

    private void ApplyInterfaceScale()
    {
        var scale = 1d;
        if (_interfaceScalePreference == "Auto")
        {
            const double designWidth = 1516;
            const double designHeight = 936;
            scale = Math.Clamp(Math.Min(Math.Max(1, LayoutScrollViewer.ActualWidth - 4) / designWidth, Math.Max(1, LayoutScrollViewer.ActualHeight - 4) / designHeight), 0.5, 1.0);
        }
        else if (!double.TryParse(_interfaceScalePreference, NumberStyles.Float, CultureInfo.InvariantCulture, out scale)) scale = 1d;
        ScaleRoot.LayoutTransform = new ScaleTransform(scale, scale);
    }

    private void RefreshPoolList(DicePool? select = null)
    {
        var selected = select ?? _pool;
        PoolList.ItemsSource = null;
        PoolList.ItemsSource = _pools;
        if (selected is not null) PoolList.SelectedItem = selected;
    }

    private void PoolList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PoolList.SelectedItem is not DicePool pool) return;
        _pool = pool;
        _selectedDie = pool.Dice.FirstOrDefault();
        _lastResult = string.Empty;
        LastRollEquation.Text = "No roll yet";
        LastRollOutcome.Text = string.Empty;
        LoadPoolControls();
        RenderTray();
    }

    private void LoadPoolControls()
    {
        if (_pool is null) return;
        _loading = true;
        try
        {
            NameBox.Text = _pool.Name;
            CharacterBox.Text = _pool.CharacterName;
            ModeBox.SelectedIndex = (int)_pool.Mode;
            AcBox.Text = _pool.OpponentAc.ToString(CultureInfo.InvariantCulture);
            UpdatePoolHeader();
            AttackOptions.Visibility = _pool.Mode == DicePoolMode.Attack ? Visibility.Visible : Visibility.Collapsed;
            LoadDieControls();
        }
        finally { _loading = false; }
    }

    private void LoadDieControls()
    {
        _loading = true;
        try
        {
            if (_selectedDie is null)
            {
                DieTypeBox.SelectedIndex = -1;
                DieLabelBox.Text = string.Empty;
                DieModifierBox.Text = string.Empty;
                DieThac0Box.Text = string.Empty;
                DieAttackBonusBox.Text = string.Empty;
                return;
            }
            for (var i = 0; i < DieTypeBox.Items.Count; i++)
                if (DieTypeBox.Items[i] is ComboBoxItem item && Convert.ToInt32(item.Tag, CultureInfo.InvariantCulture) == _selectedDie.Sides) DieTypeBox.SelectedIndex = i;
            DieLabelBox.Text = _selectedDie.Label;
            DieModifierBox.Text = _selectedDie.Modifier.ToString(CultureInfo.InvariantCulture);
            DieThac0Box.Text = _selectedDie.Thac0.ToString(CultureInfo.InvariantCulture);
            DieAttackBonusBox.Text = _selectedDie.AttackBonus.ToString(CultureInfo.InvariantCulture);
            DieAttackOptions.Visibility = _pool?.Mode == DicePoolMode.Attack ? Visibility.Visible : Visibility.Collapsed;
            DieAttackBonusOptions.Visibility = DieAttackOptions.Visibility;
        }
        finally { _loading = false; }
    }

    private void PoolField_Changed(object sender, EventArgs e)
    {
        if (_loading || _pool is null) return;
        _pool.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? "Unnamed Pool" : NameBox.Text.Trim();
        _pool.CharacterName = CharacterBox.Text.Trim();
        if (ModeBox.SelectedIndex >= 0) _pool.Mode = (DicePoolMode)ModeBox.SelectedIndex;
        if (int.TryParse(AcBox.Text, out var ac)) _pool.OpponentAc = ac;
        UpdatePoolHeader();
        AttackOptions.Visibility = _pool.Mode == DicePoolMode.Attack ? Visibility.Visible : Visibility.Collapsed;
        DieAttackOptions.Visibility = _pool.Mode == DicePoolMode.Attack ? Visibility.Visible : Visibility.Collapsed;
        DieAttackBonusOptions.Visibility = DieAttackOptions.Visibility;
        _store.SavePools(_pools);
        RefreshPoolList(_pool);
    }

    private void DieField_Changed(object sender, EventArgs e)
    {
        if (_loading || _selectedDie is null) return;
        if (DieTypeBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var sides)) _selectedDie.Sides = sides;
        _selectedDie.Label = DieLabelBox.Text.Trim();
        if (int.TryParse(DieModifierBox.Text, out var modifier)) _selectedDie.Modifier = modifier;
        if (int.TryParse(DieThac0Box.Text, out var thac0)) _selectedDie.Thac0 = thac0;
        if (int.TryParse(DieAttackBonusBox.Text, out var attackBonus)) _selectedDie.AttackBonus = attackBonus;
        _store.SavePools(_pools);
        RenderTray();
        UpdatePoolHeader();
        PoolList.Items.Refresh();
    }

    private void BuildColourButtons()
    {
        var customButton = new Button { Content = "CUSTOM…", Style = (Style)FindResource("AntiqueButton"), Padding = new Thickness(7, 4, 7, 4), ToolTip = "Choose any colour using a hex value" };
        customButton.Click += CustomColour_Click;
        ColourPanel.Children.Add(customButton);
        foreach (var hex in Colours)
        {
            var button = new Button { Width = 22, Height = 22, Margin = new Thickness(1), Tag = hex, Background = BrushFromHex(hex), ToolTip = hex };
            button.Click += (_, _) =>
            {
                if (_selectedDie is null) return;
                _selectedDie.ColourHex = hex;
                _store.SavePools(_pools);
                RenderTray();
            };
            ColourPanel.Children.Add(button);
        }
    }

    private void DieModifierDown_Click(object sender, RoutedEventArgs e) => SetDieModifier(ReadSignedValue(DieModifierBox.Text) - 1);
    private void DieModifierUp_Click(object sender, RoutedEventArgs e) => SetDieModifier(ReadSignedValue(DieModifierBox.Text) + 1);
    private void DieAttackBonusDown_Click(object sender, RoutedEventArgs e) => SetDieAttackBonus(ReadSignedValue(DieAttackBonusBox.Text) - 1);
    private void DieAttackBonusUp_Click(object sender, RoutedEventArgs e) => SetDieAttackBonus(ReadSignedValue(DieAttackBonusBox.Text) + 1);

    private static int ReadSignedValue(string text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private void SetDieModifier(int value)
    {
        if (_selectedDie is null) return;
        DieModifierBox.Text = value.ToString(CultureInfo.InvariantCulture);
    }

    private void SetDieAttackBonus(int value)
    {
        if (_selectedDie is null) return;
        DieAttackBonusBox.Text = value.ToString(CultureInfo.InvariantCulture);
    }

    private void CustomColour_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDie is null) return;
        var input = new TextBox { Text = _selectedDie.ColourHex, Margin = new Thickness(0, 8, 0, 8), FontSize = 18, HorizontalContentAlignment = HorizontalAlignment.Center };
        var preview = new Border { Height = 52, Background = BrushFromHex(_selectedDie.ColourHex), BorderBrush = Brushes.Black, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 0, 10) };
        var apply = new Button { Content = "APPLY COLOUR", Style = (Style)FindResource("AntiqueButton"), HorizontalAlignment = HorizontalAlignment.Right };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "CUSTOM DIE COLOUR", FontFamily = new FontFamily("ITC Korinna"), FontSize = 19, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = "Enter a six-digit hex colour, for example #7A3DB8.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
        panel.Children.Add(input); panel.Children.Add(preview); panel.Children.Add(apply);
        var dialog = new Window { Title = "Custom Die Colour", Width = 360, Height = 260, ResizeMode = ResizeMode.NoResize, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (Brush)FindResource("ParchmentBrush"), Content = panel };
        input.TextChanged += (_, _) => { if (TryNormaliseColour(input.Text, out var hex)) preview.Background = BrushFromHex(hex); };
        apply.Click += (_, _) =>
        {
            if (!TryNormaliseColour(input.Text, out var hex)) { MessageBox.Show(dialog, "Enter a valid colour in the form #RRGGBB.", "Invalid colour", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            _selectedDie.ColourHex = hex;
            dialog.DialogResult = true;
        };
        if (dialog.ShowDialog() == true) { _store.SavePools(_pools); RenderTray(); }
    }

    private static bool TryNormaliseColour(string text, out string hex)
    {
        hex = text.Trim();
        if (!hex.StartsWith('#')) hex = "#" + hex;
        if (hex.Length != 7) return false;
        try { _ = (Color)ColorConverter.ConvertFromString(hex)!; return true; }
        catch { return false; }
    }

    private void AddDie_Click(object sender, RoutedEventArgs e)
    {
        var choices = new ComboBox { Margin = new Thickness(0, 12, 0, 16), FontSize = 18, HorizontalContentAlignment = HorizontalAlignment.Center };
        foreach (var sides in new[] { 4, 6, 8, 10, 12, 20, 100 }) choices.Items.Add(new ComboBoxItem { Content = $"d{sides}", Tag = sides });
        choices.SelectedIndex = 5;
        var confirm = new Button { Content = "ADD DIE", Style = (Style)FindResource("AntiqueButton"), FontSize = 17, Padding = new Thickness(28, 9, 28, 9), HorizontalAlignment = HorizontalAlignment.Center };
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = "CHOOSE DIE TYPE", FontFamily = new FontFamily("ITC Korinna"), FontSize = 20, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(choices);
        panel.Children.Add(confirm);
        var dialog = new Window { Title = "Add Die", Width = 310, Height = 225, ResizeMode = ResizeMode.NoResize, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (Brush)FindResource("ParchmentBrush"), Content = panel };
        confirm.Click += (_, _) => dialog.DialogResult = true;
        if (dialog.ShowDialog() == true && choices.SelectedItem is ComboBoxItem choice && choice.Tag is int selectedSides) AddDie(selectedSides);
    }

    private void NewPool_Click(object sender, RoutedEventArgs e)
    {
        var pool = new DicePool { Name = $"Pool {_pools.Count + 1}", Dice = [new DieSpec()] };
        _pools.Add(pool);
        _store.SavePools(_pools);
        RefreshPoolList(pool);
    }

    private void DuplicatePool_Click(object sender, RoutedEventArgs e)
    {
        if (_pool is null) return;
        var copy = new DicePool
        {
            Name = _pool.Name + " Copy", CharacterName = _pool.CharacterName, Mode = _pool.Mode,
            OpponentAc = _pool.OpponentAc,
            Dice = _pool.Dice.Select(d => new DieSpec { Sides = d.Sides, Modifier = d.Modifier, Thac0 = d.Thac0, AttackBonus = d.AttackBonus, Label = d.Label, ColourHex = d.ColourHex }).ToList()
        };
        _pools.Add(copy);
        _store.SavePools(_pools);
        RefreshPoolList(copy);
    }

    private void DeletePool_Click(object sender, RoutedEventArgs e)
    {
        if (_pool is null || _pools.Count <= 1) return;
        var index = _pools.IndexOf(_pool);
        _pools.Remove(_pool);
        _pool = null;
        _store.SavePools(_pools);
        RefreshPoolList();
        PoolList.SelectedIndex = Math.Clamp(index - 1, 0, _pools.Count - 1);
    }

    private void AddDie(int sides)
    {
        if (_pool is null) return;
        var source = _selectedDie;
        var die = new DieSpec { Sides = sides, ColourHex = source?.ColourHex ?? Colours[0], Thac0 = source?.Thac0 ?? 20, AttackBonus = source?.AttackBonus ?? 0 };
        _pool.Dice.Add(die);
        _selectedDie = die;
        _store.SavePools(_pools);
        LoadDieControls();
        RenderTray();
        UpdatePoolHeader();
        PoolList.Items.Refresh();
    }

    private void RemoveDie_Click(object sender, RoutedEventArgs e)
    {
        if (_pool is null || _selectedDie is null || _pool.Dice.Count <= 1) return;
        _pool.Dice.Remove(_selectedDie);
        _selectedDie = _pool.Dice.FirstOrDefault();
        _store.SavePools(_pools);
        LoadDieControls();
        RenderTray();
        UpdatePoolHeader();
        RefreshPoolList(_pool);
    }

    private async void Roll_Click(object sender, RoutedEventArgs e)
    {
        if (_pool is null || _pool.Dice.Count == 0) return;
        foreach (var die in _pool.Dice) die.LastResult = _store.Roll(die.Sides);

        for (var frame = 0; frame < 7; frame++)
        {
            foreach (var die in _pool.Dice) die.DisplayResult = _store.Roll(die.Sides);
            RenderTray();
            await Task.Delay(70 + frame * 8);
        }
        foreach (var die in _pool.Dice) die.DisplayResult = die.LastResult;
        RenderTray();

        var total = _pool.Dice.Sum(d => d.LastResult + d.Modifier);
        var request = BuildRequestText();
        _lastResult = BuildResultText(total);
        ResultText.Text = _lastResult;
        PresentLastRoll(total);
        _store.AppendHistory(new DiceRollRecord(DateTime.Now, _pool.Name, _pool.CharacterName, request, _lastResult, total));
        RefreshRecentHistory();
    }

    private void PresentLastRoll(int total)
    {
        if (_pool is null) return;
        if (_pool.Mode == DicePoolMode.Attack && _pool.Dice.Count > 0)
        {
            LastRollEquation.Text = string.Join(Environment.NewLine, _pool.Dice.Select(die =>
            {
                var attackTotal = die.LastResult + die.Modifier + die.AttackBonus;
                return $"{AttackName(die)}: {die.LastResult} {FormatSignedTerm(die.Modifier + die.AttackBonus)} = {attackTotal}";
            }));
            LastRollOutcome.Text = string.Join(Environment.NewLine, _pool.Dice.Select(die =>
            {
                var attackTotal = die.LastResult + die.Modifier + die.AttackBonus;
                var hitAc = die.Thac0 - attackTotal;
                return $"Hit AC {hitAc}  •  vs AC {_pool.OpponentAc}  —  {AttackOutcome(die, hitAc)}";
            }));
        }
        else
        {
            LastRollEquation.Text = string.Join(" + ", _pool.Dice.Select(d => (d.LastResult + d.Modifier).ToString(CultureInfo.InvariantCulture))) + $" = {total}";
            LastRollOutcome.Text = _pool.Mode == DicePoolMode.Damage ? "DAMAGE TOTAL" : "TOTAL";
        }
    }

    private static string FormatSignedTerm(int value) => value >= 0 ? $"+ {value}" : $"− {Math.Abs(value)}";

    private void RefreshRecentHistory() => RecentHistoryList.ItemsSource = _store.ReadRecentHistory(10);

    private string BuildRequestText()
    {
        if (_pool is null) return string.Empty;
        var dice = string.Join(" + ", _pool.Dice.Select(DescribeDie));
        var who = string.IsNullOrWhiteSpace(_pool.CharacterName) ? _pool.Name : $"{_pool.CharacterName} — {_pool.Name}";
        if (_pool.Mode == DicePoolMode.Attack)
            return $"{who}: " + string.Join("; ", _pool.Dice.Select(d => $"{AttackName(d)} {DescribeDie(d)}, THAC0 {d.Thac0}, attack bonus {FormatModifier(d.AttackBonus)}, vs AC {_pool.OpponentAc}")) + ".";
        return $"{who}: Roll {dice}.";
    }

    private string BuildResultText(int total)
    {
        if (_pool is null) return string.Empty;
        if (_pool.Mode == DicePoolMode.Attack && _pool.Dice.Count > 0)
        {
            return string.Join("; ", _pool.Dice.Select(die =>
            {
                var attackTotal = die.LastResult + die.Modifier + die.AttackBonus;
                var hitAc = die.Thac0 - attackTotal;
                return $"{AttackName(die)}: d{die.Sides} [{die.LastResult}] {FormatSignedTerm(die.Modifier + die.AttackBonus)} = {attackTotal} · THAC0 {die.Thac0} · Hit AC {hitAc} · vs AC {_pool.OpponentAc}: {AttackOutcome(die, hitAc)}";
            }));
        }
        var rolled = string.Join("; ", _pool.Dice.Select(d => $"{DescribeDie(d)} [{d.LastResult}]{(d.Modifier != 0 ? $" => {d.LastResult + d.Modifier}" : string.Empty)}"));
        return $"{rolled} · Total {total}";
    }

    private static string DescribeDie(DieSpec d) => $"1d{d.Sides}{FormatModifier(d.Modifier)}{(string.IsNullOrWhiteSpace(d.Label) ? string.Empty : $" ({d.Label})")}";
    private static string AttackName(DieSpec die) => string.IsNullOrWhiteSpace(die.Label) ? $"d{die.Sides} attack" : die.Label;
    private string AttackOutcome(DieSpec die, int hitAc)
    {
        if (die.LastResult == 1) return "MISS";
        if (die.Sides == 20 && die.LastResult == 20) return "CRITICAL";
        return hitAc <= _pool!.OpponentAc ? "HIT" : "MISS";
    }
    private static string FormatModifier(int value) => value > 0 ? $"+{value}" : value < 0 ? value.ToString(CultureInfo.InvariantCulture) : string.Empty;

    private void CopyRequest_Click(object sender, RoutedEventArgs e)
    {
        var text = BuildRequestText();
        if (!string.IsNullOrWhiteSpace(text)) Clipboard.SetText(text);
    }

    private void CopyResult_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_lastResult)) Clipboard.SetText(_lastResult);
    }

    private void History_Click(object sender, RoutedEventArgs e)
    {
        var text = new TextBox { Text = _store.ReadHistory(), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(10) };
        var window = new Window { Title = "Dice Roll History", Width = 760, Height = 620, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = text };
        window.Show();
    }

    private void RenderTray()
    {
        TrayPanel.Children.Clear();
        if (_pool is null) return;
        foreach (var die in _pool.Dice)
        {
            var visual = CreateDieVisual(die);
            visual.MouseLeftButtonDown += (_, eventArgs) => { _selectedDie = die; LoadDieControls(); RenderTray(); eventArgs.Handled = true; };
            TrayPanel.Children.Add(visual);
        }
    }

    private void UpdatePoolHeader()
    {
        if (_pool is null) return;
        PoolTitle.Text = $"{_pool.Mode.ToString().ToUpperInvariant()} POOL";
        PoolSubtitle.Text = string.IsNullOrWhiteSpace(_pool.CharacterName) ? _pool.Name : _pool.CharacterName;
        PoolSummaryText.Text = _pool.Summary;
    }

    private FrameworkElement CreateDieVisual(DieSpec die, bool showNumber = true, double size = 166)
    {
        var grid = new Grid { Width = size, Height = size + 28, Margin = new Thickness(10), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Click to select and edit this die" };
        var assetName = $"d{die.Sides}";
        var faceUri = new Uri($"pack://application:,,,/CoreRulesLibrary;component/Assets/DiceRoller/DieFace-{assetName}.png", UriKind.Absolute);
        var maskUri = new Uri($"pack://application:,,,/CoreRulesLibrary;component/Assets/DiceRoller/DieFace-{assetName}-Mask.png", UriKind.Absolute);
        var mask = new ImageBrush(new BitmapImage(maskUri)) { Stretch = Stretch.Uniform };
        var colourLayer = new Border { Width = size, Height = size, Background = BrushFromHex(die.ColourHex), OpacityMask = mask, IsHitTestVisible = false };
        var face = new Image
        {
            Source = new BitmapImage(faceUri), Width = size, Height = size, Stretch = Stretch.Uniform, Opacity = 0.38, IsHitTestVisible = false,
            Effect = new DropShadowEffect { Color = die == _selectedDie ? Color.FromRgb(224, 175, 65) : Colors.Black, BlurRadius = die == _selectedDie ? 18 : 11, ShadowDepth = die == _selectedDie ? 0 : 6, Opacity = 0.95 }
        };
        var numeralTop = die.Sides switch { 4 => .43, 8 => .37, 12 or 20 => .38, 100 => .35, _ => .40 };
        var number = new TextBlock { Text = DisplayNumber(die), FontFamily = new FontFamily("Georgia"), FontSize = size * (die.Sides == 100 ? .22 : .285), FontWeight = FontWeights.Bold, Foreground = ContrastBrush(die.ColourHex), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, size * numeralTop, 0, 0), Effect = new DropShadowEffect { Color = Colors.White, BlurRadius = 2, ShadowDepth = 0, Opacity = 0.55 }, IsHitTestVisible = false };
        var label = new TextBlock { Text = string.IsNullOrWhiteSpace(die.Label) ? $"d{die.Sides}" : $"d{die.Sides} · {die.Label}", Foreground = Brushes.White, FontSize = Math.Max(11, size * .095), FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = size, Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 1, Opacity = 1 }, IsHitTestVisible = false };
        grid.Children.Add(colourLayer); grid.Children.Add(face);
        if (showNumber) grid.Children.Add(number);
        grid.Children.Add(label);
        return grid;
    }

    private static string DisplayNumber(DieSpec die)
    {
        var value = die.DisplayResult > 0 ? die.DisplayResult : (die.LastResult > 0 ? die.LastResult : 1);
        return die.Sides == 100 && value == 100 ? "00" : value.ToString(CultureInfo.InvariantCulture);
    }

    private static PointCollection PointsFor(int sides) => sides switch
    {
        4 => new([new Point(0.5, 0), new Point(1, 1), new Point(0, 1)]),
        6 => new([new Point(0.08, 0.08), new Point(0.92, 0.08), new Point(0.92, 0.92), new Point(0.08, 0.92)]),
        8 => new([new Point(0.5, 0), new Point(1, 0.5), new Point(0.5, 1), new Point(0, 0.5)]),
        10 or 100 => new([new Point(0.5,0),new Point(0.88,0.18),new Point(1,0.55),new Point(0.72,0.95),new Point(0.28,0.95),new Point(0,0.55),new Point(0.12,0.18)]),
        12 => new([new Point(0.35,0),new Point(0.65,0),new Point(0.92,0.22),new Point(1,0.55),new Point(0.78,0.9),new Point(0.5,1),new Point(0.22,0.9),new Point(0,0.55),new Point(0.08,0.22)]),
        20 => new([new Point(0.5,0),new Point(0.82,0.12),new Point(1,0.42),new Point(0.92,0.75),new Point(0.65,1),new Point(0.35,1),new Point(0.08,0.75),new Point(0,0.42),new Point(0.18,0.12)]),
        _ => new([new Point(0.5, 0), new Point(1, 0.5), new Point(0.5, 1), new Point(0, 0.5)])
    };

    private static Brush BrushFromHex(string hex)
    {
        try { return (Brush)new BrushConverter().ConvertFromString(hex)!; }
        catch { return Brushes.Beige; }
    }

    private static Brush ContrastBrush(string hex)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex)!;
            return (c.R * 299 + c.G * 587 + c.B * 114) / 1000 > 145 ? Brushes.Black : Brushes.White;
        }
        catch { return Brushes.Black; }
    }
}
