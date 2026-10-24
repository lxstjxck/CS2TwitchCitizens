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
  if (spec.endsWith(".module.scss")) return { __esModule: true,
    default: new Proxy({}, { get: (_, key) => String(key) }) };
  throw new Error(`Unexpected import: ${spec}`);
};
new Function("require", "module", "exports", compiled.outputText)(localRequire, moduleObject, moduleObject.exports);
const { parseRewardSnapshot, RewardManagerPanel } = moduleObject.exports;
assert.deepEqual(parseRewardSnapshot("broken").fixedRewards, []);
assert.deepEqual(parseRewardSnapshot('{"fixedRewards":null}').fixedRewards, []);
const snapshot = parseRewardSnapshot(JSON.stringify({ broadcasterId: "42", items: [],
  fixedRewards: [{ templateId: "ShowMessage", rewardId: "r1", cost: 750, enabled: true, status: "Synced" },
    { templateId: "CityAnnouncement", rewardId: "", cost: 1000, enabled: false, status: "Missing" }],
  legacyActive: ["old-reward"], redemptions: [], busy: false, status: "" }));
for (const locale of ["ru", "en"]) {
  const html = ReactDOMServer.renderToStaticMarkup(React.createElement(RewardManagerPanel, {
    locale, snapshot, permission: "Allowed", onCommand() {}, onReauthorize() {} }));
  assert.ok(html.includes(locale === "ru" ? "Сообщение в город" : "Message to the city"));
  assert.ok(html.includes(locale === "ru" ? "Городское объявление" : "City announcement"));
  assert.ok(html.includes("750") && html.includes("1000") && html.includes("old-reward"));
  assert.ok(!html.includes("Delete reward") && !html.includes("Color HEX") && !html.includes("Reward ID"));
  assert.ok(!html.includes("access_token") && !html.includes("Bearer"));
}
console.log("PASS: fixed reward cards, prices, migration warning, and no generic editor");
