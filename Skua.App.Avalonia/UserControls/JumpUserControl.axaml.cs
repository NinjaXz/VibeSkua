using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Skua.App.Avalonia.ViewModels;

namespace Skua.App.Avalonia.UserControls;

public partial class JumpUserControl : UserControl
{
    public JumpUserControl()
    {
        InitializeComponent();
        Cells.DropDownOpened += Cells_DropDownOpened;
        AddHandler(InputElement.KeyDownEvent, Cells_KeyDown, RoutingStrategies.Tunnel);
    }

    private void Cells_DropDownOpened(object? sender, System.EventArgs e)
    {
        if (DataContext is JumpViewModel vm)
            vm.UpdateCellsCommand.Execute(null);
    }

    private void Cells_KeyDown(object? sender, KeyEventArgs e)
    {
        // The open ComboBox popup can own keyboard focus instead of the ComboBox itself.
        if ((!Cells.IsFocused && !Cells.IsDropDownOpen) || Cells.ItemCount == 0)
            return;

        int direction = e.Key switch
        {
            Key.Up => -1,
            Key.Down => 1,
            _ => 0
        };

        if (direction == 0)
            return;

        int selectedIndex = Cells.SelectedIndex;
        if (selectedIndex < 0)
            selectedIndex = direction > 0 ? -1 : Cells.ItemCount;

        int nextIndex = System.Math.Clamp(selectedIndex + direction, 0, Cells.ItemCount - 1);

        // Commit the selection immediately rather than leaving it as uncommitted
        // dropdown preview navigation. The view model handles optional auto-jump.
        Cells.IsDropDownOpen = false;
        if (nextIndex != Cells.SelectedIndex)
            Cells.SelectedIndex = nextIndex;

        Cells.Focus();
        e.Handled = true;
    }
}