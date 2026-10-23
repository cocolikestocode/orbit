namespace Orbit.App.Views;

using System.Windows;
using System.Windows.Input;

public partial class PasswordInputDialog : Window
{
    private bool _isShowingPassword;

    public bool Confirmed { get; private set; }
    public string Password { get; private set; } = string.Empty;

    public PasswordInputDialog(string title, string message)
    {
        InitializeComponent();
        Topmost = true;
        TxtTitle.Text = title;
        TxtSubtitle.Text = message;
        Loaded += (s, e) =>
        {
            Activate();
            Focus();
            PbPassword.Focus();
        };
    }

    public static (bool Confirmed, string Password) Prompt(string title, string message, Window? owner = null)
    {
        var dialog = new PasswordInputDialog(title, message)
        {
            Topmost = true
        };
        if (owner != null && owner.IsVisible)
        {
            dialog.Owner = owner;
        }

        dialog.Activate();
        bool? result = dialog.ShowDialog();
        return (dialog.Confirmed, dialog.Password);
    }

    private void OnToggleShowClicked(object sender, RoutedEventArgs e)
    {
        _isShowingPassword = !_isShowingPassword;
        if (_isShowingPassword)
        {
            TbPassword.Text = PbPassword.Password;
            PbPassword.Visibility = Visibility.Collapsed;
            TbPassword.Visibility = Visibility.Visible;
            TbPassword.Focus();
            TbPassword.CaretIndex = TbPassword.Text.Length;
        }
        else
        {
            PbPassword.Password = TbPassword.Text;
            TbPassword.Visibility = Visibility.Collapsed;
            PbPassword.Visibility = Visibility.Visible;
            PbPassword.Focus();
        }
    }

    private void OnPasswordKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnConfirmClicked(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            OnCancelClicked(sender, e);
        }
    }

    private void OnConfirmClicked(object sender, RoutedEventArgs e)
    {
        string pwd = _isShowingPassword ? TbPassword.Text : PbPassword.Password;
        if (string.IsNullOrEmpty(pwd))
        {
            TxtError.Text = "Please enter a password.";
            return;
        }

        Confirmed = true;
        Password = pwd;
        DialogResult = true;
        Close();
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
        Close();
    }
}
