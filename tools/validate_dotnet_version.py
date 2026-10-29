#!/usr/bin/env python3

import argparse
import json
import re
import sys
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    root = parser.parse_args().root.resolve()

    sdk_version = json.loads((root / "global.json").read_text())["sdk"]["version"]
    major = sdk_version.split(".", 1)[0]
    target_framework = f"net{major}.0"
    sdk_version_pattern = r'"version"\\s\*:\\s\*"([^"]+)"'

    checks = {
        "Directory.Build.props": [
            rf"<TargetFrameworks>\s*{target_framework}\s*</TargetFrameworks>",
        ],
        "Directory.Packages.props": [
            rf"Condition=\"'\$\(TargetFramework\)' == '{target_framework}'\"",
            rf"<AspNetPackageVersion>{major}\.0\.",
            rf"<DotNetSdkPackageVersion>{major}\.0\.",
            rf"<PackageVersion Include=\"Microsoft.Bcl.Memory\" Version=\"{major}\.",
            rf"<PackageVersion Include=\"Microsoft.EntityFrameworkCore.InMemory\" Version=\"{major}\.",
            rf"<PackageVersion Include=\"System.Drawing.Common\" Version=\"{major}\.",
        ],
        "build/DependabotDiscovery/Directory.Build.props": [
            rf"<TargetFramework>{target_framework}</TargetFramework>",
        ],
        "src/Microsoft.Health.Fhir.Ignixa/Microsoft.Health.Fhir.Ignixa.csproj": [
            rf"<TargetFramework>{target_framework}</TargetFramework>",
        ],
        "build/build-variables.yml": [
            rf"defaultBuildFramework:\s*'{target_framework}'",
        ],
        "build/jobs/analyze.yml": [
            rf"-f {target_framework}",
        ],
        "build/ci-pipeline.yml": [
            rf"Windows_dotnet{major}",
        ],
        "build/pr-pipeline.yml": [
            rf"Windows_dotnet{major}",
        ],
        "build/docker/Dockerfile": [
            rf"dotnet/sdk:{major}\.",
            rf"dotnet/aspnet:{major}\.",
            rf"-f {target_framework}",
        ],
        "tools/IncludePerf/Invoke-IncludePerfABTest.ps1": [
            rf"bin/Release/{target_framework}/",
        ],
        "tools/MultiInstanceRunner/README.md": [
            rf"-f {target_framework}",
        ],
        "test/Microsoft.Health.Fhir.R4.Tests.E2E/Microsoft.Health.Fhir.R4.Tests.E2E.csproj": [
            sdk_version_pattern,
        ],
        "test/Microsoft.Health.Fhir.R4B.Tests.E2E/Microsoft.Health.Fhir.R4B.Tests.E2E.csproj": [
            sdk_version_pattern,
        ],
        "test/Microsoft.Health.Fhir.Stu3.Tests.E2E/Microsoft.Health.Fhir.Stu3.Tests.E2E.csproj": [
            sdk_version_pattern,
        ],
        "test/Microsoft.Health.Fhir.R5.Tests.E2E/Microsoft.Health.Fhir.R5.Tests.E2E.csproj": [
            sdk_version_pattern,
        ],
        ".github/prompts/updateDotnetVersion.prompt.md": [
            rf"\.NET {major}",
            rf"dotnet/sdk:{major}\.",
            rf"dotnet/aspnet:{major}\.",
        ],
    }

    errors = []
    for relative_path, patterns in checks.items():
        path = root / relative_path
        if not path.is_file():
            errors.append(f"{relative_path}: file is missing")
            continue

        text = path.read_text()
        for pattern in patterns:
            if re.search(pattern, text) is None:
                errors.append(f"{relative_path}: missing pattern {pattern}")

    if errors:
        print("\n".join(errors), file=sys.stderr)
        return 1

    print(f"Repository .NET {target_framework} references match global.json SDK {sdk_version}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
