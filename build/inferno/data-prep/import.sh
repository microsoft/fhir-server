#!/bin/bash

# This script will prepare data in a FHIR server that will be used to run Inferno tests.
# Required environment variables:
#   FHIR_ENDPOINT_FROM_HOST="https://something.azurehealthcareapis.com" or when running fhir api inside ci "http://localhost:8080"
#   FHIR_ENDPOINT_INSIDE_DOCKER="http://host.docker.internal:8080" when running both fhir api and inferno inside ci with two separate docker-compose files, there are two separate networks and we want to route from host network

set -o nounset
set -o errexit

#rm -fr output || true

# datasets location: https://github.com/inferno-community/uscore-data-sets
# docs on process that creates them: https://github.com/inferno-community/uscore-data-script
DATASET_URL="https://github.com/inferno-community/uscore-data-sets/raw/master/uscore-testing-data-12-07-2020.zip"

if [[ ! -f ./dataset.zip ]]
then
    wget ${DATASET_URL} -O dataset.zip
fi

if [[ ! -d ./output ]]
then
    unzip dataset.zip
fi
# We need to make changes to the bundle files:
# 1. MS-FHIR backed by Cosmos doesn't support transactions so we need to move to batch.
# 2. To keep all references intact we need to create resources with their ORIGINAL Ids. For that we should create via PUT.
# 3. In addition to PUT, request.url for each resource should include the intended Id
# 4. we concatinate a comma separated string with inserted patient ids, we'll need it later for test config file

PATIENT_IDS=""

for filename in output/data/*.json; do
    echo "Importing file: ${filename}"
    RESOURCE_TYPE=$(jq -r '.resourceType' ${filename})

    if [[ "${RESOURCE_TYPE}" == "Bundle" ]]; then
        PATIENT_ID=$(basename $filename .json)
        if [[ -z "$PATIENT_IDS" ]]; then 
            PATIENT_IDS=$PATIENT_ID
        else
            PATIENT_IDS="${PATIENT_IDS},${PATIENT_ID}"
        fi

        jq '. |= (.type = "batch" | .entry[].request.method = "PUT" ) | reduce range(0, .entry | length) as $d (.; (.entry[$d].request.url+="/"+.entry[$d].resource.id))' ${filename} > ${filename}.put

        # We need references to follow "resourceType/id" (although fhir should also support urn:guid, but doesn't work)
        SED_EXP=$(jq '.entry[] | "s@reference\": \"" + .fullUrl + "@reference\": \"" + .request.url + "@g;"' -r ${filename}.put)
        sed "${SED_EXP}" ${filename}.put > ${filename}.ref
        curl ${FHIR_ENDPOINT_FROM_HOST} -X POST -H "Content-Type: application/fhir+json" --data-binary @${filename}.ref
    else
        RESOURCE_ID=$(jq -r '.id' ${filename})
        curl ${FHIR_ENDPOINT_FROM_HOST}/${RESOURCE_TYPE}/${RESOURCE_ID} -X PUT -H "Content-Type: application/fhir+json" --data-binary @${filename}
    fi
done

# The following statement exports the patient ids so it will be available for the next step
echo "##vso[task.setvariable variable=PATIENT_IDS]$PATIENT_IDS"
echo "Imported patient IDs are: $PATIENT_IDS"
echo "Import is done."