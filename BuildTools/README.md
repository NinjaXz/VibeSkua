# Build Tools

Build and packaging helpers used by the repository's root-level entry points.

## Main Windows build

Run `BuildProject.bat` from the repository root. It restores and builds the .NET projects, invokes `package-build.ps1`, and launches the packaged client.

During packaging, `Setup-Flex.ps1` is run automatically. It reuses a configured or discoverable Flex SDK, or downloads Apache Flex 4.16.1 when none is available. That first-time bootstrap requires a Java 8 JDK on `PATH`; CI provisions Java 8 automatically. The finished application bundles a separate Java 21 runtime for FFDec, so this build-time Java requirement does not apply to end users.

## Release packaging

Run `Publish-Release.bat` from the repository root. It calls `BuildProject.bat` and then creates Velopack release assets.

## Android remote controller

Run:

```powershell
.\BuildTools\Build-AndroidApp.ps1
```

The generated APK is copied to the repository root and the Android project folder.

## Internal helpers

- `package-build.ps1` compiles the ActionScript client and assembles the Windows output.
- `Build-FFDecRuntime.ps1` stages the private Java 21 runtime used by bundled FFDec.

The AS3 compiler entry point remains in `Skua.AS3\compile-as3.ps1`, alongside its Flex-specific setup files.
