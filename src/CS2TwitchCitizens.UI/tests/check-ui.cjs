const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { TextDecoder } = require("node:util");
const ts = require("typescript");

function loadSource(name) {
  const source = fs.readFileSync(path.join(__dirname, `../src/mods/${name}.ts`), "utf8");
  const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS } });
  const moduleObject = { exports: {} };
  new Function("module", "exports", compiled.outputText)(moduleObject, moduleObject.exports);
  return moduleObject.exports;
}

const { filterViewers } = loadSource("viewer-search");
const { selectedViewer, canLocate, isCameraError, lifeHistory } = loadSource("viewer-view-model");
const { detectLocale, ageText, lifeText, connectionText, focusText, lifeCountText,
  lifeNumberText, viewerMetaText, viewerCountText, text, dateText, valueText } = loadSource("ui-text");

const viewer = (id, extras = {}) => ({
  twitchUserId: id, login: `login${id}`, displayName: `Viewer ${id}`,
  citizenName: `Citizen ${id}`, isValid: true, positionAvailable: true,
  currentLifeStatus: "Active", totalLives: 1, ...extras,
});
const one = [viewer("one")];
const many = Array.from({ length: 100 }, (_, index) => viewer(String(index)));

assert.deepEqual(filterViewers([], ""), []);
assert.equal(selectedViewer([], ""), undefined);
assert.equal(canLocate(selectedViewer([], "")), false);
assert.equal(selectedViewer(one, "")?.twitchUserId, "one");
assert.equal(selectedViewer(many, "75")?.twitchUserId, "75");
assert.equal(selectedViewer(many, "missing")?.twitchUserId, "0");
assert.equal(filterViewers(many, ""), many); // No expanded cards or list duplication.
assert.equal(filterViewers(many, "login75").length, 1);
assert.equal(filterViewers(many, "Citizen 75").length, 1);
assert.equal(filterViewers(one, "viewer ONE").length, 1);
assert.equal(filterViewers(one, "unknown").length, 0);
assert.equal(filterViewers(many.slice(0, 10), "citizen 9").length, 1);

const longName = "A".repeat(160);
assert.equal(filterViewers([viewer("long", { displayName: longName })], longName).length, 1);
assert.equal(filterViewers([viewer("blank", { citizenName: "" })], "missing").length, 0);
assert.equal(canLocate(viewer("ready")), true);
assert.equal(canLocate(viewer("lost", { positionAvailable: false })), false);
assert.equal(canLocate(viewer("dead", { currentLifeStatus: "Deceased" })), false);
assert.equal(canLocate(viewer("stale", { isValid: false })), false);
assert.equal(isCameraError("FocusFailed"), true);
assert.equal(isCameraError("FocusConfirmed"), false);
assert.equal(isCameraError("FollowRequested"), false);
const life = (id, status) => ({ lifeId: id, status });
assert.deepEqual(lifeHistory(life("3", "Active"), [life("1", "Deceased"), life("2", "Deceased")])
  .map(({ number, life: item }) => [number, item.lifeId]), [[3, "3"], [2, "2"], [1, "1"]]);
assert.deepEqual(lifeHistory(life("2", "Missing"), [life("1", "Deceased")]).map(({ life: item }) => item.status),
  ["Missing", "Deceased"]);
assert.deepEqual(lifeHistory(life("2", "Deceased"), [life("1", "Deceased")]).map(({ number }) => number), [2, 1]);
assert.equal(lifeHistory(undefined, []).length, 0);
const longHistory = lifeHistory(life("101", "Active"),
  Array.from({ length: 100 }, (_, index) => life(String(index + 1), "Deceased")));
assert.equal(longHistory.length, 101);
assert.equal(longHistory[0].number, 101);
assert.equal(longHistory[100].number, 1);
assert.equal(valueText("en", "home available"), "No data");
assert.equal(valueText("en", "undefined"), "No data");
assert.equal(dateText("en", ""), "No data");
assert.equal(dateText("en", "2026-07-01T00:00:00.0000000"), "July 2026");
assert.equal(dateText("ru", "2026-07-01T00:00:00.0000000"), "июль 2026 г.");
assert.equal(dateText("en", "2026-01-31T23:59:59"), "January 2026");
assert.equal(dateText("ru", "2026-12-31T23:59:59"), "декабрь 2026 г.");
for (const invalid of ["invalid", "2026-02-30", "2026-13-01", "2026-00-01", "2026-04-31"]) {
  assert.equal(dateText("en", invalid), "No data");
  assert.equal(dateText("ru", invalid), "Нет данных");
}
const savedIntl = globalThis.Intl;
try {
  globalThis.Intl = undefined;
  const singleLife = lifeHistory({ ...life("1", "Active"), startGameDate: "2026-07-01" }, []);
  assert.equal(singleLife.length, 1);
  assert.equal(dateText("ru", singleLife[0].life.startGameDate), "июль 2026 г.");
  const multipleLives = lifeHistory({ ...life("2", "Active"), startGameDate: "2026-07-01" },
    [{ ...life("1", "Deceased"), startGameDate: "2025-03-01" }]);
  assert.deepEqual(multipleLives.map(({ number }) => number), [2, 1]);
  assert.deepEqual(multipleLives.map(({ life: item }) => dateText("en", item.startGameDate)),
    ["July 2026", "March 2025"]);
} finally {
  globalThis.Intl = savedIntl;
}

