namespace Lightshot.Platform.Windows.Updates;

public static class PinnedKey
{
    // The public key (Base64 SubjectPublicKeyInfo) used to verify update manifests.
    // Generated via keygen.ps1. The private key lives outside source control.
    public const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEeDxwX8GfNKjaPvAhqKtfDBUQuQxygY+J/hwKslG2djIMJUrco7PCs/9IStiZgQMgUVGQeU3hIHo7G9hhFgpQ7g==";
    public const string Fingerprint = "0680D74C5F6FE4EFE4D52A5F445AE03EC572BFEA309E544F7159F4C66ACEAC0A";
}
