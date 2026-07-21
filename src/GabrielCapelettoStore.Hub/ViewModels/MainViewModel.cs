using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GabrielCapelettoStore.Hub.Catalog;

namespace GabrielCapelettoStore.Hub.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ICatalogSource _catalogSource;

    public MainViewModel(ICatalogSource catalogSource)
    {
        _catalogSource = catalogSource;
    }

    /// <summary>Design-time constructor: seeds the previewer with sample shelves.</summary>
    public MainViewModel() : this(new DesignCatalogSource())
    {
        foreach (var app in DesignCatalogSource.SampleCatalog.Apps)
        {
            Apps.Add(app);
        }

        AppCount = Apps.Count;
    }

    [ObservableProperty]
    public partial string StoreName { get; set; } = "Gabriel Capeletto Store";

    public ObservableCollection<CatalogApp> Apps { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCatalog))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(ShowCatalog))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCatalog))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial int AppCount { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool ShowCatalog => !IsLoading && !HasError && AppCount > 0;

    public bool ShowEmptyState => !IsLoading && !HasError && AppCount == 0;

    /// <summary>Fetches the catalog and rebuilds the shelves. Never throws — failures surface as an error state.</summary>
    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var manifest = await _catalogSource.GetCatalogAsync();

            Apps.Clear();
            foreach (var app in manifest.Apps)
            {
                Apps.Add(app);
            }

            AppCount = Apps.Count;
        }
        catch (Exception ex)
        {
            Apps.Clear();
            AppCount = 0;
            ErrorMessage = $"Couldn't reach the store catalog.\n{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private Task Reload() => LoadAsync();
}
