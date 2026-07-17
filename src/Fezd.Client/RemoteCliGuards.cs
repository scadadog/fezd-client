using System;
using System.Net;
using Fezd.Contracts;
using Fezd.Contracts.Cli;

namespace Fezd.Client
{
    /// <summary>
    /// Fail-closed checks for remote-client CLI flags that look real but are not
    /// supported on the gateway sessions/doctor APIs yet.
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
        /// Reject doctor password flags — the remote doctor GET API does not
        /// accept them (and must not put secrets in the query string).
        /// </summary>
        public static void EnsureDoctorFlagsSupported(CommandLine cl)
        {
            if (cl == null)
                throw new ArgumentNullException(nameof(cl));

            if (!string.IsNullOrEmpty(cl.GetOption("app-password")) ||
                !string.IsNullOrEmpty(cl.GetOption("app-password-old")))
            {
                throw new RemoteCommsException(
                    "Application passwords are not supported on remote doctor yet. " +
                    "Run doctor without --app-password, or use fezd-server doctor on the gateway host.",
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
