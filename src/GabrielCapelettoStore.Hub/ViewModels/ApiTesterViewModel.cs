using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GabrielCapelettoStore.Hub.Api;

namespace GabrielCapelettoStore.Hub.ViewModels;

/// <summary>
/// Dev-only "Postman-style" harness (enabled via GCSTORE_ApiTester=1) to exercise the `type: api`
/// tokenization end-to-end without the real app UX: broker a scoped token for an appId (tests the
/// server gate + mint), then fire a request at the app's own API backend with it (tests the backend's
/// verify). Stands in for an embedded api shell. Never shipped to customers.
/// </summary>
public sealed partial class ApiTesterViewModel : ObservableObject
{
    private readonly ApiTokenBroker _broker;

    // Dedicated client with a generous timeout — the tester hits arbitrary app backends, some slow;
    // the hub's shared 15s client is right for catalog/delivery but too tight here.
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private ApiToken? _token;

    public ApiTesterViewModel(ApiTokenBroker broker) => _broker = broker;

    [ObservableProperty] public partial string AppId { get; set; } = "gcstore.gcruiter";
    [ObservableProperty] public partial string Method { get; set; } = "GET";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveUrl))]
    public partial string Path { get; set; } = "/";

    [ObservableProperty] public partial string RequestBody { get; set; } = "";
    [ObservableProperty] public partial string TokenInfo { get; set; } = "";
    [ObservableProperty] public partial string Response { get; set; } = "";

    /// <summary>The app's API base URL from the brokered token (empty until a token is fetched).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveUrl))]
    public partial string ApiBaseUrl { get; set; } = "";

    /// <summary>The exact URL Send will hit — apiBaseUrl (from the token) + the path you type.</summary>
    public string EffectiveUrl => string.IsNullOrEmpty(ApiBaseUrl)
        ? "— broker a token first —"
        : ApiBaseUrl.TrimEnd('/') + (Path.StartsWith('/') ? Path : "/" + Path);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotBusy))]
    public partial bool Busy { get; set; }

    public bool NotBusy => !Busy;

    [RelayCommand]
    private async Task BrokerToken()
    {
        Busy = true;
        Response = "";
        try
        {
            var outcome = await _broker.GetAsync(AppId.Trim());
            if (outcome.Status == ApiTokenStatus.Granted && outcome.Token is { } token)
            {
                _token = token;
                ApiBaseUrl = token.ApiBaseUrl;
                TokenInfo =
                    $"apiBaseUrl : {token.ApiBaseUrl}\n" +
                    $"expiresAt  : {token.ExpiresAt:u}\n" +
                    $"token      : {token.Token}";
            }
            else
            {
                _token = null;
                ApiBaseUrl = "";
                TokenInfo = $"[{outcome.Status}] {outcome.Reason ?? "no token"}";
            }
        }
        catch (Exception ex)
        {
            _token = null;
            TokenInfo = "error: " + ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    private async Task Send()
    {
        if (_token is null)
        {
            Response = "Broker a token first.";
            return;
        }

        Busy = true;
        try
        {
            var baseUrl = _token.ApiBaseUrl.TrimEnd('/');
            var path = Path.StartsWith('/') ? Path : "/" + Path;
            using var request = new HttpRequestMessage(new HttpMethod(Method.Trim().ToUpperInvariant()), baseUrl + path);

            // Assumed convention: the JWS travels as a Bearer token. The app↔backend transport isn't
            // pinned in the contract yet — flag if the app expects a different header.
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _token.Token);

            if (!string.IsNullOrWhiteSpace(RequestBody))
            {
                request.Content = new StringContent(RequestBody, Encoding.UTF8, "application/json");
            }

            using var response = await _http.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Response = $"HTTP {(int)response.StatusCode} {response.StatusCode}\n\n{text}";
        }
        catch (Exception ex)
        {
            Response = "error: " + ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }
}
