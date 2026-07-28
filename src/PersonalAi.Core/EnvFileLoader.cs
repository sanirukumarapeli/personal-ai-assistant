namespace PersonalAi.Core;

public static class EnvFileLoader
{
    /// <summary>
    /// Loads KEY=VALUE pairs from the nearest .env walking up from <paramref name="startDirectory"/>.
    /// Does not override variables that are already set in the process environment.
    /// </summary>
    public static string? LoadNearest(string? startDirectory = null)
    {
        var dir = new DirectoryInfo(startDirectory ?? Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var envPath = Path.Combine(dir.FullName, ".env");
            if (File.Exists(envPath))
            {
                foreach (var raw in File.ReadAllLines(envPath))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith('#') || !line.Contains('='))
                        continue;

                    var idx = line.IndexOf('=');
                    var key = line[..idx].Trim();
                    var value = line[(idx + 1)..].Trim().Trim('"').Trim('\'');
                    if (key.Length == 0)
                        continue;
                    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                        Environment.SetEnvironmentVariable(key, value);
                }

                return envPath;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
