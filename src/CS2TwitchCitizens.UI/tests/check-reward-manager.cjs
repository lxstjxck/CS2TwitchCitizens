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
const { parseRewardSnapshot, RewardManagerPanel, rewardPrimaryCommand } = moduleObject.exports;
assert.deepEqual(rewardPrimaryCommand("ShowMessage", "Missing", 500),
  { operation: "FixedEnable", action: "ShowMessage", cost: 500 });
assert.deepEqual(rewardPrimaryCommand("CityAnnouncement", "Disabled", 1000),
  { operation: "FixedEnable", action: "CityAnnouncement", cost: 1000 });
assert.deepEqual(rewardPrimaryCommand("ShowMessage", "Synced", 750),
  { operation: "FixedCost", action: "ShowMessage", cost: 750 });
assert.ok(source.includes("onCommand(rewardPrimaryCommand(template.id, state, Number(cost)))"));
assert.ok(source.includes('operation: "FixedDisable", action: template.id'));
assert.deepEqual(parseRewardSnapshot("broken").fixedRewards, []);
assert.deepEqual(parseRewardSnapshot('{"fixedRewards":null}').fixedRewards, []);
const snapshot = parseRewardSnapshot(JSON.stringify({ broadcasterId: "42", items: [],
  fixedRewards: [{ templateId: "ShowMessage", rewardId: "r1", cost: 750, enabled: true, status: "Synced" },
    { templateId: "CityAnnouncement", rewardId: "", cost: 1000, enabled: false, status: "Missing" }],
  legacyActive: ["old-reward"], redemptions: [], busy: false, status: "", fresh: true }));
for (const locale of ["ru", "en"]) {
  const html = ReactDOMServer.renderToStaticMarkup(React.createElement(RewardManagerPanel, {
    locale, snapshot, permission: "Allowed", onCommand() {}, onReauthorize() {} }));
  assert.ok(html.includes(locale === "ru" ? "Сообщение в город" : "Message to the city"));
  assert.ok(html.includes(locale === "ru" ? "Городское объявление" : "City announcement"));
  assert.ok(html.includes("750") && html.includes("1000") && !html.includes("old-reward"));
  assert.ok(!html.includes("Delete reward") && !html.includes("Color HEX") && !html.includes("Reward ID"));
  assert.ok(!html.includes("access_token") && !html.includes("Bearer"));
  assert.ok(html.includes(locale === "ru" ? "Создать награду" : "Create reward"));
  assert.equal((html.match(/fixedRewardCard/g) || []).length, 2);
  assert.ok(!html.includes(locale === "ru" ? "Ожидающие погашения" : "Pending redemptions"));
  assert.ok(!html.includes(locale === "ru" ? "Синхронизировать с Twitch" : "Sync with Twitch"));
  assert.ok(!html.includes(locale === "ru" ? "Сохранить стоимость" : "Save cost"));
  const disabled = { ...snapshot, fixedRewards: snapshot.fixedRewards.map(r => r.templateId === "ShowMessage"
    ? { ...r, enabled: false, status: "Disabled" } : r) };
  const disabledHtml = ReactDOMServer.renderToStaticMarkup(React.createElement(RewardManagerPanel, {
    locale, snapshot: disabled, permission: "Allowed", onCommand() {}, onReauthorize() {} }));
  assert.ok(disabledHtml.includes(locale === "ru" ? "Включить награду" : "Enable reward"));
  const missingPermissions = ReactDOMServer.renderToStaticMarkup(React.createElement(RewardManagerPanel, {
    locale, snapshot, permission: "AuthorizationRequired", onCommand() {}, onReauthorize() {} }));
  assert.ok(missingPermissions.includes(locale === "ru" ? "Обновить разрешения Twitch" : "Update Twitch permissions"));
  for (const [status, en, ru] of [
    ["RewardTlsError", "system clock", "время системы"],
    ["RewardDnsError", "DNS", "DNS"],
    ["RewardTimeout", "Check your connection", "Проверьте соединение"],
    ["RewardResponseError", "unreadable", "нечитаемый"],
    ["RewardInternalError", "Could not process", "Не удалось обработать"],
    ["RewardStorageError", "disk access", "доступ к диску"],
    ["HTTP403", "channel eligibility", "доступность функции"],
  ]) {
    const errorHtml = ReactDOMServer.renderToStaticMarkup(React.createElement(RewardManagerPanel, {
      locale, snapshot: { ...snapshot, status, fresh: false }, permission: "Allowed",
      onCommand() {}, onReauthorize() {} }));
    assert.ok(errorHtml.includes(locale === "ru" ? ru : en), status + " " + locale);
    assert.ok(!errorHtml.includes("secret-token"));
  }
}
console.log("PASS: fixed reward cards, prices, migration warning, and no generic editor");
