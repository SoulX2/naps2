using NAPS2.ExternalApplications;
using Xunit;

namespace NAPS2.Lib.Tests.ExternalApplications;

public class ExternalArgumentResolverTests
{
    private static ExternalExportResult Export(params string[] paths) => new()
    {
        ExportId = Guid.NewGuid(),
        FilePaths = paths,
        PageCount = 3,
        Format = "pdf"
    };

    [Theory]
    [InlineData("", new string[0])]
    [InlineData("   ", new string[0])]
    [InlineData("a b  c", new[] { "a", "b", "c" })]
    [InlineData("\"a b\" c", new[] { "a b", "c" })]
    [InlineData("--x=\"a b\"", new[] { "--x=a b" })]
    [InlineData("\"\" a", new[] { "", "a" })]
    [InlineData("\"a\"\"b\"", new[] { "a\"b" })]
    [InlineData(@"C:\dir\ ""C:\my dir\""", new[] { @"C:\dir\", @"C:\my dir\" })]
    public void Tokenize(string template, string[] expected)
    {
        Assert.Equal(expected, ExternalArgumentResolver.Tokenize(template));
    }

    [Fact]
    public void Tokenize_UnterminatedQuote()
    {
        Assert.Throws<ExternalArgumentException>(() => ExternalArgumentResolver.Tokenize("\"a b"));
    }

    [Fact]
    public void Resolve_AllPlaceholders()
    {
        var args = ExternalArgumentResolver.Resolve(
            "--import \"{file}\" --pages {pageCount} --dir={directory} {filename} {format}",
            Export(@"C:\exports\document.pdf"));

        Assert.Equal(
            ["--import", @"C:\exports\document.pdf", "--pages", "3", @"--dir=C:\exports", "document.pdf", "pdf"],
            args);
    }

    [Theory]
    [InlineData(@"C:\my exports\document.pdf")]
    [InlineData(@"C:\Übergabe\文档 ü\document.pdf")]
    [InlineData(@"C:\a""b\document.pdf")]
    [InlineData(@"C:\a & b; | c\document.pdf")]
    public void Resolve_ValueStaysOneArgument(string path)
    {
        Assert.Equal([path], ExternalArgumentResolver.Resolve("{file}", Export(path)));
        Assert.Equal(["--in", path], ExternalArgumentResolver.Resolve("--in \"{file}\"", Export(path)));
    }

    [Fact]
    public void Resolve_ValueIsNotScannedForPlaceholders()
    {
        var path = @"C:\{pageCount}\{unknown}.pdf";
        Assert.Equal([path], ExternalArgumentResolver.Resolve("{file}", Export(path)));
    }

    [Fact]
    public void Resolve_FilesExpandsToMultipleArguments()
    {
        Assert.Equal(["--in", @"C:\a b.tif", @"C:\c.tif"],
            ExternalArgumentResolver.Resolve("--in {files}", Export(@"C:\a b.tif", @"C:\c.tif")));
    }

    [Fact]
    public void Resolve_OtherBracesAreLiteral()
    {
        Assert.Equal(["{123}", "{a b}", @"C:\d.pdf"],
            ExternalArgumentResolver.Resolve("{123} \"{a b}\" {file}", Export(@"C:\d.pdf")));
    }

    [Theory]
    [InlineData("{file} {nope}")]
    [InlineData("{File}")]
    [InlineData("--in={files}")]
    [InlineData("--no-document")]
    [InlineData("{filename} {pageCount}")]
    [InlineData("")]
    public void Validate_Invalid(string template)
    {
        Assert.Throws<ExternalArgumentException>(() => ExternalArgumentResolver.Validate(template));
    }

    [Theory]
    [InlineData("{file}")]
    [InlineData("{files}")]
    [InlineData("{directory}")]
    [InlineData("--in={file} --pages {pageCount}")]
    public void Validate_Valid(string template)
    {
        ExternalArgumentResolver.Validate(template);
    }
}
