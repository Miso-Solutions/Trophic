using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Trophic.Behaviors;

public static class DoubleClickBehavior
{
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.RegisterAttached("Command", typeof(ICommand), typeof(DoubleClickBehavior),
            new PropertyMetadata(null, OnCommandChanged));

    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.RegisterAttached("CommandParameter", typeof(object), typeof(DoubleClickBehavior));

    public static ICommand? GetCommand(DependencyObject obj) => (ICommand?)obj.GetValue(CommandProperty);
    public static void SetCommand(DependencyObject obj, ICommand? value) => obj.SetValue(CommandProperty, value);

    public static object? GetCommandParameter(DependencyObject obj) => obj.GetValue(CommandParameterProperty);
    public static void SetCommandParameter(DependencyObject obj, object? value) => obj.SetValue(CommandParameterProperty, value);

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Control control) return;

        if (e.OldValue != null)
            control.MouseDoubleClick -= OnMouseDoubleClick;

        if (e.NewValue != null)
            control.MouseDoubleClick += OnMouseDoubleClick;
    }

    private static void OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DependencyObject d) return;

        var command = GetCommand(d);
        var parameter = GetCommandParameter(d);

        if (command?.CanExecute(parameter) == true)
            command.Execute(parameter);
    }
}
