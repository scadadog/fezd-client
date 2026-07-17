# fezd-client

**PLC Simulator for Copia Actions** — from [SCADADOG](https://www.scadadog.com).

`fezd-client` is the remote client for **FEZD** (FEZ Dispenser). Use it from
**Copia Actions** (and GitHub Actions) to **build and simulate** Modicon PLC
projects on a managed SCADADOG gateway — without standing up your own
hardware-in-the-loop rack.

This repository ships **fezd-client only**. The Windows gateway is separate.

This product is in **beta**.

## Prerequisites

| Requirement | Today |
|---|---|
| SCADADOG **license / connection file** (`.fezd.env`) | Required |
| Project as a **`.zef`** archive | Required |
| Copia Actions or GitHub Actions runner (or any Linux/macOS/Windows host) that can run `fezd-client` | Required |

Provide a `.zef` file on every `build` / `deploy` / `export`. Binaries alone are
not enough without a connection file.

### Coming soon

| Capability | Status |
|---|---|
| Accept **`.stu`** / **`.sta`** project files | Future version |
| Compile / simulate **from repository source files** (no pre-exported archive) | Future version |

## License required

You cannot use `fezd-client` without a **license / connection file** (`.fezd.env`)
issued for your environment.

### Request beta access

Email **[info@scadadog.com](mailto:info@scadadog.com)** to request a beta license
and connection file. Include:

- Your name / organization
- Target OS (Linux, macOS, or Windows)
- A short note on the intended use (typically Copia Actions)

We will reply with next steps and a connection file to use with:

```bash
fezd-client health --connection ./your-license.fezd.env
```

## Downloads

Pre-built binaries are published automatically on every push to `main`
([Releases](https://github.com/scadadog/fezd-client/releases)):

| Asset | Platform |
|---|---|
| `fezd-client-linux-x64` | Linux x64 |
| `fezd-client-osx-arm64` | macOS Apple Silicon |
| `fezd-client-win-x64.zip` | Windows x64 |

You still need a license file from SCADADOG to run them against a gateway.

## What FEZD is doing (simulation & hardware)

FEZD is a **PLC simulator for Copia Actions**: your pipeline uploads a `.zef`,
and a managed gateway compiles and exercises it against either:

1. **Gateway-hosted PLC simulation** (`--simulator`) — no field hardware required
2. **Physical Modicon controllers** (`--target <address>`) — when you are ready for hardware

### Simulation in CI

For CI, the Copia / GitHub Actions runner does **not** run a soft-PLC locally.
The runner only runs `fezd-client`. The licensed gateway host:

- Accepts the uploaded **`.zef`** over HTTPS
- Compiles/builds the project
- Exercises it against a **managed simulation environment** for Modicon PLC workflows
- Returns artifacts (for example `.stu`) to your CI workspace
- Wipes gateway-side project and artifact files after the run

This is a practical alternative to standing up expensive hardware-in-the-loop racks
just to get compile/sim feedback in pipeline.

### What happens during a remote run

1. CI authenticates with the connection file (gateway URL + license token + TLS pin).
2. The **`.zef`** project is uploaded over HTTPS.
3. The gateway grants an **exclusive session lease** (one active simulation/deploy at a time; others queue).
4. The project is built and exercised against simulation or hardware.
5. Artifacts (for example `.stu`) are returned to the CI workspace.
6. Gateway-side project and artifact files are wiped after the run.

## CI/CD with Copia Actions (and GitHub Actions)

**Primary path:** Copia Actions. GitHub Actions works the same way on a Linux runner.

Typical pattern:

1. Store your SCADADOG-issued connection file as a CI secret (never commit it).
2. Download `fezd-client-linux-x64` from Releases.
3. Ensure your workflow has a **`.zef`** project file available.
4. `health` the gateway, then `deploy --simulator` for CI simulation (or `--target` for hardware).

### Copia Actions

```yaml
# Example Copia / Linux CI shape — project.zef must be present in the workspace
steps:
  - name: Install fezd-client
    run: |
      curl -fsSL -o fezd-client \
        -H "Authorization: Bearer $GH_TOKEN" \
        -L https://github.com/scadadog/fezd-client/releases/latest/download/fezd-client-linux-x64
      chmod +x fezd-client

  - name: Health check
    run: ./fezd-client health --connection "$FEZD_CONNECTION"

  - name: Build and simulate
    run: |
      ./fezd-client deploy project.zef \
        --connection "$FEZD_CONNECTION" \
        --simulator \
        --run \
        --stu --out ./artifacts
```

### GitHub Actions

Store the full connection file body in a repository secret named `FEZD_CONNECTION_FILE`:

```yaml
name: PLC simulate

on:
  push:
    branches: [main]
  workflow_dispatch:

jobs:
  simulate:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Install fezd-client
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        run: |
          gh release download --repo scadadog/fezd-client --pattern 'fezd-client-linux-x64' -D "$HOME/bin"
          chmod +x "$HOME/bin/fezd-client-linux-x64"
          sudo ln -sf "$HOME/bin/fezd-client-linux-x64" /usr/local/bin/fezd-client

      - name: Write connection file
        run: |
          printenv FEZD_CONNECTION_FILE > "$RUNNER_TEMP/client.fezd.env"
          chmod 600 "$RUNNER_TEMP/client.fezd.env"
        env:
          FEZD_CONNECTION_FILE: ${{ secrets.FEZD_CONNECTION_FILE }}

      - name: Health check
        run: fezd-client health --connection "$RUNNER_TEMP/client.fezd.env"

      - name: Build and simulate
        run: |
          fezd-client deploy path/to/project.zef \
            --connection "$RUNNER_TEMP/client.fezd.env" \
            --simulator \
            --run \
            --stu --out ./artifacts

      - name: Upload artifacts
        uses: actions/upload-artifact@v4
        with:
          name: fezd-artifacts
          path: artifacts/
```

Hardware path when ready:

```bash
fezd-client deploy project.zef \
  --connection "$FEZD_CONNECTION" \
  --target 192.168.1.10 \
  --run
```

If the project requires an application password, set `FEZD_APP_PASSWORD` in the
environment (preferred in CI) or pass `--app-password`. There is no built-in
default — omit it when the project does not need one. CLI overrides the
environment variable when both are set.

## Important

SCADADOG does **not** take ownership of, or liability for, your project files.
Keep projects under **version control** (or other backed-up storage) to avoid
loss of changes. This utility is provided for use **at your own risk**.

Run `fezd-client about` for company, licensing, and product information.

## Supported controllers

### Supported today

| Family | Reference range | Notes |
|---|---|---|
| Modicon M340 | `BMX P34 ••••` | Primary supported family (e.g. `BMX P34 2020`) |
| Modicon M580 (ePAC) | `BME P58 ••••` | Incl. Safety and Hot Standby variants |
| Modicon MC80 | `BMK C80 ••••` | Compact controller |
| Modicon Momentum | `171 CBU ••••••` | Ethernet-capable Momentum CPUs |
| Modicon Quantum | `140 CPU ••• ••` | Incl. Hot Standby and Safety |
| Modicon Premium / Atrium | `TSX P57 ••••` / `TSX PCI57` | Legacy Ethernet models |
| PLC simulation target | `--simulator` | Gateway-hosted simulation; no field hardware |

### Future roadmap

| Ecosystem | Status |
|---|---|
| Rockwell Automation (Allen-Bradley) | Future roadmap |
| Siemens | Future roadmap |
| Additional vendor adapters | Future roadmap |

Names identify compatibility targets only. They do not imply affiliation, partnership, or endorsement.

## Security & privacy

- Encrypted **in transit** (HTTPS/TLS; certificate pinning via the connection file)
- Encrypted **at rest** on the gateway
- Project/artifact wipe after each run (uploaded `.zef` sources and returned `.stu` / related artifacts)
- **SOC 2 Type 1** in progress
- Built with security and data privacy first

## Service targets (beta)

Beta **service targets** (not a contractual SLA with credits unless agreed in writing):

| Metric | Target |
|---|---|
| Gateway API availability | **99.9%** monthly (excludes planned maintenance) |
| Planned maintenance | Announced in advance; typically off-hours |
| Critical support response | **4 business hours** |
| Standard support response | **Next business day** via **info@scadadog.com** |
| Compile + simulation job runtime | Best-effort; **often minutes** (project size and host load vary) |
| Queue wait | Best-effort FIFO — one exclusive simulation/deploy per gateway |
| Data handling | Encrypted in transit and at rest; wipe after each run |

### What to expect in CI

- Jobs often complete in **minutes** when the gateway is free.
- If another pipeline holds the exclusive lease, your job **queues**; wall-clock time includes queue wait plus post-run reset.
- Larger projects take longer than small ones.
- Exit codes from `fezd-client` match gateway outcomes so CI can fail closed on deploy/sim errors.
- Hard contractual uptime credits are not offered during beta unless specified in a separate agreement.

## Copyright & contact

**SCADADOG LLC**  
Website: [https://www.scadadog.com](https://www.scadadog.com)  
Email: [info@scadadog.com](mailto:info@scadadog.com)

Copyright © 2024–2026 SCADADOG LLC. All rights reserved.

FEZD is a product of SCADADOG LLC. Use is subject to your SCADADOG license /
connection agreement. Redistribution of binaries without authorization is not
permitted.

SCADADOG does not take ownership of, or liability for, your project files.
Keep projects under version control to avoid loss of changes. This utility is
provided for use at your own risk.
