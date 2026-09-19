using System.Text;
using System.Text.Json;

namespace MyFrame.Core;

public enum MarketCredentialState
{
    Missing,
    Invalid,
    Expired,
    Valid
}

public sealed record MarketCredentialStatus(MarketCredentialState State, DateTimeOffset? ExpiresAt)
{
    public bool IsConfigured => State is MarketCredentialState.Valid or MarketCredentialState.Expired;
}

public interface IMarketCredentialStore : IMarketTokenStore
{
    Task SaveAsync(string token, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Validates and manages a WFM credential without exposing its value to UI state, logs or MCP.
/// Network login/renewal is intentionally supplied by a future provider; this service owns the
/// local save, status and revoke operations.
/// </summary>
public sealed class MarketCredentialService(IMarketCredentialStore store)
{
    public async Task<MarketCredentialStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        Classify(await store.ReadAsync(cancellationToken).ConfigureAwait(false));

    public async Task<MarketCredentialStatus> SaveAsync(string token,
        CancellationToken cancellationToken = default)
    {
        var status = Classify(token);
        if (status.State is not MarketCredentialState.Valid)
            throw new ArgumentException("The WFM credential is missing, malformed or expired.", nameof(token));
        await store.SaveAsync(token.Trim(), cancellationToken).ConfigureAwait(false);
        return status;
    }

    public async Task RevokeAsync(CancellationToken cancellationToken = default) =>
        await store.ClearAsync(cancellationToken).ConfigureAwait(false);

    public static MarketCredentialStatus Classify(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return new(MarketCredentialState.Missing, null);
        var parts = token.Trim().Split('.');
        if (parts.Length != 3) return new(MarketCredentialState.Invalid, null);
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload += (payload.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            if (!document.RootElement.TryGetProperty("exp", out var exp) ||
                !exp.TryGetInt64(out var seconds))
                return new(MarketCredentialState.Invalid, null);
            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
            return new(expiresAt <= DateTimeOffset.UtcNow ? MarketCredentialState.Expired : MarketCredentialState.Valid,
                expiresAt);
        }
        catch (Exception error) when (error is FormatException or JsonException or ArgumentOutOfRangeException)
        {
            return new(MarketCredentialState.Invalid, null);
        }
    }
}
