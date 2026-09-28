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
        txtInput.SelectAll();
        txtInput.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    public static string? Show(Window owner, string title, string prompt, string defaultValue = "")
    {
        var dlg = new InputDialog(title, prompt, defaultValue)
        {
            Owner = owner
        };
        return dlg.ShowDialog() == true ? dlg.InputText : null;
    }
}
