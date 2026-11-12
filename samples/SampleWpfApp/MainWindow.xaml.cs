using System.Windows;

namespace SampleWpfApp;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void RefuseKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        e.Handled = true;
    }

    private void EditableComboKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
            EditableKeyboardComboBox.Tag = int.Parse(EditableKeyboardComboBox.Tag.ToString()!) + 1;
    }

    private void RenderedInlineClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        RenderedInlineTextTarget.Tag = int.Parse(RenderedInlineTextTarget.Tag.ToString()!) + 1;
    }
}
