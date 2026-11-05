using NAPS2.EtoForms.Notifications;
using NAPS2.Scan;

namespace NAPS2.ExternalApplications;

/// <summary>
/// Passes documents from the image list to external programs.
/// </summary>
public class ExternalApplicationController(
    UiImageList imageList,
    Naps2Config config,
    ScanningContext scanningContext,
    IExternalDocumentExporter exporter,
    IExternalProcessStarter processStarter,
    ErrorOutput errorOutput,
    NotificationManager notificationManager)
{
    public IEnumerable<ExternalApplicationDefinition> Applications =>
        OperatingSystem.IsWindows() ? config.Get(c => c.ExternalApplications).Where(app => app.Enabled) : [];

    /// <summary>
    /// The application used when clicking the toolbar button directly, or null if the user has to choose one.
    /// </summary>
    public ExternalApplicationDefinition? DefaultApplication
    {
        get
        {
            var id = config.Get(c => c.DefaultExternalApplicationId);
            return Applications.FirstOrDefault(app => app.Id == id);
        }
    }

    public bool HasPagesFor(ExternalApplicationDefinition app) => GetImages(app).Any();

    /// <summary>
    /// Exports the document and starts the program with it. Returns true if the program was started, which doesn't mean
    /// that it has successfully processed the document.
    /// </summary>
    public async Task<bool> SendTo(ExternalApplicationDefinition app)
    {
        var error = GetError(app);
        if (error != null)
        {
            errorOutput.DisplayError(error);
            return false;
        }

        // Snapshot the images so that changes to the image list during the export can't affect it.
        // Images are in list order regardless of the order they were selected in.
        var pages = GetImages(app).ToList();
        using var images = pages.Select(x => x.GetClonedImage()).ToDisposableList();

        ExternalExportResult? export;
        try
        {
            export = await exporter.ExportAsync(new ExternalExportRequest
            {
                Images = images,
                IncludeOcr = app.IncludeOcr
            });
        }
        catch (Exception ex)
        {
            Log.ErrorException($"Error exporting for external application {app.Id}", ex);
            errorOutput.DisplayError(MiscResources.ErrorSaving, ex);
            return false;
        }
        if (export == null)
        {
            return false;
        }

        try
        {
            var arguments = ExternalArgumentResolver.Resolve(app.Arguments, export);
            await Task.Run(() => processStarter.Start(app.ExecutablePath, arguments));
        }
        catch (Exception ex)
        {
            Log.ErrorException($"Error starting external application {app.Id}", ex);
            exporter.DeleteExport(export);
            errorOutput.DisplayError(string.Format(UiStrings.ErrorStartingApplication, app.Name), ex);
            return false;
        }

        if (app.ClearAfterPassing)
        {
            // Only the pages that were passed, not any that were added while exporting
            imageList.Mutate(new ImageListMutation.DeleteSelected(), ListSelection.From(pages));
        }
        notificationManager.Show(new SaveNotification(
            string.Format(UiStrings.DocumentPassedToApplication, app.Name), export.FilePaths[0]));
        return true;
    }

    private string? GetError(ExternalApplicationDefinition app)
    {
        if (!HasPagesFor(app))
        {
            return string.Format(UiStrings.ExternalAppNoPages, app.Name);
        }
        // Without this check the PDF would silently be exported without OCR.
        if (app.IncludeOcr && (config.DefaultOcrParams().LanguageCode == null || scanningContext.OcrEngine == null))
        {
            return string.Format(UiStrings.ExternalAppOcrNotAvailable, app.Name);
        }
        return ExternalApplicationValidator.GetError(app);
    }

    private IEnumerable<UiImage> GetImages(ExternalApplicationDefinition app)
    {
        return app.PageSelection == ExternalPageSelection.Selected
            ? imageList.Images.Where(imageList.Selection.Contains)
            : imageList.Images;
    }
}
