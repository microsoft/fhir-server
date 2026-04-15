# SQL on FHIR Demo App — HEDIS Controlling Blood Pressure (CBP)

## Overview
This Blazor Server app demonstrates SQL on FHIR v2 ViewDefinitions with subscription-driven 
materialization. It shows how materialized analytic views stay current in near-real-time 
as clinical data changes in the FHIR server.

## Demo Scenario: HEDIS CBP Measure
**Controlling Blood Pressure** — "% of members 18-85 with hypertension whose most recent 
BP was adequately controlled (<140/90 mmHg)"

This is a real CMS/NCQA quality measure reported by every health plan in America. Currently 
calculated quarterly from batch ETL. Our system makes it available in **real-time**.

## How to Run

### Prerequisites
- FHIR Server running locally (OSS, R4, with SQL Server)
- SQL Server accessible for sqlfhir schema tables
- (Optional) Power BI Desktop for dashboard demo

### Steps
1. Start the FHIR Server: `dotnet run --project src/Microsoft.Health.Fhir.R4.Web`
2. Start this demo app: `dotnet run --project samples/apps/sqlfhir-demo/SqlOnFhirDemo`
3. Open http://localhost:5200 in your browser

### Demo Flow
1. **Setup Panel**: Register the 3 ViewDefinitions (PatientDemographics, UsCoreBloodPressures, ConditionFlat)
2. **Load Data**: Import Synthea-generated HEDIS CBP sample data (50 patients with hypertension + BPs)
3. **Watch**: The materialized views populate as the subscription engine processes the data
4. **Live Update**: Record a new BP observation → see the CBP measure rate change in real-time
5. **Power BI**: (Optional) Open the Power BI dashboard connected to sqlfhir.* tables

## ViewDefinitions Used
All three are **official SQL on FHIR v2 spec examples**:

| View | Resource | Purpose |
|------|----------|---------|
| `patient_demographics` | Patient | Name, gender, birth date for age filtering |
| `us_core_blood_pressures` | Observation | Systolic/diastolic BP values with dates |
| `condition_flat` | Condition | Hypertension diagnosis with status |

## Synthea Data Generation
Custom Synthea modules in `synthea/` generate three patient types:
- **40% Controlled**: Hypertension + BP < 140/90 (in measure, numerator)
- **35% Uncontrolled**: Hypertension + BP ≥ 140/90 (in measure, not numerator)
- **25% Healthy**: No hypertension (not in measure at all)

To regenerate data:
```bash
java -jar synthea-with-dependencies.jar -m hedis_cbp -p 50 --exporter.fhir.export=true
```

## Architecture
```
Blazor Demo App ──► FHIR Server ──► SQL Server (sqlfhir.*)
                        │                    │
                        ▼                    ▼
                   Subscription         Power BI
                   Engine               DirectQuery
```
