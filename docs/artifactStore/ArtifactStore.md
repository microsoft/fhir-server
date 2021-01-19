An unified ACR artifact store that provides the objects used by de-id export (configurations), CDM export and other potential operations in the future.

[[_TOC_]]

# Business Justification

1. In recent months we released the de-id export, and we are planning to support exporting to CDM format. An unified ACR artifact store provides the configurations for those operations, make customer's experience consistent.
2. Support customer to use [Azure Container Registry](https://azure.microsoft.com/en-us/services/container-registry/) to store the anonymization configuration for de-id export. That would be more secruity and convenient for management.

# Scenarios

Some operations in fhir server need configuation to trigger or implement, customer can use Container Registry to store configurations or other artifacts. The customized configurations need to be pushed to Container Registry that registed in Artifact store, and for the PaaS solution, there will be default artifacts content for related operations.

1. Push artifacts to Registry via [oras](https://github.com/deislabs/oras/releases).
   
2. Register the Registry in the Artifact Store.

3. In operation parameters, customer uses image reference to specify where to load the related artifacts.

4. E.g. When we use Container Registry as the artifact store instance for de-id Export, the query parameter in request should be like ```GET [base]/$export?_anonymizationConfig=testacr.azurecr.io/configuration:default```.

Target public review, we would support [Azure Conatiner Registry](https://azure.microsoft.com/en-us/services/container-registry/) as the instances of artifact store. We will try to provide similar user experience when customer use atirafct store in different operations.

# Metrics

- Processing time.
- Artifacts size.
- Success/failure processing count. 
- Push/pull count.


# Design
1. We recommand to use [oras](https://github.com/deislabs/oras/releases) to push artifacts. Here is a [doc](https://docs.microsoft.com/en-us/azure/container-registry/container-registry-oci-artifacts) from Azure on how to push or pull an OCI artifact.
   1. While the operation may only need one configuration file, we recommand to push the entire folder to the registry as an image, so that the processes are unified. How to push artifacts with multiple files can be refer [here](https://github.com/deislabs/oras#pushing-artifacts-with-multiple-files).
   2. To push the folder "testFolder":
   ```cmd
   oras.exe push testacr.azurecr.io/configuration:1.0 --manifest-config NUL:application/vnd.unknown.config.v1+json ./testFolder/:application/vnd.acme.rocket.docs.layer.v1+tar
   ```
   3. When you already pushed your artifacts, you can check them on **Repositories** of target Container Registry. Here is an example of image manifest shown on Azure Container Registry. 
   ```json
      {
         "schemaVersion": 2,
         "config": {
            "mediaType": "application/vnd.unknown.config.v1+json",
            "digest": "sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            "size": 0
            },
            "layers": [
               {
                  "mediaType": "application/vnd.acme.rocket.docs.layer.v1+tar",
                  "digest": "sha256:8f8c2964b850e28f2f525c7cc1700cc5010a8e91ba54e2a12f6f731fe9feb08a",
                  "size": 2480,
                   "annotations": {
                      "io.deis.oras.content.digest": "sha256:4aacd688f6aa2e504ce1f08d490b6a3b515fd80de393f070a360e2009a8efd6b",
                      "io.deis.oras.content.unpack": "true",
                      "org.opencontainers.image.title": "testTemplateFolder"
                      }
               }
            ]
      }
   ```
   4. Also you can directly pull it via oras like below, but our target is to download artifacts inside the fhir server.
   ```cmd
   oras.exe pull testacr.azurecr.io/configuration:1.0 -a 
   ```

2. A new "Artifact store" section will be in the UI of FHIR API on Azure, its purpose is to facilitate users to register the Container Registry as artifact stores, all the artifact store instances need to be registered than can be connected.

3. We will define a "FetchAsync" interface for operations to pull the image as the artifacts from Container Registry, the image reference with tags or digests are both supported.
   - With image tag ```<registry>/<name>:<tag>```
   - With image digest ```<registry>/<name>@<digest>```
   - If no tags or digests, will pull image with default ```latest``` tag. 

4. Current API parameters of de-id export will not be changed, and if customer trigger de-id export in released way like ```GET [base]/$export?_anonymizationConfig=config-file.json```, we support storage in ACR artifact store, and still use destination blob storage account to store the configuration to grant the downword compatibility.

5. The size of maximal artifacts to be pushed/pulled need to be set for different operations. If larger artifacts provided, then 400 should be returned with error TooLargeArtifacts.

## Operations use artifact store work flow
![Anonymized export work flow](./asserts/flow.png)

## Multiple configuration support
In Container Registry, to make sure the artifact(s) is expected, user can set the image reference like ```<registry>/<name>@<digest>``` rather than ```<registry>/<name>:<tag>``` to specify the image version 
 
## OSS vs PaaS

Most change should be at OSS project. 

For PaaS solution, we want to add an "Artifact store" section in navigation bar.

## Error Handling

1. ArtifactStoreIsNotRegisted: when provided artifact store was not registed, then `400 Bad Request` with error code should be returned in operation result. 
2. FailedToConnectToArtifactStore: when cannot to connect the provided artifact store, then `400 Bad Request` with error code should be returned in operation result. 
3. TooLargeArtifacts: when provided artifact store was too large, then `400 Bad Request` with error code should be returned in operation result. 


# Test Strategy

- Unit tests and functional tests to make sure the artifact store is working correctly.
- Will update the E2E and intergration test for de-id export to make sure it's still working as expected.

# Security

- To protect original data, user use digest.


# Other

1. Converter API: Current behavior of Converter API will not be changed. But the settings will be re-organized, and "Conversation" section maybe will be replaced by "Artifact store".

