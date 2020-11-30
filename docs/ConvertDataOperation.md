# Convert Data Operation
This feature allows data conversion from legacy formats (currently supports **Hl7v2** only) to FHIR format.The feature is currently turned off by default. To enable the feature, update the `FhirServer:Operations:ConvertData:Enabled` setting to be true.

## Conversion Template Collection
The convert data operation is based on the [FHIR Converter Project](https://github.com/microsoft/FHIR-Converter/tree/dotliquid).
To make a data conversion call, you need a template collection to define the mappings between different data formats.

We have a built-in template collection (hl7v2 to FHIR R4) which is a copy from the FHIR Converter project.
You can directly use the built-in template collection reference ```microsofthealth/fhirconverter:default``` in the request payload.

If you need to customize the templates, you can get a copy from the FHIR Converter project and customize it as you wish, then upload it to your ACR (Azure Container Registry). In this case, the template collection reference should be ```<RegistryServer>/<imageName>:<imageTag>```. Both image tags and digests are supported in the reference string.

In order to integrate the ACR to your FHIR instance, you need update `FhirServer:Operations:ConvertData:ContainerRegistries:0:RegistryServer` to your ACR login server. Besides, you need to give your App service the ```AcrPull``` permission for the ACR of your choice.

## Request format
The request payload is passed as [Parameters](http://hl7.org/fhir/parameters.html) resource to the convert data api.
To convert Hl7v2 data to FHIR, you need to pass four parameters:
1. *inputData*: raw string content of the input data.
2. *inputDataType*: data type of your input, currently only accepts *Hl7v2*.
3. *templateCollectionReference*: reference string of your template collection, can be either the built-in templates ```microsofthealth/fhirconverter:default``` or your custom image reference.
4. *entryPointTemplate*: the entry point template to process (render) the
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
            "name": "templateCollectionReference",
            "valueString": "microsofthealth/fhirconverter:default"
        },
        {
            "name": "entryPointTemplate",
            "valueString": "ADT_A01"
        },
        {
            "name": "inputDataType",
            "valueString": "Hl7V2"
        }
    ]
}
```
And the expected output with the built-in template collection:
```
{
  "resourceType": "Bundle",
  "type": "transaction",
  "entry": [
    {
      "fullUrl": "urn:uuid:b06a26a8-9cb6-ef2c-b4a7-3781a6f7f71a",
      "resource": {
        "resourceType": "Patient",
        "id": "b06a26a8-9cb6-ef2c-b4a7-3781a6f7f71a",
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
        "url": "Patient/b06a26a8-9cb6-ef2c-b4a7-3781a6f7f71a"
      }
    },
    {
      "fullUrl": "urn:uuid:c2882837-85d3-6a68-6bbd-4005636e3f26",
      "resource": {
        "resourceType": "Encounter",
        "id": "c2882837-85d3-6a68-6bbd-4005636e3f26",
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
          "reference": "Patient/b06a26a8-9cb6-ef2c-b4a7-3781a6f7f71a"
        }
      },
      "request": {
        "method": "PUT",
        "url": "Encounter/c2882837-85d3-6a68-6bbd-4005636e3f26"
      }
    },
    {
      "fullUrl": "urn:uuid:34db35cf-0015-6bb5-7d43-b248c3da7d70",
      "resource": {
        "resourceType": "Location",
        "id": "34db35cf-0015-6bb5-7d43-b248c3da7d70",
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
        "url": "Location/34db35cf-0015-6bb5-7d43-b248c3da7d70"
      }
    }
  ]
}
```