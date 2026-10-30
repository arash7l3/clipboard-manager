# 0006 — Unsigned builds until certificates exist

Status: accepted

**Decision.** There is no Apple Developer account or Windows code-signing certificate yet. Build unsigned until they are available.

**Why.** Signing is only needed for distribution (phase 5), not for development.

**Consequences.** Unsigned builds trigger Gatekeeper and SmartScreen warnings. Steps 5.2 and 5.3 are blocked until the certificates are obtained.
