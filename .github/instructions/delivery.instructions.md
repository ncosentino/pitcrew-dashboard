---
applyTo: ".github/workflows/**,.github/actions/**,.github/genesis-delivery*.json,.githooks/**"
---

# Pull-request delivery

- Treat `.github/genesis-delivery.json`, its schema, reusable actions, workflows, and
  hooks as one executable delivery contract.
- Push feature branches and deliver through pull requests. Keep direct default-branch
  updates and deletion blocked.
- Draft validation runs the declared frontend and analyzed build subset. Ready pull
  requests require fresh full CI plus the container-image gate.
- Keep test batching complete and deterministic: every discovered test assembly runs
  exactly once, and summary jobs fail when required work is skipped, cancelled, or
  failed.
- Ready Copilot-authored pull requests require the configured trusted human approval
  on the current head SHA.
- Public external-fork workflows require explicit maintainer approval before any
  proposed workflow executes on self-hosted capacity.
- Keep `pull_request_target` workflows isolated from untrusted checkout and execution.
- Do not change workflow names, required checks, draft behavior, runner routing, or
  merge policy without updating the delivery contract and focused tests together.
- Complete installer, container, platform, and hosted evidence remains with CI rather
  than a developer workstation.
