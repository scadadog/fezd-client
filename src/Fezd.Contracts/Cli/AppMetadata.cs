namespace Fezd.Contracts.Cli
{
    /// <summary>
    /// Product/version/contact facts the help and about renderers need. Gathered
    /// by each binary from its own assembly attributes and passed to the shared
    /// renderer, so no reflection lives in the (AOT-published) contracts assembly.
    /// </summary>
    public sealed class AppMetadata
    {
        public string Product { get; set; } = "FEZD — FEZ Dispenser";
        public string Version { get; set; } = "1.0.0";
        public string Company { get; set; } = "SCADADOG LLC";
        public string Copyright { get; set; } = "Copyright © 2026 SCADADOG LLC. All rights reserved.";
        public string Description { get; set; } = string.Empty;
        public string Website { get; set; } = "https://scadadog.com";
        public string Email { get; set; } = "info@scadadog.com";
    }
}
