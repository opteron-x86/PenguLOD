# Maintaining releases

`main` is the integration branch; use short-lived feature/fix branches and reviewed pull requests.
PenguLOD uses SemVer with pre-release identifiers while in alpha. `VERSION` controls the .NET
application/package version. `UPSTREAM_VERSION` controls the inherited AnvilLOD SKSE plugin;
do not bump it simply to release PenguLOD.

## Prepare a release

1. Set the next version, for example `python3 tools/version.py 0.3.0-alpha.1`.
2. Add a matching `## [0.3.0-alpha.1] - YYYY-MM-DD` section to `CHANGELOG.md`. Update the
   README version and provenance if needed. Explain limitations and any migration changes.
3. Run `bash tools/test.sh` and `bash tools/build.sh`, then `xvfb-run -a bash tools/native-smoke.sh`.
   Verify settings import and representative generation workflows when their code changes.
4. Build the AppImage with `bash tools/package-appimage.sh`. Commit, push and wait for the
   Linux and Windows build/test jobs and all package compatibility jobs to pass on that exact commit.
   Windows CI runs `tools/build-native-windows.ps1`, `tools/test-windows.ps1`,
   `tools/build-windows.ps1 -ReuseBackend` and `tools/windows-smoke.ps1`.
5. Tag that commit and push only the new tag:

```bash
git tag -a v0.3.0-alpha.1 -m 'PenguLOD 0.3.0-alpha.1'
git push origin v0.3.0-alpha.1
```

The tag pipeline verifies `vVERSION`, runs the same build/tests, and publishes a GitHub release.
Alpha/beta/rc tags are marked pre-release. Assets include the portable Linux x64 package,
AppImage, Windows x64 ZIP, committed source archive and one combined `SHA256SUMS`;
each binary package also includes the patched
FasterNGIO source and dependency notices. Release notes come from the matching changelog section.
The workflow refuses to overwrite an existing release.

## Reproduce and inspect

The build uses .NET 10 on Ubuntu 24.04 and Windows Server 2025 with Visual Studio 2026,
with checksum-verified FasterNGIO source archives. Windows dependencies use the pinned
vcpkg manifest and static MSVC runtime. A separate Windows Server 2022 job extracts and
tests the ZIP. Linux packages are tested in Ubuntu, Debian and Fedora containers.
GitHub Actions are pinned to commits and Dependabot proposes their updates. This is a repeatable
build procedure, not a promise of bit-identical archives across machines or SDK patch releases.

After a successful local build on a clean committed checkout, `bash tools/package-release.sh`
creates the source archive, release notes and checksums. Check both executable versions with
`dist/linux-x64/PenguLOD --version` and `dist/linux-x64/PenguLOD.Desktop --version`.
The archive should contain no personal settings, logs, source profiles or generated game output.

On Windows, `tools/build-windows.ps1` creates the ZIP and `SHA256SUMS-WINDOWS`.
The tag workflow verifies each platform's checksums and combines them before publishing.
It publishes only after all platform checks succeed; neither platform can publish a partial release.

If a tagged build fails, fix the source on a branch and choose a new version/tag. Do not move a
published tag or replace released binaries silently. Keep the old release for reproducibility.
