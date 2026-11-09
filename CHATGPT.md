# v2-clam-av development notes

## Purpose and architecture

This repository builds one container containing a .NET 10 minimal API and ClamAV. The API communicates with `clamd` over localhost and stores asynchronous scan jobs in memory. Recreating the container therefore loses queued, downloading, and scanning job state.

The API does not enforce authentication or authorization. Treat the deployment's network or gateway boundary as security-sensitive, especially because URL scanning can make outbound HTTP and HTTPS requests.

## Key files

- `Dockerfile`: .NET build/runtime stages and ClamAV installation.
- `scripts/start.sh`: initial signature update, `clamd` startup, and API startup.
- `conf/clamd.conf` and `conf/freshclam.conf`: ClamAV configuration.
- `src/GovUK.Dfe.ClamAV`: API implementation.
- `src/GovUK.Dfe.ClamAV.Api.Client`: generated C# client and NSwag configuration.
- `.github/workflows`: deployment and NuGet client-package workflows.

The API client packaging workflow uses GitVersion 6.8 through GitTools Actions 4.7. GitVersion 6 configuration uses `label`, not the removed v5 `tag` branch property. Package versions are assembled from `MajorMinorPatch`, `PreReleaseLabel`, and `PreReleaseNumber` so `main` remains stable and pull requests receive `prerelease.N` versions.

## Build and verification

The supported local path is Docker because the service needs both .NET and ClamAV:

```bash
docker build --build-arg CI=true -t v2-clam-av:local .
docker run --rm -p 8080:8080 v2-clam-av:local
```

Run deterministic tests with:

```bash
dotnet test src/GovUK.Dfe.ClamAV.Tests/GovUK.Dfe.ClamAV.Tests.csproj -p:CI=true
```

Run the same live ClamAV smoke checks used by CI with:

```bash
./scripts/smoke-test.sh
```

`CI=true` skips NSwag regeneration during ordinary container builds. For a complete migration or client-generation change, also build the solution without that flag so the .NET 10 NSwag target runs, then inspect changes under `src/GovUK.Dfe.ClamAV.Api.Client/Generated`.

Before deployment, check `/scan/jobs` and do not recreate the container while jobs are queued, downloading, or scanning. Validate `/healthz`, `/version`, a clean file, and the EICAR test file against a staging candidate.

## Current constraints

- Virus definitions persist only when `/var/lib/clamav` is backed by a volume.
- Signature updates currently run at container start; continuous FreshClam operation is separate work.
- The API and `clamd` share a container and do not yet have full process supervision.
- The final container currently starts the API as root; non-root execution is separate hardening work.
