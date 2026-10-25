const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const ts = require("typescript");
const React = require("react");
const ReactDOMServer = require("react-dom/server");
const visibilitySource = fs.readFileSync(path.join(__dirname, "../src/mods/notice-visibility.ts"), "utf8");
const visibilityModule = { exports: {} };
new Function("module", "exports", ts.transpileModule(visibilitySource,
  { compilerOptions: { module: ts.ModuleKind.CommonJS } }).outputText)(visibilityModule, visibilityModule.exports);
const { noticeIsVisible } = visibilityModule.exports;
const source = fs.readFileSync(path.join(__dirname, "../src/mods/interactive-panel.tsx"), "utf8");
const compiled = ts.transpileModule(source, { compilerOptions: {
  module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX,
  target: ts.ScriptTarget.ES2020, esModuleInterop: true } });
const moduleObject = { exports: {} };
const localRequire = spec => {
  if (spec === "react" || spec === "react/jsx-runtime") return require(spec);
  if (spec === "cs2/ui") return { Button: ({ children, ...props }) => React.createElement("button", {}, children) };
  if (spec === "./reward-manager") return { RewardManagerPanel: () => React.createElement("div", {}, "Rewards") };
  if (spec === "./notice-visibility") return visibilityModule.exports;
  if (spec.endsWith(".module.scss")) return { __esModule: true,
    default: new Proxy({}, { get: (_, key) => String(key) }) };
  throw new Error(`Unexpected import: ${spec}`);
};
new Function("require", "module", "exports", compiled.outputText)(localRequire, moduleObject, moduleObject.exports);
const { parseInteractiveState, parseInteractiveSettings, InteractiveNotice, InteractivePanel } = moduleObject.exports;
const box = { left: 24, top: 128, right: 374, bottom: 300, width: 350, height: 172 };
const parent = { parentElement: null, getBoundingClientRect: () =>
  ({ left: 0, top: 0, right: 1280, bottom: 720 }) };
const node = { parentElement: parent, getBoundingClientRect: () => box };
const visible = () => ({ display: "flex", visibility: "visible", opacity: "1", overflow: "visible" });
for (const [width, height] of [[1280, 720], [1920, 1080], [2560, 1440]])
  assert.equal(noticeIsVisible(node, width, height, visible), true);
assert.equal(noticeIsVisible(node, 320, 720, visible), false);
const centered = { parentElement: parent, getBoundingClientRect: () =>
  ({ left: 340, top: 180, right: 940, bottom: 540, width: 600, height: 360 }) };
assert.equal(noticeIsVisible(centered, 1280, 720, visible), true);
assert.equal(noticeIsVisible(node, 1280, 720, element =>
  element === parent ? { ...visible(), overflow: "hidden" } : visible()), true);
const clippedParent = { parentElement: null, getBoundingClientRect: () =>
  ({ left: 0, top: 0, right: 300, bottom: 720 }) };
assert.equal(noticeIsVisible({ ...node, parentElement: clippedParent }, 1280, 720, element =>
  element === clippedParent ? { ...visible(), overflowX: "auto" } : visible()), false);
assert.equal(noticeIsVisible(node, 1280, 720, element =>
  element === parent ? { ...visible(), visibility: "hidden" } : visible()), false);
assert.equal(noticeIsVisible({ ...node, getBoundingClientRect: () => ({ ...box, width: 0 }) },
  1280, 720, visible), false);
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
const entry = fs.readFileSync(path.join(__dirname, "../src/index.tsx"), "utf8");
const rootSource = fs.readFileSync(path.join(__dirname, "../src/mods/twitch-citizens.tsx"), "utf8");
assert.match(entry, /append\('GameTopRight', TwitchCitizensButton\)/);
assert.match(entry, /append\('Game', TwitchCitizensGameRoot\)/);
assert.match(rootSource, /TwitchCitizensGameRoot = \(\) => <>\s*<TwitchCitizensPanel \/>\s*<TwitchCitizensOverlay \/>/);
assert.ok(!rootSource.split("export const TwitchCitizensButton")[1]
  .split("export const TwitchCitizensOverlay")[0].includes("<InteractiveNotice"));
assert.ok(rootSource.includes("onShown={ackInteractive}"));
assert.ok(source.includes("frame >= 3") && source.includes("noticeIsVisible(noticeRef.current"));
assert.ok(!source.includes("if (event?.id) onShown(event.id)"));
console.log("PASS: interactive defaults, RU/EN panel and safe citizen notice rendering");
