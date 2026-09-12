import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtemp, mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { resolve, join } from "node:path";
import { spawnSync } from "node:child_process";
import { createCapture } from "../probe.mjs";

test("Native producer framing interoperates with the real .NET probe and rejects tampering", async () => {
  const repo = fileURLToPath(new URL("../../", import.meta.url));
  // Explicit override also allows validating a published or isolated build.
  const executable = process.env.MYFRAME_COLLECTOR_PROBE ?? resolve(repo,
    "MyFrame.Collector.Probe/bin/Debug/net10.0/MyFrame.Collector.Probe.exe");
  const artifacts = resolve(repo, "artifacts");
  await mkdir(artifacts, { recursive: true });
  const directory = await mkdtemp(join(artifacts, "collector-interop-"));
  const capture = await createCapture({ Suits: [{ Name: "synthetic-ação", Rank: 3 }] },
    crypto.randomUUID(), 1, "2026-09-12T12:00:00Z");
  const bodyPath = join(directory, capture.fileName);
  const markerPath = join(directory, capture.markerName);
  await writeFile(bodyPath, capture.body, "utf8");
  await writeFile(markerPath, capture.marker, "utf8");
  const run = () => spawnSync(executable, ["--marker", markerPath], { encoding: "utf8", timeout: 10000 });
  const valid = run();
  assert.ifError(valid.error);
  assert.equal(valid.status, 0, valid.stderr);
  const result = JSON.parse(valid.stdout);
  assert.equal(result.payloadRootObject, true);
  assert.equal(result.publishable, false);
  assert.equal(result.completeness, "unverified");
  assert.equal(valid.stdout.includes("synthetic-ação"), false);
  await writeFile(bodyPath, capture.body + " ", "utf8");
  const invalid = run();
  assert.ifError(invalid.error);
  assert.equal(invalid.status, 1);
  assert.equal(invalid.stdout, "");
  assert.equal(invalid.stderr.includes("synthetic-ação"), false);
  // Tiny synthetic artifacts intentionally retained under ignored artifacts/ for audit.
});
