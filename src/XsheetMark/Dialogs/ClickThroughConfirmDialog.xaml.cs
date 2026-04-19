using System.Windows;

namespace XsheetMark.Dialogs;

public partial class ClickThroughConfirmDialog : Window
{
    public bool Confirmed { get; private set; }
    public bool DoNotShowAgain => DontShowAgain.IsChecked == true;

    public ClickThroughConfirmDialog()
    {
        InitializeComponent();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close();
    }
}
