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
    : wxDialog(nullptr, wxID_ANY, "AutoBlend", wxDefaultPosition, wxSize(600, 900), wxDEFAULT_DIALOG_STYLE | wxRESIZE_BORDER)
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

    // Config profile - a single install (e.g. shared outside any one modlist) can still keep
    // distinct settings per use case by saving/loading separate JSON files here, instead of
    // relying on %APPDATA%\AutoBlend\settings.json (which only isolates settings when each modlist
    // gets its own copy of the exe). Mirrors PGPatcher's/AutoSeasons' own Load/Save Config pattern.
    bodySizer->Add(makeSectionLabel(body, ABTr("launcher.configProfile.label", "Config Profile")), 0,
        wxLEFT | wxRIGHT | wxTOP, BORDER_SIZE);

    auto* loadConfigButton = new wxButton(body, wxID_ANY, ABTr("launcher.configProfile.load", "Load Config..."));
    loadConfigButton->Bind(wxEVT_BUTTON, &LauncherWindow::onLoadConfig, this);
    auto* saveConfigButton = new wxButton(body, wxID_ANY, ABTr("launcher.configProfile.saveAs", "Save Config As..."));
    saveConfigButton->Bind(wxEVT_BUTTON, &LauncherWindow::onSaveConfigAs, this);

    auto* configProfileSizer = new wxBoxSizer(wxHORIZONTAL);
    configProfileSizer->Add(loadConfigButton, 0, wxALL, BORDER_SIZE);
    configProfileSizer->Add(saveConfigButton, 0, wxALL, BORDER_SIZE);
    bodySizer->Add(configProfileSizer, 0);

    // Language selector - changing it immediately relaunches the window (see onLanguageChanged)
    // rather than requiring a separate settings dialog/OK click, since this launcher is small
    // enough that a full rebuild is cheap and this keeps the UX to a single click.
    bodySizer->Add(makeSectionLabel(body, ABTr("launcher.language.label", "Language")), 0,
        wxLEFT | wxRIGHT | wxTOP, BORDER_SIZE);

    m_languages = ABLocale::getAvailableLanguages();
    wxArrayString languageChoices;
    int selectedLanguageIndex = 0;
    for (size_t i = 0; i < m_languages.size(); i++) {
        languageChoices.Add(m_languages.at(i).displayName);
        if (m_languages.at(i).code == ABLocale::getCurrentLanguage()) {
            selectedLanguageIndex = static_cast<int>(i);
        }
    }
    m_languageChoice = new wxChoice(body, wxID_ANY, wxDefaultPosition, wxDefaultSize, languageChoices);
    if (!m_languages.empty()) {
        m_languageChoice->SetSelection(selectedLanguageIndex);
    }
    m_languageChoice->Bind(wxEVT_CHOICE, &LauncherWindow::onLanguageChanged, this);
    bodySizer->Add(m_languageChoice, 0, wxEXPAND | wxALL, BORDER_SIZE);

    // Theme selector - same immediate-relaunch pattern as the language selector, since applying a
    // wxWidgets appearance change requires it to be set before the window it affects is created.
    bodySizer->Add(makeSectionLabel(body, ABTr("launcher.theme.label", "Theme")), 0,
        wxLEFT | wxRIGHT | wxTOP, BORDER_SIZE);

    wxArrayString themeChoices;
    themeChoices.Add(ABTr("launcher.theme.system", "System"));
    themeChoices.Add(ABTr("launcher.theme.light", "Light"));
    themeChoices.Add(ABTr("launcher.theme.dark", "Dark"));
    m_themeChoice = new wxChoice(body, wxID_ANY, wxDefaultPosition, wxDefaultSize, themeChoices);
    int selectedThemeIndex = 0;
    if (initParams.uiTheme == "light") {
        selectedThemeIndex = 1;
    } else if (initParams.uiTheme == "dark") {
        selectedThemeIndex = 2;
    }
    m_themeChoice->SetSelection(selectedThemeIndex);
    m_themeChoice->Bind(wxEVT_CHOICE, &LauncherWindow::onThemeChanged, this);
    bodySizer->Add(m_themeChoice, 0, wxEXPAND | wxALL, BORDER_SIZE);

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

    // Auto-generate allowlist - which source diffuse textures are allowed to get a synthesized
    // statics sibling (see AutoBlend.Core.Scanning.MissingTextureGenerator). Every landscape texture
    // with an alpha-blended shape is structurally eligible, but generation only actually runs for
    // entries listed here, so this is an opt-in table rather than an exclusion list like the two
    // above it. Collapsed by default (same wxCollapsiblePane pattern as ProgressWindow's own
    // "Show details") - this table is long (dozens of rows) and rarely needs editing, so hiding it
    // behind a click keeps the rest of the launcher compact.
    m_autoGenerateAllowlistPane = new wxCollapsiblePane(body, wxID_ANY, ABTr("launcher.autoGenerateAllowlist.label", "Auto-Generate Allowlist"));
    auto* autoGenerateAllowlistPaneWindow = m_autoGenerateAllowlistPane->GetPane();
    auto* autoGenerateAllowlistPaneSizer = new wxBoxSizer(wxVERTICAL);

    auto* autoGenerateAllowlistHelpText = new wxStaticText(autoGenerateAllowlistPaneWindow, wxID_ANY,
        ABTr("launcher.autoGenerateAllowlist.help",
            "Only these source diffuse textures (relative to Data, e.g. \"textures\\landscape\\dirt01.dds\") "
            "get a missing statics sibling synthesized automatically. Wildcards (*) allowed. Right click to "
            "add/remove rows."));
    autoGenerateAllowlistHelpText->Wrap(560);
    autoGenerateAllowlistPaneSizer->Add(autoGenerateAllowlistHelpText, 0, wxEXPAND | wxBOTTOM, BORDER_SIZE);

    m_autoGenerateAllowlistCtrl = new PGModifiableListCtrl(
        autoGenerateAllowlistPaneWindow, wxID_ANY, wxDefaultPosition, wxSize(-1, 260), wxLC_REPORT | wxLC_EDIT_LABELS | wxLC_NO_HEADER);
    m_autoGenerateAllowlistCtrl->AppendColumn(
        ABTr("launcher.autoGenerateAllowlist.column", "Texture"), wxLIST_FORMAT_LEFT, wxLIST_AUTOSIZE_USEHEADER);
    m_autoGenerateAllowlistCtrl->SetColumnWidth(0, wxLIST_AUTOSIZE_USEHEADER);

    long autoGenerateAllowlistIndex = 0;
    for (const auto& entry : initParams.autoGenerateAllowlist) {
        m_autoGenerateAllowlistCtrl->InsertItem(autoGenerateAllowlistIndex++, wxString(entry));
    }
    m_autoGenerateAllowlistCtrl->InsertItem(m_autoGenerateAllowlistCtrl->GetItemCount(), "");

    autoGenerateAllowlistPaneSizer->Add(m_autoGenerateAllowlistCtrl, 1, wxEXPAND);
    autoGenerateAllowlistPaneWindow->SetSizer(autoGenerateAllowlistPaneSizer);

    bodySizer->Add(m_autoGenerateAllowlistPane, 0, wxEXPAND | wxALL, BORDER_SIZE);
    m_autoGenerateAllowlistPane->Bind(wxEVT_COLLAPSIBLEPANE_CHANGED, &LauncherWindow::onAutoGenerateAllowlistPaneChanged, this);

    // PBR slots - when the winning source for an auto-generated statics texture is itself from a
    // PBR pack, carry its Height/RMAOS slots into the derived TextureSet too, not just Diffuse/
    // Normal. Off by default to keep the original vanilla-friendly behavior.
    m_generatePbrSlotsCheckbox = new wxCheckBox(body, wxID_ANY, ABTr("launcher.generatePbrSlots.label", "Generate PBR slots"));
    m_generatePbrSlotsCheckbox->SetValue(initParams.generatePbrSlots);
    wxFont pbrCheckboxFont = m_generatePbrSlotsCheckbox->GetFont();
    pbrCheckboxFont.SetPointSize(pbrCheckboxFont.GetPointSize() + 2);
    m_generatePbrSlotsCheckbox->SetFont(pbrCheckboxFont);
    bodySizer->Add(m_generatePbrSlotsCheckbox, 0, wxALL, BORDER_SIZE);

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
    m_autoGenerateAllowlistCtrl->Bind(pgEVT_LISTCTRL_CHANGED, [this](PGCustomListctrlChangedEvent& event) -> void {
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
    outParams.uiLanguage = ABLocale::getCurrentLanguage();
    switch (m_themeChoice->GetSelection()) {
    case 1:
        outParams.uiTheme = "light";
        break;
    case 2:
        outParams.uiTheme = "dark";
        break;
    default:
        outParams.uiTheme = "system";
        break;
    }

    outParams.gameLocation = m_gameLocationTextbox->GetValue().ToStdWstring();
    // Skyrim LE is not offered in the UI - the backend (AutoBlend.Core, via Mutagen) still
    // supports it, but nothing in this shell currently exposes a way to pick it.
    outParams.gameType = ABGameType::SKYRIM_SE;
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

    outParams.autoGenerateAllowlist.clear();
    item = -1;
    while ((item = m_autoGenerateAllowlistCtrl->GetNextItem(item)) != -1) {
        const wxString text = m_autoGenerateAllowlistCtrl->GetItemText(item);
        if (!text.IsEmpty()) {
            outParams.autoGenerateAllowlist.push_back(text.ToStdWstring());
        }
    }

    outParams.generatePbrSlots = m_generatePbrSlotsCheckbox->GetValue();
}

void LauncherWindow::onLanguageChanged([[maybe_unused]] wxCommandEvent& event)
{
    const int selection = m_languageChoice->GetSelection();
    if (selection == wxNOT_FOUND) {
        return;
    }

    const auto& selectedLang = m_languages.at(static_cast<size_t>(selection));
    if (selectedLang.code == ABLocale::getCurrentLanguage()) {
        return;
    }

    ABLocale::init(m_exePath / "AutoBlend_translations", selectedLang.code);
    EndModal(RESULT_RELAUNCH);
}

void LauncherWindow::onThemeChanged([[maybe_unused]] wxCommandEvent& event)
{
    // Unlike a language change, this needs a full process restart, not just an internal rebuild -
    // wx's MSW dark mode support turned out to be a one-way switch once enabled for a process, so
    // an in-process relaunch can't reliably get back to a lighter appearance after a darker one.
    // See main.cpp's handling of RESULT_RESTART.
    EndModal(RESULT_RESTART);
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

void LauncherWindow::onLoadConfig([[maybe_unused]] wxCommandEvent& event)
{
    wxFileDialog dialog(this, ABTr("launcher.configProfile.loadDialogTitle", "Load Config"), wxEmptyString, wxEmptyString,
        ABTr("launcher.configProfile.fileFilter", "JSON files (*.json)|*.json|All files (*.*)|*.*"),
        wxFD_OPEN | wxFD_FILE_MUST_EXIST);
    if (dialog.ShowModal() != wxID_OK) {
        return;
    }

    applyLoadedParams(ABConfig::loadFrom(filesystem::path(dialog.GetPath().ToStdWstring())));
}

void LauncherWindow::onSaveConfigAs([[maybe_unused]] wxCommandEvent& event)
{
    wxFileDialog dialog(this, ABTr("launcher.configProfile.saveDialogTitle", "Save Config As"), wxEmptyString,
        "AutoBlend_config.json", ABTr("launcher.configProfile.fileFilter", "JSON files (*.json)|*.json|All files (*.*)|*.*"),
        wxFD_SAVE | wxFD_OVERWRITE_PROMPT);
    if (dialog.ShowModal() != wxID_OK) {
        return;
    }

    ABParams current;
    getParams(current);
    ABConfig::saveTo(filesystem::path(dialog.GetPath().ToStdWstring()), current);
}

void LauncherWindow::applyLoadedParams(const ABParams& params)
{
    // uiLanguage/uiTheme are deliberately left untouched - those are this user's own app-wide
    // preference for how the tool looks, not part of a per-job profile (which game/output/mod
    // manager to target), so loading a profile shouldn't change how the window you're looking at
    // right now is themed or translated.
    m_gameLocationTextbox->SetValue(params.gameLocation);
    m_outputLocationTextbox->SetValue(params.outputLocation);
    m_modManagerChoice->SetSelection(params.modManager == ABModManagerType::MOD_ORGANIZER_2 ? 1 : 0);
    m_mo2InstancePathTextbox->SetValue(params.mo2InstancePath);
    updateMo2FieldState();
    m_textureSetNamingTemplate = params.textureSetNamingTemplate;

    m_meshBlacklistCtrl->DeleteAllItems();
    long meshBlacklistIndex = 0;
    for (const auto& rule : params.meshBlacklist) {
        m_meshBlacklistCtrl->InsertItem(meshBlacklistIndex++, wxString(rule));
    }
    m_meshBlacklistCtrl->InsertItem(m_meshBlacklistCtrl->GetItemCount(), "");

    m_editorIdKeywordsCtrl->DeleteAllItems();
    long editorIdKeywordIndex = 0;
    for (const auto& keyword : params.editorIdBlacklistKeywords) {
        m_editorIdKeywordsCtrl->InsertItem(editorIdKeywordIndex++, wxString(keyword));
    }
    m_editorIdKeywordsCtrl->InsertItem(m_editorIdKeywordsCtrl->GetItemCount(), "");

    m_autoGenerateAllowlistCtrl->DeleteAllItems();
    long autoGenerateAllowlistIndex = 0;
    for (const auto& entry : params.autoGenerateAllowlist) {
        m_autoGenerateAllowlistCtrl->InsertItem(autoGenerateAllowlistIndex++, wxString(entry));
    }
    m_autoGenerateAllowlistCtrl->InsertItem(m_autoGenerateAllowlistCtrl->GetItemCount(), "");

    m_generatePbrSlotsCheckbox->SetValue(params.generatePbrSlots);

    updateListColumnWidths();
}

void LauncherWindow::updateListColumnWidths()
{
    if (m_meshBlacklistCtrl != nullptr && m_meshBlacklistCtrl->GetColumnCount() > 0) {
        m_meshBlacklistCtrl->SetColumnWidth(0, m_meshBlacklistCtrl->GetClientSize().GetWidth());
    }
    if (m_editorIdKeywordsCtrl != nullptr && m_editorIdKeywordsCtrl->GetColumnCount() > 0) {
        m_editorIdKeywordsCtrl->SetColumnWidth(0, m_editorIdKeywordsCtrl->GetClientSize().GetWidth());
    }
    if (m_autoGenerateAllowlistCtrl != nullptr && m_autoGenerateAllowlistCtrl->GetColumnCount() > 0) {
        m_autoGenerateAllowlistCtrl->SetColumnWidth(0, m_autoGenerateAllowlistCtrl->GetClientSize().GetWidth());
    }
}

void LauncherWindow::onAutoGenerateAllowlistPaneChanged([[maybe_unused]] wxCollapsiblePaneEvent& event)
{
    updateListColumnWidths();
    Layout();
    GetSizer()->Fit(this);
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
