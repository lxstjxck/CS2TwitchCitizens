const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const ts = require("typescript");
const React = require("react");
const ReactDOMServer = require("react-dom/server");
const source = fs.readFileSync(path.join(__dirname, "../src/mods/reward-manager.tsx"), "utf8");
const compiled = ts.transpileModule(source, { compilerOptions: {
  module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX,
  target: ts.ScriptTarget.ES2020, esModuleInterop: true } });
const moduleObject = { exports: {} };
const localRequire = spec => {
  if (spec === "react" || spec === "react/jsx-runtime") return require(spec);
  if (spec === "cs2/ui") return { Button: ({ children, disabled }) => React.createElement("button", { disabled }, children) };
  if (spec.endsWith(".module.scss")) return new Proxy({}, { get: (_, key) => String(key) });
  throw new Error(`Unexpected import: ${spec}`);
};
new Function("require", "module", "exports", compiled.outputText)(localRequire, moduleObject, moduleObject.exports);
const { parseRewardSnapshot, RewardManagerPanel } = moduleObject.exports;
assert.deepEqual(parseRewardSnapshot("broken").items, []);
assert.deepEqual(parseRewardSnapshot('{"items":null,"manageableIds":"invalid"}').items, []);
const snapshot = parseRewardSnapshot(JSON.stringify({ broadcasterId: "42", items: [
  { id: "r1", title: "Награда", cost: 250, is_enabled: true } ],
  manageableIds: [], redemptions: [], busy: false, status: "" }));
assert.equal(snapshot.items.length, 1);
for (const locale of ["ru", "en"]) {
  const html = ReactDOMServer.renderToStaticMarkup(React.createElement(RewardManagerPanel, {
    locale, snapshot, rules: [], permission: "AuthorizationRequired",
    onCommand() {}, onReauthorize() {}, onRuleChange() {} }));
  assert.ok(html.includes("Награда") && html.includes("250"));
  assert.ok(html.includes(locale === "ru" ? "Синхронизировать с Twitch" : "Sync with Twitch"));
  assert.ok(html.includes(locale === "ru" ? "Внешняя" : "External"));
  assert.ok(!html.includes("access_token") && !html.includes("Bearer"));
}
console.log("PASS: reward manager malformed snapshots, RU/EN rendering, external ownership labels, no secrets");
