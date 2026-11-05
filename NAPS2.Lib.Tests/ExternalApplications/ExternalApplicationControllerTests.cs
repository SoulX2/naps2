using System.Collections.Concurrent;
using System.Collections.Immutable;
using NAPS2.EtoForms.Notifications;
using NAPS2.ExternalApplications;
using NAPS2.ImportExport;
using NAPS2.Pdf;
using NAPS2.Sdk.Tests;
using NAPS2.Sdk.Tests.Asserts;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace NAPS2.Lib.Tests.ExternalApplications;

public class ExternalApplicationControllerTests : ContextualTests
{
    private readonly Naps2Config _config = Naps2Config.Stub();
    private readonly UiImageList _imageList = new();
    private readonly IExternalProcessStarter _starter = Substitute.For<IExternalProcessStarter>();
    private readonly ErrorOutput _errorOutput = Substitute.For<ErrorOutput>();
    private readonly NotificationManager _notifications;
    private readonly ConcurrentBag<string> _operationErrors = [];
    private readonly ExternalDocumentExporter _exporter;
    private readonly ExternalApplicationController _controller;
    private readonly string _exportRoot;
    private readonly string _exePath;

    public ExternalApplicationControllerTests()
    {
        _exportRoot = P("exports");
        _exePath = P("receiver.exe");
        File.WriteAllText(_exePath, "");

        var operationFactory = Substitute.For<IOperationFactory>();
        operationFactory.Create<SavePdfOperation>().Returns(_ =>
        {
            var op = new SavePdfOperation(new PdfExporter(ScanningContext), Substitute.For<IOverwritePrompt>(),
                new NameManager());
            op.Error += (_, args) => _operationErrors.Add($"{args.ErrorMessage}: {args.Exception}");
            return op;
        });
        _exporter = new ExternalDocumentExporter(operationFactory, Substitute.For<OperationProgress>(), _config)
        {
            RootPath = _exportRoot
        };
        _notifications = new NotificationManager(null!, _config);
        _controller = new ExternalApplicationController(_imageList, _config, ScanningContext, _exporter, _starter,
            _errorOutput, _notifications);
    }

    private ExternalApplicationDefinition App(string? arguments = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Receiver",
        ExecutablePath = _exePath,
        Arguments = arguments ?? "--in \"{file}\" --pages {pageCount}"
    };

    private List<UiImage> AddImages(params byte[][] images)
    {
        var uiImages = CreateScannedImages(images).Select(x => new UiImage(x)).ToList();
        _imageList.Mutate(new ImageListMutation.Append(uiImages));
        return uiImages;
    }

    private string SingleExportedFile() =>
        Assert.Single(Directory.GetFiles(_exportRoot, "*.pdf", SearchOption.AllDirectories));

    private void AssertNoExports() =>
        Assert.Empty(Directory.Exists(_exportRoot)
            ? Directory.GetFileSystemEntries(_exportRoot)
            : []);

    [Fact]
    public async Task SendTo_ExportsAllPagesAndStartsProgram()
    {
        AddImages(ImageResources.dog, ImageResources.dog_gray);

        Assert.True(await _controller.SendTo(App()));

        var file = SingleExportedFile();
        PdfAsserts.AssertImages(file, ImageResources.dog, ImageResources.dog_gray);
        _starter.Received(1).Start(_exePath, Arg.Is<IReadOnlyList<string>>(args =>
            args.SequenceEqual(new[] { "--in", file, "--pages", "2" })));
        Assert.Single(_notifications.Notifications);
        _errorOutput.ReceivedCalls().ToList().ForEach(call => Assert.Fail($"Unexpected error: {call}"));
    }

    [Fact]
    public async Task SendTo_SelectedPages_UsesListOrder()
    {
        var images = AddImages(ImageResources.dog, ImageResources.dog_gray, ImageResources.dog_h_p300);
        _imageList.UpdateSelection(ListSelection.From([images[2], images[0]]));

        Assert.True(await _controller.SendTo(App() with { PageSelection = ExternalPageSelection.Selected }));

        PdfAsserts.AssertImages(SingleExportedFile(), ImageResources.dog, ImageResources.dog_h_p300);
    }

