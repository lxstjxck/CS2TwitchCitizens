const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const ts = require("typescript");
const React = require("react");
const ReactDOMServer = require("react-dom/server");
const source = fs.readFileSync(path.join(__dirname, "../src/mods/interactive-panel.tsx"), "utf8");
const compiled = ts.transpileModule(source, { compilerOptions: {
  module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX,
  target: ts.ScriptTarget.ES2020, esModuleInterop: true } });
const moduleObject = { exports: {} };
const localRequire = spec => {
  if (spec === "react" || spec === "react/jsx-runtime") return require(spec);
  if (spec === "cs2/ui") return { Button: ({ children, ...props }) => React.createElement("button", {}, children) };
  if (spec === "./reward-manager") return { RewardManagerPanel: () => React.createElement("div", {}, "Rewards") };
  if (spec.endsWith(".module.scss")) return { __esModule: true,
    default: new Proxy({}, { get: (_, key) => String(key) }) };
  throw new Error(`Unexpected import: ${spec}`);
};
new Function("require", "module", "exports", compiled.outputText)(localRequire, moduleObject, moduleObject.exports);
const { parseInteractiveState, parseInteractiveSettings, InteractiveNotice, InteractivePanel } = moduleObject.exports;
const defaults = parseInteractiveSettings("{}");
assert.equal(defaults.enabled, false);
assert.deepEqual(defaults.rewards, []);
assert.equal(parseInteractiveSettings("broken").enabled, false);
assert.equal(parseInteractiveState("{}").active, null);
assert.equal(parseInteractiveState("broken").queued, 0);
const sample = { id: "1", type: "CitizenMessage", twitchUserId: "42", displayName: "Зритель",
  citizenName: "Житель", canFindCitizen: true, text: "Привет, город!", localTest: true };
for (const locale of ["ru", "en"]) {
  const notice = ReactDOMServer.renderToStaticMarkup(React.createElement(InteractiveNotice,
    { event: sample, locale, onDismiss() {}, onFind() {} }));
  assert.ok(notice.includes("Привет, город!") && notice.includes("Житель"));
  assert.ok(notice.includes(locale === "ru" ? "Найти жителя" : "Find citizen"));
  const escaped = ReactDOMServer.renderToStaticMarkup(React.createElement(InteractiveNotice,
    { event: { ...sample, text: "<script>alert(1)</script>" }, locale, onDismiss() {}, onFind() {} }));
  assert.ok(escaped.includes("&lt;script&gt;") && !escaped.includes("<script>"));
  const announcement = ReactDOMServer.renderToStaticMarkup(React.createElement(InteractiveNotice,
    { event: { ...sample, type: "CityAnnouncement", text: "<b>News</b>" }, locale,
      onDismiss() {}, onFind() {}, onShown() {} }));
  assert.ok(announcement.includes("cityAnnouncement") && announcement.includes("&lt;b&gt;News&lt;/b&gt;"));
  assert.ok(announcement.includes(locale === "ru" ? "ГОРОДСКОЕ ОБЪЯВЛЕНИЕ" : "CITY ANNOUNCEMENT"));
  const panel = ReactDOMServer.renderToStaticMarkup(React.createElement(InteractivePanel,
    { locale, config: defaults, state: parseInteractiveState("{}"), status: "",
      pointsStatus: "Disconnected", pointsPermission: "AuthorizationRequired", onSave() {}, onTest() {} }));
  assert.ok(panel.includes(locale === "ru" ? "Баллы канала" : "Channel Points"));
  assert.ok(panel.includes(locale === "ru" ? "Предпросмотр" : "Preview"));
  assert.ok(panel.includes(locale === "ru" ? "История" : "History"));
  assert.ok(!panel.includes(locale === "ru" ? "Очередь</button>" : "Queue</button>"));
}
assert.ok(source.includes('onTest("ShowMessage")') && source.includes('onTest("CityAnnouncement")'));
assert.ok(source.includes("<RewardHistory") && source.includes("onCommand={onRewardCommand}"));
console.log("PASS: interactive defaults, RU/EN panel and safe citizen notice rendering");
