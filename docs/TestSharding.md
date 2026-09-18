# Test sharding

Test sharding splits one test assembly across several test processes, so that the processes can run at the same
time on different agents. It is **off by default** and must be explicitly enabled by environment variables.

It exists for the E2E test assembly, where wall clock time is dominated by waiting on a live FHIR service rather
than by CPU. It is not a general parallelization feature and it does **not** make tests run concurrently inside a
single process.

## The hard prerequisite: one independently backed service per shard

> **Every shard process must talk to its own FHIR service, backed by its own database.**
> Pointing two shards at the same service is not slower-but-correct. It is silently wrong.

The reason is that a FHIR service admits at most one active reindex job at a time, account wide. When a reindex
job is already running, `POST $reindex` does not fail - it returns the *existing* job
(`CreateReindexRequestHandler.HandleAsync`, which calls `IFhirOperationDataStore.CheckActiveReindexJobsAsync`).
Two shards sharing a service would therefore silently observe each other's job and assert against it. The E2E
reindex tests also delete all resources of several types, and cancel any running reindex job, during their
per-test initialization, which would destroy a sibling shard's state.

The same class of problem applies to any shared, account-wide state: search parameter registration, custom search
parameter status, and job queues are all per service, not per test.

So sharding is only usable once the pipeline provisions N independent services and databases. Until then, leave
the environment variables unset and the test run behaves exactly as it always has.

## Enabling sharding

Sharding applies only to assemblies marked with `[assembly: EnableTestSharding]`. This opt in matters because CI
jobs usually set environment variables for a whole job, and a job often runs several test assemblies; only
assemblies that have been reviewed for shard safety should ever be partitioned.

Set both variables on each shard process:

| Variable | Meaning |
| --- | --- |
| `MicrosoftHealthTestShardIndex` | Zero based index of the shard this process runs. |
| `MicrosoftHealthTestShardCount` | Total number of shards the assembly is split into. |

A run with `MicrosoftHealthTestShardCount=3` needs three processes, with indexes `0`, `1` and `2`. Each process
also needs:

- its own FHIR service endpoint environment variables, because the endpoint is resolved once per process
  (`TestFhirServerFactory.GetEnvironmentUrl`);
- its own results directory / TRX path, so shards do not overwrite each other's results.

Leaving both variables unset, or setting `MicrosoftHealthTestShardCount=1`, runs every test, which is the
existing behavior.

### Invalid configuration fails the run

Setting only one of the two variables, a count below 1, an index outside `[0, count)`, or a non integer value
throws `TestShardConfigurationException` while the test framework is creating its discoverer. The run fails with
a non-zero exit code. This is deliberate: a misconfigured shard must not look like a successful run of zero
tests.

## What gets partitioned

The unit of partitioning is the **test method**. A method is assigned to a shard by a stable hash
(FNV-1a, 64 bit) of its declaring type's full name plus the method name. `string.GetHashCode` is deliberately not
used, because it is randomized per process in .NET Core and every shard must agree on the same partition.

Consequences:

- **All theory rows of a method stay in the same shard.** The decision is made before theory rows are enumerated,
  so it does not depend on theory pre-enumeration settings or on test case ID formats.
- **All fixture argument set variants of a method stay in the same shard.** The hash uses the real declaring type,
  not the synthetic `Namespace.Class(StoreA, FormatA)` variant name.
- **The partition is identical in every process and on every machine**, and identical across each FHIR version's
  copy of a shared test class, because it depends only on type and method names.
- **The partition is exhaustive and disjoint.** Every method belongs to exactly one shard, so running all shards
  runs every test exactly once.

### Limitations

- **Shard balance is not guaranteed.** Methods are hashed, not scheduled, so shards can receive unequal numbers of
  tests, and test methods differ wildly in duration. Sharding creates the *opportunity* for a shorter wall clock;
  it does not promise one, and no speedup has been measured against a live pipeline.
- **Sharding ignores xUnit collections.** Two classes in the same `[CollectionDefinition]`, even one marked
  `DisableParallelization = true`, can be assigned to different shards and therefore run at the same time in
  different processes. That is safe only because each shard has its own service; it is not safe if shards share
  one.
- **Renaming a test method can move it to a different shard.** This is harmless, because shards are always run
  together.

## Verifying a change

`Microsoft.Health.Extensions.Xunit.UnitTests` proves the behavior through the real xUnit discoverer and the real
runner, not through mocks: it launches the sample assembly
`Microsoft.Health.Extensions.Xunit.TestAssets` as a child process with the environment variables set, and asserts
that the shards' discovered test sets are exhaustive, pairwise disjoint, and never split a method's variants or
theory rows. The sample assembly is deliberately excluded from `Microsoft.Health.Fhir.sln` so that its discovery
fixtures are never collected by a solution wide test run.
