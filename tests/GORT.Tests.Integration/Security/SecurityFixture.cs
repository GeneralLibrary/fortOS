using GORT.Core;
using GORT.Security.KeyStore;
using GORT.Security.Services;

namespace GORT.Tests.Integration.Security;

internal sealed class SecurityFixture : IDisposable
{
    public SecurityFixture()
    {
        DataRoot = Path.GetFullPath(Path.Combine("TestArtifacts", "Security", Guid.CreateVersion7().ToString()));
        Directory.CreateDirectory(DataRoot);
        Environment.SetEnvironmentVariable("GORT_DATA_ROOT", DataRoot);
        Database = new DatabaseProvider(DataRoot);
    }

    public string DataRoot { get; }

    public DatabaseProvider Database { get; }

    public NasTokenManager CreateTokenManager() => new(new NasKeyStore(), Database);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("GORT_DATA_ROOT", null);
    }
}
