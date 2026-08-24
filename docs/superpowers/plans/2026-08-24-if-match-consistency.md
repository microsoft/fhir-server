# If-Match Consistency Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every FHIR interaction that updates or deletes one existing resource honor the client `If-Match` header and the configured `versioned-update` policy.

**Architecture:** Carry the parsed `WeakETag` from HTTP and bundle entry dispatch through MediatR request models to the final handler. Conditional handlers reject a supplied stale tag before mutation, while SQL/Cosmos upserts remain the atomic authority for stale and missing tags; destructive hard-delete APIs perform an explicit current-version check because their datastore contract has no ETag parameter.

**Tech Stack:** .NET, ASP.NET Core MVC model binding, MediatR/Medino, xUnit, NSubstitute, SQL Server and Cosmos DB E2E fixtures.

## Global Constraints

- Preserve STU3 missing-header compatibility: 412 for STU3 and 400 for later FHIR versions.
- A supplied stale ETag returns 412 and must not mutate the resource.
- Conditional create/no-match does not require `If-Match`.
- Multi-resource conditional delete does not apply one ETag to multiple resources.
- Cover SQL Server and Cosmos DB and keep shared code compatible with STU3, R4, R4B, and R5.
- Include standard soft delete, non-standard hard delete, and purge-history.
- Do not change the hard-delete datastore contract in this focused fix.

---

### Task 1: Preserve the Existing Conditional Patch Work

**Files:**
- Modify: `src/Microsoft.Health.Fhir.Shared.Core/Features/Resources/Patch/ConditionalPatchResourceHandler.cs`
- Test: `src/Microsoft.Health.Fhir.Shared.Core.UnitTests/Features/Resources/Patch/ConditionalPatchResourceHandlerTests.cs`

**Interfaces:**
- Consumes: `ConditionalPatchResourceRequest.WeakETag`
- Produces: `UpsertResourceRequest(..., weakETag: request.WeakETag)`

- [ ] **Step 1: Cherry-pick the existing branch commit**

```powershell
git cherry-pick 94ba468e458d0ccfebfa314ccc5dddb2a016a97a
```

Expected: the conditional patch handler forwards `request.WeakETag`, and its focused tests are present.

- [ ] **Step 2: Run the focused tests**

```powershell
dotnet test src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj --no-restore --filter "FullyQualifiedName~ConditionalPatchResourceHandlerTests"
```

Expected: PASS.

---

### Task 2: Carry If-Match Through API Request Models

**Files:**
- Modify: `src/Microsoft.Health.Fhir.Core/Messages/Upsert/ConditionalUpsertResourceRequest.cs`
- Modify: `src/Microsoft.Health.Fhir.Core/Messages/Delete/DeleteResourceRequest.cs`
- Modify: `src/Microsoft.Health.Fhir.Core/Messages/Delete/ConditionalDeleteResourceRequest.cs`
- Modify: `src/Microsoft.Health.Fhir.Shared.Api/Controllers/FhirController.cs`
- Test: `src/Microsoft.Health.Fhir.Shared.Api.UnitTests/Controllers/FhirControllerTests.cs`

**Interfaces:**
- Produces: `ConditionalUpsertResourceRequest.WeakETag`
- Produces: `DeleteResourceRequest.WeakETag`
- Produces: `ConditionalDeleteResourceRequest.WeakETag`
- Consumes: `[ModelBinder(typeof(WeakETagBinder))] WeakETag ifMatchHeader`

- [ ] **Step 1: Write failing controller tests for all missing propagation paths**

Add tests that call controller methods with `WeakETag.FromVersionId("7")` and capture the MediatR request:

```csharp
WeakETag weakETag = WeakETag.FromVersionId("7");

await _fhirController.ConditionalUpdate(resource, weakETag);

await _mediator.Received(1).SendAsync<UpsertResourceResponse>(
    Arg.Is<ConditionalUpsertResourceRequest>(x => x.WeakETag == weakETag),
    Arg.Any<CancellationToken>());
```

Add the delete assertions explicitly:

```csharp
await _fhirController.Delete(
    KnownResourceTypes.Patient,
    Guid.NewGuid().ToString(),
    new HardDeleteModel(),
    weakETag,
    allowPartialSuccess: false);

await _mediator.Received(1).SendAsync(
    Arg.Is<DeleteResourceRequest>(x => x.WeakETag == weakETag),
    Arg.Any<CancellationToken>());

await _fhirController.ConditionalDelete(
    KnownResourceTypes.Patient,
    new HardDeleteModel(),
    weakETag,
    maxDeleteCount: null);

await _mediator.Received(1).SendAsync(
    Arg.Is<ConditionalDeleteResourceRequest>(x => x.WeakETag == weakETag),
    Arg.Any<CancellationToken>());
```