assert.equal(detectLocale("ru-RU"), "ru");
assert.equal(detectLocale("en-US"), "en");
assert.equal(ageText("ru", "Adult"), "Взрослый");
assert.equal(ageText("en", "Adult"), "Adult");
assert.equal(ageText("ru", ""), "Нет данных");
assert.equal(ageText("ru", "undefined"), "Нет данных");
assert.equal(lifeText("ru", "Deceased"), "Умер");
assert.equal(lifeText("en", "Missing"), "Missing");
assert.equal(lifeText("ru", "Missing"), "Недоступен");
assert.equal(lifeText("en", "Active"), "Active");
assert.equal(text("ru", "lifeHistory"), "История жизней");
assert.equal(text("en", "infoTab"), "Information");
assert.equal(connectionText("ru", "Connected"), "Twitch подключён");
assert.equal(focusText("ru", "FollowRequested"), "Слежение запрошено; проверьте камеру в игре");
assert.equal(focusText("en", "FocusConfirmed"), "Camera is near the citizen");
assert.equal(focusText("ru"), "Камера ожидает действия.");
assert.equal(lifeCountText("ru", 1), "1 жизнь");
assert.equal(lifeCountText("ru", 2), "2 жизни");
assert.equal(lifeCountText("ru", 11), "11 жизней");
assert.equal(lifeNumberText("ru", 1), "Жизнь №1");
assert.equal(lifeNumberText("en", 1), "Life 1");
assert.equal(lifeNumberText("ru", 0), "");
assert.equal(viewerMetaText("ru", "Adult", 1), "Взрослый · Жизнь №1");
assert.equal(viewerMetaText("en", "Adult", 1), "Adult · Life 1");
assert.equal(viewerMetaText("ru", "", 1), "Жизнь №1");
assert.equal(viewerMetaText("ru", "Adult", 0), "Взрослый");
assert.equal(viewerMetaText("ru", "", 0), "");
assert.equal(viewerMetaText("ru", "undefined", 0), "");
assert.equal(text("ru", "search"), "Поиск зрителя...");
assert.equal(text("en", "search"), "Search viewers...");
assert.equal(viewerCountText("en", 100), "100 viewers");

