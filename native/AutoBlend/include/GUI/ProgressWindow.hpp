#pragma once

#include "ABConfig.hpp"

#include <wx/collpane.h>
#include <wx/gauge.h>
#include <wx/wx.h>

#include <atomic>
#include <filesystem>
#include <thread>

/**
 * @brief Modal dialog that runs the AutoBlend patch pipeline on a background thread. The pipeline
 * itself lives entirely in AutoBlend.Core (C#) behind the DNNE-exported start_patch_run/
 * get_progress calls - this window's worker thread just calls start_patch_run once and polls
 * get_progress every ~150ms, translating the returned JSON snapshot into the same compact
 * status/gauge/collapsible-log UX AutoSeasons' own ProgressWindow uses. The Close button stays
 * disabled until the run finishes (success or failure), so the window never disappears out from
 * under the user mid-run.
 */
class ProgressWindow : public wxDialog {
public:
    ProgressWindow(const ABParams& params, const std::filesystem::path& exePath);
    ~ProgressWindow() override;

    ProgressWindow(const ProgressWindow&) = delete;
    auto operator=(const ProgressWindow&) -> ProgressWindow& = delete;
    ProgressWindow(ProgressWindow&&) = delete;
    auto operator=(ProgressWindow&&) -> ProgressWindow& = delete;

private:
    wxStaticText* m_statusText;
    wxGauge* m_progressGauge;
    wxCollapsiblePane* m_detailsPane;
    wxTextCtrl* m_logCtrl;
    wxButton* m_closeButton;
    wxTimer m_pulseTimer;
    std::thread m_pollThread;
    std::atomic<bool> m_stopRequested { false };

    // Called from the poll thread via wxTheApp->CallAfter; applies one status snapshot to the UI.
    void applySnapshot(const wxString& status, int current, int total, bool isIndeterminate);
    void appendLog(const wxString& text);
    void onWorkerFinished(bool success, const wxString& failureDetail);
    void onCloseButtonPressed(wxCommandEvent& event);
    void onDetailsPaneChanged(wxCollapsiblePaneEvent& event);
};
