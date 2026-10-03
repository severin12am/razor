using System.Windows;

namespace ResourcePanel;

public partial class FreeUpWindow : Window
{
    public FreeUpRequest? Request { get; private set; }
    readonly List<FindingRow> _rows;

    public FreeUpWindow(FreeUpPlan plan)
    {
        InitializeComponent();
        _rows = plan.Items.Select(item => new FindingRow
        {
            Finding = item,
            IsChecked = item.CheckedByDefault
        }).ToList();
        var pausing = _rows.Where(row => row.Finding.CheckedByDefault).ToList();
        var keeping = _rows.Where(row => !row.Finding.CheckedByDefault).ToList();
        PauseItems.ItemsSource = pausing;
        KeepItems.ItemsSource = keeping;
        EmptyPause.Visibility = pausing.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PowerBox.IsEnabled = plan.CanSetHighPerformance && !plan.AlreadyHighPerformance;
        PowerBox.IsChecked = PowerBox.IsEnabled;
        GameBox.IsEnabled = !plan.GameModeAlreadyOn;
        GameBox.IsChecked = GameBox.IsEnabled;
        var notes = new List<string>();
        if (!plan.CanSetHighPerformance)
            notes.Add("This PC has no High performance power plan.");
        else if (plan.AlreadyHighPerformance)
            notes.Add("Already on the High performance power plan.");
        if (plan.GameModeAlreadyOn)
            notes.Add("Windows Game Mode is already on.");
        AlreadyText.Text = string.Join(" ", notes);
        if (pausing.Count == 0 && !PowerBox.IsEnabled && !GameBox.IsEnabled)
            GoButton.IsEnabled = false;
    }

    void Go_Click(object sender, RoutedEventArgs e)
    {
        Request = new FreeUpRequest
        {
            Selected = _rows.Where(row => row.IsChecked).Select(row => row.Finding).ToList(),
            EndInsteadOfPause = false,
            SetHighPerformance = PowerBox.IsChecked == true && PowerBox.IsEnabled,
            EnableGameMode = GameBox.IsChecked == true && GameBox.IsEnabled
        };
        DialogResult = true;
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
