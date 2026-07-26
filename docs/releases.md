# Release & build policy

The GitHub Actions workflow [`.github/workflows/buildrelease.yml`](../.github/workflows/buildrelease.yml)
builds and publishes two independent products:

- **Server** — `TelegramDownloader`, published for 8 platforms
  (win-x64/x86/arm64, linux-x64/arm/arm64, osx-x64/arm64).
- **Mobile / desktop apps** — `TFMAudioApp`, published for Android (APK),
  Windows (zip) and macOS (Intel + Apple Silicon `.pkg`).

## Default policy

> **The Server is the only product built by default. The mobile/desktop apps
> are built on demand**, when they actually change.

Rationale: the app targets (Android/Windows/macOS) are slow and expensive to
build (MAUI workloads, code signing, per-arch macOS packaging) and change far
less often than the server. Building them on every server release wastes CI
minutes and slows down publishing, so they are opt-in.

## How to trigger a build

Builds are triggered by **publishing a GitHub Release** with a specific tag
prefix, or **manually** from the Actions tab.

### By release tag

| Tag prefix | Builds |
| --- | --- |
| `server-v*` (e.g. `server-v3.8.0`) | Server only. |
| `v*` (e.g. `v3.8.0`) | **Server only.** Mobile/desktop apps are **not** built. |
| `app-v*` (e.g. `app-v1.2.0`) | Mobile/desktop apps only (Android + Windows + macOS). |

The key change from the previous behaviour: a plain **`v*`** release no longer
builds the apps. Publish the server as usual with `v*` (or `server-v*`); when
the apps have new changes to ship, publish a separate **`app-v*`** release.

### Manually (on demand, no release needed)

Actions tab → **Build and Release** → **Run workflow**. Tick exactly the targets
you want and set the version:

- `Build Server`
- `Build Android App`
- `Build Windows App`
- `Build macOS App`
- `Version` (e.g. `1.2.0`)

This is the recommended way to produce a one-off app build (for testing or a
hotfix) without cutting a release. Manually dispatched builds upload their
output as workflow **artifacts** (they are not attached to a release, because
there is no release to attach to).

## Typical scenarios

| Scenario | What to do |
| --- | --- |
| Ship a new server version | Publish a `v3.8.0` (or `server-v3.8.0`) release. Only the server builds and is attached to the release. |
| Ship a new app version | Publish an `app-v1.2.0` release. Only the apps build and are attached to the release. |
| Ship server **and** apps together | Publish both a `v3.8.0` release and an `app-v1.2.0` release. |
| Test an app build without releasing | Run the workflow manually, tick the app targets. Download the artifacts. |

## Where the output goes

- **Release-triggered builds** (`server-v*`, `v*`, `app-v*`) attach their zips /
  APK / pkg files to that GitHub Release (the `upload-release` job).
- **Manually dispatched builds** upload their output as workflow artifacts
  (1-day retention), since there is no release to attach them to.

## Implementation note

Each app job (`build-android`, `build-windows`, `build-macos`) runs only when:

```yaml
if: |
  (github.event_name == 'workflow_dispatch' && inputs.build_<target>) ||
  (github.event_name == 'release' && startsWith(github.event.release.tag_name, 'app-v'))
```

i.e. a manual dispatch that selected that target, or an `app-v*` release. The
previous `v*` fallback was removed so a plain server release never drags the app
builds along. The `build-server` job still runs for `server-v*` and `v*`, so the
server keeps building by default.
