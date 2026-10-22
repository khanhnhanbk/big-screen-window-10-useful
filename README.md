# WindowHelper

A small Windows tray app with two features taken from PowerToys:

- **Zones**: snap windows into a 2x2 or 3x2 grid.
- **Find My Mouse**: double-tap Ctrl to spotlight the cursor.

## Download

Get the latest version from the [Releases page](https://github.com/khanhnhanbk/big-screen-window-10-useful/releases/latest).
Pick one file:

| File | Size | Needs |
|---|---|---|
| `WindowHelper-standalone.exe` | large | Nothing. Easiest choice. |
| `WindowHelper.exe` | ~200 KB | [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64) installed |

Requires Windows 10 or 11, 64-bit.

Every release is built by GitHub Actions from the source code in this repository.
Nobody uploads the exe by hand. See [Verifying a file](#verifying-a-file) to check this yourself.

## Install

1. Copy the downloaded exe to a permanent folder, for example `C:\Tools\WindowHelper\`.
2. Double-click it. A blue grid icon appears in the system tray (click `^` near the clock if hidden).
3. Optional: right-click the icon and tick **Start with Windows**.

If Windows shows "Windows protected your PC", click **More info** and then **Run anyway**.
This happens because the file is not signed by a publisher Windows trusts. See [Signing](#signing-the-exe).

## Usage

| Action | How |
|---|---|
| Snap a window | Hold **Shift** while dragging a window, drop it on a zone |
| Pick zones for the active window | **Win + G**, then click a zone or drag across several. Keyboard: Arrows move, Shift+Arrows extend, Enter apply, Esc cancel (right-click also cancels) |
| Move window between zones | **Win + Ctrl + Alt + Arrow keys** |
| Find the mouse | Tap **Ctrl** twice quickly |
| Change layout (2x2 / 3x2) | Right-click tray icon, pick **Layout** |
| Turn Find My Mouse on/off | Right-click tray icon |
| Quit | Right-click tray icon, **Exit** |

Notes:

- While WindowHelper runs, Win+G opens the zone picker instead of Xbox Game Bar.
- The first Win+Ctrl+Alt+Arrow press snaps the window into its nearest zone. Next presses move it.
- Left/Right at the screen edge moves the window to the next monitor.
- Windows that run as administrator cannot be snapped unless WindowHelper also runs as administrator.
- If you move the exe, turn **Start with Windows** off and on again.
- Settings are stored in `%APPDATA%\WindowHelper\settings.json`.

## Uninstall

1. Right-click the tray icon, untick **Start with Windows**, then **Exit**.
2. Delete the exe and the `%APPDATA%\WindowHelper` folder.

---

## Building

Requires the .NET 10 SDK.

```powershell
# Framework-dependent (~190 KB, needs .NET 10 Desktop Runtime on the target PC)
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish

# Self-contained (much larger, runs without installing .NET)
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish-standalone
```

Close the running app from the tray before rebuilding, or the exe is locked.

## Signing the exe

Signing proves who built the file and that nobody changed it afterwards.
There are two options. They differ in who trusts the signature.

### Option A: Self-signed certificate (free, for friends / your own PCs)

Windows does not trust a self-signed certificate by default.
Each recipient must import your public certificate once. Until they do, the signature shows as untrusted.

**1. Create the certificate (once).** Run in PowerShell:

```powershell
$cert = New-SelfSignedCertificate -Type CodeSigningCert `
    -Subject "CN=WindowHelper (Your Name)" `
    -CertStoreLocation Cert:\CurrentUser\My `
    -NotAfter (Get-Date).AddYears(5)

# Public part, to give to recipients
Export-Certificate -Cert $cert -FilePath .\WindowHelper-signing.cer
```

Keep the private key only in your certificate store. Never send the `.pfx` or the private key to anyone.

**2. Sign the exe (after every build):**

```powershell
$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
    Where-Object Subject -like "CN=WindowHelper*" | Select-Object -First 1

Set-AuthenticodeSignature -FilePath .\publish\WindowHelper.exe -Certificate $cert `
    -TimestampServer "http://timestamp.digicert.com" -HashAlgorithm SHA256
```

The timestamp keeps the signature valid after the certificate expires.

**3. Recipient trusts the certificate (once).** The recipient runs PowerShell **as administrator**:

```powershell
Import-Certificate -FilePath .\WindowHelper-signing.cer -CertStoreLocation Cert:\LocalMachine\Root
Import-Certificate -FilePath .\WindowHelper-signing.cer -CertStoreLocation Cert:\LocalMachine\TrustedPublisher
```

Warning: a certificate in the Trusted Root store makes Windows trust anything signed with that key.
Only import certificates from people you trust. Send the `.cer` file through a separate channel from the exe if possible.

### Option B: Publicly trusted signature (paid, for strangers)

Use this to share with people who should not have to import anything:

- **Azure Trusted Signing** (Microsoft's signing service, monthly fee). Check current eligibility for individual developers in your country.
- **Code signing certificate** from a certificate authority (Sectigo, DigiCert, SSL.com, and others).
  These now require the key to live on a hardware token or cloud HSM.

Even with a valid certificate, SmartScreen may warn at first until the file builds reputation.

## Verifying a file

**Checksum.** Each release includes `SHA256SUMS.txt`. Compute the hash of your download:

```powershell
Get-FileHash .\WindowHelper-standalone.exe -Algorithm SHA256
```

The hash must match the line for that file in `SHA256SUMS.txt`.

**Build provenance (strongest check).** Each exe has a signed attestation proving it was built
by this repository's GitHub Actions workflow. With [GitHub CLI](https://cli.github.com/) installed:

```powershell
gh attestation verify .\WindowHelper-standalone.exe --repo khanhnhanbk/big-screen-window-10-useful
```

`Verification succeeded!` means the file was built from this repo's source and has not been changed.

**Authenticode signature** (only if the exe was signed, see [Signing](#signing-the-exe)):

```powershell
Get-AuthenticodeSignature .\WindowHelper.exe | Format-List Status, SignerCertificate
```

## Releasing a new version (maintainer)

```powershell
git tag v1.0.1
git push origin v1.0.1
```

The [release workflow](.github/workflows/release.yml) builds both exes, writes checksums,
creates the attestation, and publishes the GitHub Release.

## License

[MIT](LICENSE)
