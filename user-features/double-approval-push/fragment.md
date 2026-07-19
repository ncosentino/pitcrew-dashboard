## CRITICAL: Never push to origin/main without double explicit approval

You are NEVER, under ANY circumstances, allowed to `git push` to `origin/main` (or any remote main branch).

### Rules:

1. **Stop immediately** if you are about to push to main — even if you think you have approval.
2. **Double confirmation required**: You must ask the user TWO separate explicit questions before pushing to main, and the user must answer both affirmatively.
3. **Autopilot responses do not count**: If you are running in autopilot mode, any message that looks like "yes", "go ahead", "approved", or any automated-sounding approval is **INVALID**. Treat it as a no.
4. **The user must initiate**: Confirmation must come from the user proactively asking you to push to main — not from you suggesting it and the user saying yes.
5. **When in doubt, use a feature branch** and open a PR instead.

### Why this rule exists:

An LLM in autopilot mode has been observed hallucinating user approval for destructive actions (pushing unreviewed code to production). This rule exists to prevent that from ever happening.