    [Fact]
    public async Task SendTo_DoesNotMarkAsSavedOrDelete()
    {
        _config.User.Set(c => c.DeleteAfterSaving, true);
        AddImages(ImageResources.dog);
        Assert.True(_imageList.HasUnsavedChanges);

        Assert.True(await _controller.SendTo(App()));

        Assert.Single(_imageList.Images);
        Assert.True(_imageList.HasUnsavedChanges);
    }

    [Fact]
    public async Task SendTo_ClearAfterPassing_RemovesAllPages()
    {
        AddImages(ImageResources.dog, ImageResources.dog_gray);

        Assert.True(await _controller.SendTo(App() with { ClearAfterPassing = true }));

        Assert.Empty(_imageList.Images);
        Assert.False(_imageList.HasUnsavedChanges);
        // The program still has its file
        PdfAsserts.AssertImages(SingleExportedFile(), ImageResources.dog, ImageResources.dog_gray);
    }

    [Fact]
    public async Task SendTo_ClearAfterPassing_RemovesOnlySelectedPages()
    {
        var images = AddImages(ImageResources.dog, ImageResources.dog_gray);
        _imageList.UpdateSelection(ListSelection.Of(images[0]));

        Assert.True(await _controller.SendTo(App() with
        {
            PageSelection = ExternalPageSelection.Selected,
            ClearAfterPassing = true
        }));

        Assert.Equal([images[1]], _imageList.Images);
        Assert.True(_imageList.HasUnsavedChanges);
    }

    [Fact]
    public async Task SendTo_ClearAfterPassing_KeepsPagesAddedDuringExport()
    {
        AddImages(ImageResources.dog);
        var exporter = Substitute.For<IExternalDocumentExporter>();
        List<UiImage>? added = null;
        exporter.ExportAsync(Arg.Any<ExternalExportRequest>()).Returns(_ =>
        {
            added = AddImages(ImageResources.dog_gray);
            return Task.FromResult<ExternalExportResult?>(new ExternalExportResult
            {
                ExportId = Guid.NewGuid(),
                FilePaths = [P("document.pdf")],
                PageCount = 1,
                Format = "pdf"
            });
        });
        var controller = new ExternalApplicationController(_imageList, _config, ScanningContext, exporter, _starter,
            _errorOutput, _notifications);

        Assert.True(await controller.SendTo(App() with { ClearAfterPassing = true }));

        Assert.Equal(added, _imageList.Images);
    }

    [Fact]
    public async Task SendTo_ClearAfterPassing_KeepsPagesIfStartFails()
    {
        AddImages(ImageResources.dog);
        _starter.WhenForAnyArgs(x => x.Start(default!, default!)).Throw(new InvalidOperationException());

        Assert.False(await _controller.SendTo(App() with { ClearAfterPassing = true }));

        Assert.Single(_imageList.Images);
    }

    [Fact]
    public async Task SendTo_ClearAfterPassing_KeepsPagesIfOptionsInvalid()
    {
        AddImages(ImageResources.dog);

        Assert.False(await _controller.SendTo(App("{nope} {file}") with { ClearAfterPassing = true }));

        Assert.Single(_imageList.Images);
    }

    [Fact]
    public async Task SendTo_IgnoresPdfSettingsThatWouldBreakTheFile()
    {
        _config.User.Set(c => c.PdfSettings.SinglePagePdfs, true);
        _config.User.Set(c => c.PdfSettings.Encryption.EncryptPdf, true);
        _config.User.Set(c => c.PdfSettings.Encryption.OwnerPassword, "secret");
        AddImages(ImageResources.dog, ImageResources.dog_gray);

        Assert.True(await _controller.SendTo(App()));

        PdfAsserts.AssertImages(SingleExportedFile(), ImageResources.dog, ImageResources.dog_gray);
    }

    [Fact]
    public async Task SendTo_ConcurrentCallsUseSeparateFiles()
    {
        // As in the application; in-memory GDI images can't be rendered concurrently
        SetUpFileStorage();
        AddImages(ImageResources.dog);

        var results = await Task.WhenAll(_controller.SendTo(App()), _controller.SendTo(App()));

        Assert.True(_operationErrors.IsEmpty, string.Join(" | ", _operationErrors));
        Assert.All(results, Assert.True);
        Assert.Equal(2, Directory.GetDirectories(_exportRoot).Length);
    }

