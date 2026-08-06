using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using TourMonitor.Camoufox;

namespace TourMonitor.Camoufox;

public interface ICamoufoxInstaller
{
    string? GetInstalledExecutablePathOrNull(string installDirectory);
    Task<string> EnsureInstalledAsync(string version, string installDirectory, string? downloadUrlOverride, CancellationToken cancellationToken = default);
}

/// <summary>Скачивает патченный Firefox (Camoufox) из релизов и распаковывает его.</summary>
public sealed class CamoufoxInstaller : ICamoufoxInstaller
{
    private readonly ICamoufoxReleaseResolver _releaseResolver;
    private readonly ILogger<CamoufoxInstaller> _logger;

    public CamoufoxInstaller(ICamoufoxReleaseResolver releaseResolver, ILogger<CamoufoxInstaller> logger)
    {
        _releaseResolver = releaseResolver;
        _logger = logger;
    }

    public string? GetInstalledExecutablePathOrNull(string installDirectory)
    {
        var candidate = Path.Combine(installDirectory, ExecutableName());
        return File.Exists(candidate) ? candidate : null;
    }

    public async Task<string> EnsureInstalledAsync(string version, string installDirectory, string? downloadUrlOverride, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(installDirectory);

        var installed = GetInstalledExecutablePathOrNull(installDirectory);
        if (installed is not null)
            return installed;

        _logger.LogInformation("Camoufox не найден. Скачивание и установка...");
        var asset = _releaseResolver.Resolve(version, downloadUrlOverride);
        var archivePath = Path.Combine(Path.GetTempPath(), Path.GetFileName(new Uri(asset.DownloadUrl).LocalPath));

        await DownloadAsync(asset.DownloadUrl, archivePath, cancellationToken);
        ExtractArchive(archivePath, installDirectory, asset.IsTarGzArchive);
        TryDeleteFile(archivePath);

        var discovered = LocateExecutable(installDirectory, asset.ExecutableName);
        EnsureExecutablePermission(discovered);
        _logger.LogInformation("Camoufox установлен: {Path}", discovered);
        return discovered;
    }

    private static string ExecutableName() =>
        OperatingSystem.IsWindows() ? "camoufox.exe" : "camoufox";

    private async Task DownloadAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(destinationPath);

        var buffer = new byte[81920];
        long readTotal = 0;
        var lastPrintedPercent = -1;

        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            readTotal += read;
            if (totalBytes is > 0)
            {
                var percent = (int)Math.Min(100, readTotal * 100 / totalBytes.Value);
                if (percent != lastPrintedPercent)
                {
                    lastPrintedPercent = percent;
                    _logger.LogInformation("[DOWNLOAD] {ProgressPercent}%", percent);
                }
            }
        }

        if (totalBytes is null && readTotal > 0)
            _logger.LogInformation("[DOWNLOAD] {DownloadedBytes} байт (размер неизвестен)", readTotal);
    }

    private static void ExtractArchive(string archivePath, string installDirectory, bool isTarGzArchive)
    {
        if (isTarGzArchive)
        {
            using var archiveStream = File.OpenRead(archivePath);
            using var gzipStream = new GZipStream(archiveStream, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gzipStream, installDirectory, overwriteFiles: true);
            return;
        }

        ZipFile.ExtractToDirectory(archivePath, installDirectory, overwriteFiles: true);
    }

    private static string LocateExecutable(string installDirectory, string executableName)
    {
        var directPath = Path.Combine(installDirectory, executableName);
        if (File.Exists(directPath))
            return directPath;

        var nested = Directory
            .EnumerateFiles(installDirectory, executableName, SearchOption.AllDirectories)
            .FirstOrDefault();
        if (nested is not null)
            return nested;

        throw new FileNotFoundException($"Исполняемый файл Camoufox не найден после распаковки: {executableName}");
    }

    private static void EnsureExecutablePermission(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "chmod",
                ArgumentList = { "+x", path },
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            });
            process?.WaitForExit();
        }
        catch
        {
            // Some environments already preserve executable permissions.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Ignore temp cleanup failures.
        }
    }
}
