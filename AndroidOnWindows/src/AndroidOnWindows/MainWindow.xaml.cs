using System.Windows;
using System.Windows.Input;
using AndroidOnWindows.Models;
using AndroidOnWindows.ViewModels;

namespace AndroidOnWindows;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;
        Loaded += async (_, _) => await _vm.CheckEnvironmentAsync();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        await _vm.InstallApksAsync(files);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Key mapping only fires while a text field is not capturing input.
        if (!_vm.IsMapping) return;
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;

        _vm.HandleKey(e.Key.ToString());
    }
}
