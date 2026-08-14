#include "GUI/ProgressWindow.hpp"

#include "ABLocale.hpp"
#include "util/StringUtil.hpp"

#include <AutoBlend.NativeExportNE.h>
#include <nlohmann/json.hpp>

#include <chrono>

using namespace std;

namespace {
// nlohmann::json::value(key, default) only substitutes the default for a MISSING key - a key
// that's present but JSON null (as System.Text.Json writes a null `string?` C# property, e.g.
// ProgressSnapshot.ErrorMessage before a run has failed) still throws type_error.302 when asked
// to convert to string. This covers both cases.
auto readOptionalString(const nlohmann::json& json, const char* key) -> string
{
    if (!json.contains(key) || json[key].is_null()) {
        return {};
    }
    return json[key].get<string>();
}

constexpr int BORDER_SIZE = 5;
constexpr int GAUGE_WIDTH = 320;
constexpr int GAUGE_HEIGHT = 18;
constexpr int PULSE_INTERVAL_MS = 100;
constexpr int POLL_INTERVAL_MS = 150;
constexpr int WRAP_WIDTH = 340;

// Matches LauncherWindow's own accent colors, so the two windows read as one identity.
const wxColour ACCENT(56, 142, 60);
const wxColour ACCENT_TEXT(255, 255, 255);

// AutoBlend.NativeExportNE.h declares every [UnmanagedCallersOnly] export as taking/returning
// intptr_t (Exports.cs never applies a [DNNE.C99Type] override, so DNNE falls back to IntPtr's
// natural C mapping) - these two helpers are the only place that needs to know that.
auto toIntPtr(const string& utf8) -> intptr_t { return reinterpret_cast<intptr_t>(utf8.c_str()); }
auto fromIntPtr(intptr_t ptr) -> string
{
    return ptr == 0 ? string {} : string(reinterpret_cast<const char*>(ptr));
}
}

ProgressWindow::ProgressWindow(const ABParams& params, const filesystem::path& /*exePath*/)
    : wxDialog(nullptr, wxID_ANY, "AutoBlend", wxDefaultPosition, wxSize(420, 170), wxDEFAULT_DIALOG_STYLE | wxRESIZE_BORDER)
    , m_pulseTimer(this)
{
    const wxIcon appIcon(wxICON(IDI_ICON1));
    SetIcon(appIcon);

    auto* mainSizer = new wxBoxSizer(wxVERTICAL);

    m_statusText = new wxStaticText(this, wxID_ANY, ABTr("progress.starting", "Starting..."));
    m_statusText->Wrap(WRAP_WIDTH);
    mainSizer->Add(m_statusText, 0, wxEXPAND | wxALL, BORDER_SIZE * 2);

    m_progressGauge = new wxGauge(
        this, wxID_ANY, 100, wxDefaultPosition, wxSize(GAUGE_WIDTH, GAUGE_HEIGHT), wxGA_SMOOTH);
    mainSizer->Add(m_progressGauge, 0, wxEXPAND | wxLEFT | wxRIGHT | wxBOTTOM, BORDER_SIZE * 2);

    m_detailsPane = new wxCollapsiblePane(this, wxID_ANY, ABTr("progress.showDetails", "Show details"));
    auto* paneWindow = m_detailsPane->GetPane();
    auto* paneSizer = new wxBoxSizer(wxVERTICAL);

    m_logCtrl = new wxTextCtrl(paneWindow, wxID_ANY, "", wxDefaultPosition, wxSize(-1, 260),
        wxTE_MULTILINE | wxTE_READONLY | wxTE_RICH2);
    wxFont fixedFont(10, wxFONTFAMILY_TELETYPE, wxFONTSTYLE_NORMAL, wxFONTWEIGHT_NORMAL);
    m_logCtrl->SetFont(fixedFont);
    m_logCtrl->SetBackgroundColour(wxColour(24, 24, 24));
    m_logCtrl->SetForegroundColour(wxColour(220, 220, 220));
    m_logCtrl->SetDefaultStyle(wxTextAttr(wxColour(220, 220, 220), wxColour(24, 24, 24)));
    paneSizer->Add(m_logCtrl, 1, wxEXPAND);
    paneWindow->SetSizer(paneSizer);

    mainSizer->Add(m_detailsPane, 0, wxEXPAND | wxLEFT | wxRIGHT | wxBOTTOM, BORDER_SIZE * 2);
    m_detailsPane->Bind(wxEVT_COLLAPSIBLEPANE_CHANGED, &ProgressWindow::onDetailsPaneChanged, this);

    m_closeButton = new wxButton(this, wxID_ANY, ABTr("progress.working", "Working..."));
    m_closeButton->SetBackgroundColour(ACCENT);
    m_closeButton->SetForegroundColour(ACCENT_TEXT);
    m_closeButton->Enable(false);
    m_closeButton->Bind(wxEVT_BUTTON, &ProgressWindow::onCloseButtonPressed, this);

    auto* buttonSizer = new wxBoxSizer(wxHORIZONTAL);
    buttonSizer->AddStretchSpacer();
    buttonSizer->Add(m_closeButton, 0, wxALL, BORDER_SIZE);
    mainSizer->Add(buttonSizer, 0, wxEXPAND);

    SetSizerAndFit(mainSizer);
    Centre();

    // Closing the window (X button) while the run is still in flight would tear this object down
    // out from under the poll thread - refuse it until the run has finished.
    Bind(wxEVT_CLOSE_WINDOW, [this](wxCloseEvent& event) -> void {
        if (!m_closeButton->IsEnabled()) {
            event.Veto();
            return;
        }
        EndModal(wxID_OK);
    });

    Bind(wxEVT_TIMER, [this](wxTimerEvent&) -> void { m_progressGauge->Pulse(); });
    m_pulseTimer.Start(PULSE_INTERVAL_MS);

    const auto settingsJson = ABConfig::toJson(params).dump();
    start_patch_run(toIntPtr(settingsJson));

    m_pollThread = thread([this]() -> void {
        string lastStatus;
        bool success = true;
        wxString failureDetail;

        while (!m_stopRequested.load()) {
            const intptr_t rawPtr = get_progress();
            const auto json = fromIntPtr(rawPtr);
            if (rawPtr != 0) {
                free_string(rawPtr);
            }

            try {
                const auto snapshot = nlohmann::json::parse(json);
                const auto status = snapshot.value("Status", string {});
                const auto current = snapshot.value("Current", 0);
                const auto total = snapshot.value("Total", 0);
                const auto isIndeterminate = snapshot.value("IsIndeterminate", true);
                const auto isDone = snapshot.value("IsDone", false);
                const auto isFailed = snapshot.value("IsFailed", false);
                const auto errorMessage = readOptionalString(snapshot, "ErrorMessage");

                if (status != lastStatus && !status.empty()) {
                    lastStatus = status;
                    const auto wxStatus = wxString::FromUTF8(status);
                    wxTheApp->CallAfter([this, wxStatus]() -> void { appendLog(wxStatus + "\n"); });
                }

                wxTheApp->CallAfter([this, wxStatus = wxString::FromUTF8(status), current, total, isIndeterminate]() -> void {
                    applySnapshot(wxStatus, current, total, isIndeterminate);
                });

                if (isDone) {
                    success = !isFailed;
                    failureDetail = wxString::FromUTF8(errorMessage);
                    break;
                }
            } catch (const exception& e) {
                // A transient parse failure (e.g. the very first poll racing StartPatchRun's own
                // state initialization) isn't fatal - just try again next tick.
                wxTheApp->CallAfter([this, message = string(e.what())]() -> void {
                    appendLog("\n[WARN] Failed to parse progress snapshot: " + wxString::FromUTF8(message) + "\n");
                });
            }

            this_thread::sleep_for(chrono::milliseconds(POLL_INTERVAL_MS));
        }

        wxTheApp->CallAfter([this, success, failureDetail]() -> void { onWorkerFinished(success, failureDetail); });
    });
}

