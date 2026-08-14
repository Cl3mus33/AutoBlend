#include "GUI/LauncherWindow.hpp"

#include "ABLocale.hpp"
#include "GUI/components/PGCustomListctrlChangedEvent.hpp"
#include "util/StringUtil.hpp"

#include <wx/statline.h>

using namespace std;

namespace {
constexpr int BORDER_SIZE = 5;

// Matches AutoBlend's WPF app accent colors, inherited from AutoSeasons' own identity - keeping
// both shells on the same palette is the whole point of this rewrite.
const wxColour ACCENT_DARK(27, 94, 32); // header banner background
const wxColour ACCENT(56, 142, 60); // primary button, section label text
const wxColour ACCENT_TEXT(255, 255, 255); // text on top of ACCENT_DARK/ACCENT

auto makeSectionLabel(wxWindow* parent, const wxString& text) -> wxStaticText*
{
    auto* label = new wxStaticText(parent, wxID_ANY, text);
    wxFont font = label->GetFont();
    font.SetWeight(wxFONTWEIGHT_BOLD);
    label->SetFont(font);
    label->SetForegroundColour(ACCENT);
    return label;
}
}

LauncherWindow::LauncherWindow(const ABParams& initParams, filesystem::path exePath)
    : wxDialog(nullptr, wxID_ANY, "AutoBlend", wxDefaultPosition, wxSize(600, 760), wxDEFAULT_DIALOG_STYLE | wxRESIZE_BORDER)
    , m_exePath(std::move(exePath))
    , m_textureSetNamingTemplate(initParams.textureSetNamingTemplate)
{
    const wxIcon appIcon(wxICON(IDI_ICON1));
    SetIcon(appIcon);

    auto* mainSizer = new wxBoxSizer(wxVERTICAL);

    // Header banner: same treatment as the settings window body below, gives the dialog an
    // identity of its own at a glance.
    auto* headerPanel = new wxPanel(this);
    headerPanel->SetBackgroundColour(ACCENT_DARK);
    auto* headerSizer = new wxBoxSizer(wxHORIZONTAL);
    auto* headerIcon = new wxStaticBitmap(headerPanel, wxID_ANY, wxBitmap(appIcon).ConvertToImage().Scale(32, 32, wxIMAGE_QUALITY_HIGH));
    headerSizer->Add(headerIcon, 0, wxALL | wxALIGN_CENTER_VERTICAL, BORDER_SIZE * 2);
    auto* headerTitle = new wxStaticText(headerPanel, wxID_ANY, "AutoBlend");
    wxFont headerFont = headerTitle->GetFont();
    headerFont.SetPointSize(headerFont.GetPointSize() + 4);
    headerFont.SetWeight(wxFONTWEIGHT_BOLD);
    headerTitle->SetFont(headerFont);
    headerTitle->SetForegroundColour(ACCENT_TEXT);
    headerSizer->Add(headerTitle, 0, wxALIGN_CENTER_VERTICAL);
    headerPanel->SetSizerAndFit(headerSizer);
    mainSizer->Add(headerPanel, 0, wxEXPAND);

    auto* body = new wxPanel(this);
    auto* bodySizer = new wxBoxSizer(wxVERTICAL);

    auto* introText = new wxStaticText(body, wxID_ANY,
        ABTr("launcher.intro",
            "Scans your load order for landscape texture variants (statics/blending subfolders "
            "under */landscape/) and patches matching meshes to alpha-blend instead of alpha-test, "
            "generating a dedicated output plugin with the derived texture sets."));
    introText->Wrap(530);
    bodySizer->Add(introText, 0, wxALL, BORDER_SIZE * 2);

    // Game location
    bodySizer->Add(makeSectionLabel(body, ABTr("launcher.gameLocation.label", "Game Location")), 0,
        wxLEFT | wxRIGHT | wxTOP, BORDER_SIZE);

    m_gameLocationTextbox = new wxTextCtrl(body, wxID_ANY, initParams.gameLocation);
    auto* gameBrowseButton = new wxButton(body, wxID_ANY, ABTr("common.browse", "Browse"));
    gameBrowseButton->Bind(wxEVT_BUTTON, &LauncherWindow::onBrowseGameLocation, this);

    auto* gameLocationSizer = new wxBoxSizer(wxHORIZONTAL);
    gameLocationSizer->Add(m_gameLocationTextbox, 1, wxEXPAND | wxALL, BORDER_SIZE);
    gameLocationSizer->Add(gameBrowseButton, 0, wxALL, BORDER_SIZE);
    bodySizer->Add(gameLocationSizer, 0, wxEXPAND);

    // Game type
    bodySizer->Add(makeSectionLabel(body, ABTr("launcher.gameType.label", "Game Type")), 0,
        wxLEFT | wxRIGHT | wxTOP, BORDER_SIZE);

    wxArrayString gameTypeChoices;
    gameTypeChoices.Add("Skyrim Special Edition");
    gameTypeChoices.Add("Skyrim Legendary Edition");
    m_gameTypeChoice = new wxChoice(body, wxID_ANY, wxDefaultPosition, wxDefaultSize, gameTypeChoices);
    m_gameTypeChoice->SetSelection(initParams.gameType == ABGameType::SKYRIM_LE ? 1 : 0);
    bodySizer->Add(m_gameTypeChoice, 0, wxEXPAND | wxALL, BORDER_SIZE);

    // Output location
    bodySizer->Add(makeSectionLabel(body, ABTr("launcher.outputLocation.label", "Output Location")), 0,
        wxLEFT | wxRIGHT | wxTOP, BORDER_SIZE);

    m_outputLocationTextbox = new wxTextCtrl(body, wxID_ANY, initParams.outputLocation);
    auto* outputBrowseButton = new wxButton(body, wxID_ANY, ABTr("common.browse", "Browse"));
    outputBrowseButton->Bind(wxEVT_BUTTON, &LauncherWindow::onBrowseOutputLocation, this);

    auto* outputLocationSizer = new wxBoxSizer(wxHORIZONTAL);
    outputLocationSizer->Add(m_outputLocationTextbox, 1, wxEXPAND | wxALL, BORDER_SIZE);
    outputLocationSizer->Add(outputBrowseButton, 0, wxALL, BORDER_SIZE);
    bodySizer->Add(outputLocationSizer, 0, wxEXPAND);

    // Mod manager
    bodySizer->Add(makeSectionLabel(body, ABTr("launcher.modManager.label", "Mod Manager")), 0,
        wxLEFT | wxRIGHT | wxTOP, BORDER_SIZE);

    wxArrayString modManagerChoices;
    modManagerChoices.Add(ABTr("launcher.modManager.none", "None / Vortex"));
    modManagerChoices.Add(ABTr("launcher.modManager.mo2", "Mod Organizer 2"));
    m_modManagerChoice = new wxChoice(body, wxID_ANY, wxDefaultPosition, wxDefaultSize, modManagerChoices);
    m_modManagerChoice->SetSelection(initParams.modManager == ABModManagerType::MOD_ORGANIZER_2 ? 1 : 0);
    m_modManagerChoice->Bind(wxEVT_CHOICE, &LauncherWindow::onModManagerChanged, this);
    bodySizer->Add(m_modManagerChoice, 0, wxEXPAND | wxALL, BORDER_SIZE);

    // MO2 instance path - always present (rather than appearing/disappearing with the mod manager
    // choice above), just enabled/disabled: an earlier revision toggled its visibility and users
    // didn't notice the window needed to grow to show it. Always reserving the space avoids that.
    m_mo2InstancePathLabel = makeSectionLabel(body, ABTr("launcher.mo2InstancePath.label", "MO2 Instance Path"));
    bodySizer->Add(m_mo2InstancePathLabel, 0, wxLEFT | wxRIGHT | wxTOP, BORDER_SIZE);

    m_mo2InstancePathTextbox = new wxTextCtrl(body, wxID_ANY, initParams.mo2InstancePath);
    m_mo2InstanceBrowseButton = new wxButton(body, wxID_ANY, ABTr("common.browse", "Browse"));
    m_mo2InstanceBrowseButton->Bind(wxEVT_BUTTON, &LauncherWindow::onBrowseMo2Instance, this);

    auto* mo2InstanceSizer = new wxBoxSizer(wxHORIZONTAL);
    mo2InstanceSizer->Add(m_mo2InstancePathTextbox, 1, wxEXPAND | wxALL, BORDER_SIZE);
    mo2InstanceSizer->Add(m_mo2InstanceBrowseButton, 0, wxALL, BORDER_SIZE);
    bodySizer->Add(mo2InstanceSizer, 0, wxEXPAND);

    // Mesh blacklist - inline editable table, same pattern as AutoSeasons' own blocklist.
    bodySizer->Add(makeSectionLabel(body, ABTr("launcher.meshBlacklist.label", "Mesh Blacklist")), 0,
        wxLEFT | wxRIGHT | wxTOP, BORDER_SIZE);

    auto* meshBlacklistHelpText = new wxStaticText(body, wxID_ANY,
        ABTr("launcher.meshBlacklist.help",
            "Meshes matching a rule here are never patched. Wildcards (*) allowed, e.g. \"*\\glass\\*\". "
            "Right click to add/remove rows."));
    meshBlacklistHelpText->Wrap(560);
    bodySizer->Add(meshBlacklistHelpText, 0, wxLEFT | wxRIGHT | wxTOP, BORDER_SIZE);

    m_meshBlacklistCtrl = new PGModifiableListCtrl(
        body, wxID_ANY, wxDefaultPosition, wxDefaultSize, wxLC_REPORT | wxLC_EDIT_LABELS | wxLC_NO_HEADER);
    m_meshBlacklistCtrl->AppendColumn(ABTr("launcher.meshBlacklist.column", "Rule"), wxLIST_FORMAT_LEFT, wxLIST_AUTOSIZE_USEHEADER);
    m_meshBlacklistCtrl->SetColumnWidth(0, wxLIST_AUTOSIZE_USEHEADER);

    long meshBlacklistIndex = 0;
    for (const auto& rule : initParams.meshBlacklist) {
        m_meshBlacklistCtrl->InsertItem(meshBlacklistIndex++, wxString(rule));
    }
    m_meshBlacklistCtrl->InsertItem(m_meshBlacklistCtrl->GetItemCount(), "");

    bodySizer->Add(m_meshBlacklistCtrl, 1, wxEXPAND | wxALL, BORDER_SIZE);

    // Season-locked... no - EditorID blacklist keywords, same inline editable table pattern.
    bodySizer->Add(makeSectionLabel(body, ABTr("launcher.editorIdKeywords.label", "EditorID Blacklist Keywords")), 0,
        wxLEFT | wxRIGHT | wxTOP, BORDER_SIZE);

    auto* editorIdKeywordsHelpText = new wxStaticText(body, wxID_ANY,
        ABTr("launcher.editorIdKeywords.help",
            "Records whose EditorID contains one of these words (case-insensitive) are skipped entirely - "
            "useful where alpha testing is intentional (e.g. \"ice\", \"glass\"). Right click to add/remove rows."));
    editorIdKeywordsHelpText->Wrap(560);
    bodySizer->Add(editorIdKeywordsHelpText, 0, wxLEFT | wxRIGHT | wxTOP, BORDER_SIZE);

    m_editorIdKeywordsCtrl = new PGModifiableListCtrl(
        body, wxID_ANY, wxDefaultPosition, wxDefaultSize, wxLC_REPORT | wxLC_EDIT_LABELS | wxLC_NO_HEADER);
    m_editorIdKeywordsCtrl->AppendColumn(
        ABTr("launcher.editorIdKeywords.column", "Keyword"), wxLIST_FORMAT_LEFT, wxLIST_AUTOSIZE_USEHEADER);
    m_editorIdKeywordsCtrl->SetColumnWidth(0, wxLIST_AUTOSIZE_USEHEADER);

    long editorIdKeywordIndex = 0;
    for (const auto& keyword : initParams.editorIdBlacklistKeywords) {
        m_editorIdKeywordsCtrl->InsertItem(editorIdKeywordIndex++, wxString(keyword));
    }
    m_editorIdKeywordsCtrl->InsertItem(m_editorIdKeywordsCtrl->GetItemCount(), "");

    bodySizer->Add(m_editorIdKeywordsCtrl, 1, wxEXPAND | wxALL, BORDER_SIZE);

    body->SetSizer(bodySizer);
    mainSizer->Add(body, 1, wxEXPAND | wxALL, BORDER_SIZE);

    Bind(wxEVT_SIZE, [this](wxSizeEvent& event) -> void {
        updateListColumnWidths();
        event.Skip();
    });
    m_meshBlacklistCtrl->Bind(pgEVT_LISTCTRL_CHANGED, [this](PGCustomListctrlChangedEvent& event) -> void {
        updateListColumnWidths();
        event.Skip();
    });
    m_editorIdKeywordsCtrl->Bind(pgEVT_LISTCTRL_CHANGED, [this](PGCustomListctrlChangedEvent& event) -> void {
        updateListColumnWidths();
        event.Skip();
    });

    mainSizer->Add(new wxStaticLine(this, wxID_ANY), 0, wxEXPAND | wxALL, BORDER_SIZE);

    // Buttons
    auto* buttonSizer = new wxBoxSizer(wxHORIZONTAL);
    auto* cancelButton = new wxButton(this, wxID_CANCEL, ABTr("common.cancel", "Cancel"));
    m_okButton = new wxButton(this, wxID_ANY, ABTr("launcher.startButton", "Start Patching"));
    m_okButton->SetBackgroundColour(ACCENT);
    m_okButton->SetForegroundColour(ACCENT_TEXT);
    m_okButton->Bind(wxEVT_BUTTON, &LauncherWindow::onOkButtonPressed, this);
    buttonSizer->AddStretchSpacer();
    buttonSizer->Add(cancelButton, 0, wxALL, BORDER_SIZE);
    buttonSizer->Add(m_okButton, 0, wxALL, BORDER_SIZE);
    mainSizer->Add(buttonSizer, 0, wxEXPAND);

    SetSizerAndFit(mainSizer);
    updateMo2FieldState();
}

