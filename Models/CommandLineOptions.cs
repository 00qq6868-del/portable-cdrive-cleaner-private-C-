namespace PortableCDriveCleaner.Models;

public sealed class CommandLineOptions
{
    public bool Installer { get; init; }
    public bool ElevatedLaunch { get; init; }
    public bool ReadOnly { get; init; }
    public bool SkipMigrationPrompt { get; init; }
    public bool Scheduled { get; init; }
    public bool AutoSafeOnly { get; init; }
    public bool NoPrompt { get; init; }
    public string? SelectionFile { get; init; }

    public static CommandLineOptions Parse(string[] args)
    {
        var installer = false;
        var elevatedLaunch = false;
        var readOnly = false;
        var skipMigrationPrompt = false;
        var scheduled = false;
        var autoSafeOnly = false;
        var noPrompt = false;
        string? selectionFile = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i].Trim();
            switch (arg.ToLowerInvariant())
            {
                case "--installer":
                    installer = true;
                    break;
                case "--elevated":
                    elevatedLaunch = true;
                    break;
                case "--readonly":
                    readOnly = true;
                    break;
                case "--skip-migration-prompt":
                    skipMigrationPrompt = true;
                    break;
                case "--scheduled":
                    scheduled = true;
                    break;
                case "--auto-safe-only":
                    autoSafeOnly = true;
                    break;
                case "--no-prompt":
                    noPrompt = true;
                    break;
                case "--selection":
                    if (i + 1 < args.Length)
                    {
                        selectionFile = args[++i];
                    }
                    break;
            }
        }

        return new CommandLineOptions
        {
            Installer = installer,
            ElevatedLaunch = elevatedLaunch,
            ReadOnly = readOnly,
            SkipMigrationPrompt = skipMigrationPrompt,
            Scheduled = scheduled,
            AutoSafeOnly = autoSafeOnly,
            NoPrompt = noPrompt,
            SelectionFile = selectionFile
        };
    }
}
