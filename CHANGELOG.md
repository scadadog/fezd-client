# Changelog

Notable changes to **fezd-client**.

## [Unreleased]

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
