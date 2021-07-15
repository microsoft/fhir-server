# Inferno CLI

The Inferno project contains multiple tests and sequences, which can be run using the CLI if a matching json definition file is provided.
For example, for the Single Patient tests (aka us-core), we created [this file](../cli/inferno.onc-program-us-core.json).

## How to run the tests (locally)

These steps are written here for completness, however they are not needed if the pipeline is used, as it will be done automatically.

1. Update the arguments within the json file.
1. Clone the Inferno project locally
1. Navigate to the Inferno directory
1. Copy the json file to the 'batch' directory
1. run (docker desktop must be running)

    ```sh
    docker-compose run inferno bundle exec rake db:create db:migrate "inferno:execute_batch[./batch/inferno.onc-program-us-core.json]"
    ```
