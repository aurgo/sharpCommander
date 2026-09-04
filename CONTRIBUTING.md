# Contributing to SharpCommander

Thanks for taking the time to contribute. This page lists what you need to build the project, the conventions the code follows and how a release is made.

## Building and testing

Requirements: the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). Everything runs from the repository root:

```bash
dotnet build SharpCommander.sln          # must finish with 0 errors and 0 warnings
dotnet test SharpCommander.sln           # xunit + Avalonia.Headless, a few seconds
dotnet run --project src/SharpCommander.Desktop/SharpCommander.Desktop.csproj
```

`./run-macos.sh` launches the Debug build from a temporary `.app` bundle on macOS. The `legacy/` folder holds the original 2009 WinForms application; it is not part of the solution and is not built.

The trimming and AOT analyzers are enabled, so keep the build free of `IL` warnings: no reflection-based serialization or binding (System.Text.Json uses the source-generated `JsonContext`, XAML bindings are compiled with `x:DataType`).

## Conventions

- File-scoped namespaces, `sealed` classes, XML documentation comments on public members, `.editorconfig` formatting.
- MVVM with CommunityToolkit.Mvvm: view models never reference Avalonia controls; views stay thin.
- UI strings and code comments in English, no emojis in code.
- Platform differences go through `OperatingSystem.IsWindows()/IsMacOS()/IsLinux()` and the `Path` APIs; never hard-code separators, and compare paths with `PathUtils.PathComparer` (case-sensitive on Linux).
- Every user-triggered file operation goes through `IFileOperationsService` so it gets the same confirmations, progress and error reporting.
- Add a test for every bug fix and every new behavior. UI behavior is tested headless against the real windows (see `tests/SharpCommander.Tests/MainWindowTests.cs`); services are tested against a temporary directory (`TempDir` in `TestPaths.cs`) and the fakes in `tests/SharpCommander.Tests/Fakes`.

## Pull requests

1. Fork the repository and create a branch from `master`.
2. Make the change with its tests and documentation (README shortcut table, RELEASE_NOTES entry when user-visible).
3. Make sure `dotnet build` has no warnings and `dotnet test` passes on your platform; CI runs the suite on Ubuntu, Windows and macOS.
4. Open a pull request describing what changed and why.

## Releasing

1. Set the new version in `Directory.Build.props` (the only place it is defined) and describe the release in `RELEASE_NOTES.md`.
2. Merge to `master` and push a tag `v<version>`, for example `v2.1.0`. The tag must match `Directory.Build.props`; the workflow refuses otherwise.
3. The CI workflow builds and tests on the three platforms, publishes the seven runtime identifiers with the publish scripts (`SharpCommander.app` for macOS) and attaches the ZIP archives to the GitHub release.

The scripts can be run locally too: `./publish.sh`, `.\publish.ps1` or `publish.bat`; see the Publishing section of the README.