void LauncherWindow::getParams(ABParams& outParams) const
{
    outParams.gameLocation = m_gameLocationTextbox->GetValue().ToStdWstring();
    outParams.gameType = m_gameTypeChoice->GetSelection() == 1 ? ABGameType::SKYRIM_LE : ABGameType::SKYRIM_SE;
    outParams.outputLocation = m_outputLocationTextbox->GetValue().ToStdWstring();
    outParams.modManager
        = m_modManagerChoice->GetSelection() == 1 ? ABModManagerType::MOD_ORGANIZER_2 : ABModManagerType::NONE;
    outParams.mo2InstancePath = m_mo2InstancePathTextbox->GetValue().ToStdWstring();
    // mo2ProfileName is intentionally not editable here - it's auto-detected from the instance's
    // ModOrganizer.ini (selected_profile) when a patch run starts, not something users need to see.
    // textureSetNamingTemplate isn't UI-editable either - carried through unchanged from whatever
    // was loaded from settings.json (see the class doc comment in LauncherWindow.hpp).
    outParams.textureSetNamingTemplate = m_textureSetNamingTemplate;

    outParams.meshBlacklist.clear();
    long item = -1;
    while ((item = m_meshBlacklistCtrl->GetNextItem(item)) != -1) {
        const wxString text = m_meshBlacklistCtrl->GetItemText(item);
        if (!text.IsEmpty()) {
            outParams.meshBlacklist.push_back(text.ToStdWstring());
        }
    }

    outParams.editorIdBlacklistKeywords.clear();
    item = -1;
    while ((item = m_editorIdKeywordsCtrl->GetNextItem(item)) != -1) {
        const wxString text = m_editorIdKeywordsCtrl->GetItemText(item);
        if (!text.IsEmpty()) {
            outParams.editorIdBlacklistKeywords.push_back(text.ToStdWstring());
        }
    }
}

