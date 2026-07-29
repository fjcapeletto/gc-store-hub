using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GabrielCapelettoStore.Hub.Api;

namespace GabrielCapelettoStore.Hub.ViewModels;

/// <summary>
/// The L1 client shell for an embedded `type: api` app: loads the app's descriptor, lists its actions,
/// renders a typed form per action, runs it (token brokered by the hub), and renders the result per the
/// action's container/field hints. See contract/type-api-descriptor.md.
/// </summary>
public sealed partial class ApiShellViewModel : ObservableObject
{
    private readonly ApiDescriptorClient _client;
    private readonly string _appId;
    private readonly Action<string> _openLink;
    private readonly Action _close;

    public ApiShellViewModel(ApiDescriptorClient client, string appId, string appName, Action<string> openLink, Action close)
    {
        _client = client;
        _appId = appId;
        _openLink = openLink;
        _close = close;
        Title = appName;
    }

    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial bool IsLoading { get; set; }

    public ObservableCollection<ApiActionViewModel> Actions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial ApiActionViewModel? SelectedAction { get; set; }

    public bool HasSelection => SelectedAction is not null;

    [RelayCommand]
    private void Close() => _close();

    [RelayCommand]
    private void SelectAction(ApiActionViewModel action) => SelectedAction = action;

    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusText = "Loading…";
        Actions.Clear();
        SelectedAction = null;

        var load = await _client.LoadAsync(_appId);
        IsLoading = false;

        if (!load.IsGranted)
        {
            StatusText = $"No access ({load.TokenOutcome.Status}{(load.TokenOutcome.Reason is { } r ? $": {r}" : "")}).";
            return;
        }

        if (load.Descriptor is null)
        {
            StatusText = "This app hasn't published a descriptor yet.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(load.Descriptor.App?.Title))
        {
            Title = load.Descriptor.App!.Title!;
        }

        StatusText = load.Descriptor.App?.Summary ?? "";
        foreach (var action in load.Descriptor.Actions)
        {
            Actions.Add(new ApiActionViewModel(_client, _appId, action, _openLink));
        }

        SelectedAction = Actions.FirstOrDefault();
    }
}

/// <summary>One action: a titled form (typed inputs) that runs against the app backend and holds its result.</summary>
public sealed partial class ApiActionViewModel : ObservableObject
{
    private readonly ApiDescriptorClient _client;
    private readonly string _appId;
    private readonly DescriptorAction _action;
    private readonly Action<string> _openLink;

    public ApiActionViewModel(ApiDescriptorClient client, string appId, DescriptorAction action, Action<string> openLink)
    {
        _client = client;
        _appId = appId;
        _action = action;
        _openLink = openLink;

        foreach (var input in action.Inputs)
        {
            Fields.Add(new ApiFieldViewModel(input));
        }
    }

    public string Title => string.IsNullOrWhiteSpace(_action.Title) ? _action.Id : _action.Title!;
    public string Endpoint => $"{_action.Method.ToUpperInvariant()} {_action.Path}";
    public ObservableCollection<ApiFieldViewModel> Fields { get; } = [];
    public bool HasFields => Fields.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotBusy))]
    public partial bool Busy { get; set; }

    public bool NotBusy => !Busy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    public partial ApiResultViewModel? Result { get; set; }

    public bool HasResult => Result is not null;

    [RelayCommand]
    private async Task Run()
    {
        Busy = true;
        try
        {
            var values = Fields.ToDictionary(f => f.Name, f => f.CurrentValue, StringComparer.Ordinal);
            var call = await _client.ExecuteAsync(_appId, _action, values);
            Result = ApiResultBuilder.Build(call, _action.Result, _openLink);
        }
        finally
        {
            Busy = false;
        }
    }
}

