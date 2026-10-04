import json
import math
import random
import statistics
import sys
from pathlib import Path


def paired_change(ratios):
    logs = [math.log(value) for value in ratios]
    rng = random.Random(239717)
    samples = sorted(
        math.exp(statistics.mean(rng.choices(logs, k=len(logs))))
        for _ in range(20000)
    )
    return {
        "geometricMeanChangePercent": 100 * (math.exp(statistics.mean(logs)) - 1),
        "bootstrap95ChangePercent": [
            100 * (samples[500] - 1),
            100 * (samples[19499] - 1),
        ],
        "pairRatios": ratios,
    }


def analyze(directory):
    manifest = json.loads((directory / "manifest.json").read_text(encoding="utf-8-sig"))
    records = [
        json.loads(line)
        for line in (directory / "measurements.jsonl").read_text(encoding="utf-8-sig").splitlines()
    ]
    assert len(records) == len(manifest["schedule"]) == 42, "Incomplete run schedule"
    identity = ("corpusSha256", "corpusCount", "runtime", "processorCount", "serverGc", "redoxVersion")
    for sequence, (record, scheduled) in enumerate(zip(records, manifest["schedule"]), 1):
        assert record["sequence"] == sequence
        for name in ("phase", "pair", "label", "variant", "workload"):
            assert record[name] == scheduled[name.capitalize()], (sequence, name)
        assert record["operations"] == scheduled["Passes"] * record["corpusCount"]
        assert all(record[name] == records[0][name] for name in identity), "Changed run identity"
        assert record["corpusCount"] == 1000
        assert record["corpusSha256"] == "1a49c68a6c7ee3887bd204aba8a3b422342a7206268fbba2d7dce84ed1a8fd8c"
        assert record["processorCount"] == 2 and record["serverGc"]

    result = {
        "scope": manifest["scope"],
        "method": "Paired geometric mean ratios. Percentile bootstrap of five independent pairs, 20000 resamples, seed 239717. Positive changes are worse.",
        "workloads": {},
    }
    metrics = {
        "cpuUsPerResource": ("cpuMs", 1000, True),
        "elapsedUsPerResource": ("elapsedMs", 1000, True),
        "allocatedBytesPerResource": ("allocatedBytes", 1, True),
        "endWorkingSetBytes": ("workingSetBytes", 1, False),
        "processPeakWorkingSetBytes": ("peakWorkingSetBytes", 1, False),
        "endPrivateBytes": ("privateBytes", 1, False),
    }

    for workload in ("parse", "roundtrip", "metadata"):
        rows = [record for record in records if record["workload"] == workload]
        values = {(row["phase"], row["pair"], row["label"]): row for row in rows}
        assert len(values) == 14, "Duplicate run labels"
        summary = {}
        for metric, (field, scale, per_resource) in metrics.items():
            def read(phase, pair, label):
                row = values[(phase, pair, label)]
                value = row[field] * scale / (row["operations"] if per_resource else 1)
                assert math.isfinite(value) and value > 0
                return value

            baseline = [read("AB", pair, "baseline") for pair in range(1, 6)]
            redox = [read("AB", pair, "redox") for pair in range(1, 6)]
            ratios = [b / a for a, b in zip(baseline, redox)]
            summary[metric] = {
                "baselineMedian": statistics.median(baseline),
                "redoxMedian": statistics.median(redox),
                **paired_change(ratios),
                "aaChangePercent": [
                    100 * (read("AA", pair, "A2") / read("AA", pair, "A") - 1)
                    for pair in range(1, 3)
                ],
            }
        result["workloads"][workload] = summary
    return result


if __name__ == "__main__":
    if sys.argv[1:] == ["--self-test"]:
        same = paired_change([1] * 5)
        assert same["geometricMeanChangePercent"] == 0
        assert same["bootstrap95ChangePercent"] == [0, 0]
        lower = paired_change([0.5] * 5)
        assert lower["geometricMeanChangePercent"] == -50
        assert lower["bootstrap95ChangePercent"] == [-50, -50]
        higher = paired_change([2] * 5)
        assert higher["geometricMeanChangePercent"] == 100
        assert higher["bootstrap95ChangePercent"] == [100, 100]
        print("Analysis self-tests passed.")
    elif len(sys.argv) == 2:
        print(json.dumps(analyze(Path(sys.argv[1])), indent=2))
    else:
        raise SystemExit("Usage: python analyze_results.py <results-directory> | --self-test")
