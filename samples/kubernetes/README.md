# Running in Kubernetes

*IMPORTANT:* This configuration is not suitable for production scenarios. This sample is provided as an example of how the Fhir Server project can be run on Kubernetes. Please note by default the SQL server connection is not encrypted and authentication on the FHIR Server has been disabled.

If you do not have a Kubernetes cluster and want to deploy Kubernetes on Azure documentation can be found here: [https://docs.microsoft.com/en-us/azure/aks/](https://docs.microsoft.com/en-us/azure/aks/)

## Optional: Deploying SQL Server on Kubernetes

The FHIR Server can be connected to a SQL server intance external to Kubernetes, for example Azure SQL Database. However if you wish to run SQL Server on Kubernetes follow the steps below:

### Create secret for SA password

Create a Kubernetes secret to store the SQL Server SA password, `<SA_PASSWORD>`:

```bash
kubectl create secret generic sql-settings --from-literal=password='<SA_PASSWORD>'
```

### Deploy SQL Server

The example SQL server deployment creates a Kubernetes deployment, load balancer of type `ClusterIP`, and a persistant volume claim is created with aa storage class of `volume.beta.kubernetes.io/storage-class: managed-premium`. If you are not running Azure Kubernetes Service you will need to adjust the storage class to one that exists on your cluster.

```bash
kubectl apply -f ./samples/kubernetes/sql.yaml
```

## Running the FHIR Server API Layer

### Build and push Docker image

Build the Docker image, replacing `<your-registry>` with the server name of your container registry.

```bash
docker build -f samples/docker/Dockerfile -t <your-registry>/azure-fhir-api .
```

Once built, push the image to a registry, again replace `<your-registry>` with the server name of your container registry.

```bash
docker push <your-registry>/azure-fhir-api .
```

### Create secret for SQL connection string

Create a Kubernetes secret to store the SQL server connection string. The example below shows the connection string in the format for when SQL server is deployed on Kubernetes, using the password `<SA_PASSWORD>` as used in the example above:

```bash
kubectl create secret generic fhir-api --from-literal=connection-string='Server=tcp:sql,1433;Initial Catalog=FHIR;Persist Security Info=False;User ID=sa;Password=<SA_PASSWORD>;MultipleActiveResultSets=False;Connection Timeout=30;'
```

### Deploy the FHIR Server API

Edit the file `./samples/kubernetes/azure-fhir-api.yaml` and replace `<your-registry>` with the server name of your container registry.

To deploy the FHIR API run the following command:

```bash
kubectl apply -f ./samples/kubernetes/azure-fhir-api.yaml
```

This will create a Kubernetes deployment and service of type `LoadBalancer`. To find out the wxternal ip address for the load balancer run:

```bash
kubectl get svc api
```

You should be able to access the FHIR server metadata endpoint using the address `http://<external-ip>/metadata` . Check database connectivity by querying a resource endpoitn, such as `http://<external-ip>/Patient?_summary=count`.
