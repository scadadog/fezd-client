namespace Fezd.Contracts
{
    /// <summary>
    /// Mirror of <c>Fezd.Core.ExitCodes</c> for the shared/remote code paths that
    /// must not depend on the net48 core (the Native AOT Linux client). Values are
    /// identical so a remote run reproduces the same process exit code as local.
    /// </summary>
    public static class FezdExitCodes
    {
        public const int Ok = 0;
        public const int Error = 1;
        public const int UsageError = 2;
        public const int DoctorFailed = 3;
        public const int ComError = 4;
        public const int ConnectivityError = 5;
        public const int BuildError = 6;
        public const int DeployError = 7;
        public const int TargetBusy = 8;
    }
}
