namespace OpenTaan.GameServer;

/// <summary>Finds the files a setting names that whoever runs the server keeps beside it, never in the repository.</summary>
internal static class LocalFiles
{
    /// <summary>
    /// The full path of <paramref name="file"/>, or null when there is no such file. A
    /// relative path is looked for in <paramref name="contentRoot"/> and then in each folder
    /// above it, so it is found whichever folder the server is started from.
    /// </summary>
    public static string? Locate(string file, string contentRoot)
    {
        if (Path.IsPathRooted(file))
        {
            return File.Exists(file) ? file : null;
        }

        for (var folder = new DirectoryInfo(contentRoot); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, file);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
