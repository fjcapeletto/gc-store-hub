using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GabrielCapelettoStore.Hub.ViewModels;

/// <summary>One page of the shelf — up to a 3×3 grid of tiles (phone-style paging).</summary>
public sealed class ShelfPageViewModel
{
    public ShelfPageViewModel(IReadOnlyList<ShelfItemViewModel> items) => Items = items;

    public IReadOnlyList<ShelfItemViewModel> Items { get; }
}

/// <summary>A page-indicator dot; clicking it jumps to that page.</summary>
public sealed partial class PageDotViewModel : ObservableObject
{
    private readonly Action<int> _go;

    public PageDotViewModel(int index, Action<int> go)
    {
        Index = index;
        _go = go;
    }

    public int Index { get; }

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    [RelayCommand]
    private void Go() => _go(Index);
}
