using GNAS.Core;
using GNAS.Modules.Share.Services;
using System.Security.Cryptography;

namespace GNAS.Tests.Integration.Modules;

public class FileManagerServiceTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task WriteReadSoftDeleteAndRestore_RoundTripsFile()
    {
        using var root = new TemporaryDataRoot(nameof(WriteReadSoftDeleteAndRestore_RoundTripsFile));
        var service = new FileManagerService();
        var filePath = Path.Combine(root.Root, "documents", "demo.txt");

        await service.WriteAsync(filePath, "hello", "text", overwrite: true, CancellationToken.None);
        var content = await service.ReadAsync(filePath, asBase64: false, CancellationToken.None);
        Assert.Equal("hello", content.Content);

        var deleted = await service.DeleteAsync(filePath, hardDelete: false, "tester", CancellationToken.None);
        Assert.False(deleted.HardDeleted);
        Assert.NotNull(deleted.RecyclePath);
        Assert.False(File.Exists(filePath));

        await service.RestoreAsync(deleted.RecyclePath!, filePath, CancellationToken.None);
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolvePath_OutsideDataRoot_ThrowsPermissionDenied()
    {
        using var root = new TemporaryDataRoot(nameof(ResolvePath_OutsideDataRoot_ThrowsPermissionDenied));
        var service = new FileManagerService();
        var outside = Path.GetTempFileName();
        try
        {
            await Assert.ThrowsAsync<PermissionDeniedException>(() => service.ReadAsync(outside, asBase64: false, CancellationToken.None));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResumableUpload_RejectsWrongOffsetAndFinalizesAtomically()
    {
        using var root = new TemporaryDataRoot(nameof(ResumableUpload_RejectsWrongOffsetAndFinalizesAtomically));
        var database = new DatabaseProvider(root.Root);
        var service = new FileManagerService(database: database);
        var target = Path.Combine(root.Root, "uploads", "large.bin");
        var payload = "chunk-onechunk-two"u8.ToArray();
        var sha256 = Convert.ToHexString(SHA256.HashData(payload));
        var session = await service.CreateUploadSessionAsync(target, "user:test", payload.Length, sha256, CancellationToken.None);

        await using (var first = new MemoryStream(payload[..9]))
        {
            session = await service.AppendUploadAsync(session.SessionId, "user:test", 0, first, 9, CancellationToken.None);
        }
        Assert.Equal(9, session.ReceivedBytes);

        await using (var invalid = new MemoryStream(payload[9..]))
        {
            var conflict = await Assert.ThrowsAsync<UploadOffsetConflictException>(
                () => service.AppendUploadAsync(session.SessionId, "user:test", 0, invalid, payload.Length - 9, CancellationToken.None));
            Assert.Equal(9, conflict.ExpectedOffset);
        }

        await using (var second = new MemoryStream(payload[9..]))
        {
            await service.AppendUploadAsync(session.SessionId, "user:test", 9, second, payload.Length - 9, CancellationToken.None);
        }
        await service.FinalizeUploadAsync(session.SessionId, "user:test", null, CancellationToken.None);

        Assert.Equal(payload, await File.ReadAllBytesAsync(target));
        Assert.Equal("completed", (await service.GetUploadSessionAsync(session.SessionId, "user:test", CancellationToken.None)).State);
    }

    private sealed class TemporaryDataRoot : IDisposable
    {
        private readonly string? _previous;

        public TemporaryDataRoot(string name)
        {
            _previous = Environment.GetEnvironmentVariable("GNAS_DATA_ROOT");
            Root = Path.GetFullPath(Path.Combine("TestArtifacts", "Modules", name, Guid.CreateVersion7().ToString()));
            Directory.CreateDirectory(Root);
            Environment.SetEnvironmentVariable("GNAS_DATA_ROOT", Root);
        }

        public string Root { get; }

        public void Dispose() => Environment.SetEnvironmentVariable("GNAS_DATA_ROOT", _previous);
    }
}
