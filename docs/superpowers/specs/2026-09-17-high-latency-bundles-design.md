# High-Latency Bundle Processing

## Context

FHIR batch and transaction bundles are currently limited by `BundleConfiguration.EntryLimit`, which defaults to 500 entries. Some clients are willing to accept increased request latency in exchange for processing larger bundles. The server needs an explicit per-request opt-in while retaining a configurable upper bound.

## Configuration

Add `BundleConfiguration.EntryLimitHighLatency` with a default value of 1,000. Add the corresponding `EntryLimitHighLatency` setting to the default web configuration.

The existing `EntryLimit` remains the default limit. A value of `0` continues to disable the limit for the selected processing mode.

## Request Header

Add the request header constant:

`x-ms-high-latency`

The higher limit is selected only when the first header value parses as Boolean `true` after trimming whitespace, using case-insensitive Boolean parsing. Missing, empty, `false`, or invalid values do not opt in and use the normal entry limit.

## Processing Flow

Add `HttpContext.IsHighLatencyEnabled()` alongside the existing header parsing extensions. `BundleHandler.FillRequestLists` determines one effective entry limit:

- `EntryLimitHighLatency` when `IsHighLatencyEnabled()` returns `true`.
- `EntryLimit` otherwise.

The handler compares the bundle entry count with the effective limit before processing entries.

## Error Handling

Bundles above the effective limit continue to throw `BundleEntryLimitExceededException`. The existing localized error message reports the effective limit, so high-latency requests receive an error that identifies the configured high-latency maximum.

No new error response or status code is introduced. Invalid header values safely retain the normal limit.

## Testing

Extend unit coverage to verify:

- Header parsing returns `true` only for trimmed, case-insensitive Boolean `true`.
- Missing, empty, `false`, and invalid header values return `false`.
- Requests without the header use `EntryLimit`.
- Requests with `x-ms-high-latency: true` can exceed `EntryLimit` without exceeding `EntryLimitHighLatency`.
- Requests above `EntryLimitHighLatency` throw `BundleEntryLimitExceededException`.
- The exception message contains the effective high-latency limit.

## Scope

This change applies only to batch and transaction bundle entry-count validation. It does not change bundle execution timeouts, processing logic, orchestration, authorization, or response semantics.
