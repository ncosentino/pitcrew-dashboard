# Publishing to GitHub Container Registry

Publishing is intentionally separate from image construction. The release workflow
consumes the generic `container-image` capability and does not depend on whether an
ASP.NET application or worker produced the Dockerfile.

Publishing a GitHub release with a canonical semantic version tag such as `v1.2.3`
(the leading `v` is required and build metadata is rejected):

1. Smoke-tests the dashboard and connector `linux/amd64` images before authenticating.
2. Publishes separate dashboard and connector `linux/amd64` + `linux/arm64` indexes.
3. Adds immutable semantic-version and source-SHA tags to both packages.
4. Generates SBOMs and, for public repositories, GitHub artifact attestations.

The workflow uses `GITHUB_TOKEN`; no registry password is required. New GHCR packages
may be private until their package visibility is explicitly changed in GitHub.

Artifact attestations for private or internal repositories require GitHub Enterprise
Cloud. Set the repository variable `ENABLE_CONTAINER_ATTESTATION=true` only after that
prerequisite is available; otherwise the image publishes without the attestation step.
