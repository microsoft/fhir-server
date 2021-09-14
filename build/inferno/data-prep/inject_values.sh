#!/bin/bash

pwd

echo "Injecting values to the different tests configurations"

echo "the patients are $PATIENT_IDS"
jq '. |= (.server = "'$FHIR_ENDPOINT_INSIDE_DOCKER'" | .arguments.patient_ids = "'$PATIENT_IDS'")' build/inferno/cli/inferno.onc-program-us-core.json > build/inferno/inferno-program/batch/inferno.onc-program-us-core.json
jq '. |= (.server = "'$FHIR_ENDPOINT_INSIDE_DOCKER'" | .arguments.onc_sl_url ="'$URL'" | .arguments.onc_sl_client_id ="'$CLIENT_ID'" | .arguments.onc_sl_client_secret ="'$CLIENT_SECRET'" | .arguments.onc_sl_oauth_token_endpoint ="'$OAUTH_TOKEN_ENDPOINT'" | .arguments.id_token ="'$ID_TOKEN'" | .arguments.refresh_token ="'$REFRESH_TOKEN'" )' build/inferno/cli/inferno.onc-program-full-pat-access.json > build/inferno/inferno-program/batch/inferno.onc-program-full-pat-access.json
jq '. |= (.server = "'$FHIR_ENDPOINT_INSIDE_DOCKER'" | .arguments.bulk_url ="'$BULK_URL'" | .arguments.bulk_token_endpoint ="'$BULK_TOKEN_ENDPOINT'" | .arguments.bulk_client_id ="'$BULK_CLIENT_ID'" )' build/inferno/cli/inferno.onc-program-multi-pat-auth-api.json > build/inferno/inferno-program/batch/inferno.onc-program-multi-pat-auth-api.json
jq '. |= (.server = "'$FHIR_ENDPOINT_INSIDE_DOCKER'" | .arguments.oauth_authorize_endpoint ="'$OAUTH_AUTHORIZE_ENDPOINT'" | .arguments.oauth_token_endpoint ="'$OAUTH_TOKEN_ENDPOINT'" | .arguments.oauth_register_endpoint ="'$OAUTH_REGISTER_ENDPOINT'" )' build/inferno/cli/inferno.onc-program-other.json > build/inferno/inferno-program/batch/inferno.onc-program-other.json
jq '. |= (.server = "'$FHIR_ENDPOINT_INSIDE_DOCKER'" | .arguments.url ="'$URL'" | .arguments.client_id ="'$CLIENT_ID'" | .arguments.client_secret ="'$CLIENT_SECRET'" | .arguments.id_token ="'$ID_TOKEN'" | .arguments.refresh_token ="'$REFRESH_TOKEN'")' build/inferno/cli/inferno.onc-program-standalone-ehr-prac-app.json > build/inferno/inferno-program/batch/inferno.onc-program-standalone-ehr-prac-app.json
jq '. |= (.server = "'$FHIR_ENDPOINT_INSIDE_DOCKER'" | .arguments.onc_sl_url ="'$URL'" | .arguments.oauth_authorize_endpoint = "'$OAUTH_AUTHORIZE_ENDPOINT'" | .arguments.oauth_token_endpoint = "'$OAUTH_TOKEN_ENDPOINT'" | .arguments.onc_sl_client_id ="'$CLIENT_ID'" | .arguments.onc_sl_client_secret ="'$CLIENT_SECRET'" )' build/inferno/cli/inferno.onc-program-standalone-pat-app-lim-access.json > build/inferno/inferno-program/batch/inferno.onc-program-standalone-pat-app-lim-access.json

echo "Values injection done."