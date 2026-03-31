# Expired Resource Cleanup

## Context 
   Using the new search parameter for expired resources added as part of `adr-2603-non-spec-default-search-parameters`, we have the ability to identify expired resources. We need to decide how to handle the cleanup of these resources. The cleanup process should be automatic and efficient, ensuring that expired resources do not exist in the system for longer than allowed while also minimizing the impact on system performance.

## Decision
   An automated cleanup watchdog will be implemented to periodically check for expired resources and remove them from the system. The watchdog will run at a configurable interval (e.g., daily) and will identify expired resources using the new search parameter. It will use the existing bulk delete functionality to remove expired resources. This allows for code reuse and simplifies the implementation.

## Status
   Accepted

## Consequences
   Benefits:
   - Customers will not have to manually identify and delete expired resources, reducing administrative overhead and ensuring compliance with data retention policies.
   - Expired resources will be removed in a timely manner, improving system performance and reducing storage costs.
   
   Concerns:
   - The cleanup process may impact system performance, especially if there are a large number of expired resources to delete.
   - Automated cleanup may lead to unintended data loss if resources are incorrectly marked as expired. To mitigate this, we will implement safeguards such as logging and monitoring of the cleanup process. Additionally, the feature will be disabled by default and can be enabled by customers when they are ready to use it.
