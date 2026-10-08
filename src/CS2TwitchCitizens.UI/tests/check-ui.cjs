const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const ts = require("typescript");

const source = fs.readFileSync(path.join(__dirname, "../src/mods/viewer-search.ts"), "utf8");
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS } });
const moduleObject = { exports: {} };
new Function("module", "exports", compiled.outputText)(moduleObject, moduleObject.exports);
const { filterViewers } = moduleObject.exports;
const viewers = [
  { login: "lxstjack", displayName: "LxstJack" },
  { login: "second", displayName: "Другой зритель" },
];
assert.deepEqual(filterViewers(viewers, "  LXST "), [viewers[0]]);
assert.deepEqual(filterViewers(viewers, "другой"), [viewers[1]]);
assert.deepEqual(filterViewers(viewers, "unknown"), []);
assert.equal(filterViewers(viewers, "").length, 2);
console.log("PASS: UI viewer search by login/display name");
