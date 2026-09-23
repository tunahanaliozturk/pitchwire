# Security boundaries

Pitchwire is a local demo, not a production deployment recipe. These are the boundaries to revisit
before exposing it to an untrusted network.

## What is protected?

The ingest HMAC secret, device bearer cookies, push endpoints and keys, and the integrity of match
scores and notification delivery. PostgreSQL and Redis hold the durable and cached state.

## Who can cross a boundary?

Anyone can read scores and issue a new device identity. A browser can edit only the preferences,
favourites and push subscriptions belonging to its random, hashed device token. A feed can submit
events only with a valid, time-bounded HMAC signature. The browser reaches the API through nginx on
the same origin. The feed, database and cache talk over the internal compose network.

An anonymous caller already has the public routes and can mint fresh device identities. Someone
holding a real device cookie can send legitimate writes quickly, or try another device's push
endpoint. A compromised feed already has the HMAC secret. A win for any of them would be taking over
notifications, changing another device's choices, exhausting storage or slowing match updates.

## What stops common failures?

The API validates device tokens before writes, refuses a push endpoint already owned by another
device, verifies HMACs in constant time and deduplicates feed events at a database unique index. The
ingest boundary caps bytes as they arrive, even when no length header is present. Out-of-range
timestamps are rejected rather than turning into exceptions. The browser cookie is HttpOnly, Secure
and SameSite=Lax. Nginx adds CSP, HSTS, MIME-sniffing, frame and referrer headers to successful and
error responses. Its `/api` limit uses the real socket address, not a caller-supplied forwarding
header. Compose publishes ports only on loopback. The API limits device writes only after it has
resolved the cookie to a stored device, and counts signed ingest batches separately. Rejections
return 429 and a retry window.

## What remains?

The nginx limit covers traffic through the web entry point, not direct calls to the local API port
or internal ingestion. Several clients behind one NAT share its allowance. The API's device and
ingest limits are local to one process, not shared across replicas; a remote deployment needs a
shared limiter and private service networking. HSTS is ignored on the demo's plain HTTP origin;
a real deployment needs TLS termination. `style-src-attr 'unsafe-inline'` is retained for the
formation and statistics bars' dynamic positions; scripts remain restricted to this origin.
The compose credentials and HMAC secret are deliberately development-only. Supply unique secrets
from a secret manager, stable VAPID keys, private service networking and a trusted TLS edge before
deployment. Do not trust arbitrary `X-Forwarded-*` headers from outside that edge.

Zod parses API responses in jitless mode. This avoids its `Function("")` feature probe, so the
browser can keep `script-src 'self'` without `unsafe-eval` or a spurious CSP violation.
