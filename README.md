# todo-pilot

Terminal TODO list viewer for GitHub Copilot CLI sessions.

## Usage

```bash
dnx todo-pilot
```

The first run asks for explicit consent before installing the companion Copilot CLI extension. You can also manage the extension directly:

```bash
dnx todo-pilot install
dnx todo-pilot uninstall
```

The install flow lets you choose user scope (`~/.copilot/extensions/todo-pilot`) or project scope (`.github/extensions/todo-pilot` under the git root).

## Packaging

The tool targets .NET 10 and is configured for .NET 10+ platform-specific Native AOT NuGet tool packaging.

Build and test locally with:

```bash
dotnet restore
dotnet build --no-restore
dotnet test --no-build
```

The package set contains:

- a root `todo-pilot` package that maps every supported RID;
- one Native AOT RID-specific package per supported RID.

Native AOT compilation should run on a matching host/runner for each RID. The GitHub Actions workflow builds the root package once and builds each RID package on its matching runner, then publishes all packages together.

The platform-specific tool package format uses v2 `DotnetToolSettings.xml`, so installing it requires a .NET 10+ SDK. The package set does not currently include an `any` RID fallback because Native AOT/self-contained fallback behavior with native dependencies should be validated against the target .NET 10 SDK first.

## Releasing to NuGet

Publishing is handled by `.github/workflows/publish.yml`. Push a version tag to build the root package plus all supported Native AOT RID packages and publish them together:

```bash
git tag v0.1.0
git push origin v0.1.0
```

The workflow strips the leading `v`, so `v0.1.0` publishes NuGet package version `0.1.0`.

The RID packages are built on matching GitHub-hosted runners: `ubuntu-latest`, `ubuntu-24.04-arm`, `macos-15-intel`, `macos-15`, `windows-latest`, and `windows-11-arm`.

NuGet publishing uses API-key authentication:

1. Create a nuget.org API key with permission to push `todo-pilot` and RID packages such as `todo-pilot.linux-x64`.
2. Add it to the GitHub repository secrets as `NUGET_API_KEY`.
3. Push a `v*` tag; the workflow publishes with `dotnet nuget push --skip-duplicate`.

Set the secret with the GitHub CLI:

```bash
gh secret set NUGET_API_KEY --repo simonrozsival/todo-pilot
```

Manual fallback for local package inspection on the current host RID:

```bash
dotnet pack src/TodoPilot/TodoPilot.csproj \
  -c Release \
  -r osx-arm64 \
  -p:Version=0.1.0 \
  -p:PackageVersion=0.1.0 \
  -p:RuntimeIdentifiers=osx-arm64 \
  -o artifacts/packages
```
