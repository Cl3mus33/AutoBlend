#pragma once

#include "ABConfig.hpp"
#include "GUI/components/PGModifiableListCtrl.hpp"

#include <wx/wx.h>

#include <filesystem>

/**
 * @brief Main settings dialog for AutoBlend: game location/type, output location, mod manager
 * (None/Vortex vs Mod Organizer 2), and the mesh/EditorID blacklist tables. Shown modally; on OK,
 * getParams() returns the edited settings.
 *
 * The texture set naming template is deliberately NOT exposed here - it's a power-user knob
 * (advanced token syntax, easy to break by hand-typing) that's only meant to be changed by editing
 * settings.json directly; this window just carries whatever value was loaded straight through to
 * getParams() unchanged.
 */
class LauncherWindow : public wxDialog {
public:
    LauncherWindow(const ABParams& initParams, std::filesystem::path exePath);

    void getParams(ABParams& outParams) const;

private:
    std::filesystem::path m_exePath;

    wxTextCtrl* m_gameLocationTextbox;
    wxChoice* m_gameTypeChoice;
    wxTextCtrl* m_outputLocationTextbox;
    wxChoice* m_modManagerChoice;
    wxStaticText* m_mo2InstancePathLabel;
    wxTextCtrl* m_mo2InstancePathTextbox;
    wxButton* m_mo2InstanceBrowseButton;
    std::wstring m_textureSetNamingTemplate;
    PGModifiableListCtrl* m_meshBlacklistCtrl;
    PGModifiableListCtrl* m_editorIdKeywordsCtrl;
    wxButton* m_okButton;

    void onBrowseGameLocation(wxCommandEvent& event);
    void onBrowseOutputLocation(wxCommandEvent& event);
    void onBrowseMo2Instance(wxCommandEvent& event);
    void onModManagerChanged(wxCommandEvent& event);
    void onOkButtonPressed(wxCommandEvent& event);
    void updateListColumnWidths();
    void updateMo2FieldState();
};
