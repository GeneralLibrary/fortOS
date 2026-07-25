namespace GNAS.Core;

/// <summary>Rotates the keystore master key and re-encrypts every stored secret.</summary>
public interface IMasterKeyRotationService
{
    Task RotateMasterKeyAsync(CancellationToken ct);
}
