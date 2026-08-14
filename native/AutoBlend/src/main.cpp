#include "ABConfig.hpp"
#include "GUI/LauncherWindow.hpp"
#include "GUI/ProgressWindow.hpp"
#include "util/ExceptionHandler.hpp"

#include <cpptrace/from_current.hpp>
#include <wx/wx.h>

#include <array>
#include <exception>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <knownfolders.h>
#include <shlobj.h>
#include <windows.h>

using namespace std;

namespace {
// Writes to %APPDATA%\AutoBlend\native-startup.log, flushed after every line. Exists specifically
// so a silent early-startup failure (e.g. wxEntryStart failing under MO2/USVFS, where the console
// is hidden before anything gets a chance to print) still leaves a record on disk - the failure
// mode this is built for is exactly "the app launches from MO2, the USVFS lock blips for a second,
// and then nothing happens", which a hidden console alone gives zero way to diagnose after the
// fact.
void startupLog(const string& message)
{
    PWSTR rawPath = nullptr;
    filesystem::path logPath;
    if (SHGetKnownFolderPath(FOLDERID_RoamingAppData, 0, nullptr, &rawPath) == S_OK) {
        logPath = filesystem::path(rawPath) / "AutoBlend" / "native-startup.log";
    }
    if (rawPath != nullptr) {
        CoTaskMemFree(rawPath);
    }
    if (logPath.empty()) {
        return;
    }

    error_code ec;
    filesystem::create_directories(logPath.parent_path(), ec);

    ofstream file(logPath, ios::app);
    if (file.is_open()) {
        file << message << "\n";
    }
}

auto getExecutablePath() -> filesystem::path
{
    array<wchar_t, MAX_PATH> buffer {};
    if (GetModuleFileNameW(nullptr, buffer.data(), MAX_PATH) == 0) {
        startupLog("getExecutablePath: GetModuleFileNameW failed, error " + to_string(GetLastError()));
        cerr << "Error getting executable path: " << GetLastError() << "\n";
        exit(1);
    }

    filesystem::path outPath = filesystem::path(buffer.data());
    if (filesystem::exists(outPath)) {
        return outPath;
    }

    startupLog("getExecutablePath: resolved path does not exist");
    cerr << "Error getting executable path: path does not exist\n";
    exit(1);
    return {};
}

void configureDotnetLibDirectory(const filesystem::path& exeDir)
{
    // Removes the current working directory from the default LoadLibrary search order (Microsoft's
    // documented DLL-planting mitigation) - called unconditionally, before the AutoBlend_dotnetlib
    // existence check below, so a corrupted/incomplete install doesn't leave the process running
    // its whole lifetime with an unprotected search order that includes an MO2-controlled CWD.
    if (SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS | LOAD_LIBRARY_SEARCH_USER_DIRS) == 0) {
        startupLog("configureDotnetLibDirectory: SetDefaultDllDirectories failed, error " + to_string(GetLastError()));
        cerr << "Failed to configure DLL search directories.\n";
        exit(1);
    }

    // Deliberately named AutoBlend_dotnetlib rather than "dotnetlib" - AutoSeasons ships its own
    // AutoSeasons_dotnetlib/ full of similar self-contained .NET runtime files, so a generic name
    // would collide almost entirely when both tools are installed as MO2 mods in the same modlist.
    const auto libDir = exeDir / "AutoBlend_dotnetlib";
    if (!filesystem::exists(libDir)) {
        startupLog("configureDotnetLibDirectory: " + libDir.string() + " does not exist, skipping");
        return;
    }

    if (AddDllDirectory(libDir.c_str()) == nullptr) {
        startupLog("configureDotnetLibDirectory: AddDllDirectory failed, error " + to_string(GetLastError()));
        cerr << "Failed to add dotnetlib directory to DLL search path.\n";
        exit(1);
    }

    startupLog("configureDotnetLibDirectory: configured " + libDir.string());
}

// A C++ exception thrown from inside a wx event handler (button click, list-ctrl edit, timer
// tick) is intercepted by wx's own event-dispatch machinery before it would ever reach the
// CPPTRACE_TRY in main() below - the plain wxApp used previously left that path unlogged. No
// handler currently throws, but this costs nothing and means a future one that does still leaves
// a record in native-startup.log instead of vanishing into wx's own (version-dependent) default
// handling.
class AutoBlendApp : public wxApp {
public:
    auto OnExceptionInMainLoop() -> bool override
    {
        try {
            throw;
        } catch (const exception& e) {
            startupLog(string("OnExceptionInMainLoop: ") + e.what());
        } catch (...) {
            startupLog("OnExceptionInMainLoop: exception of unknown type");
        }
        throw; // preserve wx's own default handling (typically terminates) once logged
    }
};

auto runGUI(const filesystem::path& exePath) -> int
{
    startupLog("runGUI: entered");

    wxApp::SetInstance(new AutoBlendApp()); // NOLINT(cppcoreguidelines-owning-memory)
    if (!wxEntryStart(nullptr, nullptr)) {
        startupLog("runGUI: wxEntryStart failed");
        cerr << "Failed to initialize wxWidgets.\n";
        return 1;
    }
    startupLog("runGUI: wxEntryStart succeeded");

    // Only hide the console once wx is confirmed alive - if wxEntryStart or anything below it
    // fails, a console window (and this function's own cerr output) staying visible is one more
    // way to notice, on top of the startup log above.
    if (HWND consoleWindow = GetConsoleWindow(); consoleWindow != nullptr) {
        ShowWindow(consoleWindow, SW_HIDE);
    }

    // AutoBlend always runs dark - no theme choice to apply before window creation (unlike
    // AutoSeasons, which offers System/Light/Dark).
    wxTheApp->SetAppearance(wxApp::Appearance::Dark);
    wxTheApp->MSWEnableDarkMode(wxApp::DarkMode_Always);

    auto params = ABConfig::load();
    startupLog("runGUI: settings loaded");
    if (params.outputLocation.empty()) {
        params.outputLocation = (exePath / "AutoBlend_Output").wstring();
    }

    auto* launcher = new LauncherWindow(params, exePath); // NOLINT(cppcoreguidelines-owning-memory)
    startupLog("runGUI: LauncherWindow constructed, showing modal");
    const int launcherResult = launcher->ShowModal();
    if (launcherResult == wxID_OK) {
        launcher->getParams(params);
    }
    launcher->Destroy();

    if (launcherResult != wxID_OK) {
        wxEntryCleanup();
        return 0;
    }

    ABConfig::save(params);

    auto* progress = new ProgressWindow(params, exePath); // NOLINT(cppcoreguidelines-owning-memory)
    progress->ShowModal();
    progress->Destroy();

    wxEntryCleanup();
    return 0;
}
}

auto main(int /*argC*/, char** /*argV*/) -> int
{
    SetConsoleOutputCP(CP_UTF8);
    ExceptionHandler::setMainThread();

    startupLog("=== AutoBlend starting ===");

    const auto exePath = getExecutablePath().parent_path();
    startupLog("main: exePath = " + exePath.string());
    configureDotnetLibDirectory(exePath);

    int returnCode = 0;
    CPPTRACE_TRY { returnCode = runGUI(exePath); }
    CPPTRACE_CATCH(const exception& e)
    {
        startupLog(string("main: caught exception: ") + e.what());
        ExceptionHandler::setException(e, cpptrace::from_current_exception().to_string());
    }

    if (ExceptionHandler::hasException()) {
        ExceptionHandler::throwExceptionOnMainThread();
        returnCode = 1;
    }

    startupLog("main: exiting with code " + to_string(returnCode));

    return returnCode;
}
