using CommunityToolkit.Mvvm.ComponentModel;

namespace GabrielCapelettoStore.Hub.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string StoreName { get; set; } = "Gabriel Capeletto Store";

    [ObservableProperty]
    public partial string EmptyStateMessage { get; set; } = "The shelves are empty — for now.";

    [ObservableProperty]
    public partial string StatusLine { get; set; } = "Hub is running. Waiting for the catalog.";
}
