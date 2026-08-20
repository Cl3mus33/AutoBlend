using System.Runtime.InteropServices;
using System.Text.Json;
using AutoBlend.Core.Configuration;
using AutoBlend.Core.Pipeline;
using AutoBlend.Core.Scanning;

namespace AutoBlend.NativeExport;

/// <summary>
/// C-callable entry points DNNE wraps into AutoBlend.NativeExportNE.{dll,h,lib} so the native
/// wxWidgets shell (AutoBlend/native) can drive the existing Mutagen/niflysharp patch logic
/// without ever running as a .NET apphost itself — the actual patch run happens here, in-process,
/// on a background Task; the native side polls <see cref="GetProgress"/> instead of receiving a
/// callback, since a callback crossing the native boundary while a background .NET Task is
/// mid-flight is far more fragile than polling a tiny status blob every ~150ms.
///
/// Settings load/save is deliberately NOT exposed here — the native shell's ABConfig reads/writes
/// %APPDATA%\AutoBlend\settings.json directly (mirroring AutoBlend.Core.Configuration.PatcherSettings'
/// JSON shape), the same way AutoSeasons' own ASConfig does its own file I/O rather than round-
/// tripping through its DNNE bridge just to open the settings window.
/// </summary>
public static class Exports
{
    private static volatile RunState? _currentRun;

    private sealed class RunState
    {
        public string Status = "Starting...";
        public int Current;
        public int Total;
        public bool IsIndeterminate = true;
        public bool IsDone;
        public bool IsFailed;
        public string? ResultJson;
        public string? ErrorMessage;
    }

    [UnmanagedCallersOnly(EntryPoint = "detect_mo2_profile")]
    public static IntPtr DetectMo2Profile(IntPtr instancePathPtr)
    {
        // Every [UnmanagedCallersOnly] export must never let a managed exception reach the native
        // caller — there's no P/Invoke-reverse exception handling to unwind through, so it would
        // otherwise hard-crash the whole process with no diagnostic. Marshal.PtrToStringUTF8 itself
        // doesn't throw for the inputs it's given here, but it's inside the try anyway so this
        // method's whole body follows the same "nothing here can escape" shape as the rest of the
        // file, rather than relying on that being true today staying true on the next edit.
        try
        {
            var path = Marshal.PtrToStringUTF8(instancePathPtr) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(path) && Mo2InstanceReader.TryDetectSelectedProfile(path, out var profile))
            {
                return ToNativeUtf8(profile);
            }
        }
        catch
        {
            // best-effort default; caller keeps whatever profile name it already had
        }

        return IntPtr.Zero;
    }

    [UnmanagedCallersOnly(EntryPoint = "list_mo2_profiles")]
    public static IntPtr ListMo2Profiles(IntPtr instancePathPtr)
    {
        // Same never-throw-across-the-boundary shape as DetectMo2Profile above.
        try
        {
            var path = Marshal.PtrToStringUTF8(instancePathPtr) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                return ToNativeUtf8("[]");
            }

            return ToNativeUtf8(JsonSerializer.Serialize(Mo2InstanceReader.ListProfiles(path)));
        }
        catch
        {
            return ToNativeUtf8("[]");
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "start_patch_run")]
    public static void StartPatchRun(IntPtr settingsJsonPtr)
    {
        if (_currentRun is { IsDone: false })
        {
            return; // a run is already in flight
        }

        // DeserializeSettings can throw (malformed/empty JSON from the native side) - unlike the
        // Task.Run body below, this runs synchronously on the native caller's own thread, so an
        // unhandled exception here would cross the unmanaged boundary directly and crash the
        // process instead of being observable via get_progress like every other failure mode.
        PatcherSettings settings;
        try
        {
            settings = DeserializeSettings(settingsJsonPtr);
        }
        catch (Exception ex)
        {
            _currentRun = new RunState
            {
                Status = "Failed to start",
                IsDone = true,
                IsFailed = true,
                ErrorMessage = ex.Message,
            };
            return;
        }

        var state = new RunState();
        _currentRun = state;

        Task.Run(() =>
        {
            try
            {
                var orchestrator = new PatchOrchestrator(settings);
                var result = orchestrator.Run(progress =>
                {
                    state.Status = progress.Message;
                    state.Current = progress.Current;
                    state.Total = progress.Total;
                    state.IsIndeterminate = progress.IsIndeterminate;
                });

                state.ResultJson = JsonSerializer.Serialize(result);
            }
            catch (Exception ex)
            {
                state.ErrorMessage = ex.Message;
                state.IsFailed = true;
            }
            finally
            {
                state.IsDone = true;
            }
        });
    }

    [UnmanagedCallersOnly(EntryPoint = "get_progress")]
    public static IntPtr GetProgress()
    {
        var state = _currentRun;
        var payload = new ProgressSnapshot(
            Status: state?.Status ?? string.Empty,
            Current: state?.Current ?? 0,
            Total: state?.Total ?? 0,
            IsIndeterminate: state?.IsIndeterminate ?? true,
            IsRunning: state is { IsDone: false },
            IsDone: state?.IsDone ?? false,
            IsFailed: state?.IsFailed ?? false,
            ResultJson: state?.ResultJson,
            ErrorMessage: state?.ErrorMessage);

        return ToNativeUtf8(JsonSerializer.Serialize(payload));
    }

    [UnmanagedCallersOnly(EntryPoint = "free_string")]
    public static void FreeString(IntPtr ptr)
    {
        if (ptr != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(ptr);
        }
    }

    private static PatcherSettings DeserializeSettings(IntPtr jsonPtr)
    {
        var json = Marshal.PtrToStringUTF8(jsonPtr) ?? "{}";
        return JsonSerializer.Deserialize<PatcherSettings>(json) ?? new PatcherSettings();
    }

    private static IntPtr ToNativeUtf8(string value) => Marshal.StringToCoTaskMemUTF8(value);

    private sealed record ProgressSnapshot(
        string Status,
        int Current,
        int Total,
        bool IsIndeterminate,
        bool IsRunning,
        bool IsDone,
        bool IsFailed,
        string? ResultJson,
        string? ErrorMessage);
}
