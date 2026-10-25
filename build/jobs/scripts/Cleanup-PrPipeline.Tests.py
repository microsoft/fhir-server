import argparse
from pathlib import Path
import unittest

import yaml


parser = argparse.ArgumentParser()
parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[3])
args = parser.parse_args()


def load(relative_path):
    return yaml.safe_load((args.root / relative_path).read_text())


class PrCleanupWiringTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.pipeline = load("build/pr-pipeline.yml")
        cls.template = load("build/stages/cleanup-pr-test-resources.yml")
        cls.cleanup = load("build/pr-cleanup-pipeline.yml")
        cls.calls = [
            stage for stage in cls.pipeline["stages"]
            if stage.get("template") == "./stages/cleanup-pr-test-resources.yml"
        ]
        cls.tests = {}
        cls.stages = {}
        for stage in cls.pipeline["stages"]:
            if "stage" in stage:
                cls.stages[stage["stage"]] = stage
                for job in stage.get("jobs", []):
                    if job.get("template") in (
                        "./jobs/run-sql-tests.yml", "./jobs/run-cosmos-tests.yml"
                    ):
                        cls.tests[stage["stage"]] = job
        for call in cls.calls:
            expanded = yaml.safe_dump(cls.template["stages"][0])
            for name, value in call["parameters"].items():
                expanded = expanded.replace("${{ parameters." + name + " }}", str(value))
            stage = yaml.safe_load(expanded)
            cls.stages[stage["stage"]] = stage
        cls.trigger = cls.cleanup["resources"]["pipelines"][0]["trigger"]

    def test_pr_path_only_deletes_sql_resources(self):
        sql_tests = {
            name for name, job in self.tests.items()
            if job["template"] == "./jobs/run-sql-tests.yml"
        }
        self.assertEqual(len(self.calls), len(sql_tests))
        self.assertEqual({call["parameters"]["testStage"] for call in self.calls}, sql_tests)
        self.assertEqual({p["name"] for p in self.template["parameters"]}, {"testStage", "version"})
        for call in self.calls:
            parameters = call["parameters"]
            self.assertEqual(parameters["version"], self.tests[parameters["testStage"]]["parameters"]["version"])
        script = (args.root / "build/jobs/scripts/Cleanup-PrTestResources.ps1").read_text()
        self.assertNotIn("Microsoft.DocumentDB", script)
        self.assertNotIn("resourceAssociations", script)

    def test_standalone_trigger_is_the_successful_owned_stage_frontier(self):
        expected = {
            "cleanup_" + name if job["template"] == "./jobs/run-sql-tests.yml" else name
            for name, job in self.tests.items()
        }
        filters = self.trigger.get("stages", [])
        self.assertEqual(set(filters), expected)
        self.assertEqual(len(filters), len(expected))
        self.assertEqual(self.trigger["branches"]["include"], ["*"])
        self.assertEqual(self.cleanup["trigger"], "none")
        self.assertEqual(self.cleanup["pr"], "none")
        for name in filters:
            self.assertIn(name, self.stages)
        print("Standalone AND filter:", ", ".join(sorted(filters)))

    def test_every_sql_cleanup_is_a_success_only_trigger_dependency(self):
        filters = self.trigger.get("stages", [])
        for call in self.calls:
            owner = call["parameters"]["testStage"]
            name = "cleanup_" + owner
            stage = self.stages[name]
            self.assertIn(name, filters)
            self.assertEqual(stage["dependsOn"], [owner])
            self.assertEqual(
                stage["condition"],
                f"and(not(canceled()), eq(dependencies.{owner}.result, 'Succeeded'))",
            )
            print(f"{name} depends on {owner}, success only")
        unrelated = {"BuildUnitTests", "AnalyzeSecurity", "ValidateDependabotDiscovery", "aggregateCoverage"}
        self.assertTrue(unrelated.issubset(self.stages))
        pending = list(filters)
        dependencies = set()
        while pending:
            name = pending.pop()
            if name in dependencies:
                continue
            dependencies.add(name)
            stage = self.stages[name]
            self.assertIn("dependsOn", stage, f"{name} must not implicitly depend on the previous stage")
            pending.extend(stage["dependsOn"])
        self.assertTrue(unrelated.isdisjoint(dependencies))


if __name__ == "__main__":
    unittest.main(argv=[__file__], verbosity=2)
