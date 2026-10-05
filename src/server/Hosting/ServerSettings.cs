using Microsoft.Extensions.Configuration;
using System.Net;

namespace Charac.Server.Hosting;

internal sealed record ServerSettings(string Domain, string ListenAddress, IPAddress? TrustedProxy)
{
    public static ServerSettings FromConfiguration(IConfiguration configuration)
    {
        var domain = configuration["server:domain"]?.Trim();
        var listen = configuration["server:listen"]?.Trim();
        var trustedProxyText = configuration["server:trusted_proxy"]?.Trim();

        if (string.IsNullOrEmpty(domain) || Uri.CheckHostName(domain) == UriHostNameType.Unknown)
        {
            throw new InvalidOperationException("[server] domain must be a hostname without a scheme or port.");
        }

        if (!Uri.TryCreate(listen, UriKind.Absolute, out var listenUri) ||
            listenUri.Scheme != Uri.UriSchemeHttp ||
            listenUri.AbsolutePath != "/" ||
            listenUri.Query.Length != 0 ||
            listenUri.Fragment.Length != 0 ||
            listenUri.UserInfo.Length != 0)
        {
            throw new InvalidOperationException("[server] listen must be an HTTP listening origin.");
        }

        IPAddress? trustedProxy = null;
        if (!string.IsNullOrEmpty(trustedProxyText) &&
            !IPAddress.TryParse(trustedProxyText, out trustedProxy))
            throw new InvalidOperationException("[server] trusted_proxy must be one numeric IP address.");

        return new ServerSettings(domain, listen!, trustedProxy);
    }
}
