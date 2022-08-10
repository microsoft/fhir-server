# Connecting to External Terminology Service on Azure

In order to run a terminology service, you must ensure that the correct version of the FHIR [definitions folder](http://hl7.org/fhir/R4/downloads.html "definitions folder") is loaded into your FHIR server. The Definitions should be loaded already with the Azure FHIR OSS repository, but may not be up to date and may have to be updated manually (Make sure definition version matches FHIR version). Data may have to be cleaned up as stated further down below.

## Settings Up Configuration Settings

[Here](https://confluence.hl7.org/display/FHIR/Public+Test+Servers) is a list of public test servers, some of which have terminology functionality.

**Developer**

As of date, there is no Microsoft Terminology Service; thus, we need to connect the FHIR server to an external terminology service. To do so, visit the [`appsettings.json`](https://github.com/microsoft/fhir-server/blob/feature/terminologyservice/src/Microsoft.Health.Fhir.Shared.Web/appsettings.json) file in the FHIR server repo and configure an external terminology service by adding an endpoint for ExternalTerminologyService and ProfileValidationTerminologyService.

You may also need to enable each terminology operation in the `appsettings.json` file.

**User**

Open your FHIR Server resource through [Azure Portal](https://ms.portal.azure.com/#home) and under 'Settings' click on 'Configuration.'

From there, find 'FhirServer__Operations__Terminology__ExternalTerminologyServer' and set to equal your terminology server endpoint.

Do the same for 'FhirServer__Operations__Terminology__ProfileValidationTerminologyServer.'

From there you can choose which terminology operations you want to enable through 'FhirServer__Operations__Terminology__ValidateCodeEnabled,' 'FhirServer__Operations__Terminology__LookupEnabled,' and 'FhirServer__Operations__Terminology__ExpandEnabled' by setting the value to true.


## $validate FHIR resource
[FHIR Documentation on validate](https://www.hl7.org/fhir/validation.html)

[Microsoft Documentation on validate](https://docs.microsoft.com/en-us/azure/healthcare-apis/fhir/validation-against-profiles)


To validate a FHIR resource, you must provide a profile that you can validate the resource against. 

Specifically, if you are looking to validate against US Core Profiles, you should load those specific profiles to your FHIR server. You can find these profiles on the FHIR specification [here](http://hl7.org/fhir/us/core/history.html). To save storage, you can also just load the profiles that you are trying to validate against instead of loading all US Core profiles. Data may have to be cleaned and bundled up as stated further down below.

Then, run a `POST` request to validate the FHIR resource with the following command against your FHIR server: `https://{LocalHost}/{ResourceType}/$validate?profile={USCoreProfile}` (Profile paramater is optional). The body of the request must contain the resource you are trying to validate.

## $validate-code 
[FHIR Documentation on validate-code for ValueSet](https://www.hl7.org/fhir/valueset-operation-validate-code.html)

[FHIR Documentation on validate-code for CodeSystem](https://www.hl7.org/fhir/codesystem-operation-validate-code.html)

There are currently two ways to validate a code that is in a ValueSet or CodeSystem.

You can use a `GET` request to validate a code that is part of a ValueSet or CodeSystem that is already loaded into a FHIR server: `https://{LocalHost}/{ValueSet or CodeSystem}/{ResourceID}/$validate-code?system={system}&code={code}&display={optionalDisplay}`

If the ValueSet or CodeSystem is not in your FHIR server, then you can use a `POST` request to validate the code: `https://{LocalHost}/{ValueSet or CodeSystem}/$validate-code` and the body of the request must contain a parameters resource with a code/coding along with a ValueSet or CodeSystem.

## $expand
[FHIR Documentation on expand](https://www.hl7.org/fhir/valueset-operation-expand.html)

To get the expansion of a ValueSet, you must have a ValueSet in your FHIR server or you must provide the URL for the ValueSet to be accessed. 

In the case that the ValueSet is already in the FHIR server you can use the following `GET` request:
`https://{LocalHost}/ValueSet/{ResourceID}/$expand`

If you wish to expand a ValueSet using a canonical URL, you must provide it in the URL parameter.
`https://{LocalHost}/ValueSet/$expand?url={canonical ULR}`

You can also use a `POST` request to expand a ValueSet where the body is a ValueSet resource:
`https://{LocalHost}/ValueSet/$expand?`

These requests can have "offset" and "count" as optional query parameters.

## $lookup
[FHIR Documentation on lookup](https://www.hl7.org/fhir/codesystem-operation-lookup.html)

If looking up a code using a `GET` request, you must provide the system and code in the query parameters:
`https://{LocalHost}/CodeSystem/$lookup?system={system}&code={code}`

You can also use a `POST` request to look up a code by providing a parameters resource that contains a coding in the body of the request:
`https://{LocalHost}/CodeSystem/$lookup`

## Data Clean up

Some of the FHIR resources downloaded from the FHIR specifications and US-Core Profile may require some clean up. It is suggested to clean the div element of the FHIR resources.

Regex can be helpful here: 

find     - "div": `<div (.*)>(.*)</div>`

replace  - "div": `<div>PlaceHolder</div>`

## Bundling US-Core Profiles
'''

        using Hl7.Fhir.Model;

        using Hl7.Fhir.Serialization;

        using System.Runtime.CompilerServices;


        var parser = new FhirJsonParser();

        var bundle = new Bundle();
        bundle.Type = Bundle.BundleType.Batch;

        foreach (var file in Directory.GetFiles(@"Location of US-Core Profiles", "*.json"))
        {
            var fileName = Path.GetFileName(file);
            var resourceType = fileName.Split('-')[0];
            Resource resource = null;
            string url = null;
            switch (resourceType)
            {
                case "SearchParameter":
                    resource = parser.Parse<SearchParameter>(File.ReadAllText(file));
                    url = ((SearchParameter)resource).Url;
                    break;
                case "StructureDefinition":
                    resource = parser.Parse<StructureDefinition>(File.ReadAllText(file));
                    url = ((StructureDefinition)resource).Url;
                    break;
                case "ValueSet":
                    resource = parser.Parse<ValueSet>(File.ReadAllText(file));
                    url = ((ValueSet)resource).Url;
                    break;
                default:
                    break;
            }

            if (resource != null)
            {
                bundle.Entry.Add(Utils.CreateBundleEntry(resource, url));
            }
        }

        var serializer = new FhirJsonSerializer();

        File.WriteAllText(@"outputFile.json", serializer.SerializeToString(bundle));

        return 0;

        public class Utils
        {
            public static Bundle.EntryComponent CreateBundleEntry(Resource resource, string url)
            {
                Bundle.EntryComponent entry = new Bundle.EntryComponent();
                entry.Request = new Bundle.RequestComponent() { Method = Bundle.HTTPVerb.POST, Url = resource.TypeName };
                entry.FullUrl = url;
                entry.Resource = resource;
                return entry;
            }
        }
'''
