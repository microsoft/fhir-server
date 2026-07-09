# SDK Mode Rollout

## Modes

| Mode | Use |
|---|---|
| Firely | Compatibility baseline and rollback |
| Ignixa | Target production mode |
| Hybrid | Migration and diagnosis only |

## Rollout

1. Deploy with `Firely`.
2. Run smoke tests for create, read, search, PATCH, import, export, bulk update, reindex, and conformance.
3. Deploy to non-production with `Hybrid` when diagnosing migration gaps. Use fallback guard logs to identify each Firely fallback surface and reason, and allow only shims tracked in the shim register.
4. Deploy to non-production with `Ignixa`.
5. Verify there are no Firely fallback failures. In Ignixa mode, Firely fallback is blocked with an exception whose message includes mode, surface, and reason.
6. Run E2E mode matrix.
7. Promote only when the P0 matrix is green.

## Rollback

Set SDK mode to `Firely` and redeploy. Firely mode must not require active Ignixa providers.

## Troubleshooting

In Hybrid mode, use fallback guard logs to identify fallback surface and reason, then reconcile each entry with the shim register. The register is for governance and tracking; it is not a runtime allowlist for strict Ignixa mode.

In Ignixa mode, any Firely fallback is a hard failure (`InvalidOperationException`) with mode, surface, and reason in the message. Treat it as merge- or release-blocking even when the related gap is tracked in the shim register.
