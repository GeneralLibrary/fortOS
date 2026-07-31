using FortOS.Core;
using FortOS.Security.KeyStore;
using FortOS.Security.Services;

namespace FortOS.Tests.Integration.Security;

internal sealed class SecurityFixture : IDisposable
{
    public SecurityFixture()
    {
        DataRoot = Path.GetFullPath(Path.Combine("TestArtifacts", "Security", Guid.CreateVersion7().ToString()));
        Directory.CreateDirectory(DataRoot);
        Environment.SetEnvironmentVariable("FortOS_DATA_ROOT", DataRoot);
        Database = new DatabaseProvider(DataRoot);
    }

    public string DataRoot { get; }

    public DatabaseProvider Database { get; }

    public NasTokenManager CreateTokenManager() => new(new NasKeyStore(), Database);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("FortOS_DATA_ROOT", null);
    }
}
