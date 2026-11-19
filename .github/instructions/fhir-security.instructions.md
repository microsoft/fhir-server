---
applyTo: "**"
excludeAgent: "cloud-agent"
---

# FHIR Security Review Guidelines

Supplement `AGENTS.md` and `REVIEW.md`. Trace the principal, input, control, resource, and outcome. Historical reports are leads, not proof of a defect in this revision. Apply relevant checks to changed boundaries, not a mandatory checklist for every PR.

## Authority, identity, and scope

- Check authentication and authorization separately. Inspect token signature, signing-key, issuer, audience, and lifetime validation, including defaults and disabling switches. Respect anonymous endpoints and distinguish development fixtures from production.
- Derive authority from validated identity and trusted server context, not headers, route values, resource IDs, or query parameters supplied by the caller. Missing required patient, scope, or identity context must not broaden access.
- Preserve RBAC, operation-specific `DataActions`, and SMART permissions in both directions. Resource access is not server-wide authority; an operation permission such as `Reindex` does not itself authorize every implicit read or returned representation. Check the API's required read/search authority without inventing an every-write-requires-read rule.
- Bind references, `fhirUser`, and launch context to the correct service base, resource type, ID, and compartment kind; include version when relevant. Use documented canonicalization and aliases, not naive string equality or type/ID truncation. A validly signed foreign-service identity is not automatically a local patient.
- Preserve type, action, scope kind, modifiers, and predicates through scope parsing and combination. Decoding must not turn an encoded value into new separators or constraints; preserve documented union/intersection semantics. Unsupported, missing, or malformed constraints must not silently disappear and widen access.

## Authorization survives transformations

- Check parity between ordinary and alternate execution paths: optimization, query hints, internal range searches, and provider-specific branches must preserve applicable filters. Public input must not select privileged internal behavior merely by claiming to be a trusted hint, header, or token.
- Trace restrictions through `_include`, `_revinclude`, chaining, `_has`, `$everything`, counts, and nested expression branches. Verify the final query and materialized targets after untyped-reference rewriting, expansion, and filtering, not only the original parsed input. Cover existence inference and preserve documented shared-resource exceptions.
- Re-establish current-caller resource-type, scope, and compartment checks on every continuation and included-resource page. Cached or signed state is not authorization; malformed or client-crafted state must not select a bypass. Preserve service/storage isolation without inventing a tenancy model.
- Check instance, type, and system history and version reads against the applicable authorization contract for the requested versions. Returning or testing the current resource is not a substitute for checking the historical version.
- Treat search/compartment definitions, caches, and indexes used by authorization as policy inputs. Disabling, shadowing, or widening a definition, or rebuilding an index, must not silently widen access. Verify trusted provenance and the documented unavailable/stale-state behavior, not merely that a definition exists.
- Check whether index precision, truncation, collation, or normalization loses distinctions required by authorization. Approximate FHIR search candidates are not automatically authorized results: establish that the final decision preserves the intended predicate, using a final check where needed. Documentation alone does not justify broader access; not every normalization is a defect.

## Derived effects and background work

- Conditional references and conditional operations can perform implicit searches. Check the caller's authority for those lookups and their effects; zero/one/multiple-match responses, resolved IDs, counts, links, and errors can disclose otherwise unreadable data.
- Follow bundles, header-triggered resources, and other secondary writes to their own resource/action checks. Updates and patches must preserve protected identity fields such as `resourceType` and `id`; validation before a rewrite must still hold for the resulting resource.
- For reindex, export, import, and bulk jobs, inspect creation, execution, status, cancellation, and results. Carry the submitting caller's authorized scope through trusted persisted state, queues, and retries; worker privilege and job IDs do not authorize callers. Issue signed result URLs only after applicable authorization and treat them as credentials. Preserve policy-permitted shared access, not an invented creator-only rule.
- Check additional protected-resource permissions, such as `EditProfileDefinitions`, for direct and include/revinclude-selected bulk targets. The submitter's decision must survive execution; honor the documented permit, skip/count, or reject behavior rather than blanket denial or reporting skipped resources as successful.
- Respect compatibility switches and deployment modes; check defaults and disabled paths. A stricter optional mode or an authorization-disabled fixture does not establish default protection. Do not infer external controls or production reachability without evidence.

## Inputs, outputs, and resource limits

- Keep untrusted values separate from SQL, shell, and other executable syntax. Validate dynamic identifiers and operators; parameterized values do not secure arbitrary query structure.
- For outbound imports, references, or callbacks, inspect destination policy at connection time, including redirects and name resolution. Check the allowed recipient/audience before forwarding service or managed-identity credentials. Do not assume deployment egress restrictions.
- For proxy and OAuth redirects, validate the final decoded destination and trusted origin context before forwarding authorization codes or tokens. Encoding or caller-supplied forwarding headers must not turn an untrusted destination into a trusted one.
- Check file/archive path containment and bounded expansion. Inspect parsers, regexes, FHIRPath, nested searches, bundles, and jobs for bounded size, depth, time, total work, and cancellation.
- Keep patient data, credentials, bearer tokens, connection strings, and signed URLs out of logs, errors, traces, and test artifacts. Check encoding where narratives or resource content reach an interpreting consumer.
- For build, dependency, and deployment changes, inspect provenance, permissions, secrets, and weakened scan/test controls. Never recommend running untrusted PR code with privileged credentials or treating model output as authorization.

## Evidence for enforcement

- Use synthetic two-principal or two-patient cases with an authorized-success control and denied counterpart. Assert response bodies, headers, counts, links, errors, and persisted effects as relevant, not just HTTP status.
- For touched boundaries, seek an ordinary-path control, an affected alternate path, and a relevant transformed-input case (for example encoded delimiters, foreign-base references, changed definitions, precision boundaries, or crafted continuation). Exercise later pages and requested historical versions when affected; do not demand a full cross-product.
- Verify tests reach real authorization, not mocked handlers or disabled-auth fixtures. Identify defaults, feature flags, deployment mode, SQL/Cosmos providers, and STU3/R4/R5 versions actually exercised; do not generalize one configuration to all.
- Require a concrete boundary and unauthorized outcome for a security finding. State missing runtime/deployment evidence explicitly. Recommend only bounded, isolated checks with synthetic data, not live-service probes or real patient records.
