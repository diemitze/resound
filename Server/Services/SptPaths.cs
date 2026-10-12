namespace ResoundServer.Services;

/// <summary>Finds the client's folders from inside the server's.</summary>
public static class SptPaths
{
    /// <summary>Walks up out of the server folder to BepInEx/plugins, or null.</summary>
    public static string? FindPluginsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            var candidate = Path.Join(dir.FullName, "BepInEx", "plugins");
            if (Directory.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>plugins/Resound, where add-on packs sit next to the built-in sounds/.</summary>
    public static string? FindResoundRoot()
    {
        var plugins = FindPluginsDir();
        return plugins is null ? null : Path.Join(plugins, "Resound");
    }
}
