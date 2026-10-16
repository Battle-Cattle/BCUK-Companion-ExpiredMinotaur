using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BCUKCompanion.ExpiredMinotaur.Updates;

/// <summary>A newer published release, and the zip asset to install it from.</summary>
public sealed record AvailableUpdate(Version Version, string TagName, Uri DownloadUrl, Uri ReleasePageUrl);

/// <summary>
/// Checks this repo's GitHub Releases for a newer build (published by
/// <c>.github/workflows/release.yml</c>) and installs it in place: the release zip is extracted
/// to a temp folder, then a small PowerShell script waits for this process to exit, copies the
/// new files over the install folder and relaunches the app. No WPF dependency, so the UI flow
/// in <see cref="AppUpdater"/> owns all prompts and balloons.
/// </summary>
public sealed class UpdateClient
{
    private const string RepoOwner = "Battle-Cattle";
    private const string RepoName = "BCUK-Companion-ExpiredMinotaur";
    private const string AssetSuffix = "-win-x64.zip";

    private static readonly Uri LatestReleaseUrl = new($"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");

    /// <summary>
    /// This build's version, stamped from the release tag by the release workflow. Local builds
    /// keep the csproj's 0.0.0 placeholder, which <see cref="IsReleaseBuild"/> uses to keep a
    /// developer's build from replacing itself with the published one.
    /// </summary>
    public static Version CurrentVersion { get; } = Normalize(typeof(UpdateClient).Assembly.GetName().Version ?? new Version(0, 0, 0));

    public static bool IsReleaseBuild => CurrentVersion > new Version(0, 0, 0);

    // Declared after CurrentVersion: static initializers run in textual order and this one reads it.
    private static readonly HttpClient Http = CreateHttpClient();

    /// <summary>
    /// Returns the latest non-prerelease release if it is newer than <see cref="CurrentVersion"/>
    /// and carries a Windows zip, otherwise <c>null</c>. Network and API errors propagate.
    /// </summary>
    public async Task<AvailableUpdate?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Http.GetAsync(LatestReleaseUrl, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // No published release yet.
            return null;
        }
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (release?.TagName is null || !TryParseTag(release.TagName, out var version) || version <= CurrentVersion)
        {
            return null;
        }

        var asset = release.Assets?.FirstOrDefault(a => a.Name?.EndsWith(AssetSuffix, StringComparison.OrdinalIgnoreCase) == true);
        if (asset?.BrowserDownloadUrl is null)
        {
            return null;
        }

        return new AvailableUpdate(version, release.TagName, new Uri(asset.BrowserDownloadUrl), new Uri(release.HtmlUrl ?? $"https://github.com/{RepoOwner}/{RepoName}/releases"));
    }

    /// <summary>
    /// Downloads and extracts <paramref name="update"/>, then starts the script that swaps it in
    /// once this process exits. The caller must shut the app down straight after this returns.
    /// Throws if the install folder isn't writable (e.g. under Program Files) so the caller can
    /// point the user at a manual download instead.
    /// </summary>
    public async Task PrepareAndLaunchInstallerAsync(AvailableUpdate update, CancellationToken cancellationToken = default)
    {
        string installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        string exePath = Environment.ProcessPath ?? throw new InvalidOperationException("Can't determine the running executable's path.");
        EnsureWritable(installDir);

        string workDir = Path.Combine(Path.GetTempPath(), $"{Program.DataFolderName}.Update", update.TagName);
        if (Directory.Exists(workDir))
        {
            Directory.Delete(workDir, recursive: true);
        }
        string stagingDir = Path.Combine(workDir, "files");
        Directory.CreateDirectory(stagingDir);

        string zipPath = Path.Combine(workDir, "update.zip");
        using (var response = await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            await using var file = File.Create(zipPath);
            await response.Content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }

        ZipFile.ExtractToDirectory(zipPath, stagingDir);
        if (!File.Exists(Path.Combine(stagingDir, Path.GetFileName(exePath))))
        {
            throw new InvalidDataException($"The downloaded update doesn't contain {Path.GetFileName(exePath)}.");
        }

        string scriptPath = Path.Combine(workDir, "apply-update.ps1");
        await File.WriteAllTextAsync(scriptPath, ApplyUpdateScript, cancellationToken).ConfigureAwait(false);

        // Paths go in as -File arguments, never spliced into the script text.
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in new[]
        {
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", scriptPath,
            "-ProcessId", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-Source", stagingDir, "-Target", installDir, "-Exe", exePath, "-WorkDir", workDir,
        })
        {
            startInfo.ArgumentList.Add(arg);
        }

        Process.Start(startInfo)?.Dispose();
    }

    // Waits for the app to exit (releasing its file locks and single-instance mutex), copies the
    // new build over the old one with a few retries in case antivirus briefly holds a file, then
    // relaunches. Files the new build no longer ships are left in place rather than mirroring,
    // so nothing a user dropped into the folder gets deleted.
    private const string ApplyUpdateScript = """
        param(
            [int]$ProcessId,
            [string]$Source,
            [string]$Target,
            [string]$Exe,
            [string]$WorkDir
        )
        $ErrorActionPreference = 'Stop'
        try { Wait-Process -Id $ProcessId -Timeout 60 } catch { }
        for ($attempt = 1; $attempt -le 10; $attempt++) {
            try {
                Copy-Item -Path (Join-Path $Source '*') -Destination $Target -Recurse -Force
                break
            } catch {
                if ($attempt -eq 10) { throw }
                Start-Sleep -Seconds 1
            }
        }
        Start-Process -FilePath $Exe -WorkingDirectory $Target
        Remove-Item -Path $WorkDir -Recurse -Force -ErrorAction SilentlyContinue
        """;

    private static void EnsureWritable(string directory)
    {
        string probe = Path.Combine(directory, $".update-probe-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new UnauthorizedAccessException($"Can't write to the install folder ({directory}).", ex);
        }
    }

    internal static bool TryParseTag(string tag, out Version version)
    {
        // v1.2.3, v1.2.3-beta.1, v1.2.3+meta → 1.2.3
        string core = tag.TrimStart('v', 'V').Split('-', '+')[0];
        if (Version.TryParse(core, out var parsed))
        {
            version = Normalize(parsed);
            return true;
        }
        version = new Version(0, 0, 0);
        return false;
    }

    // Compare on major.minor.patch only: the assembly version carries a 4th (revision) part of 0
    // while a parsed "1.2.3" leaves it undefined, and those would otherwise compare unequal.
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        // GitHub's API rejects requests without a User-Agent.
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(RepoName, CurrentVersion.ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("assets")] public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    }
}