- [ ] **Step 2: Run the controller tests and confirm compilation/test failure**

```powershell
dotnet test src\Microsoft.Health.Fhir.Api.UnitTests\Microsoft.Health.Fhir.R4.Api.UnitTests.csproj --no-restore --filter "FullyQualifiedName~FhirControllerTests"
```

Expected: FAIL because the controller actions and request types do not accept or expose the ETag.

- [ ] **Step 3: Add optional WeakETag properties to request messages**

Use this constructor/property pattern:

```csharp
public ConditionalUpsertResourceRequest(
    ResourceElement resource,
    IReadOnlyList<Tuple<string, string>> conditionalParameters,
    BundleResourceContext bundleResourceContext = null,
    WeakETag weakETag = null)
    : base(resource.InstanceType, conditionalParameters, bundleResourceContext)
{
    EnsureArg.IsNotNull(resource, nameof(resource));
    Resource = resource;
    WeakETag = weakETag;
}

public WeakETag WeakETag { get; }
```

Add the same trailing optional parameter and property to both delete request types. Ensure `ConditionalDeleteResourceRequest.Clone()` preserves `WeakETag`.

- [ ] **Step 4: Bind and pass If-Match in the controller**

Change these action signatures:

```csharp
public async Task<IActionResult> ConditionalUpdate(
    [FromBody] Resource resource,
    [ModelBinder(typeof(WeakETagBinder))] WeakETag ifMatchHeader)

public async Task<IActionResult> Delete(
    string typeParameter,
    string idParameter,
    HardDeleteModel hardDeleteModel,
    [ModelBinder(typeof(WeakETagBinder))] WeakETag ifMatchHeader,
    [FromQuery] bool allowPartialSuccess)

public async Task<IActionResult> PurgeHistory(
    string typeParameter,
    string idParameter,
    [ModelBinder(typeof(WeakETagBinder))] WeakETag ifMatchHeader,
    [FromQuery] bool allowPartialSuccess)

public async Task<IActionResult> ConditionalDelete(
    string typeParameter,
    HardDeleteModel hardDeleteModel,
    [ModelBinder(typeof(WeakETagBinder))] WeakETag ifMatchHeader,
    [FromQuery(Name = KnownQueryParameterNames.Count)] int? maxDeleteCount)
```

Pass `ifMatchHeader` into the corresponding request constructors. Update direct controller calls in `FhirControllerTests` with `ifMatchHeader: null`.

- [ ] **Step 5: Run controller tests**

```powershell
dotnet test src\Microsoft.Health.Fhir.Api.UnitTests\Microsoft.Health.Fhir.R4.Api.UnitTests.csproj --no-restore --filter "FullyQualifiedName~FhirControllerTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src\Microsoft.Health.Fhir.Core\Messages src\Microsoft.Health.Fhir.Shared.Api\Controllers\FhirController.cs src\Microsoft.Health.Fhir.Shared.Api.UnitTests\Controllers\FhirControllerTests.cs
git commit -m "Propagate If-Match for conditional and delete requests" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 3: Enforce If-Match in Conditional Update and Single Delete

**Files:**
- Modify: `src/Microsoft.Health.Fhir.Shared.Core/Features/Resources/Upsert/ConditionalUpsertResourceHandler.cs`
- Modify: `src/Microsoft.Health.Fhir.Shared.Core/Features/Resources/Delete/ConditionalDeleteResourceHandler.cs`
- Test: `src/Microsoft.Health.Fhir.Shared.Core.UnitTests/Features/Resources/ResourceHandlerTests_ConditionalUpsert.cs`
- Test: `src/Microsoft.Health.Fhir.Shared.Core.UnitTests/Features/Resources/ResourceHandlerTests_ConditionalDelete.cs`

**Interfaces:**
- Consumes: request `WeakETag` properties from Task 2
- Produces: `UpsertResourceRequest(..., weakETag: request.WeakETag)`
- Produces: `DeleteResourceRequest(..., weakETag: request.WeakETag)`

- [ ] **Step 1: Write failing conditional update tests**

Add one matching-tag and one stale-tag test. The stale case must prove persistence is not called:

```csharp
ConditionalUpsertResourceRequest message = SetupConditionalUpdate(
    SaveOutcomeType.Updated,
    Samples.GetDefaultObservation(),
    WeakETag.FromVersionId("stale"),
    mockResultEntry);

