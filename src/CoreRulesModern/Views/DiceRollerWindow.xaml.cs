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
    private string _lastResult = string.Empty;
    private static readonly string[] Colours = ["#E9D8AE", "#A72222", "#235DA8", "#2C7A43", "#6D3B91", "#C28C24", "#222222", "#EFEFEF", "#168A91", "#B04470"];

    public DiceRollerWindow()
    {
        InitializeComponent();
        _pools = _store.LoadPools();
        BuildColourButtons();
        BuildAddDieButtons();
        RefreshRecentHistory();
        RefreshPoolList();
        if (_pools.Count > 0) PoolList.SelectedIndex = 0;
        Closed += (_, _) => _store.SavePools(_pools);
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
            Thac0Box.Text = _pool.Thac0.ToString(CultureInfo.InvariantCulture);
            AcBox.Text = _pool.OpponentAc.ToString(CultureInfo.InvariantCulture);
            AttackModifierBox.Text = _pool.AttackModifier.ToString(CultureInfo.InvariantCulture);
            PoolTitle.Text = _pool.Name;
            PoolSubtitle.Text = _pool.CharacterName;
            PoolSummaryText.Text = $"{_pool.Mode}  •  {_pool.Summary}";
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
                return;
            }
            for (var i = 0; i < DieTypeBox.Items.Count; i++)
                if (DieTypeBox.Items[i] is ComboBoxItem item && Convert.ToInt32(item.Tag, CultureInfo.InvariantCulture) == _selectedDie.Sides) DieTypeBox.SelectedIndex = i;
            DieLabelBox.Text = _selectedDie.Label;
            DieModifierBox.Text = _selectedDie.Modifier.ToString(CultureInfo.InvariantCulture);
        }
        finally { _loading = false; }
    }

    private void PoolField_Changed(object sender, EventArgs e)
    {
        if (_loading || _pool is null) return;
        _pool.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? "Unnamed Pool" : NameBox.Text.Trim();
        _pool.CharacterName = CharacterBox.Text.Trim();
        if (ModeBox.SelectedIndex >= 0) _pool.Mode = (DicePoolMode)ModeBox.SelectedIndex;
        if (int.TryParse(Thac0Box.Text, out var thac0)) _pool.Thac0 = thac0;
        if (int.TryParse(AcBox.Text, out var ac)) _pool.OpponentAc = ac;
        if (int.TryParse(AttackModifierBox.Text, out var attackMod)) _pool.AttackModifier = attackMod;
        PoolTitle.Text = _pool.Name;
        PoolSubtitle.Text = _pool.CharacterName;
        PoolSummaryText.Text = $"{_pool.Mode}  •  {_pool.Summary}";
        AttackOptions.Visibility = _pool.Mode == DicePoolMode.Attack ? Visibility.Visible : Visibility.Collapsed;
        _store.SavePools(_pools);
        RefreshPoolList(_pool);
    }

    private void DieField_Changed(object sender, EventArgs e)
    {
        if (_loading || _selectedDie is null) return;
        if (DieTypeBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var sides)) _selectedDie.Sides = sides;
        _selectedDie.Label = DieLabelBox.Text.Trim();
        if (int.TryParse(DieModifierBox.Text, out var modifier)) _selectedDie.Modifier = modifier;
        _store.SavePools(_pools);
        RenderTray();
        if (_pool is not null) PoolSummaryText.Text = $"{_pool.Mode}  •  {_pool.Summary}";
        RefreshPoolList(_pool);
    }

    private void BuildColourButtons()
    {
        foreach (var hex in Colours)
        {
            var button = new Button { Width = 30, Height = 30, Margin = new Thickness(3), Tag = hex, Background = BrushFromHex(hex), ToolTip = hex };
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

    private void BuildAddDieButtons()
    {
        foreach (var sides in new[] { 4, 6, 8, 10, 12, 20, 100 })
        {
            var preview = new DieSpec { Sides = sides, ColourHex = "#D8C38E" };
            var visual = CreateDieVisual(preview);
            visual.Width = 82;
            visual.Height = 104;
            visual.Margin = new Thickness(5, 0, 5, 0);
            visual.ToolTip = $"Add a d{sides}";
            visual.MouseLeftButtonDown += (_, _) => AddDie(sides);
            AddDiePanel.Children.Add(visual);
        }
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
            Thac0 = _pool.Thac0, OpponentAc = _pool.OpponentAc, AttackModifier = _pool.AttackModifier,
            Dice = _pool.Dice.Select(d => new DieSpec { Sides = d.Sides, Modifier = d.Modifier, Label = d.Label, ColourHex = d.ColourHex }).ToList()
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
        var die = new DieSpec { Sides = sides, ColourHex = source?.ColourHex ?? Colours[0] };
        _pool.Dice.Add(die);
        _selectedDie = die;
        _store.SavePools(_pools);
        LoadDieControls();
        RenderTray();
        PoolSummaryText.Text = $"{_pool.Mode}  •  {_pool.Summary}";
        RefreshPoolList(_pool);
    }

    private void RemoveDie_Click(object sender, RoutedEventArgs e)
    {
        if (_pool is null || _selectedDie is null || _pool.Dice.Count <= 1) return;
        _pool.Dice.Remove(_selectedDie);
        _selectedDie = _pool.Dice.FirstOrDefault();
        _store.SavePools(_pools);
        LoadDieControls();
        RenderTray();
        PoolSummaryText.Text = $"{_pool.Mode}  •  {_pool.Summary}";
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
            var d20 = _pool.Dice.FirstOrDefault(d => d.Sides == 20) ?? _pool.Dice[0];
            var modifier = d20.Modifier + _pool.AttackModifier;
            var attackTotal = d20.LastResult + modifier;
            var hitAc = _pool.Thac0 - attackTotal;
            var hit = d20.LastResult != 1 && (d20.LastResult == 20 || hitAc <= _pool.OpponentAc);
            LastRollEquation.Text = $"{d20.LastResult} {FormatSignedTerm(modifier)} = {attackTotal}";
            LastRollOutcome.Text = $"Hit AC {hitAc}  •  vs AC {_pool.OpponentAc}  —  {(d20.LastResult == 20 ? "CRITICAL" : hit ? "HIT" : "MISS")}";
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
            return $"{who}: Roll {dice}{FormatModifier(_pool.AttackModifier)}. THAC0 {_pool.Thac0} vs AC {_pool.OpponentAc}.";
        return $"{who}: Roll {dice}.";
    }

    private string BuildResultText(int total)
    {
        if (_pool is null) return string.Empty;
        var rolled = string.Join("; ", _pool.Dice.Select(d => $"{DescribeDie(d)} [{d.LastResult}]{(d.Modifier != 0 ? $" => {d.LastResult + d.Modifier}" : string.Empty)}"));
        if (_pool.Mode == DicePoolMode.Attack && _pool.Dice.Count > 0)
        {
            var d20 = _pool.Dice.FirstOrDefault(d => d.Sides == 20) ?? _pool.Dice[0];
            var attackTotal = d20.LastResult + d20.Modifier + _pool.AttackModifier;
            var hitAc = _pool.Thac0 - attackTotal;
            var hit = d20.LastResult == 1 ? false : d20.LastResult == 20 || hitAc <= _pool.OpponentAc;
            return $"{rolled} · Attack total {attackTotal} · Hit AC {hitAc} · vs AC {_pool.OpponentAc}: {(hit ? "HIT" : "MISS")}";
        }
        return $"{rolled} · Total {total}";
    }

    private static string DescribeDie(DieSpec d) => $"1d{d.Sides}{FormatModifier(d.Modifier)}{(string.IsNullOrWhiteSpace(d.Label) ? string.Empty : $" ({d.Label})")}";
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
            visual.MouseLeftButtonDown += (_, _) => { _selectedDie = die; LoadDieControls(); RenderTray(); };
            TrayPanel.Children.Add(visual);
        }
    }

    private FrameworkElement CreateDieVisual(DieSpec die)
    {
        const double size = 132;
        var grid = new Grid { Width = size, Height = size + 30, Margin = new Thickness(12), Cursor = Cursors.Hand, ToolTip = "Click to edit this die" };
        var assetName = $"d{die.Sides}";
        var faceUri = new Uri($"pack://application:,,,/CoreRulesLibrary;component/Assets/DiceRoller/DieFace-{assetName}.png", UriKind.Absolute);
        var maskUri = new Uri($"pack://application:,,,/CoreRulesLibrary;component/Assets/DiceRoller/DieFace-{assetName}-Mask.png", UriKind.Absolute);
        var mask = new ImageBrush(new BitmapImage(maskUri)) { Stretch = Stretch.Uniform };
        var colourLayer = new Border { Width = size, Height = size, Background = BrushFromHex(die.ColourHex), OpacityMask = mask, IsHitTestVisible = false };
        var face = new Image
        {
            Source = new BitmapImage(faceUri), Width = size, Height = size, Stretch = Stretch.Uniform, Opacity = 0.64, IsHitTestVisible = false,
            Effect = new DropShadowEffect { Color = die == _selectedDie ? Colors.Gold : Colors.Black, BlurRadius = die == _selectedDie ? 15 : 9, ShadowDepth = die == _selectedDie ? 0 : 5, Opacity = 0.9 }
        };
        var number = new TextBlock { Text = DisplayNumber(die), FontFamily = new FontFamily("Georgia"), FontSize = die.Sides == 100 ? 30 : 39, FontWeight = FontWeights.Bold, Foreground = ContrastBrush(die.ColourHex), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 39, 0, 0), Effect = new DropShadowEffect { Color = Colors.White, BlurRadius = 2, ShadowDepth = 0, Opacity = 0.5 }, IsHitTestVisible = false };
        var label = new TextBlock { Text = string.IsNullOrWhiteSpace(die.Label) ? $"d{die.Sides}" : $"d{die.Sides} · {die.Label}", Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 135, Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 1, Opacity = 1 }, IsHitTestVisible = false };
        grid.Children.Add(colourLayer); grid.Children.Add(face); grid.Children.Add(number); grid.Children.Add(label);
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
