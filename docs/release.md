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
- `LightshotApp-win-Setup.exe` (Velopack installer)
- `LightshotApp-win-Portable.zip` (Portable build payload)
- `LightshotApp-<version>-full.nupkg` (Velopack update package)
- `releases.json` (Update manifest containing schema version, app version, sequence, package name, SHA-256, and size)
- `releases.json.sig` (ECDSA SHA-256 signature of `releases.json`)
- `SHA256SUMS.txt` (Cryptographic checksums of all release files)

The Velopack package ID is `LightshotApp` (install root `%LocalAppData%\LightshotApp\current\`), intentionally distinct from the user data directory `%LocalAppData%\Lightshot` to prevent uninstalls or reinstalls from wiping history and local scratch recordings.

Manifest URL: `https://github.com/miken90/lightshot/releases/latest/download/releases.json`

## 3. Rollback Strategy
- Downgrades and sequence replay attacks are strictly rejected by `ManifestVerifier`. A manifest with a sequence number less than or equal to the currently installed sequence is rejected.
- To roll back a faulty release:
  1. Revert or patch the codebase to the last known good state.
  2. Increment the `version` and assign a strictly higher `sequence` number.
  3. Publish the build as a new forward release.
- Users who already installed an unbootable build can download and run `LightshotApp-win-Setup.exe` manually to overwrite.

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

## 5. Update Flow
- **Verify-then-stage**:
  - The application periodically checks `releases.json` and its detached signature `releases.json.sig`.
  - The manifest names the full nupkg package, which is verified against ECDSA P-256 signatures and SHA-256 hashes.
  - Verified packages are staged locally under `%LocalAppData%\Lightshot\Updates`.
  - The Velopack feed (`releases.win.json`) is written into the staging directory only from verified bytes.
- **User Consent & Timing**:
  - First launch: update checks and prompts are suppressed to avoid interrupting initial onboarding.
  - Second launch and later: when an update is staged, the system tray icon displays a blue dot indicator, shows a balloon notification, and adds "Restart to Update (version)" to the tray context menu.
  - Restarts are held while work is in progress (active recordings, exports, or unsaved editor changes).
  - Staged updates automatically apply silently on application quit, or immediately when the user selects "Restart to Update".
- **Sequence Floor & Persistence**:
  - Custom settings keys (`update.enabled`, `update.sequence`, `update.pendingVersion`, `update.pendingSequence`, `app.launchCount`) are stored outside `AllKeys` to prevent schema resets.
  - The sequence floor advances only after a successful apply is verified by the newly running version.
- **Uninstall Safety**:
  - The uninstaller removes the `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` startup value first before prompting.
  - The prompt asks whether to remove settings and history, defaulting to No. An unanswered prompt keeps user data.
  - Unfinished scratch recordings under `%LocalAppData%\Lightshot\Lightshot Recordings` are always preserved when not empty.
