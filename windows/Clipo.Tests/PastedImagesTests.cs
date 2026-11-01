using Clipo.Core;
using Clipo.Services;

namespace Clipo.Tests;

public sealed class PastedImagesTests : IDisposable
{
    private static readonly DateTime s_time = new(2026, 11, 1, 14, 5, 9, DateTimeKind.Local);

    private readonly string _folder = Path.Combine(Directory.CreateTempSubdirectory("clipo-pasted-tests-").FullName, "Clipo");

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_folder)!, recursive: true);

    [Theory]
    [InlineData(ImageFormat.Png, "Image 2026-11-01 140509.png")]
    [InlineData(ImageFormat.Jpeg, "Image 2026-11-01 140509.jpg")]
    [InlineData(ImageFormat.Tiff, "Image 2026-11-01 140509.tif")]
    [InlineData(ImageFormat.Bmp, "Image 2026-11-01 140509.bmp")]
    public void FilesAreNamedLikeScreenshots(ImageFormat format, string expected) =>
        Assert.Equal(expected, PastedImages.FileName("Image", format, s_time));

    [Fact]
    public void ATranslatedNameKeepsItsLetters() =>
        Assert.Equal("تصویر 2026-11-01 140509.png", PastedImages.FileName("تصویر", ImageFormat.Png, s_time));

    [Fact]
    public void ANameCannotLeaveTheFolderOrBreakTheFileName() =>
        Assert.Equal("a_b_c_ 2026-11-01 140509.png", PastedImages.FileName("a/b\\c:", ImageFormat.Png, s_time));

    [Fact]
    public void SavingWritesTheFileAndCreatesTheFolder()
    {
        var path = new PastedImages(_folder).Save([1, 2, 3], "Image 1.png");

        Assert.NotNull(path);
        Assert.Equal("Image 1.png", Path.GetFileName(path));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(path));
    }

    [Fact]
    public void OnlyTheLastFileIsKept()
    {
        var images = new PastedImages(_folder);
        images.Save([1], "Image 1.png");
        var last = images.Save([2], "Image 2.png");

        Assert.Equal([last], Directory.GetFiles(_folder).Select(Path.GetFullPath));
    }

    [Fact]
    public void SavingTheSameNameAgainReplacesTheFile()
    {
        var images = new PastedImages(_folder);
        images.Save([1], "Image.png");
        var path = images.Save([2, 2], "Image.png");

        Assert.Equal([2, 2], File.ReadAllBytes(path!));
        Assert.Single(Directory.GetFiles(_folder));
    }

    [Fact]
    public void ClearDeletesEverything()
    {
        var images = new PastedImages(_folder);
        images.Save([1], "Image.png");

        images.Clear();

        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public void ClearingAFolderThatIsNotThereDoesNothing() => new PastedImages(_folder).Clear();
}
