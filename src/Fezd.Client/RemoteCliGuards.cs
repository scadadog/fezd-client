using System;
using System.Net;
using Fezd.Contracts;
using Fezd.Contracts.Cli;

namespace Fezd.Client
{
    /// <summary>
    /// Fail-closed checks for remote-client CLI flags that look real but are not
    /// supported on the gateway sessions API yet.
    /// </summary>
    public static class RemoteCliGuards
    {
        /// <summary>
        /// Reject deploy flags that the sessions API does not honor, so users are
        /// not silently ignored.
        /// </summary>
        public static void EnsureDeployFlagsSupported(CommandLine cl)
        {
            if (cl == null)
                throw new ArgumentNullException(nameof(cl));

            if (!string.IsNullOrEmpty(cl.GetOption("mode")))
            {
                throw new RemoteCommsException(
                    "--mode is not supported on fezd-client deploy sessions yet " +
                    "(primary/secondary is fezd-server local only). Omit --mode.",
                    FezdExitCodes.UsageError);
            }

            if (cl.GetSwitch("download") == false)
            {
                throw new RemoteCommsException(
                    "--no-download is not supported on fezd-client deploy sessions " +
                    "(the gateway always downloads). Omit --no-download.",
                    FezdExitCodes.UsageError);
            }
        }

        /// <summary>
        /// Gateway doctor is GET query-string only and does not accept application
        /// passwords. Reject those flags so they are not silently ignored (and so
        /// secrets are never placed on the URL).
        /// </summary>
        public static void EnsureDoctorFlagsSupported(CommandLine cl)
        {
            if (cl == null)
                throw new ArgumentNullException(nameof(cl));

            if (!string.IsNullOrEmpty(cl.GetOption("app-password")) || cl.HasFlag("app-password"))
            {
                throw new RemoteCommsException(
                    "--app-password is not supported on fezd-client doctor " +
                    "(the gateway GET /api/v1/doctor cannot take secrets). " +
                    "Run deep password checks with fezd-server doctor on the host.",
                    FezdExitCodes.UsageError);
            }

            if (!string.IsNullOrEmpty(cl.GetOption("app-password-old")) || cl.HasFlag("app-password-old"))
            {
                throw new RemoteCommsException(
                    "--app-password-old is not supported on fezd-client doctor. " +
                    "Run deep password checks with fezd-server doctor on the host.",
                    FezdExitCodes.UsageError);
            }
        }

        /// <summary>
        /// Non-loopback gateways must use HTTPS. Cleartext HTTP is only allowed
        /// for localhost / loopback development.
        /// </summary>
        public static void EnsureSecureTransport(Uri baseUrl)
        {
            if (baseUrl == null)
                throw new ArgumentNullException(nameof(baseUrl));

            if (baseUrl.Scheme == Uri.UriSchemeHttps)
                return;

            if (baseUrl.Scheme == Uri.UriSchemeHttp && IsLoopbackHost(baseUrl))
                return;

            throw new RemoteCommsException(
                "Non-loopback gateway requires https:// (refuse cleartext HTTP). " +
                "Use an https URL, or http:// only against localhost/loopback.",
                FezdExitCodes.UsageError);
        }

        public static bool IsLoopbackHost(Uri baseUrl)
        {
            if (baseUrl == null)
                return false;
            if (baseUrl.IsLoopback)
                return true;
            string host = baseUrl.DnsSafeHost;
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
                return true;
            if (IPAddress.TryParse(host, out IPAddress ip))
                return IPAddress.IsLoopback(ip);
            return false;
        }
    }
}
