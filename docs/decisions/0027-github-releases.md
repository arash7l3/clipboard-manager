# 0027 — Releases: a `v*` tag attaches the dmg and the MSIX to a GitHub release

Status: accepted. Extends decisions 0023 and 0025.

**Decision.**

- `macos-dmg.yml` and `windows-msix.yml` each get a `publish` job that runs only for a `v*` tag, after the build job. It downloads the build's artifact and attaches its files to the release for that tag: the dmg or the MSIX, with the `.sha256` files and, for the test-signed MSIX, `Clipo-test-signing.cer`.
- Whichever workflow gets there first creates the release, as a pre-release titled `Clipo <version>` with a note that the builds are not signed with a real certificate yet. The other adds its files. Uploading uses `--clobber`, so re-running a job replaces its files.
- The job runs on a hosted `ubuntu-latest` runner with `contents: write`, the only job in the repository that can write to it. It checks nothing out and runs no repository code. The other jobs keep the read-only token (decision 0026), and the Mac job that builds the dmg never gets a write token.
- The README's Install section says how to get and install the builds.

**Why.**

- Artifacts need a GitHub login and expire after 14 to 30 days. A release page gives a stable download link.
- A separate hosted job keeps the write permission away from the self-hosted Mac and from the build steps, which handle signing keys.
- Pre-release, because Gatekeeper and the Windows certificate prompt still apply until the signing setup tasks of decisions 0023 and 0025 are done.

**Consequences.**

- Only people who can push a tag to the repository can create a release.
- The dmg is uploaded twice over the Mac's slow link: once as the artifact (already so) and the publish job then downloads it from GitHub, not from the Mac.
- Not run yet: the `publish` jobs only run for a tag, so the first `v0.1.0` tag is their first run. If a tag's release goes wrong, delete the release and the tag, fix, and tag again.