ProgressWindow::~ProgressWindow()
{
    m_stopRequested.store(true);
    if (m_pollThread.joinable()) {
        m_pollThread.join();
    }
}

void ProgressWindow::appendLog(const wxString& text) { m_logCtrl->AppendText(text); }

void ProgressWindow::applySnapshot(const wxString& status, int current, int total, bool isIndeterminate)
{
    if (!status.IsEmpty()) {
        m_statusText->SetLabel(status);
        m_statusText->Wrap(WRAP_WIDTH);
        // Wrap() can change how many lines the label needs - without re-running the sizer, the
        // gauge below keeps its old position and visually overlaps the now-taller text.
        Layout();
        GetSizer()->Fit(this);
    }

    if (isIndeterminate || total <= 0) {
        if (!m_pulseTimer.IsRunning()) {
            m_pulseTimer.Start(PULSE_INTERVAL_MS);
        }
        return;
    }

    m_pulseTimer.Stop();
    const int pct = clamp(static_cast<int>((static_cast<double>(current) / total) * 100.0), 0, 100);
    m_progressGauge->SetValue(pct);
}

void ProgressWindow::onWorkerFinished(bool success, const wxString& failureDetail)
{
    m_pulseTimer.Stop();
    m_progressGauge->SetValue(100);
    m_statusText->SetLabel(success ? ABTr("progress.done", "Done.") : ABTr("progress.failed", "Failed - see details below."));
    m_statusText->Wrap(WRAP_WIDTH);
    m_closeButton->SetLabel(success ? ABTr("progress.doneClose", "Done - Close") : ABTr("progress.failedClose", "Failed - Close"));
    m_closeButton->Enable(true);

    if (!success) {
        if (!failureDetail.IsEmpty()) {
            appendLog("\n[ERROR] " + failureDetail + "\n");
        }
        if (!m_detailsPane->IsExpanded()) {
            m_detailsPane->Expand();
        }
        Layout();
        GetSizer()->Fit(this);
        return;
    }

    wxMessageBox(
        ABTr("progress.completion.message",
            "Generation complete. Don't forget to enable AutoBlend's output plugin in your mod manager's "
            "plugin list if it isn't already active."),
        ABTr("progress.completion.title", "AutoBlend"), wxOK | wxICON_INFORMATION, this);
    // Dismissing this message box means the same thing as clicking "Done - Close" - just close.
    EndModal(wxID_OK);
}

void ProgressWindow::onCloseButtonPressed([[maybe_unused]] wxCommandEvent& event) { EndModal(wxID_OK); }

void ProgressWindow::onDetailsPaneChanged([[maybe_unused]] wxCollapsiblePaneEvent& event)
{
    Layout();
    GetSizer()->Fit(this);
}
