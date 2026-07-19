# Container packaging

The generated Dockerfile builds the application with Docker Buildx and produces a
Linux image that runs as a non-root user. Builder stages contain the .NET SDK and,
when a frontend is selected, Node.js. The final image contains neither build
toolchain.

## Publication modes

`DotNetPublishMode` controls how .NET is placed in the final image:

- `FrameworkDependent` uses the Microsoft runtime image. The deployment machine
  still requires only Docker; the .NET runtime lives inside the image.
- `SelfContained` publishes the runtime with the application and uses the smaller
  `runtime-deps` base. This gives the application tighter runtime ownership, but
  duplicates runtime files into its application layers.

Do not infer which mode is smaller from its name. Compare compressed image layers,
shared-base reuse, startup behavior, and servicing requirements for the intended
deployment topology.

Trimming, single-file publication, Native AOT, and chiseled images are intentionally
not enabled. Each requires compatibility testing beyond ordinary containerization.

## Frontend dependency lockfiles

When `package-lock.json` exists, container builds use `npm ci`. Without one they
fall back to `npm install --no-audit --no-fund`, matching a freshly scaffolded
project's current behavior. Commit the generated application's resolved lockfile
before publishing production images when reproducible frontend dependencies matter.

## Stateful applications

Container packaging does not select a database or persistence adapter. If an
application uses SQLite, run one application replica, keep the database on a named
persistent volume by default, and use SQLite's online backup mechanism instead of
copying database and WAL files independently. Document host UID ownership when bind
mounts are offered.

Move to a client/server database when horizontal application replicas, multiple
writers, managed high availability, or higher write concurrency become real
requirements.

## Validation

`.github/workflows/container-ci.yml` builds and runs `linux/amd64`, checks the
declared health contract, verifies non-root execution, rejects SDK or Node build
tooling in the final filesystem, and cross-builds `linux/arm64` without emulation.
