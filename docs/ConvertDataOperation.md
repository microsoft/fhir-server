# Convert Data Operation
The ```$convert-data``` operation allows data conversion from legacy formats (currently supports **Hl7v2** only) to FHIR format. This feature is currently turned off by default. To enable the feature, update the `FhirServer:Operations:ConvertData:Enabled` setting to be true.

## Conversion Template Collection
The convert data operation is based on the [FHIR Converter Project](https://github.com/microsoft/FHIR-Converter/tree/dotliquid).
To make a data conversion call, you need a template collection to define the mappings between different data formats.

We have a built-in template collection (hl7v2 to FHIR R4), which is a copy from the [FHIR Converter project](https://github.com/microsoft/FHIR-Converter/releases/tag/v3.0). Currently the version of templates is ```v3.0``` and we will periodically update to the latest stable release in the future.

To convert with the built-in template collection, you can directly use the built-in template collection reference ```microsofthealth/fhirconverter:default``` in the request payload.

If you want to refine or customize the output from the built-in template collection, you can also build your own template collections.
To convert with custom template collection, you should follow the following steps:
1. Get a copy of the [default template collection](https://github.com/microsoft/FHIR-Converter/releases/tag/v3.0) from the FHIR Converter project and customize it.
2. Compress the templates and push to your ACR. You can use a [commandline tool](https://github.com/microsoft/FHIR-Converter/releases/tag/v3.0) to help upload your templates.
3. Register the ACR server to your FHIR service configuration by setting `FhirServer:Operations:ConvertData:ContainerRegistryServers:0` to your ACR login server. You can register multiple ACRs by adding more entries in the configuration as the following picture shows.  
![acr-registration](./images/convert-data/acr-registration.png)
4. Give your FHIR service the `AcrPull` permission for the ACR you have registered. 
![acr-rbac](./images/convert-data/acr-rbac.png)
5. Now you are able to use your custom template collection in the format of `<RegistryServer>/<imageName>:<imageTag>` `<RegistryServer>/<imageName>@<imageDigest>`. 
Both image tag and digest are supported in the reference string, but we strongly recommend to choose digest because it's immutable and stable. 

## Request format
The request payload is passed as [Parameters](http://hl7.org/fhir/parameters.html) resource to the ```$convert-data``` api.
To convert Hl7v2 data to FHIR, you need to pass four parameters:
1. *inputData*: raw string content of the input data.
2. *inputDataType*: data type of your input, currently only accepts *Hl7v2*.
3. *templateCollectionReference*: reference string of your template collection, can be either the built-in templates ```microsofthealth/fhirconverter:default``` or your custom image reference.
4. *rootTemplate*: the root template to process (render) the
input data.

Here is a sample request to convert data:
```json
{
    "resourceType": "Parameters",
    "parameter": [
        {
            "name": "inputData",
            "valueString": "MSH|^~\\&|SIMHOSP|SFAC|RAPP|RFAC|20200508131015||ADT^A01|517|T|2.3|||AL||44|ASCII\nEVN|A01|20200508131015|||C005^Whittingham^Sylvia^^^Dr^^^DRNBR^PRSNL^^^ORGDR|\nPID|1|3735064194^^^SIMULATOR MRN^MRN|3735064194^^^SIMULATOR MRN^MRN~2021051528^^^NHSNBR^NHSNMBR||Kinmonth^Joanna^Chelsea^^Ms^^CURRENT||19870624000000|F|||89 Transaction House^Handmaiden Street^Wembley^^FV75 4GJ^GBR^HOME||020 3614 5541^HOME|||||||||C^White - Other^^^||||||||\nPD1|||FAMILY PRACTICE^^12345|\nPV1|1|I|OtherWard^MainRoom^Bed 183^Simulated Hospital^^BED^Main Building^4|28b|||C005^Whittingham^Sylvia^^^Dr^^^DRNBR^PRSNL^^^ORGDR|||CAR|||||||||16094728916771313876^^^^visitid||||||||||||||||||||||ARRIVED|||20200508131015||"
        },
        {
            "name": "inputDataType",
            "valueString": "Hl7v2"
        },
        {
            "name": "templateCollectionReference",
            "valueString": "microsofthealth/fhirconverter:default"
        },
        {
            "name": "rootTemplate",
            "valueString": "ADT_A01"
        }
    ]
}
```
And the expected response with the built-in template collection. Note the response content type is `text/plain` because the output format is determined by the mapping definition from your templates.
```
{
  "resourceType": "Bundle",
  "type": "transaction",
  "entry": [
    {
      "fullUrl": "urn:uuid:9d697ec3-48c3-3e17-db6a-29a1765e22c6",
      "resource": {
        "resourceType": "Patient",
        "id": "9d697ec3-48c3-3e17-db6a-29a1765e22c6",
        "identifier": [
          {
            "value": "3735064194"
          },
          {
            "value": "3735064194"
          },
          {
            "value": "2021051528"
          }
        ],
        "name": [
          {
            "family": "Kinmonth",
            "given": [
              "Joanna",
              "Chelsea"
            ],
            "prefix": [
              "Ms"
            ]
          }
        ],
        "birthDate": "1987-06-24",
        "gender": "female",
        "address": [
          {
            "line": [
              "89 Transaction House",
              "Handmaiden Street"
            ],
            "city": "Wembley",
            "postalCode": "FV75 4GJ",
            "country": "GBR"
          }
        ],
        "telecom": [
          {
            "value": "020 3614 5541",
            "use": "home"
          }
        ]
      },
      "request": {
        "method": "PUT",
        "url": "Patient/9d697ec3-48c3-3e17-db6a-29a1765e22c6"
      }
    },
    {
      "fullUrl": "urn:uuid:7f6c5e84-04ee-8b73-e018-d66073b40c3e",
      "resource": {
        "resourceType": "Encounter",
        "id": "7f6c5e84-04ee-8b73-e018-d66073b40c3e",
        "class": {
          "code": "IMP",
          "display": "inpatient encounter",
          "system": "http://terminology.hl7.org/CodeSystem/v3-ActCode"
        },
        "status": "unknown",
        "location": [
          {
            "location": {
              "reference": "Location/"
            },
            "status": "active"
          }
        ],
        "participant": [
          {
            "type": [
              {
                "coding": [
                  {
                    "code": "ATND",
                    "system": "http://terminology.hl7.org/CodeSystem/v3-ParticipationType",
                    "display": "attender"
                  }
                ]
              }
            ]
          }
        ],
        "serviceType": {
          "coding": [
            {
              "code": "CAR"
            }
          ]
        },
        "identifier": [
          {
            "value": "16094728916771313876",
            "type": {
              "coding": [
                {
                  "system": "http://terminology.hl7.org/CodeSystem/v2-0203"
                }
              ],
              "text": "visit number"
            }
          }
        ],
        "period": {
          "start": "2020-05-08T13:10:15Z"
        },
        "subject": {
          "reference": "Patient/9d697ec3-48c3-3e17-db6a-29a1765e22c6"
        }
      },
      "request": {
        "method": "PUT",
        "url": "Encounter/7f6c5e84-04ee-8b73-e018-d66073b40c3e"
      }
    },
    {
      "fullUrl": "urn:uuid:50becdb5-ff56-56c6-40a1-6d554dca80f0",
      "resource": {
        "resourceType": "Location",
        "id": "50becdb5-ff56-56c6-40a1-6d554dca80f0",
        "mode": "instance",
        "physicalType": {
          "coding": [
            {
              "system": "http://terminology.hl7.org/CodeSystem/location-physical-type"
            }
          ]
        }
      },
      "request": {
        "method": "PUT",
        "url": "Location/50becdb5-ff56-56c6-40a1-6d554dca80f0"
      }
    }
  ]
}
```