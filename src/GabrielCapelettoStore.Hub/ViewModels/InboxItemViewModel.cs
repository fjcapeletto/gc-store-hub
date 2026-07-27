using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GabrielCapelettoStore.Hub.ViewModels;

/// <summary>One item in a web app's inbox (a pushed deal): an optional preview thumbnail, a title, a
/// link opened in the browser, plus client-side mark-read / delete actions.</summary>
public sealed partial class InboxItemViewModel : ObservableObject
{
    private readonly Action<string> _open;
    private readonly Action<string> _markRead;
    private readonly Action<string> _markUnread;
    private readonly Action<string> _delete;

    public InboxItemViewModel(
        string id, string title, string url, string? publishedAt, string? imageUrl, bool isRead,
        Action<string> open, Func<string, Task<Bitmap?>> resolveImage,
        Action<string> markRead, Action<string> markUnread, Action<string> delete)
    {
        Id = id;
        Title = title;
        Url = url;
        TimestampLabel = FormatTimestamp(publishedAt);
        IsRead = isRead;
        _open = open;
        _markRead = markRead;
        _markUnread = markUnread;
        _delete = delete;

        if (!string.IsNullOrWhiteSpace(imageUrl))
        {
            // Same preview the toast used; served from our domain, cached. Fetch off the UI thread and
            // fill in when ready (started on the UI thread, so the continuation lands back there).
            _ = LoadImageAsync(imageUrl!, resolveImage);
        }
    }

    public string Id { get; }
    public string Title { get; }
    public string Url { get; }
    public bool IsRead { get; }

    /// <summary>Read tab shows "mark unread"; unread tab shows "mark read". Delete shows on both.</summary>
    public bool CanMarkRead => !IsRead;
    public bool CanMarkUnread => IsRead;

    /// <summary>Succinct published time for the icon's hover tooltip: "d MMM HH:mm" (year only if older).</summary>
    public string TimestampLabel { get; }

    private static string FormatTimestamp(string? publishedAt)
    {
        if (!DateTimeOffset.TryParse(publishedAt, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t))
        {
            return publishedAt ?? "";
        }

        var local = t.ToLocalTime();
        var format = local.Year == DateTimeOffset.Now.Year ? "d MMM HH:mm" : "d MMM yyyy HH:mm";
        return local.ToString(format, CultureInfo.InvariantCulture);
    }

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

    [RelayCommand]
    private void MarkRead() => _markRead(Id);

    [RelayCommand]
    private void MarkUnread() => _markUnread(Id);

    [RelayCommand]
    private void Delete() => _delete(Id);
}
