```mermaid
sequenceDiagram
    Client->>FhirServer: POST Resource
    FhirServer->>Middleware:
    Middleware->>FhirController:
    FhirController->>Mediatr: Create Resource
    Mediatr->>CreateResourceHandler: CreateResourceRequest
    CreateResourceHandler->>AuthorizationService: Check Access
    AuthorizationService->>CreateResourceHandler:
    CreateResourceHandler->>ResourceReferenceResolver: Resolve references
    ResourceReferenceResolver->>CreateResourceHandler:
    CreateResourceHandler->>ResourceWrapperFactory: Create
    ResourceWrapperFactory->>SearchIndexer: Extract
    SearchIndexer->>ResourceWrapperFactory:
    ResourceWrapperFactory->>CreateResourceHandler:
    CreateResourceHandler->>FhirDataStore: Upsert
    FhirDataStore->>CreateResourceHandler:
    CreateResourceHandler->>Mediatr: UpsertResourceResponse
    Mediatr->>FhirController:
    FhirController->>Middleware:
    Middleware->>FhirServer:
    FhirServer->>Client: FhirResult
```