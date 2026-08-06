using System.Runtime.InteropServices;

namespace TourMonitor.Camoufox;

public interface ICamoufoxReleaseResolver
{
    CamoufoxReleaseAsset Resolve(string version, string? downloadUrlOverride);
}

/// <summary>Резолвит ассет патченного Firefox из релизов daijro/camoufox по ОС/архитектуре.</summary>
public sealed class CamoufoxReleaseResolver : ICamoufoxReleaseResolver
{
    public CamoufoxReleaseAsset Resolve(string version, string? downloadUrlOverride)
    {
        if (!string.IsNullOrWhiteSpace(downloadUrlOverride))
        {
            var fileName = Path.GetFileName(new Uri(downloadUrlOverride).LocalPath);
            var isTarGz = fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
            return new CamoufoxReleaseAsset(
                downloadUrlOverride,
                isTarGz,
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "camoufox.exe" : "camoufox");
        }

        var assetVersion = version.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? version[1..]
            : version;
        var assetPrefix = $"https://github.com/daijro/camoufox/releases/download/{version}/camoufox-{assetVersion}";
        var executableName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "camoufox.exe" : "camoufox";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new CamoufoxReleaseAsset($"{assetPrefix}-win.x86_64.zip", false, executableName);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x86_64";
            return new CamoufoxReleaseAsset($"{assetPrefix}-lin.{arch}.zip", false, executableName);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x86_64";
            return new CamoufoxReleaseAsset($"{assetPrefix}-mac.{arch}.zip", false, executableName);
        }

        throw new PlatformNotSupportedException("Unsupported OS platform for Camoufox installation.");
    }
}