void LauncherWindow::onBrowseGameLocation([[maybe_unused]] wxCommandEvent& event)
{
    wxDirDialog dialog(this, ABTr("launcher.gameLocation.dialogTitle", "Select Game Location"), m_gameLocationTextbox->GetValue());
    if (dialog.ShowModal() == wxID_OK) {
        m_gameLocationTextbox->SetValue(dialog.GetPath());
    }
}

void LauncherWindow::onBrowseOutputLocation([[maybe_unused]] wxCommandEvent& event)
{
    wxDirDialog dialog(
        this, ABTr("launcher.outputLocation.dialogTitle", "Select Output Location"), m_outputLocationTextbox->GetValue());
    if (dialog.ShowModal() == wxID_OK) {
        m_outputLocationTextbox->SetValue(dialog.GetPath());
    }
}

void LauncherWindow::onBrowseMo2Instance([[maybe_unused]] wxCommandEvent& event)
{
    wxDirDialog dialog(
        this, ABTr("launcher.mo2InstancePath.dialogTitle", "Select MO2 Instance Folder"), m_mo2InstancePathTextbox->GetValue());
    if (dialog.ShowModal() == wxID_OK) {
        m_mo2InstancePathTextbox->SetValue(dialog.GetPath());
    }
}

void LauncherWindow::onModManagerChanged([[maybe_unused]] wxCommandEvent& event) { updateMo2FieldState(); }

