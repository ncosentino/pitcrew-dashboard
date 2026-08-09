# Physical device evidence procedure

## Status: BLOCKER

Issue #91 requires physical iPhone and Android smoke evidence. This machine
has **no** `adb` or `idevice` tooling, and no approved physical-device evidence
has been collected.

## Requirement

Before issue #91 can honestly close, the following must be satisfied:

1. **iPhone evidence:** Safari/iOS smoke test on a physical iPhone (not
   simulated) for at least one representative fleet route at the 390px
   viewport.
2. **Android evidence:** Chrome/Android smoke test on a physical Android
   device for at least one representative fleet route.

## Acceptable evidence

- Screenshots or screen recordings from a physical device showing the
  dashboard fleet page rendered correctly.
- Accessibility audit results from the device's native assistive technology
  (VoiceOver on iOS, TalkBack on Android).

## Procedure (when infrastructure is available)

1. Deploy the dashboard to a network-accessible host (loopback mode with
   port forwarding, or a hosted staging environment).
2. Connect the physical device to the same network.
3. Navigate to the fleet overview route.
4. Capture evidence:
   - Full-page screenshot
   - Overflow measurement (no horizontal scroll on the fleet page)
   - VoiceOver/TalkBack reading order for the page heading and first node
5. Attach evidence to the pull request as an artifact or inline image.

## Why this is a blocker

The repository has no approved remote device infrastructure (BrowserStack,
Sauce Labs, or similar) configured with usable credentials. The CI workflow
cannot fabricate physical device evidence. Manual capture on a developer's
device is the only currently approved path.

## Resolution path

Either:
- A developer manually runs the procedure above and attaches evidence, OR
- The team accepts an ADR that approves a remote device testing service and
  configures repository secrets for it, enabling CI-automated device evidence.

Until one of these is resolved, issue #91's physical device evidence
requirement remains unmet.
