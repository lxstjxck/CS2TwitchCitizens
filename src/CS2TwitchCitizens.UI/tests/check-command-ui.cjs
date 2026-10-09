const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const ts = require("typescript");
const React = require("react");
const ReactDOMServer = require("react-dom/server");

const sourceDir = path.join(__dirname, "../src/mods");
const loaded = new Map();
function load(name) {
  if (loaded.has(name)) return loaded.get(name);
  const tsx = path.join(sourceDir, `${name}.tsx`);
  const filename = fs.existsSync(tsx) ? tsx : path.join(sourceDir, `${name}.ts`);
  const source = fs.readFileSync(filename, "utf8");
  const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX,
    target: ts.ScriptTarget.ES2020, esModuleInterop: true } });
  const moduleObject = { exports: {} };
  loaded.set(name, moduleObject.exports);
  const mockApi = { bindValue: () => ({}), bindLocalValue: () => ({}), bindTriggerWithArgs: () => () => {}, useValue: () => "{}" };
  const mockUi = { Button: ({ children, onSelect, ...props }) => React.createElement("button", { className: props.className,
    onClick: onSelect }, children), Panel: ({ children }) => React.createElement("div", null, children),
    Scrollable: ({ children }) => React.createElement("div", null, children) };
  const localRequire = spec => {
    if (spec === "react" || spec === "react/jsx-runtime") return require(spec);
    if (spec === "react-dom/server") return ReactDOMServer;
    if (spec === "cs2/api") return mockApi;
    if (spec === "cs2/ui") return mockUi;
    if (spec.endsWith(".module.scss")) return new Proxy({}, { get: (_, key) => String(key) });
    if (spec.startsWith("./")) return load(spec.slice(2));
    throw new Error(`Unexpected import ${spec}`);
  };
  new Function("require", "module", "exports", compiled.outputText)(localRequire, moduleObject, moduleObject.exports);
  loaded.set(name, moduleObject.exports);
  return moduleObject.exports;
}

const { defaultCommands, parseCommandConfig, normalizeCommandConfig, safeFieldGroups, previewFields } = load("command-config");
const { CommandEditor, CommandTabBoundary } = load("twitch-citizens");
const { commandPreview, helpText, categoryText, nextAdvanced, nextHelp, joinRepeatPreview } = load("command-ux");
const defaults = defaultCommands();
const csharpFixture = fs.readFileSync(path.join(__dirname, "../../../tests/fixtures/command-settings-v1.json"), "utf8");
assert.deepEqual(parseCommandConfig(csharpFixture), { config: defaults, recovered: false });
assert.deepEqual(parseCommandConfig("").config, defaults);
assert.equal(parseCommandConfig("").recovered, true);
assert.deepEqual(parseCommandConfig(undefined).config, defaults);
assert.deepEqual(parseCommandConfig("{}").config, defaults);
assert.deepEqual(parseCommandConfig("null").config, defaults);

const cases = [
  { ...defaults, me: { ...defaults.me, selectedFields: undefined } },
  { ...defaults, me: { ...defaults.me, selectedFields: null } },
  { ...defaults, me: { ...defaults.me, selectedFields: "name" } },
  { ...defaults, me: { ...defaults.me, selectedFields: 42 } },
  { ...defaults, me: { enabled: false } },
  { ...defaults, history: null },
];
for (const raw of cases) {
  const { config, recovered } = normalizeCommandConfig(raw);
  assert.equal(recovered, true);
  assert.ok(Array.isArray(config.me.selectedFields));
  for (const name of ["join", "me", "find", "history"]) assert.ok(Array.isArray(config[name].selectedFields));
  for (const locale of ["ru", "en"]) {
    for (const name of ["join", "me", "find", "history"]) {
      const html = ReactDOMServer.renderToStaticMarkup(React.createElement(CommandEditor,
        { config, recovered, locale, initialCommand: name }));
      assert.ok(html.includes(`!${name}`));
      assert.ok(html.includes(locale === "ru" ? "Дополнительные настройки команды" : "Advanced command settings"));
    }
  }
}

assert.deepEqual(safeFieldGroups("me", undefined), safeFieldGroups("me"));
assert.deepEqual(safeFieldGroups("me", null), []);
assert.deepEqual(safeFieldGroups("me", { me: [] }), []);
assert.equal(normalizeCommandConfig(defaults, null).recovered, true);
assert.equal(normalizeCommandConfig(defaults, { join: [], me: [], find: [], history: [] }).recovered, true);
assert.deepEqual(previewFields("me", defaults, null), []);
const customized = { ...defaults, me: { ...defaults.me, viewerCooldownSeconds: 71,
  selectedFields: ["name", "school"] }, language: "en" };
