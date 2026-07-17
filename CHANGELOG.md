# Changelog

Notable changes to **fezd-client**.

## [Unreleased]

### Added

- **`fezd-client update`** — download and install the latest release for this
  OS/arch from GitHub Releases (checksum-verified).
- Best-effort update notice on other commands when a newer release exists
  (`Update available… Run: fezd-client update`). Opt out with
  `FEZD_SKIP_UPDATE_CHECK=1`.

## [2.3.3] - 2026-07-17

### Fixed

- `ping` / remote verbs accept a `.fezd.env` path as a positional
  (`fezd-client ping ./client.fezd.env`) in addition to `--connection`.
- Clearer errors when `--connection` has no path, or the file lacks `FEZD_URL`.
- Connection file parser accepts `export KEY=`, UTF-8/UTF-16 BOM, and strips
  zero-width characters that broke key matching.

### Changed

- Client help/about lead with **PLC Simulator for Copia Actions** (no EcoStruxure
  tagline). Remote command copy clarifies gateway upload; examples prefer
  `ping` and `deploy --simulator`.
- `platforms` client note avoids vendor tooling branding.

## [2.3.2] - 2026-07-17

### Changed

- Version bump for release packaging.

## [2.3.1] - 2026-07-16

### Changed

- Non-loopback gateways no longer require `--pin` / `FEZD_PIN`. Credentials are
  `FEZD_URL` + `FEZD_TOKEN`; TLS uses the OS trust store (Let's Encrypt / corp
  inspection roots). `--pin` remains as deprecated legacy for direct self-signed.
- `ping` soft-fails direct TCP so corp HTTP-proxy-only egress still reaches
  `/healthz`; success no longer requires the TCP rung.
- System HTTP(S) proxy is honored by default; `--no-proxy` forces direct connect.
- Large uploads set `ExpectContinue = false` for picky corp proxies.

## [2.3.0] - 2026-07-16

### Added

- `health` command (aliases: `ping`, `remote`) for gateway reachability checks.
- Help accepts `?` / `-?` in addition to `help` / `--help` / `-h`.
- `about` and help footer show SCADADOG LLC attribution
  (`https://www.scadadog.com`, `info@scadadog.com`, Copyright © 2024–2026).

### Changed

- Marketed as **PLC Simulator for Copia Actions**; README documents `.zef`
  prerequisite and future `.stu`/`.sta` + repo-source compile support.
- Client `doctor` removed from the remote surface (host doctor stays on
  `fezd-server`).
- Client `about` / README: no liability for project files; keep projects under
  version control; use at your own risk.
- Help Options lists and command copy clarify `.zef` is required today.

## [2.2.3] - 2026-07-16

### Added

- `cancel <session-id>` to cancel a queued or running deploy session on the gateway.
- `--remote-timeout` global option (HTTP client timeout for gateway calls).

### Changed

- Client help hides host-only globals (`--config`, `--com-timeout`, `--log-level`,
  `--verbose`, `--json`) and LocalOnly command options such as `--mode`.
- Help Options lists are derived from the shared catalog (filtered by remote mode).
- Doctor / platforms copy no longer implies local UDE on the client.
- `--test-project` documented as a path on the gateway host.

### Fixed

- Fail closed on unsupported remote flags: `--mode`, `--no-download`, and doctor
  `--app-password` (previously accepted then silently ignored).
- Reject non-loopback `http://` gateway URLs (HTTPS required off-loopback).
- Host-only verb rejection is driven by the command catalog (including aliases).

## [2.2.0] - 2026-07-15

Initial public repository split (client + shared contracts).
