# Ignixa Shim Register

This register tracks known compatibility shims and SDK fallbacks while Ignixa support is completed. New Firely/Ignixa bridges must be added here or removed before merge readiness.

| ID | Surface | File(s) | Allowed mode | Reason | Severity / priority | Owner / team | Planned closure | Validation |
|---|---|---|---|---|---|---|---|---|
| SHIM-PATCH-001 | PATCH | `src/Microsoft.Health.Fhir.Shared.Core/Features/Resources/Patch/` | Hybrid only | FHIRPath PATCH currently depends on Firely model infrastructure. | P0 | FHIR | Implement Ignixa-native PATCH path or an explicitly supported adapter. | PATCH unit/E2E coverage in Ignixa and Hybrid modes. |
| SHIM-VALIDATION-001 | Resource model attribute and conformance validation | `src/Microsoft.Health.Fhir.Shared.Core/Features/Validation/IgnixaResourceValidator.cs` | Hybrid only | Non-Ignixa `ResourceElement`, conformance resources, and final model attribute validation currently require Firely validation behavior. | P0 | FHIR | Implement Ignixa-native validation closure or fail fast in Ignixa mode. | Ignixa validator fallback tests in Ignixa and Hybrid modes. |
| SHIM-SEARCH-001 | Search value conversion | `src/Microsoft.Health.Fhir.Core/Features/Search/Converters/` | Hybrid and Ignixa | Search converter boundary still uses `ITypedElement` compatibility infrastructure. | P1 | FHIR | Add SDK-aware converter seam or approve the adapter as supported infrastructure. | Search indexing/converter tests in both modes. |
