# Installer

Shruti's packaging flow produces a self-contained x64 MSIX from the WinUI app
publish output. Self-contained release packages include both the .NET runtime
and Windows App SDK runtime, so a clean machine does not need a separate
Windows App Runtime installation.

## Build

Run from the repository root:

```powershell
.\scripts\package-msix.ps1 -Configuration Release -Platform x64
```

The script:

- builds the `whisper.cpp` native shim with CMake,
- publishes `src/Shruti.App.WinUI` as a self-contained `win-x64` app,
- stages a full-trust MSIX layout under `artifacts\installer\stage`,
- verifies `shruti_whisper.dll` is present in the package layout, and
- writes the package to `artifacts\installer\output\Shruti-0.1.0.0-x64.msix`.

The default command intentionally produces an unsigned CI artifact with the
development publisher `CN=Shruti Dev`. It cannot be installed until it is signed
by a certificate trusted on the target machine.

## Package Verification

`package-msix.ps1` automatically verifies the identity, publisher, version,
architecture, required assets, native transcription DLL, self-contained .NET
and Windows App SDK files, absence of PDBs, and signature when signing is
enabled. To verify an existing unsigned CI artifact independently:

```powershell
.\scripts\verify-msix-package.ps1 `
  -PackagePath .\artifacts\installer\output\Shruti-0.1.0.0-x64.msix `
  -ExpectedVersion 0.1.0.0 `
  -ExpectedPublisher "CN=Shruti Dev" `
  -RuntimeMode SelfContained
```

## Signing

The manifest publisher must exactly match the signing certificate subject.
When a certificate is supplied, the package script derives the publisher from
the certificate, signs with SHA-256, and verifies that Windows trusts the
resulting signature.

For a PFX file, put its password in an environment variable so it is not placed
on the command line:

```powershell
$env:SHRUTI_SIGNING_CERT_PASSWORD = "<PFX password>"
.\scripts\package-msix.ps1 `
  -Configuration Release `
  -Platform x64 `
  -Version 0.1.0.0 `
  -CertificatePath C:\secure\shruti-signing.pfx `
  -TimestampUrl https://timestamp.example.com
```

For a certificate whose private key is already in the Windows certificate
store:

```powershell
.\scripts\package-msix.ps1 `
  -Version 0.1.0.0 `
  -CertificateThumbprint "<certificate thumbprint>" `
  -CertificateStoreLocation CurrentUser `
  -TimestampUrl https://timestamp.example.com
```

Use a locally trusted self-signed certificate only for development. Public
distribution requires a valid code-signing identity trusted by target devices.
Do not commit PFX files or certificate passwords.

## Install, Update, Uninstall, And Data Preservation

Run lifecycle validation in a clean Windows VM or dedicated test account. Build
two trusted, signed packages with the same identity and publisher and increasing
versions, then run:

```powershell
.\scripts\test-msix-lifecycle.ps1 `
  -PackagePath .\artifacts\installer\output\Shruti-0.1.0.0-x64.msix `
  -UpdatePackagePath .\artifacts\installer\output\Shruti-0.1.1.0-x64.msix `
  -UninstallAfterTest `
  -ConfirmLifecycleTest
```

The confirmation switch is required because the script changes package
installation state for the current user. It refuses to replace an existing
Shruti package, verifies both signatures before installation, checks installed
versions, and confirms that pre-existing files under
`%LOCALAPPDATA%\Shruti\Models`, `Settings`, `Transcripts`, and `Recordings`
remain byte-for-byte unchanged after install, update, and optional uninstall.
The report is written under `artifacts\installer\validation`.

After installation, manually launch Shruti and complete the clean-machine MVP
check: install/import a model, record audio, transcribe offline, insert into
Notepad and a browser field, use clipboard fallback, and cancel without
insertion.

## Current Limitations

- CI produces an unsigned package because no signing identity is stored in the repository.
- A trusted production signing certificate and clean-machine VM run are still required before release.
- The first package is x64-only.
- Self-contained deployment increases package size and requires rebuilding the package to pick up Windows App SDK servicing updates.
- Direct insertion into elevated apps remains deferred; ordinary MSIX packaging and signing do not grant UIAccess.
