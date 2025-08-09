# ADR 2508: Runtime Thread Adaptation for Long-Running FHIR Operations

## Context

The FHIR server performs various long-running operations such as exports, imports, and bulk updates that can execute for hours or even days depending on data volume. These operations traditionally use static thread configurations that are determined at application startup and remain fixed throughout the operation lifecycle.

### Current Challenges

1. **Static Thread Allocation**: Thread counts are calculated once at operation start and never adjusted, even as system conditions change during execution.

2. **Long-Running Operation Inefficiency**: Export operations processing millions of resources may run for hours with suboptimal thread counts if system resources change during execution.

3. **Dynamic Environment Adaptation**: In cloud environments with auto-scaling, container orchestration, or shared resource pools, optimal thread counts can vary significantly during operation execution.

4. **Resource Contention**: System load from other processes or concurrent operations may require thread count adjustments to maintain system stability.

5. **No Runtime Visibility**: Operators have limited insight into how thread allocation decisions are made during long-running operations.

### Technical Requirements

- Maintain backward compatibility with existing static threading configurations
- Provide runtime thread count recalculation capabilities for long-running operations
- Enable monitoring and logging of thread adaptation decisions
- Ensure graceful handling of thread count changes during active parallel execution
- Support both adaptive and static threading modes based on configuration

## Decision

We will implement a **DynamicThreadPoolManager** component that provides runtime thread adaptation capabilities for long-running FHIR operations.

### Core Implementation

1. **IDynamicThreadPoolManager Interface**: A new service interface that manages adaptive parallel execution with periodic thread count recalculation.

2. **Runtime Recalculation**: Thread counts will be recalculated periodically during operation execution (default every 30 seconds) based on current system conditions.

3. **Hybrid Threading Mode Support**: 
   - **Adaptive Mode**: When configuration value is 0 or negative, use runtime thread adaptation
   - **Static Mode**: When configuration value is positive, use traditional fixed threading

4. **Integration with Existing Services**: The DynamicThreadPoolManager will integrate with existing IDynamicThreadingService and IRuntimeResourceMonitor components.

5. **Operation-Specific Implementation**: Initial implementation will focus on export operations with the capability to extend to import and bulk update operations.

### Export Operation Integration

```csharp
// In SqlExportOrchestratorJob
private async Task<bool> ExecuteWithRuntimeAdaptationAsync(...)
{
    await _dynamicThreadPoolManager.ExecuteAdaptiveParallelAsync(
        resourceTypes,
        async (resourceType, ct) => await ProcessResourceTypeAsync(resourceType, ...),
        OperationType.Export,
        TimeSpan.FromSeconds(30),  // Recalculation interval
        cancellationToken);
}
```

### Configuration Approach

- **CoordinatorMaxDegreeOfParallelization = 0**: Enable adaptive threading with runtime recalculation
- **CoordinatorMaxDegreeOfParallelization > 0**: Use static threading (existing behavior)

## Status

**Accepted** - Implementation completed August 8, 2025

## Consequences

### Positive Outcomes

1. **Improved Long-Running Operation Performance**: Operations can adapt to changing system conditions, potentially improving completion times by 10-30% for multi-hour operations.

2. **Better Resource Utilization**: Thread counts can increase when resources become available or decrease when system is under pressure.

3. **Cloud-Native Optimization**: Automatic adaptation to container scaling events, VM resizing, or changing resource limits.

4. **Enhanced Monitoring**: Runtime thread adaptation events provide valuable insights for performance tuning and capacity planning.

5. **Backward Compatibility**: Existing configurations continue to work unchanged, providing a smooth migration path.

6. **Operational Flexibility**: Operators can choose between static and adaptive threading based on their specific environment needs.

### Potential Challenges

1. **Increased Complexity**: Additional service component adds complexity to the threading architecture.

2. **Resource Monitoring Overhead**: Periodic recalculation requires ongoing system resource monitoring.

3. **Timing Considerations**: Thread count changes during active parallel execution may temporarily impact performance.

4. **Configuration Interpretation**: The dual meaning of configuration value 0 (adaptive) vs positive values (static) requires clear documentation.

5. **Testing Complexity**: Runtime adaptation behavior is more difficult to test than static configurations.

### Migration Impact

- **Zero Breaking Changes**: Existing deployments continue to work without modification
- **Opt-In Adoption**: New adaptive behavior is only enabled when configuration is explicitly set to 0
- **Monitoring Enhancement**: New log entries provide visibility into thread adaptation decisions

### Performance Characteristics

- **Memory Overhead**: Minimal additional memory usage for Timer and monitoring objects
- **CPU Overhead**: Periodic recalculation every 2 minutes has negligible CPU impact
- **Latency Impact**: Thread count changes are applied to subsequent parallel execution batches, not active threads

### Integration Points

1. **Export Operations**: Primary integration point for immediate benefit
2. **Import Operations**: Future integration for large-scale data imports
3. **Bulk Update Operations**: Potential future integration for bulk update scenarios
4. **Monitoring Systems**: Enhanced telemetry for operational visibility

### Future Evolution

This ADR establishes the foundation for more advanced threading adaptations:
- Machine learning-based thread optimization
- Predictive thread scaling based on operation patterns
- Integration with external monitoring and orchestration systems
- Custom adaptation policies for specific operation types

---

**ADR Number**: 2508  
**Created**: August 8, 2025  
**Authors**: Development Team  
**Related Components**: Microsoft.Health.Fhir.Core.Features.Threading, SqlExportOrchestratorJob  
**Supersedes**: None  
**Superseded By**: None
