# fezd-client

**fezd-client** is the remote client for [FEZD](https://scadadog.com) (FEZ Dispenser).
It talks to a **fezd-server** gateway over HTTPS. It does **not** include the Windows
server, UDE, or EcoStruxure Control Expert.

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

## Copyright

Copyright © 2026 SCADADOG LLC. All rights reserved.