const css = fs.readFileSync(path.join(__dirname, "../src/mods/twitch-citizens.module.scss"), "utf8");
const jsx = fs.readFileSync(path.join(__dirname, "../src/mods/twitch-citizens.tsx"), "utf8");
const utf8 = new TextDecoder("utf-8", { fatal: true });
for (const file of ["ui-text.ts", "twitch-citizens.tsx"]) {
  const content = utf8.decode(fs.readFileSync(path.join(__dirname, `../src/mods/${file}`)));
  assert.doesNotMatch(content, /\uFFFD/);
}
for (const file of fs.readdirSync(path.join(__dirname, "../src/mods"))) {
  if (!/\.tsx?$/.test(file)) continue;
  const content = fs.readFileSync(path.join(__dirname, "../src/mods", file), "utf8");
  assert.doesNotMatch(content, /\bIntl\b/, `${file} must not depend on Intl`);
}
assert.match(css, /\.root\[data-locale="ru"\],\s*\.panel\[data-locale="ru"\]\s*\{\s*font-family:\s*"Noto Sans"/);
assert.match(css, /\.panel\s*\{\s*font-family:\s*var\(--fontFamily\)/);
assert.match(css, /\.root\[data-locale="ru"\] \.control,\s*\.panel\[data-locale="ru"\] \.control,\s*\.panel\[data-locale="ru"\] \.search\s*\{\s*font-family:\s*"Noto Sans"/);
assert.match(css, /\.search\s*\{[^}]*font-family:\s*var\(--fontFamily\)/s);
assert.doesNotMatch(css, /font-family:\s*inherit/);
assert.doesNotMatch(css, /font-family:\s*(?:monospace|"Perfect DOS VGA 437")/);
assert.doesNotMatch(css, /closeButton|closeMark|closeIcon/);
assert.match(css, /\.panel\s*\{[^}]*width:\s*420px/s);
assert.match(css, /\.panel\s*\{[^}]*max-height:\s*calc\(100vh - 104px\)/s);
assert.match(css, /\.viewerList\s*\{[^}]*max-height:\s*176px/s);
assert.match(css, /\.facts\s*\{[^}]*flex-wrap:\s*wrap/s);
assert.match(css, /\.longValue\s*\{[^}]*overflow-wrap:\s*break-word/s);
assert.match(css, /\.rowMeta,\s*\.cardMeta\s*\{[^}]*white-space:\s*nowrap/s);
assert.match(css, /\.control:focus-visible\s*\{[^}]*outline:\s*2px/s);
assert.match(css, /\.control:focus,\s*\.control:global\(\.focused\)\s*\{[^}]*outline:\s*2px/s);
assert.match(css, /\.control:disabled,\s*\.control\[data-disabled="true"\]/s);
assert.match(css, /\.panel \.tab\[data-selected="true"\]/);
assert.match(css, /\.search:focus-visible/);
assert.doesNotMatch(css, /width:\s*100vw|height:\s*100vh|\bmin\(/);
assert.doesNotMatch(css, /(^|\n)\s*(html|body|\*)\s*\{/);
assert.doesNotMatch(css, /(^|\n)\s*button\s*\{/);
for (const [width, height] of [[1280, 720], [1920, 1080], [2560, 1440]]) {
  const small = width <= 1500;
  const panelWidth = small ? 380 : 420;
  const right = small ? 16 : 24;
  const top = small ? 66 : 76;
  const maxHeight = height - (small ? 86 : 104);
  assert.ok(panelWidth + right < width && top + maxHeight < height);
}
assert.match(jsx, /disabled=\{!canLocate\(selected\)\}/);
assert.match(jsx, /lifeHistory\(selected\.currentLife, selected\.previousLives \|\| \[\]\)/);
assert.match(jsx, /viewMode === "history"/);
assert.match(jsx, /setViewMode\("info"\); panelOpen\.update\(false\)/);
assert.match(css, /\.historyList\s*\{[^}]*max-height:\s*290px/s);
assert.match(jsx, /<Panel[^>]*data-locale=\{locale\}[^>]*onClose=\{\(\) => \{ setViewMode\("info"\); panelOpen\.update\(false\); \}\}/);
assert.doesNotMatch(jsx, /styles\.closeButton|styles\.closeMark|closeIcon:|[×✕✖]/);
assert.match(jsx, /tooltipLabel=\{displayName\(viewer, locale\)\}/);
assert.doesNotMatch(jsx, /○|◎|· #|>null<|>undefined</);
assert.equal((jsx.match(/<div className=\{styles\.card\}/g) || []).length, 1);
assert.match(jsx, /module\.scss/);
for (const trigger of ["connectTwitch", "cancelTwitch", "reconnectTwitch", "disconnectTwitch", "openTwitchVerification"]) {
  assert.match(jsx, new RegExp(`bindTriggerWithArgs<\\[string\\]>\\(group, "${trigger}"\\)`));
}
for (const key of ["connectTwitch", "waitingAuth", "codeExpired", "authDenied", "reauthorize", "storageError"]) {
  assert.ok(text("ru", key) && text("en", key));
}
assert.doesNotMatch(jsx, /accessToken|refreshToken|deviceCode|Authorization/);
const deployed = path.join(process.env.CSII_USERDATAPATH || "", "Mods", "CS2TwitchCitizens.UI");
if (process.env.CSII_USERDATAPATH) {
  const js = utf8.decode(fs.readFileSync(path.join(deployed, "CS2TwitchCitizens.UI.mjs")));
  const builtCss = utf8.decode(fs.readFileSync(path.join(deployed, "CS2TwitchCitizens.UI.css")));
  assert.doesNotMatch(js, /\bIntl\b/, "Deployed UI bundle must not depend on Intl");
  for (const label of ["Взрослый", "Поиск зрителя...", "Найти в городе"]) {
    assert.ok(js.includes(label), `Missing RU text in bundle: ${label}`);
  }
  assert.ok(js.includes("Search viewers..."));
  assert.ok(builtCss.includes('font-family:"Noto Sans"'));
  assert.doesNotMatch(builtCss, /closeButton_|closeMark_|closeIcon_/);
  assert.doesNotMatch(js, /closeButton_[A-Za-z0-9]+|closeMark_[A-Za-z0-9]+/);
  assert.doesNotMatch(builtCss, /(^|})\s*(html|body|\*)\s*\{/);
  assert.doesNotMatch(js, /closeIcon_[A-Za-z0-9]+/);
}
console.log("PASS: UI search, RU/EN text and bundle, explicit control fonts, native close only, interaction wiring, layout guards");
