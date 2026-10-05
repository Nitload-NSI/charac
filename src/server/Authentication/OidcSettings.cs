namespace Charac.Server.Authentication;

internal sealed record OidcSettings(string Issuer, string ClientId, string DiscoveryUrl)
{
    public static OidcSettings? FromConfiguration(IConfiguration configuration)
    {
        var issuer = configuration["oidc:issuer"]?.Trim();
        var clientId = configuration["oidc:client_id"]?.Trim();
        var discovery = configuration["oidc:discovery_url"]?.Trim();
        if (string.IsNullOrWhiteSpace(issuer) && string.IsNullOrWhiteSpace(clientId) &&
            string.IsNullOrWhiteSpace(discovery))
            return null;
        if (string.IsNullOrWhiteSpace(clientId) ||
            !Uri.TryCreate(issuer, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidOperationException("Configure [oidc] issuer as an HTTPS URL and client_id as a non-empty value.");
        discovery ??= issuer!.TrimEnd('/') + "/.well-known/openid-configuration";
        if (!Uri.TryCreate(discovery, UriKind.Absolute, out var metadataUri) ||
            metadataUri.Scheme != Uri.UriSchemeHttps || metadataUri.UserInfo.Length != 0 ||
            metadataUri.Query.Length != 0 || metadataUri.Fragment.Length != 0)
            throw new InvalidOperationException("[oidc] discovery_url must be an HTTPS URL.");
        return new OidcSettings(issuer!, clientId, discovery);
    }
}
