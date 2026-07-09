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
3. Deploy to non-production with `Ignixa`.
4. Verify fallback guard reports no unapproved Firely fallback.
5. Run E2E mode matrix.
6. Promote only when the P0 matrix is green.

## Rollback

Set SDK mode to `Firely` and redeploy. Firely mode must not require active Ignixa providers.

## Troubleshooting

Use fallback guard logs to identify surface and reason. Any Firely fallback in Ignixa mode is a merge-blocking defect unless listed in the shim register.
