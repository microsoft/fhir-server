```mermaid
sequenceDiagram
    Client->>FhirServer: GET Resource
    FhirServer->>Middleware: GET Resource
    Middleware->>FhirController: GET Resource
    FhirController->>Medino: Get Resource
    Medino->>GetResourceHandler: Resource Key
    GetResourceHandler->>AuthorizationService: Check Access
    AuthorizationService-->>GetResourceHandler: Access
    GetResourceHandler->>FhirDataStore: Resource Key
    FhirDataStore-->>GetResourceHandler: ResourceWrapper
    GetResourceHandler-->>Medino: GetResourceResponse
    Medino-->>FhirController: RawResourceElement
    FhirController-->>Middleware: FhirResult
    Middleware-->>FhirServer: FhirResult
    FhirServer-->>Client: FhirResult
```