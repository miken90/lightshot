# Release & Update Architecture

## 1. Key Custody
- **Algorithm**: ECDSA NIST P-256 (secp256r1) with SHA-256 (RFC 3279 DER sequence signatures).
- **Private Key Location**: Stored exclusively outside the repository at `%USERPROFILE%\.lightshot-release\update-signing-key`.
- **Access Control**: Protected by Windows ACLs granted solely to the current user (`icacls /inheritance:r /grant:r`).
- **Public Key**: Pinned in application source code at `src/Lightshot.Platform.Windows/Updates/PinnedKey.cs`.
- **Key Loss / Compromise Recovery**:
  - If the private key is lost or compromised, updates cannot be signed for existing installations.
  - Recovery requires shipping a new installer with an updated pinned public key constant.
  - Recommendation: Maintain an encrypted offline backup of the private key.

## 2. Release Artifacts & URLs
Each GitHub release publishes:
- `Lightshot-win-Setup.exe` (Velopack installer)
- `Lightshot-win-Portable.zip` (Portable build payload)
- `Lightshot-<version>-full.nupkg` (Velopack update package)
- `releases.json` (Update manifest containing schema version, app version, sequence, package name, SHA-256, and size)
- `releases.json.sig` (ECDSA SHA-256 signature of `releases.json`)
- `SHA256SUMS.txt` (Cryptographic checksums of all release files)

Manifest URL: `https://github.com/miken90/lightshot/releases/latest/download/releases.json`

## 3. Rollback Strategy
- Downgrades and sequence replay attacks are strictly rejected by `ManifestVerifier`. A manifest with a sequence number less than or equal to the currently installed sequence is rejected.
- To roll back a faulty release:
  1. Revert or patch the codebase to the last known good state.
  2. Increment the `version` and assign a strictly higher `sequence` number.
  3. Publish the build as a new forward release.
- Users who already installed an unbootable build can download and run `Lightshot-win-Setup.exe` manually to overwrite.

## 4. Windows SmartScreen & Smart App Control
- **Unsigned Binaries**: Binaries are unsigned by deliberate design decision to remain lightweight, open-source, and free of commercial certificate dependencies.
- **Windows Defender SmartScreen**:
  - SmartScreen reputation is tied to the cryptographic file hash of each new build.
  - New releases will display "Windows protected your PC".
  - Users install by selecting **More info** -> **Run anyway**.
  - Integrity can be independently verified against `SHA256SUMS.txt`.
- **Smart App Control (SAC)**:
  - When SAC is enabled in enforcement mode on Windows 11, it automatically blocks unsigned applications lacking established cloud reputation without offering a "Run anyway" prompt.
  - SAC users who choose to run Lightshot must configure SAC in Evaluation or Off mode at their own discretion. Lightshot never attempts to disable or prompt SAC modifications in-app.
