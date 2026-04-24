using System.Text.Json;
using PortableCDriveCleaner.Infrastructure;
using PortableCDriveCleaner.Models;

namespace PortableCDriveCleaner.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly PortableContext _context;

    public SettingsService(PortableContext context)
    {
        _context = context;
    }

    public AppSettings Load()
    {
        if (!File.Exists(_context.SettingsPath))
        {
            var defaults = new AppSettings();
            Save(defaults);
            return defaults;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_context.SettingsPath), JsonOptions) ?? new AppSettings();
            settings.Normalize();
            Save(settings);
            return settings;
        }
        catch
        {
            var fallback = new AppSettings();
            Save(fallback);
            return fallback;
        }
    }

    public AppSettings Save(AppSettings settings)
    {
        settings.Normalize();
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_context.SettingsPath, json);
        return settings;
    }
}
