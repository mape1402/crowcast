# CrowCast

Empty .NET library scaffold for the `CrowCast` NuGet package.

## Structure

- `src/CrowCast`: the single library project.
- `.github/workflows/build-and-release.yml`: build, package validation, and release automation.
- `Directory.Build.props`: shared build and NuGet metadata.

## Build locally

```powershell
dotnet restore CrowCast.sln
dotnet build CrowCast.sln --configuration Release
dotnet pack src/CrowCast/CrowCast.csproj --configuration Release --output nupkgs
```

## Release

1. Add a non-empty `## [vX.Y.Z]` section to `CHANGELOG.md`.
2. Replace the comment in `.release` with the same tag, for example `v0.1.0`.
3. Push the change to `main`.

The repository must define the `NUGET_USER` variable and configure a nuget.org Trusted Publishing policy for the GitHub workflow.
