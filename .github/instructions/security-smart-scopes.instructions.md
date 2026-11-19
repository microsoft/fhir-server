---
applyTo: "src/**/SMART/**,src/**/Features/Security/**,src/**/Features/Context/**,src/**/Features/Conformance/**,src/**/Modules/**,src/**/Registration/**,src/Microsoft.Health.Fhir.Api.OpenIddict/**,src/**/Configs/**,src/**/roles.json,src/**/appsettings*.json,src/**/Controllers/**,src/**/Features/Filters/**,src/**/Features/KnownHeaders.cs,src/**/Startup.cs,src/**/Features/Operations/Security/**,src/**/Features/Search/SearchOptionsFactory.cs"
excludeAgent: "cloud-agent"
---

# FHIR Security Review: Identity, SMART Scopes, and the Request Surface

These files turn a token into `DataActions` and an `AccessControlContext`, and decide which requests reach a handler. A mistake here widens access for every later check.

## Parsing scopes

1. **Malformed input must not widen.** `SmartClinicalScopesMiddleware` rejects malformed clinical scopes, unknown resource types, and constraints it cannot represent. Flag a change that truncates or drops them instead. Nonclinical scopes such as `openid` grant nothing and are ignored.
2. **Unenforceable scopes are withdrawn whole.** A parsed scope can prove unenforceable for a query. Withdraw the whole scope, not only its predicate. Deny the request only when a requested type is left with no enforceable scope. Keep the documented behavior for wildcard scopes.
3. **Split before decoding.** Split scope strings on the raw delimiters first, then decode each part. A decoded value must not introduce a new separator, resource type, or constraint.
4. **Keep the scope kind.** `patient/`, `user/`, and `system/` scopes must not be mixed or promoted. A `patient/` scope binds to the selected patient. A wildcard resource type does not remove the compartment.
5. **Combine scopes as documented.** Keep the documented union and intersection behavior when several scopes cover one resource type. A search-parameter constraint on one scope must not be dropped or applied to another. Check v1 and v2 scope syntax separately.
6. **Mapping changes widen everyone.** A change to how scope permissions map to `DataActions`, a new `DataActions` value, or an edit to `roles.json` or a default role changes what existing tokens can do. Ask who gains the new action.

## Identity

7. **Authentication and authorization are separate.** Check signature, signing keys, issuer, audience, and lifetime validation, and any switch that disables them. A change to `Security.Enabled`, `Authorization.Enabled`, or `EnableSmartWithoutAuth`, or to their shipped values in `appsettings.json`, needs a stated reason.
8. **Bind `fhirUser` and launch context.** Where the scope kind uses them, they must resolve to a local resource with the expected service base, type, and ID. A validly signed identity from another service is not a local patient. A claim the scope kind requires must refuse the request when it is missing, as `ErrorOnMissingFhirUserClaim` does. `system/` scopes skip user binding, and a `patient/` scope with a valid selected-patient claim does not also need `fhirUser`.
9. **Role and SMART checks intersect.** `RoleBasedFhirAuthorizationService.CheckAccess` intersects the role's actions with the result of `SmartScopeFhirAuthorizationService.CheckSMARTScopeAccess`. A change to either must keep that intersection, so neither one alone grants an action. `SecurityModule` selects `DisabledFhirAuthorizationService` when security or authorization is off. Check that an enabled deployment cannot select it.

## Request surface

10. **New endpoints.** A new controller action or operation needs authentication, an audit event type, and a `DataActions` check in its handler for the specific operation. A new `[AllowAnonymous]` needs a stated reason and must not return or change resource data.
11. **New parameters and headers.** A new query parameter or entry in `KnownHeaders` is public input. If it changes validation, consistency, or which code path runs, check that a low-privilege caller cannot use it to skip a control.
12. **Filters and middleware order.** A change to the pipeline order in `FhirServerServiceCollectionExtensions`, or a filter that returns early, must not let a request reach a handler before `AccessControlContext` is populated.
13. **Advertised and enforced must match.** The capability statement and `.well-known/smart-configuration` (`GetSmartConfigurationHandler`) must not advertise a scope or capability the server does not enforce.
14. **Proxy and redirects.** For `AadSmartOnFhirProxyController`, validate the final decoded redirect destination before forwarding a code or token. Forwarding headers supplied by the caller are untrusted.

## Tests to ask for

- A token with the new or changed scope and a token without it, sent through the real middleware.
- An encoded, malformed, and mixed-context variant of the scope string.
- The default configuration, not only the stricter optional mode.
