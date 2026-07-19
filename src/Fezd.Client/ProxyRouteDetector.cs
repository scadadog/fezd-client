using System;
using System.Net;

namespace Fezd.Client
{
    /// <summary>Describes whether the system HTTP proxy applies to a destination.</summary>
    public sealed class ProxyRouteInfo
    {
        internal ProxyRouteInfo(bool usesProxy, Uri proxyUri)
        {
            UsesProxy = usesProxy;
            ProxyUri = proxyUri;
        }

        public bool UsesProxy { get; }
        public Uri ProxyUri { get; }

        public string Description => UsesProxy
            ? "proxy " + FormatEndpoint(ProxyUri)
            : "direct";

        private static string FormatEndpoint(Uri uri)
        {
            if (uri == null)
                return "(unknown)";
            return uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);
        }
    }

    /// <summary>
    /// Resolves the route that the HTTP client will use, including system proxy
    /// bypass rules such as NO_PROXY and Windows proxy/PAC configuration.
    /// </summary>
    public static class ProxyRouteDetector
    {
        public static ProxyRouteInfo Detect(Uri destination, bool noProxy)
        {
            return Detect(destination, noProxy, WebRequest.DefaultWebProxy);
        }

        public static ProxyRouteInfo Detect(Uri destination, bool noProxy, IWebProxy systemProxy)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (noProxy || systemProxy == null)
                return new ProxyRouteInfo(false, null);

            try
            {
                if (systemProxy.IsBypassed(destination))
                    return new ProxyRouteInfo(false, null);

                Uri proxyUri = systemProxy.GetProxy(destination);
                if (proxyUri == null || SameEndpoint(proxyUri, destination))
                    return new ProxyRouteInfo(false, null);

                return new ProxyRouteInfo(true, proxyUri);
            }
            catch
            {
                // Proxy/PAC discovery can fail independently of the request. Leave
                // HttpClient on its normal system-proxy behavior and report direct.
                return new ProxyRouteInfo(false, null);
            }
        }

        private static bool SameEndpoint(Uri left, Uri right)
        {
            return string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase)
                && left.Port == right.Port;
        }
    }
}
