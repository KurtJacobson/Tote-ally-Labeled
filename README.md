# Tote-ally Labeled

Prints storage tote labels on a Zebra network printer (ZPL over TCP port 9100). It is a Windows desktop
app: a WebView2 window showing a page served by the app's own web server on port 5050, which phones and
other computers on the network can also open while the app is running.

Settings and the logo are stored per user in `%LOCALAPPDATA%\ToteLabels`.

## Build and run

```
dotnet run
```

## Build the installer

Needs the .NET 8 SDK and [Inno Setup 6](https://jrsoftware.org/isdl.php).

```
powershell -ExecutionPolicy Bypass -File build\make-installer.ps1
```

This publishes the app self-contained into `dist\` and writes `installer\Output\Tote-ally-Labeled-Setup-<version>.exe`.

## Versions

The version comes from git, using the same rule as Vordr (`build\release-version.ps1`):

| Where HEAD is | Version |
|---|---|
| On tag `v0.1.0` | `0.1.0` |
| 3 commits past `v0.1.0` | `0.1.0.3` |
| 3 commits past `v0.2.0-dev.1` | `0.2.0-dev.1.3` |
| No tags yet | `0.1.0.<commit count>` |

The version is shown at the bottom of Settings.

## Releases

GitHub Actions (`.github/workflows/release.yml`) builds the installer on every push and pull request and
keeps it as a workflow artifact for a week. Pushing a tag publishes a GitHub release with the installer
attached; a tag containing `-` is marked as a pre-release.

```
git tag v0.1.0
git push origin v0.1.0
```