void LauncherWindow::updateMo2FieldState()
{
    const bool isMo2 = m_modManagerChoice->GetSelection() == 1;
    m_mo2InstancePathLabel->Enable(isMo2);
    m_mo2InstancePathTextbox->Enable(isMo2);
    m_mo2InstanceBrowseButton->Enable(isMo2);
}

void LauncherWindow::updateListColumnWidths()
{
    if (m_meshBlacklistCtrl != nullptr && m_meshBlacklistCtrl->GetColumnCount() > 0) {
        m_meshBlacklistCtrl->SetColumnWidth(0, m_meshBlacklistCtrl->GetClientSize().GetWidth());
    }
    if (m_editorIdKeywordsCtrl != nullptr && m_editorIdKeywordsCtrl->GetColumnCount() > 0) {
        m_editorIdKeywordsCtrl->SetColumnWidth(0, m_editorIdKeywordsCtrl->GetClientSize().GetWidth());
    }
}

void LauncherWindow::onOkButtonPressed([[maybe_unused]] wxCommandEvent& event)
{
    if (m_gameLocationTextbox->GetValue().IsEmpty()) {
        wxMessageBox(ABTr("launcher.missingGameLocation.message", "Please select your game's install location."),
            ABTr("launcher.missingGameLocation.title", "Missing Game Location"), wxOK | wxICON_WARNING, this);
        return;
    }

    if (m_outputLocationTextbox->GetValue().IsEmpty()) {
        wxMessageBox(ABTr("launcher.missingOutputLocation.message", "Please select an output location."),
            ABTr("launcher.missingOutputLocation.title", "Missing Output Location"), wxOK | wxICON_WARNING, this);
        return;
    }

    if (m_modManagerChoice->GetSelection() == 1 && m_mo2InstancePathTextbox->GetValue().IsEmpty()) {
        wxMessageBox(ABTr("launcher.missingMo2Instance.message", "Please select your Mod Organizer 2 instance folder."),
            ABTr("launcher.missingMo2Instance.title", "Missing MO2 Instance"), wxOK | wxICON_WARNING, this);
        return;
    }

    EndModal(wxID_OK);
}
