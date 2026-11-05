using Eto.Forms;
using NAPS2.EtoForms.Layout;
using NAPS2.EtoForms.Widgets;
using NAPS2.ExternalApplications;

namespace NAPS2.EtoForms.Ui;

/// <summary>
/// Adds or edits a single external application.
/// </summary>
internal class ExternalApplicationForm : EtoDialogBase
{
    private readonly TextBox _name = new();
    private readonly TextBox _executablePath = new();
    private readonly TextBox _arguments = new();
    private readonly EnumDropDownWidget<ExternalPageSelection> _pageSelection = new()
    {
        Format = x => x == ExternalPageSelection.Selected ? UiStrings.SelectedPages : UiStrings.AllPages
    };
    private readonly CheckBox _includeOcr = C.CheckBox(UiStrings.ExternalAppIncludeOcr);
    private readonly CheckBox _clearAfterPassing = C.CheckBox(UiStrings.ExternalAppClearAfterPassing);
    private readonly CheckBox _enabled = C.CheckBox(UiStrings.ExternalAppEnabled);

    public ExternalApplicationForm(Naps2Config config) : base(config)
    {
        Title = UiStrings.ExternalAppFormTitle;
        IconName = "application_cascade";
    }

    /// <summary>
    /// The application to edit. If it's valid when the form closes with Result = true, this is the new value.
    /// </summary>
    public ExternalApplicationDefinition Application { get; set; } = new() { Id = Guid.NewGuid() };

    public bool Result { get; private set; }

    protected override void BuildLayout()
    {
        FormStateController.DefaultExtraLayoutSize = new Eto.Drawing.SizeF(150, 0);
        FormStateController.FixedHeightLayout = true;

        _name.Text = Application.Name;
        _executablePath.Text = Application.ExecutablePath;
        _arguments.Text = Application.Arguments;
        _pageSelection.SelectedItem = Application.PageSelection;
        _includeOcr.Checked = Application.IncludeOcr;
        _clearAfterPassing.Checked = Application.ClearAfterPassing;
        _enabled.Checked = Application.Enabled;

        LayoutController.Content = L.Column(
            C.Label(UiStrings.ExternalAppNameLabel),
            _name.NaturalWidth(400),
            C.Label(UiStrings.ExternalAppProgramLabel),
            L.Row(
                _executablePath.Scale().AlignCenter(),
                C.Button("...", ChooseExecutable).Width(40).AlignCenter()
            ),
            C.Label(UiStrings.ExternalAppArgumentsLabel),
            _arguments,
            C.Label(UiStrings.ExternalAppPlaceholdersHint),
            C.Label(UiStrings.ExternalAppPagesLabel),
            _pageSelection.AsControl().MinWidth(150),
            _includeOcr,
            _clearAfterPassing,
            _enabled,
            C.Filler(),
            L.Row(
                C.Filler(),
                L.OkCancel(
                    C.OkButton(this, Submit),
                    C.CancelButton(this))
            )
        );
    }

    private void ChooseExecutable()
    {
        var dialog = new OpenFileDialog { CheckFileExists = true };
        dialog.Filters.Add(new FileFilter(UiStrings.ExternalAppProgramFiles, ".exe"));
        EtoPlatform.Current.ConfigureFileDialog(dialog);
        if (dialog.ShowDialog(this) == DialogResult.Ok)
        {
            _executablePath.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(_name.Text))
            {
                _name.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
            }
        }
    }

    private bool Submit()
    {
        var app = Application with
        {
            Name = _name.Text.Trim(),
            ExecutablePath = _executablePath.Text.Trim(),
            Arguments = _arguments.Text.Trim(),
            PageSelection = _pageSelection.SelectedItem,
            IncludeOcr = _includeOcr.IsChecked(),
            ClearAfterPassing = _clearAfterPassing.IsChecked(),
            Enabled = _enabled.IsChecked()
        };
        var error = ExternalApplicationValidator.GetError(app);
        if (error != null)
        {
            MessageBox.Show(this, error, UiStrings.ExternalAppFormTitle, MessageBoxButtons.OK, MessageBoxType.Warning);
            return false;
        }
        Application = app;
        Result = true;
        return true;
    }
}
