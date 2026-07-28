using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Microsoft.Extensions.Options;
using PersonalAi.Core.Options;

namespace PersonalAi.Core.Gmail;

public interface IGoogleAuthService
{
    bool IsConfigured { get; }
    string GetAuthorizationUrl(string state = "personal-ai");
    Task<GoogleTokenRecord> ExchangeCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<(bool Connected, string? Email)> GetStatusAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task<GmailService> CreateGmailServiceAsync(CancellationToken cancellationToken = default);
}

public sealed class GoogleAuthService : IGoogleAuthService
{
    private readonly GoogleOptions _options;
    private readonly IGoogleTokenStore _tokenStore;

    public GoogleAuthService(IOptions<GoogleOptions> options, IGoogleTokenStore tokenStore)
    {
        _options = options.Value;
        _tokenStore = tokenStore;
    }

    public bool IsConfigured => _options.IsConfigured;

    public string GetAuthorizationUrl(string state = "personal-ai")
    {
        EnsureConfigured();
        var flow = CreateFlow();
        // GoogleAuthorizationCodeRequestUrl exposes AccessType; the base type does not.
        var request = (Google.Apis.Auth.OAuth2.Requests.GoogleAuthorizationCodeRequestUrl)
            flow.CreateAuthorizationCodeRequest(_options.RedirectUri);
        request.State = state;
        request.AccessType = "offline";
        // Prompt is already set on the flow (consent) — do not append duplicate query params.
        return request.Build().AbsoluteUri;
    }

    public async Task<GoogleTokenRecord> ExchangeCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var flow = CreateFlow();
        var token = await flow.ExchangeCodeForTokenAsync(
                "default",
                code,
                _options.RedirectUri,
                cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            var existing = await _tokenStore.GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            if (existing is null || string.IsNullOrWhiteSpace(existing.RefreshToken))
            {
                throw new InvalidOperationException(
                    "Google did not return a refresh token. Revoke app access at https://myaccount.google.com/permissions and connect again.");
            }

            token.RefreshToken = existing.RefreshToken;
        }

        var email = await FetchEmailAsync(token.AccessToken, cancellationToken).ConfigureAwait(false);
        var record = new GoogleTokenRecord(
            "default",
            token.RefreshToken,
            token.AccessToken,
            token.IssuedUtc.AddSeconds(token.ExpiresInSeconds ?? 3600),
            email);

        await _tokenStore.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task<(bool Connected, string? Email)> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var record = await _tokenStore.GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (record is null || string.IsNullOrWhiteSpace(record.RefreshToken))
            return (false, null);
        return (true, record.Email);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default) =>
        _tokenStore.DeleteAsync(cancellationToken: cancellationToken);

    public async Task<GmailService> CreateGmailServiceAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var record = await _tokenStore.GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (record is null || string.IsNullOrWhiteSpace(record.RefreshToken))
        {
            throw new InvalidOperationException(
                "Gmail is not connected. Open http://localhost:5080/auth/google to connect.");
        }

        var flow = CreateFlow();
        var tokenResponse = new TokenResponse
        {
            RefreshToken = record.RefreshToken,
            AccessToken = record.AccessToken,
            IssuedUtc = DateTime.UtcNow.AddMinutes(-50),
            ExpiresInSeconds = 1
        };

        // Force refresh on first use if we don't trust stored access token expiry.
        if (record.AccessTokenExpiresAt is { } exp && exp > DateTimeOffset.UtcNow.AddMinutes(2)
            && !string.IsNullOrWhiteSpace(record.AccessToken))
        {
            tokenResponse.AccessToken = record.AccessToken;
            tokenResponse.IssuedUtc = DateTime.UtcNow;
            tokenResponse.ExpiresInSeconds = (int)Math.Max(60, (exp - DateTimeOffset.UtcNow).TotalSeconds);
        }

        var credential = new UserCredential(flow, "default", tokenResponse);
        if (string.IsNullOrWhiteSpace(credential.Token.AccessToken) || credential.Token.IsStale)
        {
            var refreshed = await credential.RefreshTokenAsync(cancellationToken).ConfigureAwait(false);
            if (!refreshed && string.IsNullOrWhiteSpace(credential.Token.AccessToken))
                throw new InvalidOperationException("Could not refresh Google access token. Reconnect Gmail.");
        }

        await _tokenStore.SaveAsync(
                record with
                {
                    AccessToken = credential.Token.AccessToken,
                    AccessTokenExpiresAt = credential.Token.IssuedUtc.AddSeconds(
                        credential.Token.ExpiresInSeconds ?? 3600),
                    RefreshToken = string.IsNullOrWhiteSpace(credential.Token.RefreshToken)
                        ? record.RefreshToken
                        : credential.Token.RefreshToken
                },
                cancellationToken)
            .ConfigureAwait(false);

        return new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Personal AI Assistant"
        });
    }

    private GoogleAuthorizationCodeFlow CreateFlow() =>
        new(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = _options.ClientId,
                ClientSecret = _options.ClientSecret
            },
            Scopes = GoogleOptions.Scopes,
            Prompt = "consent"
        });

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new InvalidOperationException(
                "GOOGLE_CLIENT_ID / GOOGLE_CLIENT_SECRET are missing. See docs/YOU_GMAIL_SETUP.md.");
        }
    }

    private static async Task<string?> FetchEmailAsync(string? accessToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return null;

        try
        {
            using var client = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v2/userinfo");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return doc.RootElement.TryGetProperty("email", out var email) ? email.GetString() : null;
        }
        catch
        {
            return null;
        }
    }
}
