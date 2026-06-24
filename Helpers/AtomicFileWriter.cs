using System.IO;

namespace StrideBrowser.Helpers;

/// <summary>
/// Writes a file atomically by writing to a .tmp file first,
/// then renaming to the target path. This prevents corruption
/// if the app crashes mid-write.
/// </summary>
public static class AtomicFileWriter
{
    public static void WriteAllText(string path, string contents)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        var tmpPath = path + ".tmp";
        File.WriteAllText(tmpPath, contents);
        File.Move(tmpPath, path, overwrite: true);
    }
}
