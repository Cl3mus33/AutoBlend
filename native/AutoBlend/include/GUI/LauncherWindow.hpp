#pragma once

#include "ABConfig.hpp"
#include "ABLocale.hpp"
#include "GUI/components/PGModifiableListCtrl.hpp"

#include <wx/wx.h>

#include <filesystem>
#include <vector>

/**
 * @brief Main settings dialog for AutoBlend: game location/type, output location, mod manager
 * (None/Vortex vs Mod Organizer 2), the mesh/EditorID blacklist tables, and app-wide language/
 * theme preferences. Shown modally; on OK, getParams() returns the edited settings.
 *
 * The texture set naming template is deliberately NOT exposed here - it's a power-user knob
 * (advanced token syntax, easy to break by hand-typing) that's only meant to be changed by editing
 * settings.json directly; this window just carries whatever value was loaded straight through to
 * getParams() unchanged.
 */
class LauncherWindow : public wxDialog {
public:
    /** ShowModal result indicating the launcher should be rebuilt in-process (e.g. after a
     * language change) - see main.cpp's handling loop. */
    constexpr static int RESULT_RELAUNCH = wxID_HIGHEST + 1;
    /** ShowModal result indicating the whole process should be restarted (after a theme change -
     * wx's MSW dark mode support can't be reliably re-toggled within one already-running process,
     * see onThemeChanged()). */
    constexpr static int RESULT_RESTART = wxID_HIGHEST + 2;

    LauncherWindow(const ABParams& initParams, std::filesystem::path exePath);

    void getParams(ABParams& outParams) const;

private:
    std::filesystem::path m_exePath;
    std::vector<ABLocale::Language> m_languages;

    wxChoice* m_languageChoice;
    wxChoice* m_themeChoice;
    wxTextCtrl* m_gameLocationTextbox;
    wxTextCtrl* m_outputLocationTextbox;
    wxChoice* m_modManagerChoice;
    wxStaticText* m_mo2InstancePathLabel;
    wxTextCtrl* m_mo2InstancePathTextbox;
    wxButton* m_mo2InstanceBrowseButton;
    wxStaticText* m_mo2ProfileLabel;
    wxChoice* m_mo2ProfileChoice;
    std::wstring m_textureSetNamingTemplate;
    PGModifiableListCtrl* m_meshBlacklistCtrl;
    PGModifiableListCtrl* m_editorIdKeywordsCtrl;
    wxDialog* m_autoGenerateAllowlistDialog;
    PGModifiableListCtrl* m_autoGenerateAllowlistCtrl;
    wxCheckBox* m_generatePbrSlotsCheckbox;
    wxButton* m_okButton;

    void onLanguageChanged(wxCommandEvent& event);
    void onThemeChanged(wxCommandEvent& event);
    void onBrowseGameLocation(wxCommandEvent& event);
    void onBrowseOutputLocation(wxCommandEvent& event);
    void onBrowseMo2Instance(wxCommandEvent& event);
    void onModManagerChanged(wxCommandEvent& event);
    void onLoadConfig(wxCommandEvent& event);
    void onSaveConfigAs(wxCommandEvent& event);
    void onOkButtonPressed(wxCommandEvent& event);
    void onEditAllowlistButtonPressed(wxCommandEvent& event);
    void commitPendingListEdits();
    void updateListColumnWidths();
    void updateMo2FieldState();
    void refreshMo2Profiles(const std::wstring& preferredProfile = L"");
    void applyLoadedParams(const ABParams& params);
};
