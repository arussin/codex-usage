# Build a Windows package

For maintainers. Users should download a build from [Releases](https://github.com/arussin/codex-usage/releases).

## Prerequisites

- Windows x64, PowerShell, Git and the .NET 10 SDK.
- A signed-in Codex CLI on the build account for the existing live smoke read.
- Access to NuGet, or a populated cache for the SDK's runtime/build packages.
- An empty build directory outside the source checkout and outside OneDrive.

From a clean checkout, run:

```powershell
$revision = git rev-parse HEAD
.\tools\Build-Release.ps1 -SourceDirectory (Get-Location).Path `
  -WorkDirectory 'C:\Builds\CodexUsageTray-single' -Revision $revision
```

Use `-Dotnet 'C:\path\to\dotnet.exe'` for a non-PATH SDK. Use
`-PackageLayout Folder` only to produce the earlier loose-file layout.

## Single-EXE settings

The default build is self-contained for `win-x64`, with
`PublishSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`,
`EnableCompressionInSingleFile=true`, `PublishTrimmed=false` and embedded debug
symbols. These properties apply consistently to restore, build and publish.
Application C# source and export behavior do not change with the package layout.

The helper builds in a separate copy, verifies publish output has only the EXE,
adds the MIT license, runs that exact EXE's existing `--self-test` mode, and checks
formatting. Self-tests use temporary fixture files and a unique temporary Run
registry value, exercise the popup, and make one authenticated quota read. They
do not run the normal tray or change its live export or startup entry. Keep
self-test logs private.

Single-file .NET applications extract bundled native libraries into a per-user
cache, normally `%TEMP%\.net`. Compression adds startup work; it does not remove
runtime components. See [Microsoft's single-file deployment reference](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview).

## Release verification

Record the source revision, SDK/runtime versions and checksums. Package the EXE,
license, installation guide and sanitized build information. Do not include
build logs, credentials, user settings or usage snapshots.

Test the exact packaged EXE after extracting it into a separate directory with
no companion DLLs. Repeat normal launch, usage/export and startup checks on a
separate Windows account before replacing a previously accepted package. Keep
the earlier draft and its acceptance evidence available during this check.
