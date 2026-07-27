using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GabrielCapelettoStore.Hub.Web;

namespace GabrielCapelettoStore.Hub.ViewModels;

/// <summary>One selectable delivery-mode card in a web app's delivery-config modal.</summary>
public sealed partial class WebDeliveryOptionViewModel : ObservableObject
{
    private readonly Action<WebDeliveryOptionViewModel> _select;

    public WebDeliveryOptionViewModel(
        WebDeliveryMode mode, string label, string description, bool selected,
        Action<WebDeliveryOptionViewModel> select)
    {
        Mode = mode;
        Label = label;
        Description = description;
        _select = select;
        IsSelected = selected;
    }

    public WebDeliveryMode Mode { get; }
    public string Label { get; }
    public string Description { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Select() => _select(this);
}
