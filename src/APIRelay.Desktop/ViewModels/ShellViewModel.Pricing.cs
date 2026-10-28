using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using APIRelay.Desktop.Services;

namespace APIRelay.Desktop.ViewModels;

internal sealed partial class ShellViewModel
{
    private string priceSearch = "";
    private ModelCostConfig? selectedPrice;
    private bool hasPriceErrors;
    public bool HasPriceErrors
    {
        get => hasPriceErrors;
        set { Set(ref hasPriceErrors, value); CommandManager.InvalidateRequerySuggested(); }
    }
    public ObservableCollection<ModelCostConfig> Prices { get; } = [];
    public ICollectionView PricesView { get; private set; } = null!;
    public string PriceSearch { get => priceSearch; set { Set(ref priceSearch, value); PricesView.Refresh(); } }
    public ModelCostConfig? SelectedPrice { get => selectedPrice; set => Set(ref selectedPrice, value); }
    public ICommand SavePricesCommand { get; private set; } = null!;
    public ICommand RefreshPricesCommand { get; private set; } = null!;
    public ICommand ResetPricesCommand { get; private set; } = null!;
    public ICommand AddPriceCommand { get; private set; } = null!;
    public ICommand RemovePriceCommand { get; private set; } = null!;
    private void InitializePricingCommands()
    {
        foreach (var price in engine.ModelCosts) Prices.Add(price);
        PricesView = CollectionViewSource.GetDefaultView(Prices);
        PricesView.Filter = o => o is ModelCostConfig p && p.ModelName.Contains(PriceSearch, StringComparison.OrdinalIgnoreCase);
        SavePricesCommand = Command(async () =>
        {
            engine.SavePrices(Prices);
            await RefreshRecordsAsync();
            Notify(T["Saved"]);
        }, () => !HasPriceErrors);
        RefreshPricesCommand = Command(async () =>
        {
            var reference = await ModelsDevPricing.FetchAsync(metadataClient, lifetime.Token);
            var existing = Prices.Where(p => p.ModelName.Length > 0).GroupBy(p => p.ModelName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
            foreach (var price in reference)
            {
                if (existing.TryGetValue(price.ModelName, out var current) && current.Overwrite) continue;
                existing[price.ModelName] = new()
                {
                    ModelName = price.ModelName,
                    InputCostPerMillion = price.InputCostPerMillion,
                    ReferencePrice = true,
                    OutputCostPerMillion = price.OutputCostPerMillion,
                    CacheHitCostPerMillion = price.CacheHitCostPerMillion ?? price.InputCostPerMillion,
                    CacheCreationCostPerMillion = price.CacheCreationCostPerMillion ?? price.InputCostPerMillion
                };
            }
            Prices.Clear();
            foreach (var price in existing.Values.OrderBy(p => p.ModelName)) Prices.Add(price);
            Notify(T.Chinese ? "参考价格已更新，点击保存后生效。" : "Reference prices updated. Save to apply.");
        });
        ResetPricesCommand = Command(() =>
        {
            if (!DialogService.Confirm(T["ResetConfirm"])) return;
            Prices.Clear();
            foreach (var price in DefaultPrices()) Prices.Add(price);
            Notify(T.Chinese ? "默认价格已载入，点击保存后生效。" : "Defaults loaded. Save to apply.");
        });
        AddPriceCommand = Command(() =>
        {
            PriceSearch = "";
            SelectedPrice = new() { ModelName = "", Overwrite = true };
            Prices.Insert(0, SelectedPrice);
        });
        RemovePriceCommand = Command(() => { if (SelectedPrice != null) Prices.Remove(SelectedPrice); }, () => SelectedPrice != null);
    }
}
