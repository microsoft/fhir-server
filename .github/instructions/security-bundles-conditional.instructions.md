---
applyTo: "src/**/Features/Resources/**,src/**/Bundle/**,src/**/Features/Validation/**,src/**/Features/Security/Authorization/**"
excludeAgent: "cloud-agent"
---

# FHIR Security Review: Bundles, Conditional Operations, and Writes

A bundle, a conditional operation, or a reference can make the server search or write on the caller's behalf. Those derived actions need the caller's authority just as a direct request would.

## Bundles

1. **The bundle gate is an admission check.** `BundleHandler` admits any caller that holds at least one permission. Flag work on a bundle entry that happens before that entry's own request is authorized, such as reference resolution, ID rewriting, or validation that reads stored data.
2. **Inner requests keep the caller's context.** Each entry must run with the outer principal and `AccessControlContext`, and with its own resource type in the request context. The SMART authorization service evaluates permissions against the request context's resource type.
3. **Bundle type and options are caller input.** Batch, transaction, parallel processing, and bundle headers must not change which checks run.

## Conditional operations and references

4. **A conditional operation performs a search.** Conditional create, update, patch, and delete, and conditional references resolved by `ResourceReferenceResolver`, call `ConditionalSearchAsync`. The caller needs read or search authority for that lookup as well as the write action. Use the helpers in `AuthorizationServiceExtensions` where they fit.
5. **The lookup runs under the caller's restrictions.** A conditional search must apply the caller's SMART scope and compartment. It must not match resources the caller could not find with an ordinary search.
6. **Outcomes disclose data.** Zero, one, or many matches, resolved IDs, and distinct error messages can reveal that an unreadable resource exists. Check what a caller without read access learns.
7. **Deletes that expand.** A conditional or bulk delete with `_include`, `_revinclude`, or reference removal needs delete authority on every type it removes, not only the route type.

## Writes

8. **Identity fields are fixed.** Update and patch must not change `resourceType` or `id`. Validation that ran before a patch or a rewrite must still hold for the resource that is stored.
9. **Secondary writes get their own checks.** A header or option that creates or changes another resource needs the action on that resource too.
10. **Compartment changes.** A write under a patient-restricted scope can move a resource into or out of the caller's compartment by changing a reference. Check what the scope contract allows before and after the change. Do not assume every write requires read access.

## Tests to ask for

- A caller with a write scope and no read or search scope sends the operation directly and inside a transaction bundle. Both must be treated the same.
- A conditional reference to another patient's resource from a patient-scoped caller. Assert the response and what was stored.