/// <summary>A typed form field (string/number/date → text, bool → toggle, enum → dropdown).</summary>
public sealed partial class ApiFieldViewModel : ObservableObject
{
    public ApiFieldViewModel(ActionInput input)
    {
        Name = input.Name;
        Label = string.IsNullOrWhiteSpace(input.Label) ? input.Name : input.Label!;
        Type = input.Type.ToLowerInvariant();
        Options = input.Options;

        if (input.Default is { } d)
        {
            if (Type == "bool")
            {
                BoolValue = d.ValueKind == JsonValueKind.True;
            }
            else
            {
                Value = d.ValueKind == JsonValueKind.String ? d.GetString() ?? "" : d.ToString();
            }
        }
    }

    public string Name { get; }
    public string Label { get; }
    public string Type { get; }
    public IReadOnlyList<string> Options { get; }

    [ObservableProperty] public partial string Value { get; set; } = "";
    [ObservableProperty] public partial bool BoolValue { get; set; }

    public bool IsBool => Type == "bool";
    public bool IsEnum => Type == "enum";
    public bool IsPlain => !IsBool && !IsEnum;

    /// <summary>The value to send on the wire.</summary>
    public string CurrentValue => IsBool ? (BoolValue ? "true" : "false") : Value;
}

/// <summary>The rendered result of an action call: records (keyValue/table), a link, or raw text/json.</summary>
public sealed partial class ApiResultViewModel : ObservableObject
{
    public string StatusText { get; set; } = "";
    public bool IsError { get; set; }
    public string ErrorText { get; set; } = "";

    public bool ShowRecords { get; set; }
    public List<RecordViewModel> Records { get; } = [];

    public bool ShowLink { get; set; }
    public string? LinkUrl { get; set; }
    public Action<string>? LinkOpen { get; set; }

    public bool ShowText { get; set; }
    public string RawText { get; set; } = "";

    [RelayCommand]
    private void OpenLink()
    {
        if (!string.IsNullOrEmpty(LinkUrl))
        {
            LinkOpen?.Invoke(LinkUrl);
        }
    }
}

public sealed class RecordViewModel
{
    public List<FieldCellViewModel> Cells { get; } = [];
}

/// <summary>One field/cell in a record — plain text, a clickable link, or a JSON/mono block.</summary>
public sealed partial class FieldCellViewModel : ObservableObject
{
    public string Label { get; set; } = "";
    public bool HasLabel => !string.IsNullOrEmpty(Label);
    public string Text { get; set; } = "";
    public string? Url { get; set; }
    public Action<string>? OpenLink { get; set; }

    public bool IsText { get; set; }
    public bool IsLink { get; set; }
    public bool IsJson { get; set; }

    [RelayCommand]
    private void Open()
    {
        if (!string.IsNullOrEmpty(Url))
        {
            OpenLink?.Invoke(Url);
        }
    }
}

