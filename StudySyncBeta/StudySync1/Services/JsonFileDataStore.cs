using System.Text.Json;
using StudySync1.Models;

namespace StudySync1.Services;

public interface IDataStore
{
    Task<AppState> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(AppState state, CancellationToken ct = default);
}

public sealed class JsonFileDataStore : IDataStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

    public JsonFileDataStore(IWebHostEnvironment env)
    {
        var dataDir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDir);
        _path = Path.Combine(dataDir, "AppState.json");
    }

    public async Task<AppState> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return new AppState();

        try
        {
            await using var fs = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<AppState>(fs, _opts, ct)
                   ?? new AppState();
        }
        catch (JsonException)
        {
            // JSON is invalid or schema drift happened — quarantine the file
            var badPath = _path + ".bad";
            try { File.Copy(_path, badPath, overwrite: true); } catch { /* ignore */ }
            try { File.Delete(_path); } catch { /* ignore */ }

            return new AppState();
        }
    }

    public async Task SaveAsync(AppState state, CancellationToken ct = default)
    {
        var tmp = _path + ".tmp";
        await using (var fs = File.Create(tmp))
            await JsonSerializer.SerializeAsync(fs, state, _opts, ct);

        File.Copy(tmp, _path, overwrite: true);
        File.Delete(tmp);
    }
}