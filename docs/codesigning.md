# Authenticode Code-Signing for Snoop.GenericInjector.dll

`Snoop.GenericInjector.dll` is a native C++ DLL injected into target processes at
runtime via `CreateRemoteThread`.  Windows Defender and SmartScreen flag unsigned DLLs
loaded this way, which can cause security pop-ups or outright injection failures on
machines with strict security policy.  This document describes how signing is wired up
and how to provision a certificate.

---

## Current state

- **Release artifacts** — signed when the GitHub secret `CODESIGN_PFX_BASE64` is
  configured (see below).  Until a cert is provisioned, release DLLs are unsigned and
  the sign step is silently skipped.
- **CI builds** — never signed.  The `agent-ci` workflow runs an informational
  `signtool verify` step that is `continue-on-error: true`; it surfaces a warning if
  the DLL is unsigned but does not fail the build.
- **Local developer builds** — unsigned by default.  Optional signing can be enabled
  via the `SNOOP_CODESIGN_PFX` environment variable (see below).

---

## Getting a certificate

Use an **EV (Extended Validation) code-signing certificate** from a Microsoft-trusted
CA such as DigiCert, Sectigo, or GlobalSign.  An EV cert bypasses SmartScreen's
reputation period immediately.  OV certificates work but may still trigger SmartScreen
for the first few thousand downloads.

---

## Provisioning the GitHub secret (repo owner)

1. Export your PFX:

   ```powershell
   # On Windows
   certutil -encode cert.pfx cert.b64
   # Copy the base64 content (without header/footer lines) to clipboard
   ```

2. In the GitHub repository go to **Settings → Secrets → Actions** and add:

   | Secret name              | Value                             |
   |--------------------------|-----------------------------------|
   | `CODESIGN_PFX_BASE64`    | Base64-encoded PFX content        |
   | `CODESIGN_PFX_PASSWORD`  | Passphrase protecting the PFX     |

3. Push a version tag (e.g. `v6.1.0`).  The `release` workflow will import the cert,
   sign all three architecture DLLs (x86, x64, ARM64), and verify signatures before
   packaging.

---

## Optional local signing (developers)

Set these environment variables before running `dotnet build` or MSBuild in Release
configuration:

```powershell
$env:SNOOP_CODESIGN_PFX          = "C:\path\to\your.pfx"
$env:SNOOP_CODESIGN_PFX_PASSWORD = "your-passphrase"   # optional if PFX is not password-protected
```

The `CodeSigning.targets` MSBuild import in `Snoop.GenericInjector.vcxproj` detects
these variables and runs `signtool sign` + `signtool verify` automatically after the
linker step.  When the variables are not set the target is a no-op.

---

## Manual signtool fallback

If CI signing is not yet configured and you need to ship a signed build today:

```powershell
$signtool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe"
foreach ($dll in @("bin\Release\Snoop.GenericInjector.x86.dll",
                    "bin\Release\Snoop.GenericInjector.x64.dll",
                    "bin\Release\Snoop.GenericInjector.ARM64.dll")) {
    & $signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 `
      /f "your.pfx" /p "passphrase" $dll
}
# Verify
foreach ($dll in Get-ChildItem bin\Release\Snoop.GenericInjector.*.dll) {
    & $signtool verify /pa $dll
}
```

---

## Verifying a signed DLL

```powershell
signtool verify /pa bin\Release\Snoop.GenericInjector.x64.dll
# Expected output: "Successfully verified: ..."
```

Or via PowerShell's `Get-AuthenticodeSignature`:

```powershell
(Get-AuthenticodeSignature "bin\Release\Snoop.GenericInjector.x64.dll").Status
# Expected: Valid
```
