# Running in Kubernetes

```bash
az group create -n AzueFHIRServer -l uksouth
az aks create -n AzueFHIRServer -g Demo-AKSDigiTrial --kubernetes-version 1.14.8 --node-count 1 --vm-set-type VirtualMachineScaleSets  --node-vm-size Standard_F16s_v2
```

```bash
kubectl apply -f ./samples/kubernetes/k8s.yaml
```

### TODO

- persistant volume
- node sizing
- resource limits
-  secret