await Assert.ThrowsAsync<PreconditionFailedException>(
    () => _mediator.SendAsync<UpsertResourceResponse>(message));

await _fhirDataStore.DidNotReceive().UpsertAsync(
    Arg.Any<ResourceWrapperOperation>(),
    Arg.Any<CancellationToken>());
```

Change `SetupConditionalUpdate` to accept `WeakETag weakETag` and pass it to the request.

- [ ] **Step 2: Write failing conditional delete tests**

For one matched resource with version `"7"`, verify a stale request throws 412 and neither `UpsertAsync` nor `HardDeleteAsync` is called:

```csharp
await Assert.ThrowsAsync<PreconditionFailedException>(
    () => _mediator.SendAsync<DeleteResourceResponse>(message));

await _fhirDataStore.DidNotReceive().UpsertAsync(
    Arg.Any<ResourceWrapperOperation>(),
    Arg.Any<CancellationToken>());
await _fhirDataStore.DidNotReceive().HardDeleteAsync(
    Arg.Any<ResourceKey>(),
    Arg.Any<bool>(),
    Arg.Any<bool>(),
    Arg.Any<CancellationToken>());
```

The matching-tag persistence assertion is added in Task 4, where `DeletionService` begins forwarding the request tag.

- [ ] **Step 3: Run both test classes and verify failure**

```powershell
dotnet test src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj --no-restore --filter "FullyQualifiedName~ResourceHandlerTests"
```

Expected: the new propagation/mismatch assertions FAIL.

- [ ] **Step 4: Replace synthesized conditional update tags with the client tag**

In `HandleSingleMatch`, compare a supplied client tag before modifying the resource:

```csharp
if (request.WeakETag != null && request.WeakETag.VersionId != resourceWrapper.Version)
{
    throw new PreconditionFailedException(
        string.Format(Core.Resources.ResourceVersionConflict, request.WeakETag.VersionId));
}
```

Construct `UpsertResourceRequest` with `request.WeakETag`, not `WeakETag.FromVersionId(resourceWrapper.Version)`. A null value must reach persistence so `versioned-update` can reject it.

- [ ] **Step 5: Validate and propagate conditional delete tags**

In the one-match branch, compare `request.WeakETag.VersionId` with the matched wrapper version. On success, construct `DeleteResourceRequest` with `weakETag: request.WeakETag`. Do not apply this path to `DeleteMultipleAsync`.

- [ ] **Step 6: Run both test classes**

```powershell
dotnet test src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj --no-restore --filter "FullyQualifiedName~ResourceHandlerTests"
```

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add src\Microsoft.Health.Fhir.Shared.Core\Features\Resources\Upsert src\Microsoft.Health.Fhir.Shared.Core\Features\Resources\Delete\ConditionalDeleteResourceHandler.cs src\Microsoft.Health.Fhir.Shared.Core.UnitTests\Features\Resources\ResourceHandlerTests_Conditional*.cs
git commit -m "Enforce If-Match on conditional resource mutations" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 4: Enforce If-Match Across Delete Modes

**Files:**
- Modify: `src/Microsoft.Health.Fhir.Shared.Core/Features/Resources/Delete/DeletionService.cs`
- Modify: `src/Microsoft.Health.Fhir.Shared.Core.UnitTests/Features/Resources/Delete/DeletionServiceTests.cs`
- Modify: `src/Microsoft.Health.Fhir.Shared.Core.UnitTests/Features/Resources/ResourceHandlerTests.cs`
- Modify: `test/Microsoft.Health.Fhir.Shared.Tests.Integration/Persistence/FhirStorageTestsFixture.cs`

**Interfaces:**
- Consumes: `DeleteResourceRequest.WeakETag`
- Consumes: `IConformanceProvider.RequireETag(string, CancellationToken)`
- Produces: soft-delete `ResourceWrapperOperation` with the client ETag and policy
- Produces: hard-delete/purge precondition validation against `IFhirDataStore.GetAsync`

- [ ] **Step 1: Write failing soft-delete persistence tests**

In `DeletionServiceTests`, configure `RequireETag("Patient")` to return true and capture the `ResourceWrapperOperation`:

```csharp
await _service.DeleteAsync(
    new DeleteResourceRequest(
        "Patient",
        "id",
        DeleteOperation.SoftDelete,
        weakETag: WeakETag.FromVersionId("7")),
    CancellationToken.None);

await fhirDataStore.Received().UpsertAsync(
    Arg.Is<ResourceWrapperOperation>(x =>
        x.WeakETag.VersionId == "7" &&
        x.RequireETagOnUpdate),
    Arg.Any<CancellationToken>());
