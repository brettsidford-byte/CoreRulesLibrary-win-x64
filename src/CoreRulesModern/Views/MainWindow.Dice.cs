using System.Windows;

namespace CoreRulesModern.Views;

public partial class MainWindow
{
    private DiceRollerWindow? _diceRollerWindow;

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
