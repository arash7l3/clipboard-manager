# 0005 — One monorepo

Status: accepted

**Decision.** Core, macOS app and Windows app live in this one repository: `core/`, `macos/`, `windows/`.

**Why.** The UniFFI bindings and both apps change together, so one PR can update all three.

**Consequences.** CI runs jobs per directory. A Windows runner is still needed for phases 3 and 5.3.