```

- [ ] **Step 2: Write failing hard-delete and purge tests**

For each destructive operation, return a current wrapper with version `"7"`. Assert:

```csharp
await Assert.ThrowsAsync<PreconditionFailedException>(() =>
    _service.DeleteAsync(staleRequest, CancellationToken.None));

await fhirDataStore.DidNotReceive().HardDeleteAsync(
    Arg.Any<ResourceKey>(),
    Arg.Any<bool>(),
    Arg.Any<bool>(),
    Arg.Any<CancellationToken>());
```

Also assert a missing tag is rejected when `RequireETag` is true and a matching tag reaches `HardDeleteAsync`.

- [ ] **Step 3: Run deletion service tests and verify failure**

```powershell
dotnet test src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj --no-restore --filter "FullyQualifiedName~DeletionServiceTests"
```

Expected: new tests FAIL because delete ignores the ETag and versioning policy.

- [ ] **Step 4: Implement soft-delete enforcement**

Resolve the policy once for single-resource delete:

```csharp
bool requireETagOnUpdate = await _conformanceProvider.Value.RequireETag(
    key.ResourceType,
    cancellationToken);
```

Pass `request.WeakETag` and `requireETagOnUpdate` into the tombstone `ResourceWrapperOperation`.

- [ ] **Step 5: Implement hard-delete and purge validation**

Before calling `HardDeleteAsync`, load the current wrapper. If it exists, reject a missing required tag using the same STU3/later-version exception behavior as the datastores, and reject a supplied tag whose version differs. Inject `IModelInfoProvider` into `DeletionService` only if needed to preserve the STU3 status distinction, and update its three direct constructor call sites.

Use a focused helper:

```csharp
private void ValidatePrecondition(
    string resourceType,
    string currentVersion,
    WeakETag weakETag,
    bool requireETag)
{
    if (requireETag && weakETag == null)
    {
        string message = string.Format(
            Core.Resources.IfMatchHeaderRequiredForResource,
            resourceType);

        if (_modelInfoProvider.Version == FhirSpecification.Stu3)
        {
            throw new PreconditionFailedException(message);
        }

        throw new BadRequestException(message);
    }

    if (weakETag != null && weakETag.VersionId != currentVersion)
    {
        throw new PreconditionFailedException(
            string.Format(Core.Resources.ResourceVersionConflict, weakETag.VersionId));
    }
}
```

Do not apply this validation to `DeleteMultipleAsync`.

- [ ] **Step 6: Run deletion and resource handler tests**

```powershell
dotnet test src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj --no-restore --filter "FullyQualifiedName~DeletionServiceTests|FullyQualifiedName~ResourceHandlerTests"
```

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add src\Microsoft.Health.Fhir.Shared.Core\Features\Resources\Delete\DeletionService.cs src\Microsoft.Health.Fhir.Shared.Core.UnitTests\Features\Resources\Delete\DeletionServiceTests.cs src\Microsoft.Health.Fhir.Shared.Core.UnitTests\Features\Resources\ResourceHandlerTests.cs test\Microsoft.Health.Fhir.Shared.Tests.Integration\Persistence\FhirStorageTestsFixture.cs
git commit -m "Apply version preconditions to resource deletion" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 5: Add Versioned-Update End-to-End Coverage

**Files:**
- Create: `test/Microsoft.Health.Fhir.Shared.Tests.E2E/Rest/IfMatchTests.cs`
- Modify: `test/Microsoft.Health.Fhir.Shared.Tests.E2E/Microsoft.Health.Fhir.Shared.Tests.E2E.projitems`
- Modify: `test/Configuration/testconfiguration.json`

**Interfaces:**
- Consumes: in-process `FhirServer:CoreFeatures:Versioning:ResourceTypeOverrides`
- Produces: HTTP-level coverage for regular/conditional update, patch, delete, hard delete, purge-history, and bundle `request.ifMatch`

- [ ] **Step 1: Configure a low-impact resource type as versioned-update**

Add this under `FhirServer.CoreFeatures` in `testconfiguration.json`:

```json
"Versioning": {
  "ResourceTypeOverrides": {
    "Medication": "versioned-update"
  }
}
```

Medication is already created by E2E fixtures but is not updated or deleted without a tag.

- [ ] **Step 2: Add the shared E2E test class and project include**

Create `IfMatchTests` with `HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)`, retain the fixture, and skip configuration-specific assertions when `!fixture.IsUsingInProcTestServer`.

Add a raw request helper so every verb can set the exact header:

```csharp
private async Task<HttpResponseMessage> SendAsync(
    HttpMethod method,
    string uri,
    Resource resource = null,
    string mediaType = null,
    string ifMatch = null)
{
    using var request = new HttpRequestMessage(method, uri);
    if (resource != null)
    {
        request.Content = new StringContent(
            resource.ToJson(),
            Encoding.UTF8,
            mediaType ?? "application/fhir+json");
    }

    if (ifMatch != null)
    {
        request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
    }

    return await _client.HttpClient.SendAsync(request);
}
```

Register the file in `Microsoft.Health.Fhir.Shared.Tests.E2E.projitems`.

- [ ] **Step 3: Write failing regular update and delete tests**

For separate Medication resources, send current, stale, and missing tags. Assert current succeeds, stale returns 412, and missing returns the model-specific configured response. Verify the resource version or existence after each rejection.

Cover:

```text
PUT Medication/{id}
DELETE Medication/{id}
DELETE Medication/{id}?hardDelete=true
DELETE Medication/{id}/$purge-history
```

- [ ] **Step 4: Write failing conditional operation tests**

Use `_id={id}` so each request resolves to one resource. Cover:

```text
PUT Medication?_id={id}
PATCH Medication?_id={id} (application/json-patch+json)
PATCH Medication?_id={id} (application/fhir+json)
DELETE Medication?_id={id}
DELETE Medication?_id={id}&hardDelete=true
```

For conditional update no-match, PUT a Medication with a new id and `_id={newId}` without `If-Match`; assert 201 to prove create remains allowed.

- [ ] **Step 5: Add one bundle regression**

Submit a transaction bundle containing an update entry with:

```csharp
Request = new Bundle.RequestComponent
{
    Method = Bundle.HTTPVerb.PUT,
    Url = $"Medication/{medication.Id}",
    IfMatch = WeakETag.FromVersionId(medication.Meta.VersionId).ToString(),
}
```

Assert the entry succeeds, then repeat with a stale `IfMatch` and assert the entry status is 412.

- [ ] **Step 6: Run the E2E tests**

```powershell
dotnet test test\Microsoft.Health.Fhir.R4.Tests.E2E\Microsoft.Health.Fhir.R4.Tests.E2E.csproj --no-restore --filter "FullyQualifiedName~IfMatchTests"
```

Expected: PASS for SQL Server and Cosmos DB fixture rows available in the local test environment.

- [ ] **Step 7: Commit**

```powershell
git add test\Configuration\testconfiguration.json test\Microsoft.Health.Fhir.Shared.Tests.E2E
git commit -m "Test If-Match across single-resource interactions" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 6: Cross-Version Verification and Review

