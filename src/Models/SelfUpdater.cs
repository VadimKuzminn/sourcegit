using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Models
{
    /// <summary>
    ///     In-place updates from the GitHub releases of this fork (Windows, macOS and Linux AppImage).
    ///
    ///     A release must carry a `sourcegit_{version}.{rid}.zip` asset (for example
    ///     `sourcegit_2026.23.2.win-x64.zip`). For Windows it holds the published app files, either
    ///     at the root of the archive or inside a single folder. For macOS it holds `SourceGit.app`.
    ///     For Linux the asset is the AppImage itself: `sourcegit_{version}.linux-x64.AppImage`.
    ///
    ///     Windows does not allow overwriting a running executable or a loaded DLL, but it does
    ///     allow renaming them. So the update renames every file it replaces to `*.update-old`,
    ///     copies the new files in, restarts the app and removes the renamed files on the next start.
    ///
    ///     On macOS the whole app bundle is replaced, because its signature covers every file in it.
    ///     The new bundle is unpacked next to the running one, the running one is renamed to
    ///     `SourceGit.app.update-old` (macOS allows that while the app runs) and the new one takes
    ///     its place. The renamed bundle is removed on the next start as well.
    ///
    ///     On Linux only an AppImage can be updated (a deb or rpm install belongs to the package
    ///     manager). The new AppImage is renamed over the running one: the running instance keeps
    ///     reading the old file, which disappears once it exits.
    /// </summary>
    public static class SelfUpdater
    {
        public const string Repository = "valuce/sourcegit";
        public const string ReleasesPage = $"https://github.com/{Repository}/releases/latest";

        /// <summary>
        ///     Fetches the latest release. The `SOURCEGIT_UPDATE_FEED` environment variable can point
        ///     to another URL returning the same JSON as the GitHub "latest release" API.
        /// </summary>
        public static async Task<Version> FetchLatestAsync()
        {
            var feed = Environment.GetEnvironmentVariable(FeedOverrideEnv);
            if (string.IsNullOrWhiteSpace(feed))
                feed = $"https://api.github.com/repos/{Repository}/releases/latest";

            using var client = CreateHttpClient(TimeSpan.FromSeconds(10));
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            var data = await client.GetStringAsync(feed).ConfigureAwait(false);
            return JsonSerializer.Deserialize(data, JsonCodeGen.Default.Version);
        }

        public static VersionAsset FindPackage(List<VersionAsset> assets)
        {
            var rid = PackageRuntime;
            if (assets == null || rid == null)
                return null;

            var suffix = OperatingSystem.IsLinux() ? $".{rid}.AppImage" : $".{rid}.zip";
            return assets.Find(x =>
                !string.IsNullOrEmpty(x.DownloadUrl) &&
                !string.IsNullOrEmpty(x.Name) &&
                x.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        ///     Downloads the package of the given release and puts its files in place of the
        ///     current ones. The running process keeps working; call <see cref="LaunchUpdatedApp"/>
        ///     and quit to switch to the new version.
        /// </summary>
        /// <param name="progress">Download progress in percent, or -1 while the files are being replaced.</param>
        public static async Task InstallAsync(Version ver, IProgress<double> progress, CancellationToken token)
        {
            var package = ver.Package ?? throw new InvalidOperationException("The release has no package for this platform.");
            var appDir = AppDirectory;
            var bundle = OperatingSystem.IsMacOS() ? GetBundleToReplace() : null;
            var appImage = OperatingSystem.IsLinux() ? AppImage ?? throw new InvalidOperationException("SourceGit is not running from an AppImage. Please update it the way it was installed.") : null;
            var target = bundle ?? appImage;
            EnsureWritable(target != null ? Path.GetDirectoryName(target)! : appDir);

            var workDir = Path.Combine(Path.GetTempPath(), $"sourcegit-update-{Guid.NewGuid():N}");
            Directory.CreateDirectory(workDir);

            try
            {
                var downloaded = Path.Combine(workDir, "package");
                await DownloadAsync(package, downloaded, progress, token).ConfigureAwait(false);
                await VerifyDigestAsync(package, downloaded, token).ConfigureAwait(false);

                token.ThrowIfCancellationRequested();
                progress?.Report(-1);

                await Task.Run(() =>
                {
                    if (bundle != null)
                    {
                        ReplaceAppBundle(downloaded, bundle);
                    }
                    else if (appImage != null && OperatingSystem.IsLinux())
                    {
                        ReplaceAppImage(downloaded, appImage);
                    }
                    else
                    {
                        var extractDir = Path.Combine(workDir, "files");
                        ZipFile.ExtractToDirectory(downloaded, extractDir);
                        ApplyPackage(extractDir, appDir);
                    }
                }, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    Directory.Delete(workDir, true);
                }
                catch
                {
                    // Leftovers in %TEMP% are harmless.
                }
            }
        }

        /// <summary>
        ///     Starts the (already replaced) executable. The new process waits until this one exits,
        ///     so the caller should quit right after this call.
        /// </summary>
        public static void LaunchUpdatedApp()
        {
            var pid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);

            // Through LaunchServices, so the new version runs as a regular app (Dock icon, activation)
            // rather than as a child of this process. `-n` starts it although this instance still runs.
            if (OperatingSystem.IsMacOS() && AppBundle is { } bundle)
            {
                RunTool("/usr/bin/open", "-n", "--env", $"{WaitPidEnv}={pid}", bundle);
                return;
            }

            // On Linux the new AppImage is started like any program; its runtime sets its own variables.
            // The working directory must not be inside the old AppImage, which goes away with this process.
            var exe = OperatingSystem.IsLinux() && AppImage is { } appImage ? appImage : Environment.ProcessPath!;
            var start = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
            };

            start.Environment[WaitPidEnv] = pid;
            Process.Start(start);
        }

        /// <summary>
        ///     Must run at the very start of the process: waits for the previous instance when the app
        ///     was restarted by an update, then removes the files the update renamed.
        /// </summary>
        public static void FinishPendingUpdate()
        {
            var pidStr = Environment.GetEnvironmentVariable(WaitPidEnv);
            if (!string.IsNullOrEmpty(pidStr))
            {
                // Do not leak the variable to git or other child processes.
                Environment.SetEnvironmentVariable(WaitPidEnv, null);

                if (int.TryParse(pidStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
                {
                    try
                    {
                        using var self = Process.GetCurrentProcess();
                        using var previous = Process.GetProcessById(pid);

                        // Windows reports the old process under the name its executable was renamed
                        // to (`SourceGit.exe.update-old`), so only the beginning of the name must match.
                        if (previous.ProcessName.StartsWith(self.ProcessName, StringComparison.OrdinalIgnoreCase))
                            previous.WaitForExit(TimeSpan.FromSeconds(30));
                    }
                    catch
                    {
                        // The previous instance has already exited.
                    }
                }
            }

            try
            {
                var list = CleanupListPath;
                if (!File.Exists(list))
                    return;

                var remaining = new List<string>();
                foreach (var file in File.ReadAllLines(list))
                {
                    if (!file.EndsWith(BackupSuffix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        if (File.Exists(file))
                            File.Delete(file);
                        else if (Directory.Exists(file))
                            Directory.Delete(file, true);
                    }
                    catch
                    {
                        // Still in use (another instance of the old version is running). Retry next time.
                        remaining.Add(file);
                    }
                }

                if (remaining.Count == 0)
                    File.Delete(list);
                else
                    File.WriteAllLines(list, remaining);
            }
            catch
            {
                // Cleanup is best effort.
            }
        }

        private static string AppDirectory => Path.GetDirectoryName(Environment.ProcessPath)!;

        /// <summary>
        ///     The `.app` bundle the app runs from (macOS), or null when it does not run from one.
        /// </summary>
        private static string AppBundle
        {
            get
            {
                // <name>.app/Contents/MacOS/<executable>
                var macOS = AppDirectory;
                var contents = Path.GetDirectoryName(macOS);
                var bundle = Path.GetDirectoryName(contents);
                if (bundle == null ||
                    !bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ||
                    !Path.GetFileName(contents).Equals("Contents", StringComparison.Ordinal) ||
                    !Path.GetFileName(macOS).Equals("MacOS", StringComparison.Ordinal))
                    return null;

                return bundle;
            }
        }

        /// <summary>
        ///     The AppImage file the app runs from (Linux), or null when it does not run from one.
        /// </summary>
        private static string AppImage
        {
            get
            {
                // Both are set by the AppImage runtime. APPDIR (where the image is mounted) must hold
                // this executable, or the variables were only inherited from another AppImage.
                var appImage = Environment.GetEnvironmentVariable("APPIMAGE");
                var mountDir = Environment.GetEnvironmentVariable("APPDIR");
                if (string.IsNullOrEmpty(appImage) || string.IsNullOrEmpty(mountDir) || !File.Exists(appImage))
                    return null;

                var prefix = mountDir.TrimEnd('/') + "/";
                return Environment.ProcessPath?.StartsWith(prefix, StringComparison.Ordinal) == true ? appImage : null;
            }
        }

        /// <summary>
        ///     Runtime identifier of the release package that fits this installation, or null when
        ///     this installation cannot be updated in place.
        /// </summary>
        private static string PackageRuntime
        {
            get
            {
                var arch = RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X64 => "x64",
                    Architecture.Arm64 => "arm64",
                    _ => null,
                };

                if (arch == null)
                    return null;

                if (OperatingSystem.IsWindows())
                    return $"win-{arch}";

                // Only a SourceGit.app can be replaced, not, for example, a `dotnet run` output folder.
                if (OperatingSystem.IsMacOS() && AppBundle != null)
                    return $"osx-{arch}";

                if (OperatingSystem.IsLinux() && AppImage != null)
                    return $"linux-{arch}";

                return null;
            }
        }

        // A file added inside a signed app bundle breaks its signature, so macOS keeps the list outside.
        private static string CleanupListPath => OperatingSystem.IsMacOS()
            ? Path.Combine(Native.OS.BasicDirectories.CacheDir, CleanupListFile)
            : Path.Combine(AppDirectory, CleanupListFile);

        private static string GetBundleToReplace()
        {
            var bundle = AppBundle ?? throw new InvalidOperationException("SourceGit is not running from an app bundle. Please download the update manually.");

            // A quarantined app started from Downloads runs from a read-only copy (App Translocation).
            if (bundle.Contains("/AppTranslocation/", StringComparison.Ordinal))
                throw new InvalidOperationException("SourceGit is running from a temporary read-only copy made by macOS. Move SourceGit.app to the Applications folder, start it from there and try again.");

            return bundle;
        }

        private static HttpClient CreateHttpClient(TimeSpan timeout)
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version ?? new System.Version(0, 0);
            var client = new HttpClient() { Timeout = timeout };

            // GitHub rejects API requests without a User-Agent.
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"SourceGit/{version}");
            return client;
        }

        private static void EnsureWritable(string dir)
        {
            var probe = Path.Combine(dir, $".update-write-test-{Guid.NewGuid():N}");
            try
            {
                File.WriteAllText(probe, string.Empty);
                File.Delete(probe);
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                throw new UnauthorizedAccessException($"Cannot write to '{dir}' ({e.Message}). Please download the update manually.");
            }
        }

        private static async Task DownloadAsync(VersionAsset package, string target, IProgress<double> progress, CancellationToken token)
        {
            using var client = CreateHttpClient(TimeSpan.FromSeconds(30));
            using var response = await client.GetAsync(package.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? package.Size;
            var received = 0L;
            var lastPercent = -1;
            var buffer = new byte[81920];

            await using (var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            await using (var output = File.Create(target))
            {
                int read;
                while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    received += read;

                    if (total > 0)
                    {
                        var percent = (int)(received * 100 / total);
                        if (percent != lastPercent)
                        {
                            lastPercent = percent;
                            progress?.Report(percent);
                        }
                    }
                }
            }

            if (total > 0 && received != total)
                throw new IOException($"Download is incomplete: received {received} of {total} bytes.");
        }

        private static async Task VerifyDigestAsync(VersionAsset package, string file, CancellationToken token)
        {
            // GitHub reports "sha256:<hex>" for release assets. Other feeds may omit it.
            const string prefix = "sha256:";
            if (string.IsNullOrEmpty(package.Digest) || !package.Digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return;

            await using var stream = File.OpenRead(file);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
            if (!hash.Equals(package.Digest.Substring(prefix.Length), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The downloaded package is corrupted (SHA-256 mismatch).");
        }

        private static void ApplyPackage(string extractDir, string appDir)
        {
            var exeName = Path.GetFileName(Environment.ProcessPath)!;
            var exe = Directory.EnumerateFiles(extractDir, exeName, SearchOption.AllDirectories)
                .OrderBy(x => x.Length)
                .FirstOrDefault() ?? throw new InvalidDataException($"The package does not contain {exeName}.");

            var srcRoot = Path.GetDirectoryName(exe)!;
            var replaced = new List<(string File, string Backup)>();
            var created = new List<string>();

            try
            {
                foreach (var src in Directory.EnumerateFiles(srcRoot, "*", SearchOption.AllDirectories))
                {
                    var dst = Path.Combine(appDir, Path.GetRelativePath(srcRoot, src));
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);

                    if (File.Exists(dst))
                    {
                        var backup = MakeBackupPath(dst);
                        File.Move(dst, backup);
                        replaced.Add((dst, backup));
                    }
                    else
                    {
                        created.Add(dst);
                    }

                    File.Copy(src, dst);
                }
            }
            catch
            {
                // Put the old version back so the app keeps working.
                foreach (var file in created)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // Ignore
                    }
                }

                for (var i = replaced.Count - 1; i >= 0; i--)
                {
                    var (file, backup) = replaced[i];
                    try
                    {
                        if (File.Exists(file))
                            File.Delete(file);
                        File.Move(backup, file);
                    }
                    catch
                    {
                        // Ignore
                    }
                }

                throw;
            }

            // The renamed files are still in use by this process. Remove them on the next start.
            if (replaced.Count > 0)
                File.AppendAllLines(CleanupListPath, replaced.Select(x => x.Backup));
        }

        private static void ReplaceAppBundle(string zip, string bundle)
        {
            var exeName = Path.GetFileName(Environment.ProcessPath)!;

            // Unpack next to the running bundle, so the swap below is a rename on the same volume.
            var staging = Path.Combine(Path.GetDirectoryName(bundle)!, $".sourcegit-update-{Guid.NewGuid():N}");

            try
            {
                // Unlike ZipFile, ditto keeps symlinks and everything else a signed bundle relies on.
                RunTool("/usr/bin/ditto", "-x", "-k", zip, staging);

                var fresh = Directory.EnumerateDirectories(staging, "*.app", SearchOption.AllDirectories)
                    .Where(x => File.Exists(Path.Combine(x, "Contents", "MacOS", exeName)))
                    .OrderBy(x => x.Length)
                    .FirstOrDefault() ?? throw new InvalidDataException($"The package does not contain an app with {exeName}.");

                var backup = MakeBackupPath(bundle);
                Directory.Move(bundle, backup);

                try
                {
                    Directory.Move(fresh, bundle);
                }
                catch
                {
                    // Put the old version back so the app keeps working.
                    Directory.Move(backup, bundle);
                    throw;
                }

                try
                {
                    File.AppendAllLines(CleanupListPath, [backup]);
                }
                catch
                {
                    // The new version is in place. The old bundle then stays until the next update.
                }
            }
            finally
            {
                try
                {
                    if (Directory.Exists(staging))
                        Directory.Delete(staging, true);
                }
                catch
                {
                    // Ignore
                }
            }
        }

        [SupportedOSPlatform("linux")]
        private static void ReplaceAppImage(string downloaded, string appImage)
        {
            // Copy next to the running AppImage first, so replacing it is a rename on the same file system.
            var staging = Path.Combine(Path.GetDirectoryName(appImage)!, $".sourcegit-update-{Guid.NewGuid():N}");

            try
            {
                File.Copy(downloaded, staging);
                File.SetUnixFileMode(staging, File.GetUnixFileMode(appImage) | UnixFileMode.UserExecute);
                File.Move(staging, appImage, true);
            }
            catch
            {
                try
                {
                    File.Delete(staging);
                }
                catch
                {
                    // Ignore
                }

                throw;
            }
        }

        private static void RunTool(string tool, params string[] args)
        {
            var start = new ProcessStartInfo(tool)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };

            foreach (var arg in args)
                start.ArgumentList.Add(arg);

            using var proc = Process.Start(start) ?? throw new InvalidOperationException($"Cannot start {tool}.");
            var error = proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            if (proc.ExitCode != 0)
                throw new IOException($"{Path.GetFileName(tool)} failed: {error.Trim()}");
        }

        private static string MakeBackupPath(string path)
        {
            var backup = path + BackupSuffix;

            try
            {
                if (Directory.Exists(backup))
                    Directory.Delete(backup, true);
                else if (File.Exists(backup))
                    File.Delete(backup);

                return backup;
            }
            catch
            {
                // Locked leftover of a previous update that is still running somewhere.
                return $"{path}.{Guid.NewGuid():N}{BackupSuffix}";
            }
        }

        private const string FeedOverrideEnv = "SOURCEGIT_UPDATE_FEED";
        private const string WaitPidEnv = "SOURCEGIT_UPDATE_WAIT_PID";
        private const string BackupSuffix = ".update-old";
        private const string CleanupListFile = ".update-cleanup";
    }
}
