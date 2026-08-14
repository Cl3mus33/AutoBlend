using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using AutoBlend.Core.Configuration;
using AutoBlend.Core.Pipeline;
using AutoBlend.Core.Scanning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace AutoBlend.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService = new();

    [ObservableProperty]
    private string gameLocation = string.Empty;

    [ObservableProperty]
    private GameType gameType = GameType.SkyrimSE;

    [ObservableProperty]
    private string outputLocation = string.Empty;

    [ObservableProperty]
    private ModManagerType modManager = ModManagerType.None;

    [ObservableProperty]
    private string mo2InstancePath = string.Empty;

    [ObservableProperty]
    private string mo2ProfileName = "Default";

    [ObservableProperty]
    private string textureSetNamingTemplate = "{Type}{Name}";

    [ObservableProperty]
    private LandscapeFolderRule? selectedLandscapeFolderRule;

    [ObservableProperty]
    private string? selectedMeshBlacklistEntry;

    [ObservableProperty]
    private string? selectedEditorIdKeyword;

    [ObservableProperty]
    private string newMeshBlacklistEntry = string.Empty;

    [ObservableProperty]
    private string newEditorIdKeyword = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrimaryActionCommand))]
    private bool isRunning;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private int progressValue;

    [ObservableProperty]
    private int progressMax = 1;

    [ObservableProperty]
    private bool progressIsIndeterminate;

    [ObservableProperty]
    private bool hasStarted;

    [ObservableProperty]
    private bool hasCompleted;

    [ObservableProperty]
    private bool hasFailed;

    [ObservableProperty]
    private bool showDetails;

    public ObservableCollection<LandscapeFolderRule> LandscapeFolderRules { get; } = new();
    public ObservableCollection<string> MeshBlacklist { get; } = new();
    public ObservableCollection<string> EditorIdBlacklistKeywords { get; } = new();
    public ObservableCollection<string> DetailLines { get; } = new();

    public IReadOnlyList<GameType> GameTypes { get; } = Enum.GetValues<GameType>();

    public IReadOnlyList<ModManagerOption> ModManagerOptions { get; } = new[]
    {
        new ModManagerOption(ModManagerType.None, "None / Vortex"),
        new ModManagerOption(ModManagerType.ModOrganizer2, "Mod Organizer 2"),
    };

    public MainViewModel()
    {
        Load(_settingsService.Load());
    }

    private void Load(PatcherSettings settings)
    {
        GameLocation = settings.GameLocation;
        GameType = settings.GameType;
        OutputLocation = settings.OutputLocation;
        ModManager = settings.ModManager;
        Mo2InstancePath = settings.Mo2InstancePath;
        Mo2ProfileName = settings.Mo2ProfileName;
        TextureSetNamingTemplate = settings.TextureSetNamingTemplate;

        LandscapeFolderRules.Clear();
        foreach (var rule in settings.LandscapeFolderRules)
        {
            LandscapeFolderRules.Add(rule);
        }

        MeshBlacklist.Clear();
        foreach (var entry in settings.MeshBlacklist)
        {
            MeshBlacklist.Add(entry);
        }

        EditorIdBlacklistKeywords.Clear();
        foreach (var keyword in settings.EditorIdBlacklistKeywords)
        {
            EditorIdBlacklistKeywords.Add(keyword);
        }
    }

    private PatcherSettings ToSettings() => new()
    {
        GameLocation = GameLocation,
        GameType = GameType,
        OutputLocation = OutputLocation,
        ModManager = ModManager,
        Mo2InstancePath = Mo2InstancePath,
        Mo2ProfileName = Mo2ProfileName,
        TextureSetNamingTemplate = TextureSetNamingTemplate,
        LandscapeFolderRules = LandscapeFolderRules.ToList(),
        MeshBlacklist = MeshBlacklist.ToList(),
        EditorIdBlacklistKeywords = EditorIdBlacklistKeywords.ToList(),
    };

    [RelayCommand]
    private void BrowseGameLocation()
    {
        var path = BrowseFolder(GameLocation);
        if (path is not null)
        {
            GameLocation = path;
        }
    }

    [RelayCommand]
    private void BrowseOutputLocation()
    {
        var path = BrowseFolder(OutputLocation);
        if (path is not null)
        {
            OutputLocation = path;
        }
    }

    [RelayCommand]
    private void BrowseMo2InstancePath()
    {
        var path = BrowseFolder(Mo2InstancePath);
        if (path is not null)
        {
            Mo2InstancePath = path;
        }
    }

    // Defaults the profile field to whatever MO2 itself currently has active
    // (ModOrganizer.ini's selected_profile), so picking the instance path is normally enough on
    // its own — still overridable for anyone who wants to target a non-active profile.
    partial void OnMo2InstancePathChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        try
        {
            if (Mo2InstanceReader.TryDetectSelectedProfile(value, out var detected))
            {
                Mo2ProfileName = detected;
            }
        }
        catch
        {
            // best-effort default; leave whatever profile name was already there
        }
    }

    private static string? BrowseFolder(string initialDirectory)
    {
        var dialog = new OpenFolderDialog
        {
            InitialDirectory = Directory.Exists(initialDirectory) ? initialDirectory : string.Empty,
        };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    [RelayCommand]
    private void AddLandscapeFolderRule() => LandscapeFolderRules.Add(new LandscapeFolderRule("new-folder", "NewType"));

    [RelayCommand]
    private void RemoveLandscapeFolderRule()
    {
        if (SelectedLandscapeFolderRule is not null)
        {
            LandscapeFolderRules.Remove(SelectedLandscapeFolderRule);
        }
    }

    [RelayCommand]
    private void AddMeshBlacklistEntry()
    {
        if (string.IsNullOrWhiteSpace(NewMeshBlacklistEntry))
        {
            return;
        }

        MeshBlacklist.Add(NewMeshBlacklistEntry.Trim());
        NewMeshBlacklistEntry = string.Empty;
    }

    [RelayCommand]
    private void RemoveMeshBlacklistEntry()
    {
        if (SelectedMeshBlacklistEntry is not null)
        {
            MeshBlacklist.Remove(SelectedMeshBlacklistEntry);
        }
    }

    [RelayCommand]
    private void AddEditorIdKeyword()
    {
        if (string.IsNullOrWhiteSpace(NewEditorIdKeyword))
        {
            return;
        }

        EditorIdBlacklistKeywords.Add(NewEditorIdKeyword.Trim());
        NewEditorIdKeyword = string.Empty;
    }

    [RelayCommand]
    private void RemoveEditorIdKeyword()
    {
        if (SelectedEditorIdKeyword is not null)
        {
            EditorIdBlacklistKeywords.Remove(SelectedEditorIdKeyword);
        }
    }

    [RelayCommand]
    private void Cancel() => Application.Current.Shutdown();

    [RelayCommand]
    private void ToggleDetails() => ShowDetails = !ShowDetails;

    // One button whose meaning changes with run state, matching AutoSeasons: "Start Patching"
    // before a run, "Done - Close" / "Failed - Close" after — the user explicitly dismisses the
    // app once they've had a chance to read the status/details, rather than it vanishing on a timer.
    [RelayCommand(CanExecute = nameof(CanRunPrimaryAction))]
    private async Task PrimaryAction()
    {
        if (HasCompleted)
        {
            Application.Current.Shutdown();
            return;
        }

        await StartPatching();
    }

    private bool CanRunPrimaryAction() => !IsRunning;

    private async Task StartPatching()
    {
        var settings = ToSettings();
        _settingsService.Save(settings);

        HasStarted = true;
        HasCompleted = false;
        HasFailed = false;
        DetailLines.Clear();
        IsRunning = true;
        StatusMessage = "Starting...";
        ProgressIsIndeterminate = true;
        try
        {
            var result = await Task.Run(() =>
            {
                var orchestrator = new PatchOrchestrator(settings);
                return orchestrator.Run(OnProgress);
            });

            var logPath = WriteLog(settings, result, exception: null);
            DetailLines.Add($"Static/MoveableStatic records scanned: {result.RecordsScanned}");
            DetailLines.Add($"Meshes patched in place: {result.MeshesPatchedInPlace}");
            DetailLines.Add($"Meshes duplicated (conflict): {result.MeshesDuplicated}");
            DetailLines.Add($"TextureSets created: {result.TextureSetsCreated}");
            DetailLines.Add($"Alternate Textures assigned: {result.AlternateTexturesAssigned}");
            DetailLines.Add(result.OutputEspPath is not null ? $"Plugin written: {result.OutputEspPath}" : "No plugin written (nothing in scope).");
            if (result.Warnings.Count > 0)
            {
                DetailLines.Add($"{result.Warnings.Count} warning(s) — see {logPath} for the full list:");
                foreach (var warning in result.Warnings.Take(50))
                {
                    DetailLines.Add($" - {warning}");
                }
            }
            DetailLines.Add($"Log written to: {logPath}");

            StatusMessage = "Done.";
            ProgressIsIndeterminate = false;
            ProgressValue = ProgressMax;
            HasCompleted = true;
        }
        catch (Exception ex)
        {
            var logPath = WriteLog(settings, result: null, exception: ex);
            DetailLines.Add(ex.Message);
            DetailLines.Add($"Full details written to: {logPath}");
            StatusMessage = "Failed - see details below.";
            HasFailed = true;
            HasCompleted = true;
        }
        finally
        {
            IsRunning = false;
        }
    }

    // Called from PatchOrchestrator.Run() on the background thread (it's invoked inside
    // Task.Run) — must never touch DetailLines (an ObservableCollection) directly here, only
    // simple properties, which WPF's binding tolerates from a non-UI thread for display purposes.
    private void OnProgress(PatchProgress progress)
    {
        StatusMessage = progress.Message;
        ProgressIsIndeterminate = progress.IsIndeterminate;
        if (!progress.IsIndeterminate)
        {
            ProgressMax = progress.Total;
            ProgressValue = progress.Current;
        }
    }

    private static string WriteLog(PatcherSettings settings, PatchRunResult? result, Exception? exception)
    {
        var summary = new StringBuilder();
        summary.AppendLine($"AutoBlend run — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        summary.AppendLine();

        if (result is not null)
        {
            summary.AppendLine($"Static/MoveableStatic records scanned: {result.RecordsScanned}");
            summary.AppendLine($"Meshes patched in place: {result.MeshesPatchedInPlace}");
            summary.AppendLine($"Meshes duplicated (conflict): {result.MeshesDuplicated}");
            summary.AppendLine($"TextureSets created: {result.TextureSetsCreated}");
            summary.AppendLine($"Alternate Textures assigned: {result.AlternateTexturesAssigned}");
            summary.AppendLine(result.OutputEspPath is not null
                ? $"Plugin written: {result.OutputEspPath}"
                : "No plugin written (nothing in scope).");

            if (result.Warnings.Count > 0)
            {
                summary.AppendLine();
                summary.AppendLine($"{result.Warnings.Count} warning(s):");
                foreach (var warning in result.Warnings)
                {
                    summary.AppendLine($" - {warning}");
                }
            }
        }

        if (exception is not null)
        {
            summary.AppendLine("PATCH RUN FAILED:");
            summary.AppendLine(exception.ToString());
        }

        var outputLocation = string.IsNullOrWhiteSpace(settings.OutputLocation)
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\AutoBlend"
            : settings.OutputLocation;
        Directory.CreateDirectory(outputLocation);
        var logPath = Path.Combine(outputLocation, "AutoBlend-log.txt");
        File.WriteAllText(logPath, summary.ToString());
        return logPath;
    }
}
