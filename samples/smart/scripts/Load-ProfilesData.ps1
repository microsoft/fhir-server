$FHIR_URL = "https://workspace-fhirdata.fhir.azurehealthcareapis.com"

git clone https://github.com/microsoft/fhir-loader.git
cd fhir-loader

git checkout fhir-loader-cli
git pull

cd .\src\FhirLoader.Tool\
dotnet pack

# Uninstall if already installed
# dotnet tool uninstall FhirLoader.Tool --global
dotnet tool install --global --add-source .\nupkg\ FhirLoader.Tool

# Load sample data
microsoft-fhir-loader --blob https://ahdssampledata.blob.core.windows.net/fhir/uscore-testing-data-09-17-2022/ --fhir $FHIR_URL

# Download US Core
cd $HOME/Downloads
npm --registry https://packages.simplifier.net install hl7.fhir.us.core@3.1.1

# Load us core
microsoft-fhir-loader --package $HOME\Downloads\node_modules\hl7.fhir.us.core\ --fhir $FHIR_URL