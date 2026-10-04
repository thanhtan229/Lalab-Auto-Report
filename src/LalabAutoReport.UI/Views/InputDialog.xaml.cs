using System.Windows;

namespace LalabAutoReport.UI.Views;

public partial class InputDialog : Window
{
    public string InputText => txtInput.Text;

    public InputDialog(string title, string prompt, string defaultValue = "")
    {
        InitializeComponent();
        Title = title;
        lblPrompt.Text = prompt;
        txtInput.Text = defaultValue;
        Loaded += (_, _) =>
        {
            txtInput.Focus();
            txtInput.SelectAll();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    public static string? Show(Window? owner, string title, string prompt, string defaultValue = "")
    {
        var dlg = new InputDialog(title, prompt, defaultValue);
        var targetOwner = owner ?? Application.Current?.MainWindow;
        if (targetOwner != null && targetOwner.IsVisible)
        {
            dlg.Owner = targetOwner;
        }
        else
        {
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        return dlg.ShowDialog() == true ? dlg.InputText : null;
    }
}
