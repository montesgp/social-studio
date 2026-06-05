using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Packaging;
using Incoders.Hive.Domain.Social.ContentGeneration;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Packaging;

/// <summary>
/// TDD tests for <see cref="GeneratedPackageBuilder"/>.
/// </summary>
public sealed class PackageBuilderTests
{
    [Fact]
    public async Task BuildAsync_SingleAsset_ProducesZipWithAssetAndManifest()
    {
        var builder = new GeneratedPackageBuilder();
        var asset = RenderedAsset.Create("carousel", "image/png", "carousel.png", new byte[] { 1, 2, 3, 4, 5 });
        var input = new BuildPackageInput(
            Assets: [asset],
            InferredFields: [],
            Warnings: [],
            OrganizationId: Guid.NewGuid(),
            JobId: Guid.NewGuid());

        var result = await builder.BuildAsync(input, CancellationToken.None);

        Assert.True(result.SizeBytes > 0);
        Assert.NotNull(result.Manifest);
        Assert.Single(result.Manifest.Formats);
        Assert.Equal(result.SizeBytes, result.Manifest.TotalSizeBytes);

        // Verify ZIP contents — no client folder, so legacy flat layout
        result.Content.Seek(0, SeekOrigin.Begin);
        using var archive = new ZipArchive(result.Content, ZipArchiveMode.Read, leaveOpen: true);
        Assert.Contains(archive.Entries, e => e.FullName == "manifest.json");
        Assert.Contains(archive.Entries, e => e.FullName.Contains("carousel"));
        // Flat layout: no client_folder_name in manifest
        Assert.Null(result.Manifest.ClientFolderName);
    }

    [Fact]
    public async Task BuildAsync_MultipleAssets_ManifestContainsAllFormats()
    {
        var builder = new GeneratedPackageBuilder();
        var assets = new[]
        {
            RenderedAsset.Create("carousel", "image/png", "carousel.png", new byte[] { 1, 2, 3 }),
            RenderedAsset.Create("flyer", "image/png", "flyer.png", new byte[] { 4, 5, 6, 7 }),
        };
        var input = new BuildPackageInput(
            Assets: assets,
            InferredFields: ["brand.ColorPalette"],
            Warnings: ["carousel: brand color inferred"],
            OrganizationId: Guid.NewGuid(),
            JobId: Guid.NewGuid(),
            PublicationByFormat: new Dictionary<string, PublicationMetadata>
            {
                ["carousel"] = new("Carousel publication description"),
                ["flyer"] = new("Flyer publication description"),
            });

        var result = await builder.BuildAsync(input, CancellationToken.None);

        Assert.Equal(2, result.Manifest.Formats.Count);
        Assert.Contains("carousel", result.Manifest.Formats.Keys);
        Assert.Contains("flyer", result.Manifest.Formats.Keys);
        Assert.Single(result.Manifest.InferredFields);
        Assert.Single(result.Manifest.Warnings);
        Assert.Equal(
            "Carousel publication description",
            result.Manifest.Formats["carousel"].Publication?.Description);
        Assert.Equal(
            "Flyer publication description",
            result.Manifest.Formats["flyer"].Publication?.Description);
    }

    [Fact]
    public async Task BuildAsync_CarouselMultiSlide_ZipHasSlideEntries()
    {
        var builder = new GeneratedPackageBuilder();
        var assets = new[]
        {
            RenderedAsset.Create("carousel", "image/png", "carousel.png", new byte[] { 1, 2, 3 }),
            RenderedAsset.Create("carousel", "image/png", "carousel.png", new byte[] { 4, 5, 6 }),
        };
        var input = new BuildPackageInput(
            Assets: assets,
            InferredFields: [],
            Warnings: [],
            OrganizationId: Guid.NewGuid(),
            JobId: Guid.NewGuid());

        var result = await builder.BuildAsync(input, CancellationToken.None);

        result.Content.Seek(0, SeekOrigin.Begin);
        using var archive = new ZipArchive(result.Content, ZipArchiveMode.Read, leaveOpen: true);
        // Both carousel slides should be present under carousel/ (flat layout, no client folder)
        Assert.Contains(archive.Entries, e => e.FullName == "carousel/slide_1.png");
        Assert.Contains(archive.Entries, e => e.FullName == "carousel/slide_2.png");
        Assert.Equal(2, archive.Entries.Count(e => e.FullName.StartsWith("carousel/")));
    }

