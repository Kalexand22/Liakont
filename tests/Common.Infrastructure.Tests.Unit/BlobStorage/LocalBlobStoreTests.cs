namespace Stratum.Common.Infrastructure.Tests.Unit.BlobStorage;

using FluentAssertions;
using Microsoft.Extensions.Options;
using Stratum.Common.Infrastructure.BlobStorage;
using Xunit;

public sealed class LocalBlobStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LocalBlobStore _sut;

    public LocalBlobStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"stratum-blob-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var options = Options.Create(new BlobStorageOptions { BasePath = _tempDir });
        _sut = new LocalBlobStore(options);
    }

    [Fact]
    public async Task PutAsync_Returns_BlobReference_With_Correct_Metadata()
    {
        await using var stream = new MemoryStream("hello"u8.ToArray());

        var result = await _sut.PutAsync("test-container", "doc.pdf", "application/pdf", stream);

        result.Should().NotBeNull();
        result.Filename.Should().Be("doc.pdf");
        result.ContentType.Should().Be("application/pdf");
        result.SizeBytes.Should().Be(5);
        result.StorageKey.Should().StartWith("test-container/");
        result.StorageKey.Should().EndWith(".pdf");
    }

    [Fact]
    public async Task PutAsync_Creates_File_On_Disk()
    {
        await using var stream = new MemoryStream("content"u8.ToArray());

        var result = await _sut.PutAsync("uploads", "file.txt", "text/plain", stream);

        var fullPath = Path.Combine(_tempDir, result.StorageKey);
        File.Exists(fullPath).Should().BeTrue();
    }

    [Fact]
    public async Task PutAsync_Generates_Different_Keys_For_Same_Filename()
    {
        await using var s1 = new MemoryStream("a"u8.ToArray());
        await using var s2 = new MemoryStream("b"u8.ToArray());

        var ref1 = await _sut.PutAsync("c", "same.txt", "text/plain", s1);
        var ref2 = await _sut.PutAsync("c", "same.txt", "text/plain", s2);

        ref1.StorageKey.Should().NotBe(ref2.StorageKey);
    }

    [Fact]
    public async Task GetAsync_Returns_Readable_Stream_With_Correct_Content()
    {
        var content = "blob content"u8.ToArray();
        await using var storeStream = new MemoryStream(content);
        var blobRef = await _sut.PutAsync("data", "doc.bin", "application/octet-stream", storeStream);

        await using var readStream = await _sut.GetAsync(blobRef.StorageKey);
        using var ms = new MemoryStream();
        await readStream.CopyToAsync(ms);

        ms.ToArray().Should().BeEquivalentTo(content);
    }

    [Fact]
    public async Task DeleteAsync_Removes_File_From_Disk()
    {
        await using var stream = new MemoryStream("to-delete"u8.ToArray());
        var blobRef = await _sut.PutAsync("tmp", "temp.dat", "application/octet-stream", stream);

        await _sut.DeleteAsync(blobRef.StorageKey);

        var fullPath = Path.Combine(_tempDir, blobRef.StorageKey);
        File.Exists(fullPath).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_Does_Not_Throw_If_File_Missing()
    {
        var act = () => _sut.DeleteAsync("nonexistent/key.bin");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetAsync_Throws_FileNotFoundException_For_Missing_Key()
    {
        var act = () => _sut.GetAsync("missing/blob.dat");
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task PutAsync_Throws_On_Null_Stream()
    {
        var act = () => _sut.PutAsync("c", "f.txt", "text/plain", null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PutAsync_Throws_On_Empty_ContainerHint()
    {
        await using var stream = new MemoryStream("x"u8.ToArray());
        var act = () => _sut.PutAsync(string.Empty, "f.txt", "text/plain", stream);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task PutAsync_Throws_On_Empty_Filename()
    {
        await using var stream = new MemoryStream("x"u8.ToArray());
        var act = () => _sut.PutAsync("c", string.Empty, "text/plain", stream);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task PutAsync_Throws_On_Empty_ContentType()
    {
        await using var stream = new MemoryStream("x"u8.ToArray());
        var act = () => _sut.PutAsync("c", "f.txt", string.Empty, stream);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetAsync_Throws_On_Empty_StorageKey()
    {
        var act = () => _sut.GetAsync(string.Empty);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task DeleteAsync_Throws_On_Empty_StorageKey()
    {
        var act = () => _sut.DeleteAsync(string.Empty);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task PutAsync_Handles_Filename_Without_Extension()
    {
        await using var stream = new MemoryStream("data"u8.ToArray());

        var result = await _sut.PutAsync("misc", "README", "text/plain", stream);

        result.StorageKey.Should().StartWith("misc/");
        result.Filename.Should().Be("README");
        var fullPath = Path.Combine(_tempDir, result.StorageKey);
        File.Exists(fullPath).Should().BeTrue();
    }

    [Fact]
    public async Task PutAsync_Handles_Filename_With_Special_Characters()
    {
        await using var stream = new MemoryStream("data"u8.ToArray());

        var result = await _sut.PutAsync("docs", "rapport (final) v2.pdf", "application/pdf", stream);

        result.Filename.Should().Be("rapport (final) v2.pdf");
        result.StorageKey.Should().EndWith(".pdf");
        var fullPath = Path.Combine(_tempDir, result.StorageKey);
        File.Exists(fullPath).Should().BeTrue();
    }

    [Fact]
    public async Task Full_Round_Trip_Put_Get_Delete()
    {
        var content = "round-trip blob"u8.ToArray();
        await using var storeStream = new MemoryStream(content);
        var blobRef = await _sut.PutAsync("rt", "trip.bin", "application/octet-stream", storeStream);

        // Get
        var readStream = await _sut.GetAsync(blobRef.StorageKey);
        using var ms = new MemoryStream();
        await readStream.CopyToAsync(ms);
        await readStream.DisposeAsync();
        ms.ToArray().Should().BeEquivalentTo(content);

        // Delete
        await _sut.DeleteAsync(blobRef.StorageKey);
        var fullPath = Path.Combine(_tempDir, blobRef.StorageKey);
        File.Exists(fullPath).Should().BeFalse();
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\Windows\\win.ini")]
    [InlineData("/etc/passwd")]
    public async Task GetAsync_Rejects_Path_Traversal(string maliciousKey)
    {
        var act = () => _sut.GetAsync(maliciousKey);
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\Windows\\win.ini")]
    [InlineData("/etc/passwd")]
    public async Task DeleteAsync_Rejects_Path_Traversal(string maliciousKey)
    {
        var act = () => _sut.DeleteAsync(maliciousKey);
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task PutAsync_Sanitizes_Separator_In_ContainerHint()
    {
        await using var stream = new MemoryStream("data"u8.ToArray());

        var result = await _sut.PutAsync("/etc", "test.txt", "text/plain", stream);

        // Separators should be replaced — blob must be under base path
        result.StorageKey.Should().NotContain("/etc/");
        var fullPath = Path.Combine(_tempDir, result.StorageKey);
        Path.GetFullPath(fullPath).Should().StartWith(Path.GetFullPath(_tempDir));
    }

    [Fact]
    public async Task PutAsync_Sanitizes_DotDot_ContainerHint()
    {
        await using var stream = new MemoryStream("safe"u8.ToArray());

        var result = await _sut.PutAsync("..", "test.txt", "text/plain", stream);

        // Container hint should be sanitized — blob must be under base path
        var fullPath = Path.Combine(_tempDir, result.StorageKey);
        Path.GetFullPath(fullPath).Should().StartWith(Path.GetFullPath(_tempDir));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}
