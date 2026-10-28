using System.Windows.Controls;
using System.Windows;
using APIRelay.Desktop.ViewModels;
namespace APIRelay.Desktop.Views.Pages;
public partial class ProvidersView : UserControl
{
    public ProvidersView() => InitializeComponent();
    private bool updatingKey;
    private void KeyBox_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        updatingKey = true;
        try { ((PasswordBox)sender).Password = (e.NewValue as ProviderDraft)?.ApiKey ?? ""; }
        finally { updatingKey = false; }
    }
    private void KeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!updatingKey && sender is PasswordBox { DataContext: ProviderDraft draft } box) draft.ApiKey = box.Password;
    }
}
