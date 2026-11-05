using System.Collections.Immutable;
using Eto.Forms;
using NAPS2.EtoForms.Desktop;
using NAPS2.EtoForms.Layout;
using NAPS2.ExternalApplications;

namespace NAPS2.EtoForms.Ui;

internal class ExternalApplicationsPanel : SettingsPanel
{
    private const int BUTTON_WIDTH = 160;

    private readonly DesktopFormProvider _desktopFormProvider;
    private readonly ListBox _list = new();
    private readonly Button _add;
    private readonly Button _edit;
    private readonly Button _delete;
    private readonly Button _toggleDefault;
    private readonly Button _moveUp;
    private readonly Button _moveDown;
    private readonly CheckBox _rememberSettings = new() { Text = UiStrings.RememberTheseSettings };
    private List<ExternalApplicationDefinition> _apps = [];
    private Guid? _defaultId;

    public ExternalApplicationsPanel(IFormBase form, DesktopFormProvider desktopFormProvider) : base(form)
    {
        _desktopFormProvider = desktopFormProvider;
        _add = C.Button(UiStrings.ExternalAppAdd, Add);
        _edit = C.Button(UiStrings.ExternalAppEdit, Edit);
        _delete = C.Button(UiStrings.Delete, Delete);
        _toggleDefault = C.Button(UiStrings.ExternalAppSetDefault, ToggleDefault);
        _moveUp = C.Button(UiStrings.MoveUp, () => Move(-1));
        _moveDown = C.Button(UiStrings.MoveDown, () => Move(1));
        _list.SelectedIndexChanged += (_, _) => UpdateEnabled();
        _list.Activated += (_, _) => Edit();
        LoadValues(Config);
    }

    public override string Title => UiStrings.ExternalApps;

    public override string IconName => "application_cascade";

    private bool IsLocked => Config.AppLocked.Has(c => c.ExternalApplications) ||
                             Config.AppLocked.Has(c => c.DefaultExternalApplicationId);

    public override LayoutElement BuildScrollableLayout()
    {
        return L.Column(
            L.GroupBox(
                UiStrings.ExternalApps,
                L.Row(
                    _list.Height(200).Scale(),
                    L.Column(
                        _add.Width(BUTTON_WIDTH),
                        _edit.Width(BUTTON_WIDTH),
                        _delete.Width(BUTTON_WIDTH),
                        C.Spacer(),
                        _toggleDefault.Width(BUTTON_WIDTH),
                        C.Spacer(),
                        _moveUp.Width(BUTTON_WIDTH),
                        _moveDown.Width(BUTTON_WIDTH)
                    )
                )
            )
        );
    }

    public override LayoutElement BuildFixedLayout()
    {
        return _rememberSettings;
    }

    private void LoadValues(Naps2Config config)
    {
        _rememberSettings.Checked = config.Get(c => c.RememberExternalApplications);
        _apps = config.Get(c => c.ExternalApplications).ToList();
        _defaultId = config.Get(c => c.DefaultExternalApplicationId);
        UpdateList(-1);
    }

    private string GetListText(ExternalApplicationDefinition app)
    {
        var text = app.Enabled ? app.Name : string.Format(UiStrings.ExternalAppDisabledFormat, app.Name);
        return app.Id == _defaultId ? string.Format(UiStrings.ExternalAppDefaultFormat, text) : text;
    }

    private void UpdateList(int selectedIndex)
    {
        _list.Items.Clear();
        foreach (var app in _apps)
        {
            _list.Items.Add(GetListText(app));
        }
        _list.SelectedIndex = selectedIndex;
        UpdateEnabled();
    }

    private void UpdateEnabled()
    {
        var hasSelection = _list.SelectedIndex >= 0 && !IsLocked;
        _list.Enabled = !IsLocked;
        _add.Enabled = !IsLocked;
        _rememberSettings.Enabled = !IsLocked;
        _edit.Enabled = _delete.Enabled = _toggleDefault.Enabled = hasSelection;
        _toggleDefault.Text = hasSelection && _apps[_list.SelectedIndex].Id == _defaultId
            ? UiStrings.ExternalAppClearDefault
            : UiStrings.ExternalAppSetDefault;
        _moveUp.Enabled = hasSelection && _list.SelectedIndex > 0;
        _moveDown.Enabled = hasSelection && _list.SelectedIndex < _apps.Count - 1;
    }

    private void Add()
    {
        var form = Form.FormFactory.Create<ExternalApplicationForm>();
        form.ShowModal();
        if (form.Result)
        {
            _apps.Add(form.Application);
            // The first application is what the button should use, until the user chooses otherwise
            _defaultId ??= form.Application.Id;
            UpdateList(_apps.Count - 1);
        }
    }

    private void Edit()
    {
        int index = _list.SelectedIndex;
        if (index < 0 || IsLocked) return;
        var form = Form.FormFactory.Create<ExternalApplicationForm>();
        form.Application = _apps[index];
        form.ShowModal();
        if (form.Result)
        {
            _apps[index] = form.Application;
            UpdateList(index);
        }
    }

    private void Delete()
    {
        int index = _list.SelectedIndex;
        if (index < 0) return;
        if (MessageBox.Show(string.Format(MiscResources.ConfirmDelete, _apps[index].Name), MiscResources.Delete,
                MessageBoxButtons.OKCancel, MessageBoxType.Question, MessageBoxDefaultButton.OK) == DialogResult.Ok)
        {
            if (_apps[index].Id == _defaultId)
            {
                _defaultId = null;
            }
            _apps.RemoveAt(index);
            UpdateList(Math.Min(index, _apps.Count - 1));
        }
    }

    private void ToggleDefault()
    {
        int index = _list.SelectedIndex;
        if (index < 0) return;
        var id = _apps[index].Id;
        _defaultId = _defaultId == id ? null : id;
        UpdateList(index);
    }

    private void Move(int offset)
    {
        int index = _list.SelectedIndex;
        int newIndex = index + offset;
        if (index < 0 || newIndex < 0 || newIndex >= _apps.Count) return;
        (_apps[index], _apps[newIndex]) = (_apps[newIndex], _apps[index]);
        UpdateList(newIndex);
    }

    public override void Save()
    {
        if (IsLocked) return;
        bool remember = _rememberSettings.IsChecked();
        bool changed = _defaultId != Config.Get(c => c.DefaultExternalApplicationId) ||
                       !_apps.SequenceEqual(Config.Get(c => c.ExternalApplications));
        if (!changed && remember == Config.Get(c => c.RememberExternalApplications))
        {
            // Avoid unnecessarily copying values from other scopes (e.g. defaults)
            return;
        }

        // Same approach as SettingsPanelWithRememberCheckbox, which only supports a single setting
        var runTransact = Config.Run.BeginTransaction();
        var userTransact = Config.User.BeginTransaction();
        var transactToWrite = remember ? userTransact : runTransact;

        runTransact.Remove(c => c.ExternalApplications);
        runTransact.Remove(c => c.DefaultExternalApplicationId);
        userTransact.Remove(c => c.ExternalApplications);
        userTransact.Remove(c => c.DefaultExternalApplicationId);
        transactToWrite.Set(c => c.ExternalApplications, _apps.ToImmutableList());
        transactToWrite.Set(c => c.DefaultExternalApplicationId, _defaultId);
        userTransact.Set(c => c.RememberExternalApplications, remember);

        runTransact.Commit();
        userTransact.Commit();
        if (changed)
        {
            _desktopFormProvider.DesktopForm.ApplyToolbarSettings();
        }
    }

    public override void RestoreDefaults()
    {
        LoadValues(Config.DefaultsOnly);
    }
}
