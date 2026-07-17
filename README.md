# fezd-client

**fezd-client** is the remote client for [FEZD](https://scadadog.com) — FEZ Dispenser from SCADADOG.

Are you trying to go from legacy PLC programming to **industrial DevOps**?

Version control platforms like Copia help teams manage PLC project history, but CI pipelines still need a place to **compile, simulate, and exercise** code against Modicon targets before production. Full hardware-in-the-loop racks are expensive to stand up and operate. FEZD provides managed simulation and hardware pipeline capacity that fits beside **Copia Actions** and **GitHub Actions**, so you can build, test, and iterate — including AI-assisted workflows — until a project is ready for a production environment.

This repository ships **fezd-client only**. The Windows gateway is separate. The client does not embed engineering-host software.

This product is in **beta**.

## License required

You cannot use `fezd-client` without a **license / connection file** (`.fezd.env`)
issued for your environment. Binaries alone are not enough.

### Request beta access

Email **[info@scadadog.com](mailto:info@scadadog.com)** to request a beta license
and connection file. Include:

- Your name / organization
- Target OS (Linux, macOS, or Windows)
- A short note on the intended use

We will reply with next steps and a connection file to use with:

```bash
fezd-client ping --connection ./your-license.fezd.env
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

FEZD connects industrial DevOps pipelines to a managed Windows gateway that can compile Modicon PLC projects and exercise them against either:

1. **Gateway-hosted PLC simulation** (`--simulator`) — no field hardware required
2. **Physical Modicon controllers** (`--target <address>`) — when you are ready for hardware

### Simulation environment (Schneider Electric Modicon)

For CI, the GitHub Actions / Copia runner does **not** run a soft-PLC locally. The runner only runs `fezd-client`. The licensed gateway host:

- Accepts the uploaded project over HTTPS
- Compiles/builds the project
- Exercises it against a **managed simulation environment** for Schneider Electric Modicon PLC workflows
- Returns artifacts (for example `.stu`) to your CI workspace
- Wipes gateway-side project and artifact files after the run

This is a practical alternative to standing up expensive hardware-in-the-loop racks just to get compile/sim feedback in pipeline.

Simulation here means: the gateway owns the Modicon-compatible execution environment; your CI job stays thin (install client → authenticate → deploy → collect artifacts). Exact host tooling stays on the gateway and is not part of this public client repo.

### What happens during a remote run

1. CI authenticates with the connection file (gateway URL + license token + TLS pin).
2. The project is uploaded over HTTPS.
3. The gateway grants an **exclusive session lease** (one active simulation/deploy at a time; others queue).
4. The project is built and exercised against simulation or hardware.
5. Artifacts (for example `.stu`) are returned to the CI workspace.
6. Gateway-side project and artifact files are wiped after the run.

## CI/CD with GitHub Actions and Copia

Typical pattern:

1. Store your SCADADOG-issued connection file as a CI secret (never commit it).
2. Download `fezd-client-linux-x64` from Releases.
3. `ping` the gateway, then `deploy --simulator` for CI simulation (or `--target` for hardware).

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

      - name: Ping gateway
        run: fezd-client ping --connection "$RUNNER_TEMP/client.fezd.env"

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

### Copia Actions

Same Linux-runner shape: install `fezd-client-linux-x64`, inject the connection file from your secret store, then `ping` / `deploy`.

```yaml
# Example Copia / Linux CI shape
steps:
  - name: Install fezd-client
    run: |
      curl -fsSL -o fezd-client \
        -H "Authorization: Bearer $GH_TOKEN" \
        -L https://github.com/scadadog/fezd-client/releases/latest/download/fezd-client-linux-x64
      chmod +x fezd-client

  - name: Ping gateway
    run: ./fezd-client ping --connection "$FEZD_CONNECTION"

  - name: Build and simulate
    run: |
      ./fezd-client deploy project.zef \
        --connection "$FEZD_CONNECTION" \
        --simulator \
        --run \
        --stu --out ./artifacts
```

Hardware path when ready:

```bash
fezd-client deploy project.zef \
  --connection "$FEZD_CONNECTION" \
  --target 192.168.1.10 \
  --run
```

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

## Copyright

Copyright © 2026 SCADADOG LLC. All rights reserved.