**Files:**
- Review all modified files from Tasks 1-5.

**Interfaces:**
- Consumes: completed implementation
- Produces: cross-version build/test evidence and reviewed diff

- [ ] **Step 1: Run targeted unit tests for all supported FHIR versions**

```powershell
$projects = @(
  "src\Microsoft.Health.Fhir.Stu3.Core.UnitTests\Microsoft.Health.Fhir.Stu3.Core.UnitTests.csproj",
  "src\Microsoft.Health.Fhir.R4.Core.UnitTests\Microsoft.Health.Fhir.R4.Core.UnitTests.csproj",
  "src\Microsoft.Health.Fhir.R4B.Core.UnitTests\Microsoft.Health.Fhir.R4B.Core.UnitTests.csproj",
  "src\Microsoft.Health.Fhir.R5.Core.UnitTests\Microsoft.Health.Fhir.R5.Core.UnitTests.csproj"
)
foreach ($project in $projects) {
  dotnet test $project --no-restore --filter "FullyQualifiedName~ConditionalPatchResourceHandlerTests|FullyQualifiedName~ResourceHandlerTests|FullyQualifiedName~DeletionServiceTests"
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
```

Expected: all targeted tests PASS.

- [ ] **Step 2: Run API controller tests**

```powershell
dotnet test src\Microsoft.Health.Fhir.Api.UnitTests\Microsoft.Health.Fhir.R4.Api.UnitTests.csproj --no-restore --filter "FullyQualifiedName~FhirControllerTests"
```

Expected: PASS.

- [ ] **Step 3: Inspect the complete diff**

```powershell
git --no-pager diff main...HEAD --check
git --no-pager diff main...HEAD --stat
git status --short
```

Expected: no whitespace errors and no unintended files.

- [ ] **Step 4: Request code review**

Invoke the repository code-review workflow over `main...HEAD`, address only high-confidence issues related to this change, and rerun the affected tests.
