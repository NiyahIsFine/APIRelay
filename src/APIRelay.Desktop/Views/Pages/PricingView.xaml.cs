using System.Windows.Controls;
using System.Windows;
using APIRelay.Desktop.ViewModels;
namespace APIRelay.Desktop.Views.Pages;
public partial class PricingView : UserControl
{
    private readonly HashSet<ValidationError> errors = [];
    public PricingView()
    {
        InitializeComponent();
        AddHandler(Validation.ErrorEvent, new EventHandler<ValidationErrorEventArgs>((_, e) =>
        {
            if (e.Action == ValidationErrorEventAction.Added) errors.Add(e.Error);
            else errors.Remove(e.Error);
            if (DataContext is ShellViewModel model) model.HasPriceErrors = errors.Count > 0;
        }));
    }
    private void PriceGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit && e.Column is not DataGridCheckBoxColumn && e.Row.Item is ModelCostConfig price)
            price.Overwrite = true;
    }
}