    [Fact]
    public async Task BuildAsync_WithClientName_FilesPlacedUnderClientFolder()
    {
        var builder = new GeneratedPackageBuilder();
        var assets = new[]
        {
            RenderedAsset.Create("flyer", "image/png", "flyer.png", new byte[] { 1, 2 }),
            RenderedAsset.Create("carousel", "image/png", "carousel.png", new byte[] { 3, 4 }),
        };
        var input = new BuildPackageInput(
            Assets: assets,
            InferredFields: [],
            Warnings: [],
            OrganizationId: Guid.NewGuid(),
            JobId: Guid.NewGuid(),
            ClientName: "Acme Corp");

        var result = await builder.BuildAsync(input, CancellationToken.None);

        result.Content.Seek(0, SeekOrigin.Begin);
        using var archive = new ZipArchive(result.Content, ZipArchiveMode.Read, leaveOpen: true);

        // Files live under the display-name folder (PKG-01: case and spaces preserved)
        Assert.Contains(archive.Entries, e => e.FullName == "Acme Corp/flyer.png");
        Assert.Contains(archive.Entries, e => e.FullName == "Acme Corp/carousel/slide_1.png");
        // manifest.json stays at root
        Assert.Contains(archive.Entries, e => e.FullName == "manifest.json");
        // Manifest records the display-name folder (PKG-01)
        Assert.Equal("Acme Corp", result.Manifest.ClientFolderName);
    }

    [Fact]
    public async Task BuildAsync_ClientNameNormalisation_SanitizesIllegalChars()
    {
        var builder = new GeneratedPackageBuilder();
        var asset = RenderedAsset.Create("hook", "image/png", "hook.png", new byte[] { 9 });
        var input = new BuildPackageInput(
            Assets: [asset],
            InferredFields: [],
            Warnings: [],
            OrganizationId: Guid.NewGuid(),
            JobId: Guid.NewGuid(),
            ClientName: "  My Client & Co.! ");

        var result = await builder.BuildAsync(input, CancellationToken.None);

        result.Content.Seek(0, SeekOrigin.Begin);
        using var archive = new ZipArchive(result.Content, ZipArchiveMode.Read, leaveOpen: true);
        // & . ! are all legal; space/leading-space trimmed; result preserves case (PKG-01)
        Assert.Contains(archive.Entries, e => e.FullName == "My Client & Co.!/hook.png");
        Assert.Equal("My Client & Co.!", result.Manifest.ClientFolderName);
    }

    // ─── B-2: NormalizeFolderName unit tests (PKG-01) ────────────────────────────

