using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Fezd.Contracts;
using Fezd.Contracts.Updates;

namespace Fezd.Remote
{
    /// <summary>
    /// Version check + self-update against public scadadog/fezd-client Releases.
    /// </summary>
    internal static class SelfUpdate
    {
        private const string Owner = "scadadog";
        private const string Repo = "fezd-client";
        private const string Product = "client";
        private const string SkipEnv = "FEZD_SKIP_UPDATE_CHECK";

        public static void MaybeWarnIfOutdated(string currentVersion)
        {
            try
            {
                if (IsSkipRequested())
                    return;
                if (UpdateCheckCache.TryRead(Product, UpdateCheckCache.DefaultTtl,
                        out string cachedLatest, out bool cachedOutdated))
                {
                    if (cachedOutdated)
                        WriteNotice(currentVersion, cachedLatest);
                    return;
                }

                using (var gh = new GitHubReleaseClient(Owner, Repo, token: null,
                           timeout: TimeSpan.FromSeconds(3)))
                {
                    if (!gh.TryGetLatestVersion(out string latest, out _, TimeSpan.FromSeconds(3)))
                        return;
                    bool outdated = SemVer.IsNewer(latest, currentVersion);
                    UpdateCheckCache.Write(Product, latest, outdated);
                    if (outdated)
                        WriteNotice(currentVersion, latest);
                }
            }
            catch
            {
                /* never block the real command */
            }
        }

        public static int RunUpdate(string currentVersion)
        {
            string rid = DetectRid();
            if (rid == null)
            {
                Console.Error.WriteLine(
                    "ERROR: Unsupported OS/arch for auto-update. Download manually from " +
                    "https://github.com/scadadog/fezd-client/releases/latest");
                return FezdExitCodes.Error;
            }

            string assetName = rid == "win-x64"
                ? "fezd-client-win-x64.zip"
                : "fezd-client-" + rid;

            Console.WriteLine("Checking GitHub Releases for fezd-client...");
            using (var gh = new GitHubReleaseClient(Owner, Repo, token: null,
                       timeout: TimeSpan.FromSeconds(120)))
            {
                GitHubRelease release;
                try
                {
                    release = gh.GetLatestRelease();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("ERROR: Could not reach GitHub Releases: " + ex.Message);
                    return FezdExitCodes.ConnectivityError;
                }

                string latest = SemVer.Normalize(release.TagName);
                if (!SemVer.IsNewer(latest, currentVersion))
                {
                    Console.WriteLine($"Already up to date (FEZD {currentVersion}).");
                    return FezdExitCodes.Ok;
                }

                Console.WriteLine($"Updating {currentVersion} → {latest} ({assetName})...");

                string tempDir = Path.Combine(Path.GetTempPath(), "fezd-update-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                string assetPath = Path.Combine(tempDir, assetName);
                try
                {
                    string sumsText;
                    try
                    {
                        sumsText = gh.DownloadAssetText(release, "SHA256SUMS.txt");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("ERROR: Could not download SHA256SUMS.txt: " + ex.Message);
                        return FezdExitCodes.ConnectivityError;
                    }

                    try
                    {
                        gh.DownloadAsset(release, assetName, assetPath);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("ERROR: Could not download " + assetName + ": " + ex.Message);
                        return FezdExitCodes.ConnectivityError;
                    }

                    if (!Sha256Sums.VerifyFile(assetPath, sumsText, assetName))
                    {
                        Console.Error.WriteLine("ERROR: SHA256 checksum mismatch for " + assetName + ".");
                        return FezdExitCodes.Error;
                    }

                    string newBinary = PrepareBinary(assetPath, rid, tempDir);
                    string target = CurrentExePath();
                    ReplaceExecutable(target, newBinary);

                    Console.WriteLine($"Updated fezd-client to {latest}.");
                    Console.WriteLine("Run: fezd-client --version");
                    return FezdExitCodes.Ok;
                }
                finally
                {
                    try { Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
                }
            }
        }

        private static void WriteNotice(string current, string latest)
        {
            Console.Error.WriteLine(
                $"Update available: you are on {SemVer.Normalize(current)}; latest is {latest}.");
            Console.Error.WriteLine("Run: fezd-client update");
        }

        private static bool IsSkipRequested()
        {
            string v = Environment.GetEnvironmentVariable(SkipEnv);
            if (string.IsNullOrWhiteSpace(v))
                return false;
            v = v.Trim();
            return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   v.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private static string DetectRid()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                RuntimeInformation.OSArchitecture == Architecture.X64)
                return "win-x64";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
                RuntimeInformation.OSArchitecture == Architecture.X64)
                return "linux-x64";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
                RuntimeInformation.OSArchitecture == Architecture.Arm64)
                return "osx-arm64";
            return null;
        }

        private static string PrepareBinary(string assetPath, string rid, string tempDir)
        {
            if (rid == "win-x64")
            {
                string extractDir = Path.Combine(tempDir, "extract");
                Directory.CreateDirectory(extractDir);
                ZipFile.ExtractToDirectory(assetPath, extractDir);
                string exe = FindFile(extractDir, "fezd-client.exe");
                if (exe == null)
                    throw new InvalidOperationException("Zip did not contain fezd-client.exe.");
                return exe;
            }

            // Unix bare binary (may be named fezd-client-linux-x64 etc.)
            string dest = Path.Combine(tempDir, "fezd-client-new");
            File.Copy(assetPath, dest, overwrite: true);
            TryMakeExecutable(dest);
            return dest;
        }

        private static void TryMakeExecutable(string path)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;
            try
            {
                File.SetUnixFileMode(path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            catch
            {
                /* best effort */
            }
        }

        private static string FindFile(string root, string fileName)
        {
            foreach (string path in Directory.GetFiles(root, fileName, SearchOption.AllDirectories))
                return path;
            return null;
        }

        private static void ReplaceExecutable(string targetPath, string newBinaryPath)
        {
            string dir = Path.GetDirectoryName(targetPath) ?? ".";
            string fileName = Path.GetFileName(targetPath);
            string staging = Path.Combine(dir, fileName + ".new");
            string backup = Path.Combine(dir, fileName + ".old");

            File.Copy(newBinaryPath, staging, overwrite: true);
            TryMakeExecutable(staging);

            try
            {
                if (File.Exists(backup))
                    File.Delete(backup);
            }
            catch { /* ignore */ }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Cannot overwrite a running .exe — move aside first.
                File.Move(targetPath, backup);
                try
                {
                    File.Move(staging, targetPath);
                }
                catch
                {
                    try { File.Move(backup, targetPath); } catch { /* best effort restore */ }
                    throw;
                }
                try { File.Delete(backup); } catch { /* leftover .old is fine */ }
            }
            else
            {
                // Unix: replace atomically where possible.
                if (File.Exists(targetPath))
                {
                    try { File.Delete(targetPath); }
                    catch
                    {
                        File.Move(targetPath, backup);
                    }
                }
                File.Move(staging, targetPath);
                try { if (File.Exists(backup)) File.Delete(backup); } catch { /* ignore */ }
            }
        }

        private static string CurrentExePath()
        {
            try
            {
                string path = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(path))
                    return path;
            }
            catch { /* ignore */ }
            return Environment.GetCommandLineArgs()[0];
        }
    }
}