const roundtrip = parseCommandConfig(JSON.stringify(customized));
assert.equal(roundtrip.recovered, false);
assert.equal(roundtrip.config.me.viewerCooldownSeconds, 71);
assert.deepEqual(roundtrip.config.me.selectedFields, ["name", "school"]);
assert.equal(roundtrip.config.language, "en");
assert.equal(nextAdvanced(null, "global"), "global");
assert.equal(nextAdvanced("global", "global"), null);
assert.equal(nextAdvanced("global", "command"), "command");
assert.equal(nextHelp(null, "queue"), "queue");
assert.equal(nextHelp("queue", "queue"), null);
assert.equal(categoryText("Family / Семья", "ru"), "Семья");
assert.equal(categoryText("Family / Семья", "en"), "Family");
assert.match(helpText("sharedCooldown", "ru"), /одного зрителя/);
assert.match(helpText("queue", "en"), /queue is full/);
assert.match(helpText("home", "ru"), /Адрес домашнего здания/);
assert.match(commandPreview("me", defaults), /Алекс/);
assert.match(commandPreview("join", defaults), /стали жителем/);
assert.match(commandPreview("me", { ...defaults, language: "en" }), /Alex/);
assert.equal(commandPreview("me", { ...defaults, me: { ...defaults.me, maxResponseLength: 12 } }).length, 12);
assert.doesNotMatch(commandPreview("me", { ...defaults, me: { ...defaults.me, selectedFields: ["school"] } }), /учёба/);
assert.match(commandPreview("me", { ...defaults, missingData: "label", me: { ...defaults.me, selectedFields: ["school"] } }), /нет данных/);
assert.match(commandPreview("me", { ...defaults, responsesEnabled: false }), /выключены/);
assert.match(joinRepeatPreview(defaults), /уже житель/);
assert.match(joinRepeatPreview({ ...defaults, join: { ...defaults.join, alreadyJoinedResponse: false } }), /без ответа/);
for (const name of ["join", "me", "find", "history"]) {
  const html = ReactDOMServer.renderToStaticMarkup(React.createElement(CommandEditor,
    { config: roundtrip.config, recovered: false, locale: "ru", initialCommand: name,
      initialAdvanced: "command", initialGroup: safeFieldGroups(name)[0]?.category || null, initialHelp: "viewerCooldown" }));
  assert.match(html, /Демонстрационные данные/);
  assert.match(html, /Пример ответа/);
  assert.match(html, /один зритель ждёт|Сколько секунд один зритель/);
  if (name !== "join") assert.match(html, /Имя жителя|Тип местоположения|Всего жизней/);
}
const advancedGlobal = ReactDOMServer.renderToStaticMarkup(React.createElement(CommandEditor,
  { config: defaults, recovered: false, locale: "en", initialAdvanced: "global" }));
assert.match(advancedGlobal, /Shared cooldown/);
assert.match(advancedGlobal, /Reset all settings/);
assert.doesNotMatch(advancedGlobal, /Reply preview/);
const resetConfirmation = ReactDOMServer.renderToStaticMarkup(React.createElement(CommandEditor,
  { config: defaults, recovered: false, locale: "en", initialAdvanced: "global", initialReset: "all" }));
assert.match(resetConfirmation, /Reset saved settings/);
assert.match(resetConfirmation, /Yes, reset/);
assert.match(resetConfirmation, /Cancel/);
for (const name of ["me", "find", "history"]) {
  for (const group of safeFieldGroups(name)) for (const field of group.fields) {
    assert.notEqual(helpText(field.id, "ru"), "Описание недоступно.");
    assert.notEqual(helpText(field.id, "en"), "Description unavailable.");
  }
}
const recoveredHtml = ReactDOMServer.renderToStaticMarkup(React.createElement(CommandEditor,
  { config: defaults, recovered: true, saveFailed: true, locale: "en" }));
assert.ok(recoveredHtml.includes("Safe values are shown"));
assert.ok(recoveredHtml.includes("Could not save command settings"));
const boundary = new CommandTabBoundary({ locale: "en", children: null });
boundary.state = { failed: true, recoveryOpen: false, confirmReset: false };
assert.ok(ReactDOMServer.renderToStaticMarkup(boundary.render()).includes("Advanced recovery settings"));
boundary.state = { failed: true, recoveryOpen: true, confirmReset: true };
assert.ok(ReactDOMServer.renderToStaticMarkup(boundary.render()).includes("Yes, reset"));

for (let i = 0; i < 24; i++) {
  const name = ["join", "me", "find", "history"][i % 4];
  const locale = i % 2 ? "ru" : "en";
  ReactDOMServer.renderToStaticMarkup(React.createElement(CommandEditor,
    { config: roundtrip.config, recovered: false, locale, initialCommand: name }));
}

const editorSource = fs.readFileSync(path.join(sourceDir, "command-editor.tsx"), "utf8");
const editorCss = fs.readFileSync(path.join(sourceDir, "twitch-citizens.module.scss"), "utf8");
assert.match(editorSource, /className=\{styles\.commandTabName\}>!\{name\}<\/span>/);
assert.doesNotMatch(editorSource, /✓/);
assert.match(editorCss, /\.commandTabName\s*\{[^}]*white-space:\s*nowrap/s);
assert.match(editorCss, /\.commandToggle\[data-selected="true"\] \.toggleMark:after\s*\{[^}]*border-right:[^}]*border-bottom:/s);
assert.match(editorCss, /\.panel \.permissionButton\s*\{[^}]*justify-content:\s*flex-start/s);
assert.match(editorCss, /\.commandSection:last-child\s*\{[^}]*padding-bottom:\s*0/s);
assert.doesNotMatch(editorSource, /<select|<option|<details|<summary|type="number"|type="checkbox"/);
assert.match(editorSource, /onMouseEnter/);
assert.match(editorSource, /onMouseLeave/);
assert.match(editorSource, /onSelect=\{\(\) => setPinnedHelp/);
assert.match(editorSource, /setConfirmReset\("all"\)/);
assert.match(editorSource, /selectedFields: checked/);
const panelSource = fs.readFileSync(path.join(sourceDir, "twitch-citizens.tsx"), "utf8");
assert.match(panelSource, /class CommandTabBoundary/);
assert.match(panelSource, /Commands tab render failed/);
console.log("PASS: command tab data boundary, malformed settings/catalog, RU/EN render, rapid tab sequence, persistence shape");