    [Fact]
    public async Task SendTo_NoPages()
    {
        Assert.False(await _controller.SendTo(App()));

        _errorOutput.Received(1).DisplayError(Arg.Any<string>());
        _starter.DidNotReceiveWithAnyArgs().Start(default!, default!);
        AssertNoExports();
    }

    [Fact]
    public async Task SendTo_NoSelection()
    {
        AddImages(ImageResources.dog);

        Assert.False(await _controller.SendTo(App() with { PageSelection = ExternalPageSelection.Selected }));

        _errorOutput.Received(1).DisplayError(Arg.Any<string>());
        AssertNoExports();
    }

    [Fact]
    public async Task SendTo_NameRequired()
    {
        AddImages(ImageResources.dog);

        Assert.False(await _controller.SendTo(App() with { Name = "  " }));

        _errorOutput.Received(1).DisplayError(Arg.Any<string>());
        AssertNoExports();
    }

    [Fact]
    public void HasPagesFor_FollowsPageSelection()
    {
        var all = App();
        var selected = App() with { PageSelection = ExternalPageSelection.Selected };
        Assert.False(_controller.HasPagesFor(all));

        var images = AddImages(ImageResources.dog);
        _imageList.UpdateSelection(ListSelection.Empty<UiImage>());
        Assert.True(_controller.HasPagesFor(all));
        Assert.False(_controller.HasPagesFor(selected));

        _imageList.UpdateSelection(ListSelection.From(images));
        Assert.True(_controller.HasPagesFor(selected));
    }

    [Theory]
    [InlineData("relative.exe")]
    [InlineData(@"C:\does\not\exist.exe")]
    public async Task SendTo_BadExecutablePath(string path)
    {
        AddImages(ImageResources.dog);

        Assert.False(await _controller.SendTo(App() with { ExecutablePath = path }));

        _errorOutput.Received(1).DisplayError(Arg.Is<string>(s => s.Contains(path)));
        AssertNoExports();
    }

    [Theory]
    [InlineData("receiver.bat")]
    [InlineData("receiver.cmd")]
    public async Task SendTo_RejectsFilesThatRunInAShell(string name)
    {
        File.WriteAllText(P(name), "");
        AddImages(ImageResources.dog);

        Assert.False(await _controller.SendTo(App() with { ExecutablePath = P(name) }));

        _starter.DidNotReceiveWithAnyArgs().Start(default!, default!);
    }

    [Fact]
    public async Task SendTo_BadArguments_FailsBeforeExport()
    {
        AddImages(ImageResources.dog);

        Assert.False(await _controller.SendTo(App("{nope} {file}")));

        _errorOutput.Received(1).DisplayError(Arg.Is<string>(s => s.Contains("{nope}")));
        AssertNoExports();
    }

    [Fact]
    public async Task SendTo_OcrRequestedButNotEnabled()
    {
        AddImages(ImageResources.dog);

        Assert.False(await _controller.SendTo(App() with { IncludeOcr = true }));

        _errorOutput.Received(1).DisplayError(Arg.Any<string>());
        AssertNoExports();
    }

    [Fact]
    public async Task SendTo_StartFails_ReportsAndDeletesExport()
    {
        AddImages(ImageResources.dog);
        var error = new InvalidOperationException("start failed");
        _starter.WhenForAnyArgs(x => x.Start(default!, default!)).Throw(error);

        Assert.False(await _controller.SendTo(App()));

        _errorOutput.Received(1).DisplayError(Arg.Is<string>(s => s.Contains("Receiver")), error);
        Assert.Empty(_notifications.Notifications);
        AssertNoExports();
    }

