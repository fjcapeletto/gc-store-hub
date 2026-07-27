using System;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GabrielCapelettoStore.Hub.ViewModels;

/// <summary>One item in a web app's inbox (a pushed deal): an optional preview thumbnail, a title, and
/// a link opened in the browser.</summary>
public sealed partial class InboxItemViewModel : ObservableObject
{
    private readonly Action<string> _open;

    public InboxItemViewModel(
        string title, string url, string? publishedAt, string? imageUrl,
        Action<string> open, Func<string, Task<Bitmap?>> resolveImage)
    {
        Title = title;
        Url = url;
        PublishedAt = publishedAt;
        _open = open;

        if (!string.IsNullOrWhiteSpace(imageUrl))
        {
            // Same preview the toast used; served from our domain, cached. Fetch off the UI thread and
            // fill in when ready (started on the UI thread, so the continuation lands back there).
            _ = LoadImageAsync(imageUrl!, resolveImage);
        }
    }

    public string Title { get; }
    public string Url { get; }
    public string? PublishedAt { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    public partial Bitmap? Image { get; set; }

    public bool HasImage => Image is not null;

    private async Task LoadImageAsync(string imageUrl, Func<string, Task<Bitmap?>> resolveImage)
    {
        try
        {
            Image = await resolveImage(imageUrl);
        }
        catch
        {
            // Best effort — a missing preview just leaves the row without a thumbnail.
        }
    }

    [RelayCommand]
    private void Open() => _open(Url);
}
