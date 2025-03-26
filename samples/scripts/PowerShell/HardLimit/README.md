# Hard Limit Testing with Azure Health Data Services
 
This guide provides instructions to test hard limits in Azure Health Data Services using FHIR services and Private Link. The testing includes deploying and validating the infrastructure, configuring events, and testing endpoint accessibility.
 
## Prerequisites Needed
 
1. **An Azure Account**
    * You must have an active Azure account. If you don't have one, you can sign up [here](https://azure.microsoft.com/free/).
 
2. **Azure PowerShell**
    * Ensure Azure PowerShell is installed and configured. Installation instructions can be found [here](https://docs.microsoft.com/powershell/azure/).
 
3. **Azure CLI (Optional)**
    * Azure CLI can be used for additional debugging. Installation instructions are available [here](https://docs.microsoft.com/cli/azure/).
    
4. **Access to an Azure Health Data Services Workspace**
    * An existing workspace is required. If you don't have one, follow [this guide](https://docs.microsoft.com/azure/healthcare-apis/fhir/) to create a workspace.
 
## 1. Overview
 
The testing setup includes deploying FHIR services and Event Hubs, configuring Private Link, and validating connectivity. By testing hard limits, you ensure that the infrastructure can handle large-scale workloads and meet stringent performance requirements.
 
### Deployed Components
 
1. **FHIR Services**
    * Deploys up to 80 FHIR services to test scalability.
 
2. **Event Hub**
    * Used for monitoring and handling events from FHIR services.
 
3. **Private Endpoint**
    * A secure connection to restrict access to the FHIR services within a Virtual Network (VNet).
 
4. **Private DNS Zone**
    * Provides DNS resolution for Private Link endpoints within the VNet.
 
## 2. Deploy required Azure Resources to configure Events and Private link
 
### Deploy Using ARM Templates
 
1. Clone the repository:
    ```
    git clone https://github.com/microsoft/fhir-server.git
    ```
 
2. Log in to Azure:
    ```
    az login
    ```
 
3. Set the Azure Subscription:
    ```
    az account set --subscription <subscription_id>
    ```
    Replace [subscription_id] with the ID of the subscription you want to use for this deployment. You can find your subscription information by running az account list.

    **Note** : This step is particularly important if you have multiple subscriptions, as it ensures that the resources are deployed to the correct

4. Deploy the resources for events configuration:

    * Change to the **Events** directory:  
        ```
        cd Events
        ```
        <details>
        <summary>Click to expand to see deployment instructions.</summary>
        <br/>

        * To deploy the required resources for Events configuration, use the following command:
            ```
            az deployment group create --resource-group <resource_group_name> --template-file <template_file> --parameters <parameters_file>
            ```
            
            ### Parameters
            
            | Parameter | Description | Example Value |
            |-----------|-------------|---------------|
            | prefix | Unique prefix for naming resources | "test" |
            | fhirServiceCount | Number of FHIR services to deploy | 80 |
            | eventHubNamespaceName | Name of the Event Hub namespace | "myeventhubns" |
            | eventHubName | Name of the Event Hub instance | "myeventhub" |
        
        </details>

5. Deploy the resources for Private link configuration:
    * Change to the **PrivateLink** directory:  
        ```
        cd PrivateLink
        ```
        <details>
        <summary>Click to expand to see deployment instructions.</summary>
        <br/>

        *  To deploy the required resources for Private Link configuration, use the following command:
            ```
            az deployment group create --resource-group <resource_group_name> --template-file <template_file> --parameters <parameters_file>
            ```
            
            ### Parameters
            
            | Parameter | Description | Example Value |
            |-----------|-------------|---------------|
            | workspaceName | Name of the Azure Health Data Services workspace. | "testworkspace" |
            | location | Azure region where the resources will be deployed. | "eastus" |
            | virtualNetworkName | Name of the Virtual Network to use for the Private Endpoint. | "myvnet" |

        </details>

## 3. Configure and validate Events and Private link
 
### Step 1: Configure Events
* Change to the **Events** directory:  
    ```
    cd Events
    ```
* Run EnableEvents.ps1 to configure Event Hub integration with deployed FHIR services:
    ```
    .\EnableEvents.ps1
    ```
 
### Step 2: Validate Events
* Use ValidateEvents.ps1 to validate event configurations:
    ```
    .\ValidateEvents.ps1
    ```

## 4. Configure and validate Private link
  
### Step 1: Enable Private Link
* Change to the **PrivateLink** directory:  
    ```
    cd PrivateLink
    ```
* Set up Private Link connectivity using EnablePrivateLink.ps1:
    ```
    .\EnablePrivateLink.ps1
    ```
 
### Step 2: Validate Private Link
* Test connectivity and verify access restrictions with ValidatePrivateLink.ps1:
    ```
    .\ValidatePrivateLink.ps1
    ```
## 5. Troubleshooting
 
* Ensure the Virtual Machine is in the same VNet as the Private Endpoint.
* Validate that NSG and firewall rules allow traffic within the VNet.