/// <summary>Turns a raw call result + the action's result hints into a renderable <see cref="ApiResultViewModel"/>.</summary>
internal static class ApiResultBuilder
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static ApiResultViewModel Build(ApiCallResult call, ActionResult? spec, Action<string> openLink)
    {
        if (call.Error is not null)
        {
            return new ApiResultViewModel { IsError = true, StatusText = "Request failed", ErrorText = call.Error };
        }

        var vm = new ApiResultViewModel { StatusText = $"HTTP {call.Status}" };
        var render = (spec?.Render ?? "json").ToLowerInvariant();
        var fields = spec?.Fields ?? new Dictionary<string, string>();

        switch (render)
        {
            case "link":
                vm.LinkUrl = ExtractUrl(call.Body);
                if (vm.LinkUrl is not null) { vm.ShowLink = true; vm.LinkOpen = openLink; }
                else { vm.ShowText = true; vm.RawText = call.Body; }
                return vm;

            case "text":
                vm.ShowText = true; vm.RawText = call.Body; return vm;

            case "html":
                vm.ShowText = true; vm.RawText = HtmlToText(call.Body); return vm;
        }

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(call.Body);
            root = doc.RootElement.Clone();
        }
        catch
        {
            vm.ShowText = true; vm.RawText = call.Body; return vm;
        }

        if (render == "keyValue")
        {
            vm.Records.Add(BuildRecord(root, fields, openLink));
            vm.ShowRecords = true;
            return vm;
        }

        if (render == "table")
        {
            var array = ResolveArray(root, spec?.ItemsPath);
            if (array is { ValueKind: JsonValueKind.Array } arr)
            {
                foreach (var el in arr.EnumerateArray())
                {
                    vm.Records.Add(BuildRecord(el, fields, openLink));
                }
                vm.ShowRecords = true;
            }
            else
            {
                vm.ShowText = true; vm.RawText = Serialize(root);
            }
            return vm;
        }

        vm.ShowText = true; vm.RawText = Serialize(root); // json (default)
        return vm;
    }

    private static RecordViewModel BuildRecord(JsonElement obj, IReadOnlyDictionary<string, string> fields, Action<string> openLink)
    {
        var record = new RecordViewModel();
        if (obj.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in obj.EnumerateObject())
            {
                record.Cells.Add(BuildCell(p.Name, p.Value, fields, openLink));
            }
        }
        else
        {
            record.Cells.Add(BuildCell("", obj, fields, openLink));
        }

        return record;
    }

    private static FieldCellViewModel BuildCell(string name, JsonElement value, IReadOnlyDictionary<string, string> fields, Action<string> openLink)
    {
        var cell = new FieldCellViewModel { Label = name };
        var render = fields.TryGetValue(name, out var r)
            ? r.ToLowerInvariant()
            : value.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? "json" : "text";

        switch (render)
        {
            case "link":
                cell.IsLink = true; cell.Url = AsString(value); cell.Text = cell.Url ?? ""; cell.OpenLink = openLink;
                break;
            case "html":
                cell.IsText = true; cell.Text = HtmlToText(AsString(value) ?? "");
                break;
            case "json":
                cell.IsJson = true; cell.Text = Serialize(value);
                break;
            case "date":
            case "datetime":
                cell.IsText = true; cell.Text = FormatDate(AsString(value) ?? "");
                break;
            default:
                if (value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    cell.IsJson = true; cell.Text = Serialize(value);
                }
                else
                {
                    cell.IsText = true; cell.Text = AsString(value) ?? "";
                }
                break;
        }

        return cell;
    }

    private static JsonElement? ResolveArray(JsonElement root, string? itemsPath)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root;
        }

        if (!string.IsNullOrWhiteSpace(itemsPath) && root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(itemsPath!, out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            return arr;
        }

        return null;
    }

    private static string? ExtractUrl(string body)
    {
        var trimmed = body.Trim();
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind == JsonValueKind.String)
            {
                return doc.RootElement.GetString();
            }
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("url", out var u))
            {
                return u.GetString();
            }
        }
        catch
        {
            // not JSON — treat the raw body as the URL if it looks like one
        }

        return trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? trimmed : null;
    }

    private static string? AsString(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "",
        _ => v.GetRawText(),
    };

    private static string Serialize(JsonElement v) => JsonSerializer.Serialize(v, Pretty);

    private static string FormatDate(string s)
        => DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t)
            ? t.ToLocalTime().ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture)
            : s;

    // Interim `html` render: strip to readable text (block tags → line breaks, decode entities).
    // Rich no-script HTML rendering is a pending fidelity upgrade.
    private static string HtmlToText(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return "";
        }

        var withBreaks = Regex.Replace(html, @"<\s*(br|/p|/div|/li|/h[1-6]|/tr)\s*/?>", "\n", RegexOptions.IgnoreCase);
        var noTags = Regex.Replace(withBreaks, "<[^>]+>", "");
        var decoded = WebUtility.HtmlDecode(noTags);
        var collapsed = Regex.Replace(decoded, @"[ \t]+", " ");
        collapsed = Regex.Replace(collapsed, @"\n{3,}", "\n\n");
        return collapsed.Trim();
    }
}
