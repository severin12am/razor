using System.Windows;

namespace ResourcePanel;

public partial class ConfirmWindow : Window
{
    readonly string? _required;

    ConfirmWindow(string title, string message, IReadOnlyList<string>? bullets, string yesText, string noText, string? requiredPhrase)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        YesButton.Content = yesText;
        NoButton.Content = noText;
        if (bullets is { Count: > 0 })
            BulletText.Text = string.Join("\n", bullets.Select(static line => "• " + line));
        else
            BulletText.Visibility = Visibility.Collapsed;
        _required = requiredPhrase;
        if (!string.IsNullOrWhiteSpace(requiredPhrase))
        {
            PhraseLabel.Visibility = Visibility.Visible;
            PhraseBox.Visibility = Visibility.Visible;
            PhraseLabel.Text = "Type " + requiredPhrase + " to continue.";
            YesButton.IsEnabled = false;
        }
    }

    public string Phrase => PhraseBox.Text.Trim();

    public static bool Show(Window owner, string title, string message, IReadOnlyList<string>? bullets, string yesText, string noText = "Cancel", string? requiredPhrase = null)
    {
        var window = new ConfirmWindow(title, message, bullets, yesText, noText, requiredPhrase) { Owner = owner };
        return window.ShowDialog() == true;
    }

    public static string? ShowForPhrase(Window owner, string title, string message, IReadOnlyList<string>? bullets, string requiredPhrase)
    {
        var window = new ConfirmWindow(title, message, bullets, "Apply", "Cancel", requiredPhrase) { Owner = owner };
        return window.ShowDialog() == true ? window.Phrase : null;
    }

    void Phrase_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_required != null)
            YesButton.IsEnabled = string.Equals(PhraseBox.Text.Trim(), _required, StringComparison.Ordinal);
    }

    void Yes_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    void No_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
