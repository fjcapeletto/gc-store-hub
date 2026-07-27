using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GabrielCapelettoStore.Hub.ViewModels;

/// <summary>One item in a web app's inbox (a pushed deal): a title + a link opened in the browser.</summary>
public sealed partial class InboxItemViewModel : ObservableObject
{
    private readonly Action<string> _open;

    public InboxItemViewModel(string title, string url, string? publishedAt, Action<string> open)
    {
        Title = title;
        Url = url;
        PublishedAt = publishedAt;
        _open = open;
    }

    public string Title { get; }
    public string Url { get; }
    public string? PublishedAt { get; }

    [RelayCommand]
    private void Open() => _open(Url);
}
