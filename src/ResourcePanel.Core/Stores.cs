using System.Text.Json;

namespace ResourcePanel;

public static class AppPaths
{
    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppInfo.ProductName);

    public static string ActionsFile => Path.Combine(Root, "actions.json");
    public static string ProfileFile => Path.Combine(Root, "profile.json");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string StartupBackup => Path.Combine(Root, "startup-backup");

    public static void Ensure() => Directory.CreateDirectory(Root);
}

public static class JsonFiles
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void WriteAtomic<T>(string path, T value)
    {
        AppPaths.Ensure();
        var json = JsonSerializer.Serialize(value, Options);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    public static T ReadOrNew<T>(string path) where T : new()
    {
        try
        {
            if (!File.Exists(path))
                return new T();
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? new T();
        }
        catch (JsonException)
        {
            return new T();
        }
        catch (IOException)
        {
            return new T();
        }
    }
}

public static class SettingsStore
{
    static readonly object Gate = new();

    public static AppSettings Load()
    {
        lock (Gate)
            return JsonFiles.ReadOrNew<AppSettings>(AppPaths.SettingsFile);
    }

    public static void Save(AppSettings settings)
    {
        lock (Gate)
            JsonFiles.WriteAtomic(AppPaths.SettingsFile, settings);
    }
}

public static class ActionLog
{
    const int MaxEntries = 2000;
    static readonly object Gate = new();

    public static void Append(string action, string target, bool success, string? detail = null, int? pid = null)
    {
        lock (Gate)
        {
            var file = JsonFiles.ReadOrNew<ActionLogFile>(AppPaths.ActionsFile);
            file.Entries.Add(new ActionEntry
            {
                Time = DateTimeOffset.Now,
                Action = action,
                Target = target,
                Pid = pid,
                Success = success,
                Detail = detail
            });
            if (file.Entries.Count > MaxEntries)
                file.Entries.RemoveRange(0, file.Entries.Count - MaxEntries);
            JsonFiles.WriteAtomic(AppPaths.ActionsFile, file);
        }
    }

    public static IReadOnlyList<ActionEntry> ReadRecent(int take = 200)
    {
        lock (Gate)
        {
            var file = JsonFiles.ReadOrNew<ActionLogFile>(AppPaths.ActionsFile);
            return file.Entries.TakeLast(take).Reverse().ToList();
        }
    }
}

public static class ProfileStore
{
    static readonly object Gate = new();

    public static ProfileDocument Load()
    {
        lock (Gate)
            return JsonFiles.ReadOrNew<ProfileDocument>(AppPaths.ProfileFile);
    }

    public static void Save(ProfileDocument document)
    {
        lock (Gate)
            JsonFiles.WriteAtomic(AppPaths.ProfileFile, document);
    }

    public static void Add(ProfileChange change)
    {
        lock (Gate)
        {
            var document = JsonFiles.ReadOrNew<ProfileDocument>(AppPaths.ProfileFile);
            document.Changes.Add(change);
            JsonFiles.WriteAtomic(AppPaths.ProfileFile, document);
        }
    }

    public static void Replace(ProfileDocument document) => Save(document);
}
