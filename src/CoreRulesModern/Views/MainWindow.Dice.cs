using System.Windows;
using System.Windows.Controls;

namespace CoreRulesModern.Views;

public partial class MainWindow
{
    private DiceRollerWindow? _diceRollerWindow;
    private bool _diceButtonAdded;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_diceButtonAdded) return;
        _diceButtonAdded = true;

        if (ApplicationTitlePrefixText.Parent is not StackPanel titlePanel || titlePanel.Parent is not DockPanel header) return;
        var toolbar = header.Children.OfType<StackPanel>().FirstOrDefault(panel => panel != titlePanel && DockPanel.GetDock(panel) == Dock.Right);
        if (toolbar is null) return;

        var button = new Button
        {
            Content = "Dice roller…",
            FontSize = 11,
            Height = 22,
            MinHeight = 0,
            Padding = new Thickness(7, 0, 7, 0),
            Margin = new Thickness(2, 0, 2, 0),
            ToolTip = "Open the AD&D dice pool roller"
        };
        button.Click += DiceRoller_Click;
        toolbar.Children.Insert(0, button);
    }

    private void DiceRoller_Click(object sender, RoutedEventArgs e)
    {
        if (_diceRollerWindow is { IsLoaded: true })
        {
            if (_diceRollerWindow.WindowState == WindowState.Minimized) _diceRollerWindow.WindowState = WindowState.Normal;
            _diceRollerWindow.Activate();
            return;
        }

        _diceRollerWindow = new DiceRollerWindow { Owner = this };
        _diceRollerWindow.Closed += (_, _) => _diceRollerWindow = null;
        _diceRollerWindow.Show();
    }
}
