# Security Policy

## Supported Versions

Only the latest release (`main` / newest tag) receives security fixes.

| Version | Supported |
| --- | --- |
| 1.0.x | ✅ |
| < 1.0 | ❌ |

## Reporting a Vulnerability

Please report security vulnerabilities **privately** via [GitHub Security Advisories](https://github.com/circlequicklauncher/CircleQuickLauncher/security/advisories/new) (preferred) or by emailing the maintainers listed in the repository.

**Do not open a public issue for security reports.**

Include:

- A description of the vulnerability and its impact.
- Steps to reproduce (PoC, affected version, OS build).
- Any known mitigations.

We will acknowledge reports within **72 hours** and aim to ship a fix or advisory within **14 days** for confirmed high-impact issues.

## Threat Model / Scope

CircleQuickLauncher runs as the current user with standard (non-elevated) privileges. Relevant surfaces:

- **Local config files** (`%APPDATA%\CircleQuickLauncher\`) — JSON settings, app list, imported PNG icons. These are user-writable and **not** treated as trusted input.
- **Launch targets** — the app list stores executable paths that are Shell-Executed on click. Users should only add executables they trust; the app validates path shape and existence but does not sandbox or inspect binaries.
- **Imported icons** — PNG files are validated for min dimensions / non-empty data but are not deeply parsed; treat untrusted PNGs with care as with any image viewer.

Out of scope:

- Denial-of-service via resource exhaustion (the app is lightweight by design).
- Issues requiring physical access or an already-compromised user account.
- Social-engineering of the user into adding malicious executables.

## Hardening Notes

- No network calls, no telemetry, no auto-update fetches.
- No elevated / admin required (installer requests admin only for Program Files + HKLM convenience; portable runs without elevation).
- Single-instance mutex + pipe prevent accidental multi-instance races.
- Config writes are atomic (temp file + rename) to reduce corruption risk.
