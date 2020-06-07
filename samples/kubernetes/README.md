# Running Microsoft FHIR server for Azure in Kubernetes

The Microsoft FHIR server for Azure can be deployed in a Kubernetes cluster. This document describes how to deploy and configure [Azure Kubernetes Service (AKS)](https://azure.microsoft.com/services/kubernetes-service/). Specifically, it describes how to install [Azure Service Operator](https://github.com/Azure/azure-service-operator) in the cluster to allow easy deployment of managed databases (Azure SQL or Cosmos DB). The repo contains a [helm](https://helm.sh) chart that leverages the Azure Service Operator to deploy and configure both FHIR service and backend database. 

## Deploy and configure cluster