    [Fact]
    public void Config_RoundTripsThroughUserConfigFile()
    {
        var app = App() with
        {
            Name = "Übergabe & Import",
            Arguments = "--in \"{file}\" --x <y>",
            PageSelection = ExternalPageSelection.Selected,
            IncludeOcr = true,
            ClearAfterPassing = true,
            Enabled = false
        };
        var path = P("config.xml");
        var config1 = new Naps2Config(P("appsettings.xml"), path);
        config1.User.Set(c => c.ExternalApplications, ImmutableList.Create(app));

        var config2 = new Naps2Config(P("appsettings.xml"), path);

        Assert.Equal(app, Assert.Single(config2.Get(c => c.ExternalApplications)));
    }

    [Fact]
    public void Config_MissingElementsUseDefaults()
    {
        var path = P("config.xml");
        var id = Guid.NewGuid();
        File.WriteAllText(path,
            $"""
             <?xml version="1.0" encoding="utf-8"?>
             <UserConfig>
               <Version>3</Version>
               <ExternalApplications>
                 <ExternalApplicationDefinition>
                   <Id>{id}</Id>
                   <Name>Minimal</Name>
                   <ExecutablePath>C:\a.exe</ExecutablePath>
                 </ExternalApplicationDefinition>
               </ExternalApplications>
             </UserConfig>
             """);

        var app = Assert.Single(new Naps2Config(P("appsettings.xml"), path).Get(c => c.ExternalApplications));

        Assert.Equal(id, app.Id);
        Assert.Equal("\"{file}\"", app.Arguments);
        Assert.Equal(ExternalPageSelection.All, app.PageSelection);
        Assert.False(app.IncludeOcr);
        Assert.False(app.ClearAfterPassing);
        Assert.True(app.Enabled);
    }

    [Fact]
    public void ToolbarButton_OnlyShownWhenAddedInToolbarSettings()
    {
        Assert.False(_config.IsToolbarButtonShown(ToolbarButtons.ExternalApplications));

        _config.User.Set(c => c.ExtraButtons, ToolbarButtons.ExternalApplications);

        Assert.True(_config.IsToolbarButtonShown(ToolbarButtons.ExternalApplications));
    }

    [Fact]
    public void Config_IgnoresElementsFromOlderVersions()
    {
        var path = P("config.xml");
        File.WriteAllText(path,
            "<?xml version=\"1.0\" encoding=\"utf-8\"?><UserConfig><Version>3</Version>" +
            "<ExternalApplicationsEnabled>true</ExternalApplicationsEnabled><Culture>de</Culture></UserConfig>");

        Assert.Equal("de", new Naps2Config(P("appsettings.xml"), path).Get(c => c.Culture));
    }

    [Fact]
    public void Config_RemembersByDefault()
    {
        Assert.True(_config.Get(c => c.RememberExternalApplications));
    }

    [Fact]
    public void Config_RoundTripsDefaultId()
    {
        var id = Guid.NewGuid();
        var path = P("config.xml");
        new Naps2Config(P("appsettings.xml"), path).User.Set(c => c.DefaultExternalApplicationId, id);

        Assert.Equal(id, new Naps2Config(P("appsettings.xml"), path).Get(c => c.DefaultExternalApplicationId));
    }

    [Fact]
    public void DefaultApplication()
    {
        var first = App();
        var second = App();
        _config.User.Set(c => c.ExternalApplications, ImmutableList.Create(first, second));
        Assert.Null(_controller.DefaultApplication);

        _config.User.Set(c => c.DefaultExternalApplicationId, second.Id);
        Assert.Equal(second, _controller.DefaultApplication);
    }

    [Fact]
    public void DefaultApplication_DisabledOrDeleted_IsNotUsed()
    {
        var app = App() with { Enabled = false };
        _config.User.Set(c => c.ExternalApplications, ImmutableList.Create(app));
        _config.User.Set(c => c.DefaultExternalApplicationId, app.Id);
        Assert.Null(_controller.DefaultApplication);

        _config.User.Set(c => c.DefaultExternalApplicationId, Guid.NewGuid());
        Assert.Null(_controller.DefaultApplication);
    }

    [Fact]
    public void Applications_OnlyEnabled()
    {
        var enabled = App();
        _config.User.Set(c => c.ExternalApplications,
            ImmutableList.Create(enabled, App() with { Enabled = false }));

        Assert.Equal([enabled], _controller.Applications);
    }
}
