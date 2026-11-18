---
applyTo: "src/**/SMART/**,src/**/Features/Security/**,src/**/Features/Context/**,src/**/Configs/**,src/**/roles.json,src/**/Controllers/**,src/**/Features/Filters/**,src/**/Features/KnownHeaders.cs"
excludeAgent: "cloud-agent"
---

# FHIR Security Review: Identity, SMART Scopes, and the Request Surface

These files turn a token into `DataActions` and an `AccessControlContext`, and decide which requests reach a handler. A mistake here widens access for every later check.

## Parsing scopes

1. **Unknown must refuse.** In `SmartClinicalScopesMiddleware`, a scope that is malformed, unsupported, or constrained by a parameter the server cannot filter on must be rejected. It must not be dropped, and it must not be granted without its constraint.
2. **Split before decoding.** Split scope strings on the raw delimiters first, then decode each part. A decoded value must not introduce a new separator, resource type, or constraint.
3. **Keep the scope kind.** `patient/`, `user/`, and `system/` scopes must not be mixed or promoted. A `patient/` scope binds to the selected patient. A wildcard resource type does not remove the compartment.
4. **Combine scopes as documented.** Keep the documented union and intersection behavior when several scopes cover one resource type. A search-parameter constraint on one scope must not be dropped or applied to another. Check v1 and v2 scope syntax separately.
5. **Mapping changes widen everyone.** A change to how scope permissions map to `DataActions`, a new `DataActions` value, or an edit to `roles.json` or a default role changes what existing tokens can do. Ask who gains the new action.

## Identity

6. **Authentication and authorization are separate.** Check signature, signing keys, issuer, audience, and lifetime validation, and any switch that disables them. `Security.Enabled`, `Authorization.Enabled`, and `EnableSmartWithoutAuth` must not default to a weaker mode.
7. **Bind `fhirUser` and launch context.** They must resolve to a local resource with the expected service base, type, and ID. A validly signed identity from another service is not a local patient. A missing claim must refuse the request, as `ErrorOnMissingFhirUserClaim` does.
8. **The authorization services must agree.** A rule added to `SMARTScopeFhirAuthorizationService` or `RoleBasedFhirAuthorizationService` needs a decision for the other. Check what registers `DisabledFhirAuthorizationService` and that an enabled deployment cannot select it.

## Request surface

9. **New endpoints.** A new controller action or operation needs authentication, an audit event type, and a `DataActions` check in its handler for the specific operation. A new `[AllowAnonymous]` needs a stated reason and must not return or change resource data.
10. **New parameters and headers.** A new query parameter or entry in `KnownHeaders` is public input. If it changes validation, consistency, or which code path runs, check that a low-privilege caller cannot use it to skip a control.
11. **Filters and middleware order.** A change to the pipeline order, or a filter that returns early, must not let a request reach a handler before `AccessControlContext` is populated.
12. **Advertised and enforced must match.** The capability statement and `.well-known/smart-configuration` must not advertise a scope or capability the server does not enforce.
13. **Proxy and redirects.** For `AadSmartOnFhirProxyController`, validate the final decoded redirect destination before forwarding a code or token. Forwarding headers supplied by the caller are untrusted.

## Tests to ask for

- A token with the new or changed scope and a token without it, sent through the real middleware.
- An encoded, malformed, and mixed-context variant of the scope string.
- The default configuration, not only the stricter optional mode.
