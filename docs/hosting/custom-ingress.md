# Custom ingress contract

An ingress adapter can publish PitCrew Dashboard without changing the
application image when it satisfies this contract:

1. Join the `pitcrew-hosted` Compose network.
2. Forward HTTP requests privately to `http://dashboard:8080`.
3. Terminate public HTTPS before forwarding.
4. Preserve the original `Host` header.
5. Set `X-Forwarded-Proto` to the public request scheme.
6. Set `X-Forwarded-For` to the client chain.
7. Use a fixed private address and configure that exact address through
   `PitCrew__ReverseProxy__KnownProxyAddresses`.
8. Do not publish the dashboard container port directly.
9. Keep SQLite and data-protection storage on the dashboard host.
10. Depend on the dashboard with `condition: service_healthy` and
    `restart: true`, so Compose stops ingress during dashboard updates and
    restarts it only after the replacement is healthy.

Browser security headers and HSTS are application-owned. An ingress adapter
should not add a second Content Security Policy or duplicate HSTS and framing
headers, because divergent duplicate policies can break the SPA.

The provider-neutral base health check verifies the versioned hosted-ingress
contract in addition to general application health. This prevents an adapter
managed through the complete Compose model from exposing a pre-v0.2.0 image
that does not emit the application-owned browser security policy.

Caddy and Cloudflare Tunnel both use `172.31.0.2` as the fixed ingress address.
An additional adapter may use another address if it updates the dashboard's
known-proxy configuration in the same Compose overlay.
