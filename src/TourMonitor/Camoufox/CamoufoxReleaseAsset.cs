namespace TourMonitor.Camoufox;

public sealed record CamoufoxReleaseAsset(
    string DownloadUrl,
    bool IsTarGzArchive,
    string ExecutableName);