    [Theory]
    [InlineData("Acme Corp", "Acme Corp")]               // case + spaces preserved
    [InlineData("UPPERCASE NAME", "UPPERCASE NAME")]      // case preserved
    [InlineData("lowercase name", "lowercase name")]      // lowercase preserved
    public void NormalizeFolderName_PreservesCase(string input, string expected)
    {
        var result = GeneratedPackageBuilder.NormalizeFolderName(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Café & Co", "Café & Co")]               // accent + & preserved (dot not at end)
    [InlineData("Empörung GmbH", "Empörung GmbH")]       // German umlaut preserved
    [InlineData("résumé", "résumé")]                     // French accent preserved
    [InlineData("Café & Co.", "Café & Co")]              // trailing dot trimmed (Windows path rule)
    public void NormalizeFolderName_PreservesAccentsAndSymbols(string input, string expected)
    {
        var result = GeneratedPackageBuilder.NormalizeFolderName(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Client:Name/Test?", "ClientNameTest")]      // : / ? stripped (reserved)
    [InlineData("file<name>here", "filenamehere")]            // < > stripped
    [InlineData("say\"hello\\world", "sayhelloworld")]        // " \ stripped
    [InlineData("pipe|and*star", "pipeandstar")]              // | * stripped
    public void NormalizeFolderName_StripsWindowsReservedChars(string input, string expected)
    {
        var result = GeneratedPackageBuilder.NormalizeFolderName(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("<>:\"", "")]    // all-reserved → empty
    [InlineData("", "")]         // empty stays empty
    [InlineData("   ", "")]      // whitespace-only → empty
    public void NormalizeFolderName_EmptyAfterSanitization_ReturnsEmpty(string input, string expected)
    {
        var result = GeneratedPackageBuilder.NormalizeFolderName(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void NormalizeFolderName_NullInput_ReturnsEmpty()
    {
        // xUnit [InlineData] cannot pass a C# null literal to a non-null string parameter,
        // so the null case is anchored explicitly here. Impl guards via IsNullOrWhiteSpace.
        var result = GeneratedPackageBuilder.NormalizeFolderName(null);
        Assert.Equal(string.Empty, result);
    }

    [Theory]
    [InlineData("folder. ", "folder")]        // trailing dot then space trimmed
    [InlineData("folder  ", "folder")]        // trailing space trimmed
    [InlineData("folder...", "folder")]       // multiple trailing dots trimmed
    [InlineData("  leading", "leading")]      // leading whitespace trimmed
    public void NormalizeFolderName_TrimsTrailingDotAndSpace(string input, string expected)
    {
        var result = GeneratedPackageBuilder.NormalizeFolderName(input);
        Assert.Equal(expected, result);
    }

    // ─── Cross-OS hardening tests (WU-B hardening) ───────────────────────────────

    [Fact]
    public void NormalizeFolderName_StripsDel0x7F()
    {
        // DEL (0x7F) must be stripped on all OSes
        var input = "folder\x7Fname";
        var result = GeneratedPackageBuilder.NormalizeFolderName(input);
        Assert.Equal("foldername", result);
    }

    [Theory]
    [InlineData(".config", "config")]          // leading dot → Unix hidden-file prevention
    [InlineData(".hidden", "hidden")]          // leading dot stripped
    [InlineData(". config", "config")]         // leading dot+space both stripped
    public void NormalizeFolderName_TrimsLeadingDot(string input, string expected)
    {
        var result = GeneratedPackageBuilder.NormalizeFolderName(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void NormalizeFolderName_TrimsLeadingSpace()
    {
        // Leading space stripped (separate from TrimStart in Trim(); explicit cross-OS rule)
        var result = GeneratedPackageBuilder.NormalizeFolderName("  MyFolder");
        Assert.Equal("MyFolder", result);
    }

    [Fact]
    public void NormalizeFolderName_TrailingDotTrimmed_CafeCase()
    {
        // Trailing dot trimmed — middle dot preserved
        var result = GeneratedPackageBuilder.NormalizeFolderName("Café & Co.");
        Assert.Equal("Café & Co", result);
    }

    [Fact]
    public void NormalizeFolderName_MiddleDotPreserved()
    {
        // Middle dot must NOT be stripped
        var result = GeneratedPackageBuilder.NormalizeFolderName("Co. Ltd");
        Assert.Equal("Co. Ltd", result);
    }

    [Theory]
    [InlineData("NUL", "NUL_")]        // Windows device name → append _
    [InlineData("con", "con_")]        // case-insensitive match
    [InlineData("COM1", "COM1_")]      // COM port
    [InlineData("LPT9", "LPT9_")]      // LPT port
    [InlineData("PRN", "PRN_")]        // PRN device
    [InlineData("AUX", "AUX_")]        // AUX device
    [InlineData("CLOCK$", "CLOCK$_")] // CLOCK$ device (with dollar sign)
    public void NormalizeFolderName_DeviceName_Neutralized(string input, string expected)
    {
        var result = GeneratedPackageBuilder.NormalizeFolderName(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Conway", "Conway")]       // merely contains "con" — must NOT be neutralized
    [InlineData("NULspace", "NULspace")]   // contains NUL as prefix — must NOT be neutralized
    [InlineData("MyNUL", "MyNUL")]         // NUL as suffix — must NOT be neutralized
    [InlineData("COM10", "COM10")]         // COM10 is NOT a reserved name (COM1-COM9 only)
    public void NormalizeFolderName_DeviceName_OnlyWholeNameNeutralized(string input, string expected)
    {
        var result = GeneratedPackageBuilder.NormalizeFolderName(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task BuildAsync_ManifestJson_ContainsFormatsAndInferredFields()
    {
        var builder = new GeneratedPackageBuilder();
        var asset = RenderedAsset.Create("hook", "image/png", "hook.png", new byte[] { 9, 8, 7 });
        var input = new BuildPackageInput(
            Assets: [asset],
            InferredFields: ["brand.Typography"],
            Warnings: [],
            OrganizationId: Guid.NewGuid(),
            JobId: Guid.NewGuid());

        var result = await builder.BuildAsync(input, CancellationToken.None);

        result.Content.Seek(0, SeekOrigin.Begin);
        using var archive = new ZipArchive(result.Content, ZipArchiveMode.Read, leaveOpen: true);
        var manifestEntry = archive.GetEntry("manifest.json");
        Assert.NotNull(manifestEntry);

        await using var stream = manifestEntry!.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var json = await reader.ReadToEndAsync();
        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("formats", out _));
    }

    [Fact]
    public async Task BuildAsync_EmptyAssetList_ThrowsDomainError()
    {
        var builder = new GeneratedPackageBuilder();
        var input = new BuildPackageInput(
            Assets: [],
            InferredFields: [],
            Warnings: [],
            OrganizationId: Guid.NewGuid(),
            JobId: Guid.NewGuid());

        await Assert.ThrowsAsync<PackagingException>(() =>
            builder.BuildAsync(input, CancellationToken.None));
    }
}
