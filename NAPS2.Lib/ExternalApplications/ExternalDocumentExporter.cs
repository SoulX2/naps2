using NAPS2.ImportExport;
using NAPS2.Ocr;
using NAPS2.Pdf;

namespace NAPS2.ExternalApplications;

public interface IExternalDocumentExporter
{
    /// <summary>
    /// Exports the images to a new file. Returns null if the export failed (in which case the error has already been
    /// reported to the user) or was cancelled.
    /// </summary>
    Task<ExternalExportResult?> ExportAsync(ExternalExportRequest request);

    void DeleteExport(ExternalExportResult export);
}

public sealed class ExternalExportRequest
{
    public required IList<ProcessedImage> Images { get; init; }

    public required bool IncludeOcr { get; init; }
}

public sealed class ExternalExportResult
{
    public required Guid ExportId { get; init; }

    public required IReadOnlyList<string> FilePaths { get; init; }

    public required int PageCount { get; init; }

    public required string Format { get; init; }
}

public class ExternalDocumentExporter(
    IOperationFactory operationFactory,
    OperationProgress operationProgress,
    Naps2Config config) : IExternalDocumentExporter
{
    private const string FOLDER_NAME = "ExternalExports";

    /// <summary>
    /// The folder containing a subfolder per export. This is deliberately not in Paths.Temp, as that's cleared on startup
    /// and the receiving program might still be using the file.
    /// </summary>
    public string RootPath { get; init; } = Path.Combine(Paths.AppData, FOLDER_NAME);

    public async Task<ExternalExportResult?> ExportAsync(ExternalExportRequest request)
    {
        var exportId = Guid.NewGuid();
        var filePath = Path.Combine(RootPath, exportId.ToString("N"), "document.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        // The user's settings for saved PDFs must not stop the receiving program from reading the document, so it's always
        // a single unencrypted file.
        var pdfSettings = config.Get(c => c.PdfSettings) with
        {
            SinglePagePdfs = false,
            Encryption = new PdfEncryption()
        };
        var ocrParams = request.IncludeOcr ? config.DefaultOcrParams() : OcrParams.Empty;

        var op = operationFactory.Create<SavePdfOperation>();
        if (op.Start(filePath, request.Images, pdfSettings, ocrParams,
                saveParams: new SaveParams { OverwriteMode = OverwriteMode.Confirmed }))
        {
            operationProgress.ShowProgress(op);
        }
        var result = new ExternalExportResult
        {
            ExportId = exportId,
            FilePaths = [filePath],
            PageCount = request.Images.Count,
            Format = "pdf"
        };
        if (await op.Success && File.Exists(filePath))
        {
            return result;
        }
        DeleteExport(result);
        return null;
    }

    public void DeleteExport(ExternalExportResult export)
    {
        try
        {
            // Only ever delete the export's own subfolder
            var folder = Path.Combine(RootPath, export.ExportId.ToString("N"));
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.ErrorException($"Error deleting external export {export.ExportId}", ex);
        }
    }
}
