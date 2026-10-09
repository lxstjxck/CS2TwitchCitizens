import React, { useMemo, useState } from "react";
import { bindLocalValue, bindTriggerWithArgs, bindValue, useValue } from "cs2/api";
import { Button, Panel, Scrollable } from "cs2/ui";
import styles from "./twitch-citizens.module.scss";
import { filterViewers } from "./viewer-search";
import { canLocate, isCameraError, lifeHistory, LifeSummary, selectedViewer } from "./viewer-view-model";
import { ageText, connectionText, dateText, detectLocale, focusText, lifeCountText, lifeText, Locale, text, valueText, viewerCountText, viewerMetaText } from "./ui-text";
import { defaultCommands, parseCommandConfig } from "./command-config";
import { CommandEditor } from "./command-editor";
export { CommandEditor } from "./command-editor";

type Viewer = {
  twitchUserId: string;
  login: string;
  displayName: string;
  citizenName: string;
  age: string;
  hasHome: boolean;
  hasWorkplace: boolean;
  isValid: boolean;
  positionAvailable: boolean;
  currentLifeId: string;
  totalLives: number;
  currentLifeStatus: string;
  currentLife: LifeSummary | null;
  previousLives: LifeSummary[];
};
type Snapshot = { twitchStatus: string; channelId: string; gameLoaded: boolean; viewers: Viewer[] };
type FocusFeedback = { viewerId: string; result: string; sequence: number };
type Auth = { state: string; login: string; displayName: string; userCode: string; verificationUri: string;
  expiresAt: number; error: string; eventSubStatus: string; legacyConfig: boolean;
  writePermission: "Allowed" | "AuthorizationRequired" | "Checking"; reauthorizationState: string };
const group = "cs2twitchcitizens";
const snapshotBinding = bindValue<string>(group, "snapshot", "{}");
const focusResultBinding = bindValue<string>(group, "focusResult", "");
const authBinding = bindValue<string>(group, "auth", "{}");
const commandsBinding = bindValue<string>(group, "commandSettings", "{}");
const commandsStatusBinding = bindValue<string>(group, "commandSettingsStatus", "");
const updateCommands = bindTriggerWithArgs<[string]>(group, "updateCommandSettings");
const connectTwitch = bindTriggerWithArgs<[string]>(group, "connectTwitch");
const cancelTwitch = bindTriggerWithArgs<[string]>(group, "cancelTwitch");
const reconnectTwitch = bindTriggerWithArgs<[string]>(group, "reconnectTwitch");
const reauthorizeTwitch = bindTriggerWithArgs<[string]>(group, "reauthorizeTwitch");
const disconnectTwitch = bindTriggerWithArgs<[string]>(group, "disconnectTwitch");
const openTwitchVerification = bindTriggerWithArgs<[string]>(group, "openTwitchVerification");
const focusCitizen = bindTriggerWithArgs<[string]>(group, "focusCitizen");
const followCitizen = bindTriggerWithArgs<[string]>(group, "followCitizen");
const stopFollowing = bindTriggerWithArgs<[string]>(group, "stopFollowing");
const panelOpen = bindLocalValue(false);
const localeBinding = bindLocalValue<Locale>(detectLocale());

function parseSnapshot(value: string): Snapshot {
  try {
    const parsed = JSON.parse(value) as Partial<Snapshot>;
    return {
      twitchStatus: parsed.twitchStatus ?? "Disabled",
      channelId: parsed.channelId ?? "",
      gameLoaded: parsed.gameLoaded ?? false,
      viewers: Array.isArray(parsed.viewers) ? parsed.viewers : [],
    };
  } catch {
    return { twitchStatus: "Disabled", channelId: "", gameLoaded: false, viewers: [] };
  }
}

function parseFeedback(value: string): FocusFeedback | null {
  try { return JSON.parse(value) as FocusFeedback; }
  catch { return null; }
}
function parseAuth(value: string): Auth {
  try {
    const data = JSON.parse(value) as Partial<Auth>;
    return { state: data.state || "Disconnected", login: data.login || "", displayName: data.displayName || "",
      userCode: data.userCode || "", verificationUri: data.verificationUri || "",
      expiresAt: data.expiresAt || 0, error: data.error || "",
      eventSubStatus: data.eventSubStatus || "Disabled", legacyConfig: !!data.legacyConfig,
      writePermission: data.writePermission === "Allowed" || data.writePermission === "Checking"
        ? data.writePermission : "AuthorizationRequired",
      reauthorizationState: data.reauthorizationState || "Idle" };
  } catch { return { state: "Disconnected", login: "", displayName: "", userCode: "", verificationUri: "",
    expiresAt: 0, error: "", eventSubStatus: "Disabled", legacyConfig: false,
    writePermission: "AuthorizationRequired", reauthorizationState: "Idle" }; }
}

function displayName(viewer: Viewer, locale: Locale): string {
  return viewer.displayName || viewer.login || text(locale, "noData");
}

export const TwitchCitizensButton = () => {
  const open = useValue(panelOpen);
  const locale = useValue(localeBinding);
  const snapshotJson = useValue(snapshotBinding);
  const snapshot = useMemo(() => parseSnapshot(snapshotJson), [snapshotJson]);

  return <div className={styles.root} data-locale={locale}>
    <Button variant="default" className={`${styles.control} ${styles.openButton}`}
      onSelect={() => panelOpen.update(!open)}>
      {text(locale, "open")} {snapshot.viewers.length > 0 ? `(${snapshot.viewers.length})` : ""}
    </Button>
  </div>;
};

export const TwitchCitizensPanel = () => {
  const open = useValue(panelOpen);
  const locale = useValue(localeBinding);
  const [tab, setTab] = useState<"residents" | "settings" | "commands">("residents");
  const [search, setSearch] = useState("");
  const [selectedId, setSelectedId] = useState("");
  const [viewMode, setViewMode] = useState<"info" | "history">("info");
  const snapshotJson = useValue(snapshotBinding);
  const snapshot = useMemo(() => parseSnapshot(snapshotJson), [snapshotJson]);
  const feedbackJson = useValue(focusResultBinding);
  const authJson = useValue(authBinding);
  const auth = useMemo(() => parseAuth(authJson), [authJson]);
  const commandsJson = useValue(commandsBinding);
  const commandsStatus = useValue(commandsStatusBinding);
  const commandState = useMemo(() => parseCommandConfig(commandsJson), [commandsJson]);
  const feedback = useMemo(() => parseFeedback(feedbackJson), [feedbackJson]);
  const viewers = useMemo(() => filterViewers(snapshot.viewers, search), [snapshot.viewers, search]);
  const selected = selectedViewer(viewers, selectedId);
  const history = selected ? lifeHistory(selected.currentLife, selected.previousLives || []) : [];
  const selectedFeedback = feedback && selected && feedback.viewerId === selected.twitchUserId
    ? feedback.result : undefined;
  const boundCount = snapshot.viewers.filter((viewer) => viewer.currentLifeStatus === "Active" && viewer.isValid).length;
  const totalLives = snapshot.viewers.reduce((count, viewer) => count + viewer.totalLives, 0);

  if (!open) return null;
  return <Panel className={styles.panel} data-locale={locale} onClose={() => { setViewMode("info"); panelOpen.update(false); }}
    header={<div className={styles.header}>
      <span className={styles.brandGlyph} aria-hidden="true">TC</span>
      <div className={styles.headerText}>
        <strong>{text(locale, "title")}</strong>
        <small>{text(locale, "subtitle")}</small>
      </div>
    </div>}>
    <Scrollable className={styles.body} vertical trackVisibility="scrollable">
      <div className={styles.content}>
        <div className={styles.connection} data-status={snapshot.twitchStatus}>
          <span className={styles.connectionDot} aria-hidden="true" />
          <span className={styles.connectionLabel}>{connectionText(locale, snapshot.twitchStatus)}</span>
          <span className={styles.connectionCount}>{viewerCountText(locale, boundCount)}</span>
        </div>

        <div className={styles.stats}>
          <div className={styles.stat}><span>{text(locale, "bound")}</span><strong>{boundCount}</strong></div>
          <div className={styles.stat}><span>{text(locale, "totalLives")}</span><strong>{totalLives}</strong></div>
        </div>

        <div className={styles.tabs}>
          <Button variant="default" selected={tab === "residents"} data-selected={tab === "residents"}
            className={`${styles.control} ${styles.tab}`} onSelect={() => setTab("residents")}>{text(locale, "residents")}</Button>
          <Button variant="default" selected={tab === "settings"} data-selected={tab === "settings"}
            className={`${styles.control} ${styles.tab}`} onSelect={() => setTab("settings")}>{text(locale, "settings")}</Button>
          <Button variant="default" selected={tab === "commands"} data-selected={tab === "commands"}
            className={`${styles.control} ${styles.tab}`} onSelect={() => setTab("commands")}>{locale === "ru" ? "Команды" : "Commands"}</Button>
        </div>

        {tab === "commands" ? <CommandTabBoundary locale={locale}>
          <CommandEditor config={commandState.config} recovered={commandState.recovered || commandsStatus === "Recovered"}
            saveFailed={commandsStatus === "SaveFailed"} locale={locale}
            onSave={next => updateCommands(JSON.stringify(next))} />
        </CommandTabBoundary> : tab === "settings" ? <div className={styles.settings}>
          <div className={styles.settingsRow}><span>{text(locale, "connection")}</span><strong>{connectionText(locale, snapshot.twitchStatus)}</strong></div>
          <div className={styles.settingsRow}><span>{text(locale, "channel")}</span><strong className={styles.longValue}>{snapshot.channelId || text(locale, "channelMissing")}</strong></div>
          <div className={styles.settingsRow}><span>{text(locale, "sendPermission")}</span><strong>{text(locale,
            auth.writePermission === "Allowed" ? "permissionAllowed" : auth.writePermission === "Checking"
              ? "permissionChecking" : "permissionRequired")}</strong></div>
          {auth.writePermission === "AuthorizationRequired" && auth.state === "Connected" &&
            auth.reauthorizationState === "Idle" && <Button variant="default" className={`${styles.control} ${styles.authButton}`}
              onSelect={() => reauthorizeTwitch("")}>{text(locale, "updateTwitchPermissions")}</Button>}
          {auth.state === "Connected" && auth.error && <p className={styles.authError}>{text(locale,
            auth.error === "ClientIdMissing" ? "clientIdMissing" : auth.error === "StorageError" ? "storageError" :
            auth.error === "CodeExpired" ? "codeExpired" : auth.error === "Denied" ? "authDenied" : "networkError")}</p>}
          {auth.legacyConfig && <p className={styles.helper}>{text(locale, "legacyNotice")}</p>}
          {(auth.state === "Disconnected" || auth.state === "Error") && <>
            {auth.state === "Error" && <p className={styles.authError}>{text(locale, auth.error === "ClientIdMissing" ? "clientIdMissing" :
              auth.error === "StorageError" ? "storageError" : auth.error === "CodeExpired" ? "codeExpired" :
              auth.error === "Denied" ? "authDenied" : auth.error === "Reauthorize" ? "reauthorize" : "networkError")}</p>}
            <Button variant="default" className={`${styles.control} ${styles.authButton}`}
              onSelect={() => connectTwitch("")}>{text(locale, "connectTwitch")}</Button>
          </>}
          {(auth.state === "Requesting" || auth.state === "Pending" || auth.state === "Restoring" ||
            auth.reauthorizationState === "Requesting" || auth.reauthorizationState === "Pending") && <>
            <p className={styles.helper}>{text(locale, auth.state === "Pending" || auth.reauthorizationState === "Pending" ? "waitingAuth" : "requestingAuth")}</p>
            {(auth.state === "Pending" || auth.reauthorizationState === "Pending") && <>
              <strong className={styles.userCode}>{auth.userCode}</strong>
              <span className={styles.longValue}>{auth.verificationUri}</span>
              <span>{text(locale, "codeExpires")}: {Math.max(0, Math.ceil(auth.expiresAt - Date.now() / 1000))} s</span>
              <Button variant="default" className={`${styles.control} ${styles.authButton}`}
                onSelect={() => openTwitchVerification("")}>{text(locale, "openTwitch")}</Button>
            </>}
            {auth.state !== "Restoring" && <Button variant="default" className={`${styles.control} ${styles.authButton}`}
              onSelect={() => cancelTwitch("")}>{text(locale, "cancelAuth")}</Button>}
          </>}
          {auth.state === "Connected" && <>
            <div className={styles.settingsRow}><span>{text(locale, "login")}</span><strong>{auth.login}</strong></div>
            {auth.displayName && <div className={styles.settingsRow}><span>{text(locale, "displayName")}</span><strong>{auth.displayName}</strong></div>}
            <div className={styles.settingsRow}><span>EventSub</span><strong>{connectionText(locale, auth.eventSubStatus)}</strong></div>
            <Button variant="default" className={`${styles.control} ${styles.authButton}`}
              onSelect={() => reconnectTwitch("")}>{text(locale, "reconnectTwitch")}</Button>
            <Button variant="default" className={`${styles.control} ${styles.authButton}`}
              onSelect={() => disconnectTwitch("")}>{text(locale, "disconnectTwitch")}</Button>
          </>}
          <div className={styles.settingsRow}><span>{text(locale, "language")}</span>
            <div className={styles.languageButtons}>
              <Button variant="default" selected={locale === "ru"} data-selected={locale === "ru"}
                className={`${styles.control} ${styles.languageButton}`} onSelect={() => localeBinding.update("ru")}>RU</Button>
              <Button variant="default" selected={locale === "en"} data-selected={locale === "en"}
                className={`${styles.control} ${styles.languageButton}`} onSelect={() => localeBinding.update("en")}>EN</Button>
            </div>
          </div>
          <p className={styles.helper}>{text(locale, "languageAuto")}</p>
          <p className={styles.helper}>{text(locale, "settingsNote")}</p>
        </div> : <>
          {!snapshot.gameLoaded && <div className={styles.notice}>{text(locale, "loadCity")}</div>}
          <input className={styles.search} type="text" value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder={text(locale, "search")} aria-label={text(locale, "searchLabel")} />
          <Scrollable className={styles.viewerList} vertical trackVisibility="scrollable">
            {viewers.length === 0 && <div className={styles.empty}>{text(locale, "empty")}</div>}
            {viewers.map((viewer) => <Button variant="default" key={viewer.twitchUserId}
              selected={viewer.twitchUserId === selected?.twitchUserId}
              data-selected={viewer.twitchUserId === selected?.twitchUserId}
              tooltipLabel={displayName(viewer, locale)}
              className={`${styles.control} ${styles.viewerRow}`} onSelect={() => setSelectedId(viewer.twitchUserId)}>
              <span className={styles.rowText}>
                <strong className={styles.longValue} title={displayName(viewer, locale)}>
                  {text(locale, "name")}: {displayName(viewer, locale)}
                </strong>
                {viewerMetaText(locale, viewer.age, viewer.totalLives) &&
                  <small className={styles.rowMeta} title={viewerMetaText(locale, viewer.age, viewer.totalLives)}>
                    {viewerMetaText(locale, viewer.age, viewer.totalLives)}
                  </small>}
              </span>
              <span className={styles.lifePill} data-life={viewer.currentLifeStatus}>
                {lifeText(locale, viewer.currentLifeStatus)}
              </span>
            </Button>)}
          </Scrollable>

          {selected && <div className={styles.card}>
            <div className={styles.cardHeader}>
              <div className={styles.cardIdentity}>
                <strong className={styles.longValue} title={displayName(selected, locale)}>{displayName(selected, locale)}</strong>
                {selected.login && selected.login !== selected.displayName &&
                  <small className={styles.longValue}>@{selected.login}</small>}
                {selected.citizenName && selected.citizenName !== displayName(selected, locale) &&
                  <small className={styles.longValue}>{text(locale, "citizenName")}: {selected.citizenName}</small>}
                {viewerMetaText(locale, selected.age, selected.totalLives) &&
                  <small className={styles.cardMeta} title={viewerMetaText(locale, selected.age, selected.totalLives)}>
                    {viewerMetaText(locale, selected.age, selected.totalLives)}
                  </small>}
              </div>
              <span className={styles.lifePill} data-life={selected.currentLifeStatus}>
                {lifeText(locale, selected.currentLifeStatus)}
              </span>
            </div>

            <div className={styles.cardTabs}>
              <Button variant="default" selected={viewMode === "info"} data-selected={viewMode === "info"}
                className={`${styles.control} ${styles.cardTab}`} onSelect={() => setViewMode("info")}>{text(locale, "infoTab")}</Button>
              <Button variant="default" selected={viewMode === "history"} data-selected={viewMode === "history"}
                className={`${styles.control} ${styles.cardTab}`} onSelect={() => setViewMode("history")}>{text(locale, "lifeHistory")}</Button>
            </div>

            {viewMode === "history" ? <Scrollable className={styles.historyList} vertical trackVisibility="scrollable">
              {history.length === 0 &&
                <div className={styles.empty}>{text(locale, "emptyHistory")}</div>}
              {history.map(({ number, life }) =>
                <div className={styles.historyEntry} key={life.lifeId || number}>
                  <div className={styles.historyHeader}>
                    <strong>{text(locale, "lifeNumber")}{number}</strong>
                    <span className={styles.lifePill} data-life={life.status}>{lifeText(locale, life.status)}</span>
                  </div>
                  <div className={styles.historyFacts}>
                    <div className={styles.fact}><span>{text(locale, "citizenName")}</span><strong>{valueText(locale, number === selected.totalLives && life.status === "Active" ? selected.citizenName || life.originalCitizenName?.replace(/^(custom|label):/, "") : life.originalCitizenName?.replace(/^(custom|label):/, ""))}</strong></div>
                    <div className={styles.fact}><span>{text(locale, "age")}</span><strong>{ageText(locale, number === selected.totalLives && life.status === "Active" ? selected.age || life.lastKnownAge : life.lastKnownAge)}</strong></div>
                    <div className={styles.fact}><span>{text(locale, "startDate")}</span><strong>{dateText(locale, life.startGameDate)}</strong></div>
                    {life.endGameDate && <div className={styles.fact}><span>{text(locale, "endDate")}</span><strong>{dateText(locale, life.endGameDate)}</strong></div>}
                    <div className={styles.fact}><span>{text(locale, "home")}</span><strong>{valueText(locale, life.lastKnownHome)}</strong></div>
                    <div className={styles.fact}><span>{text(locale, "workplace")}</span><strong>{valueText(locale, life.lastKnownWorkplace)}</strong></div>
                    {life.causeOfDeath && <div className={styles.fact}><span>{text(locale, "causeOfDeath")}</span><strong>{life.causeOfDeath}</strong></div>}
                  </div>
                </div>)}
            </Scrollable> : <>
            <div className={styles.facts}>
              <div className={styles.fact}><span>{text(locale, "age")}</span><strong>{ageText(locale, selected.age)}</strong></div>
              <div className={styles.fact}><span>{text(locale, "history")}</span><strong>{lifeCountText(locale, selected.totalLives)}</strong></div>
              <div className={styles.fact}><span>{text(locale, "home")}</span><strong>{selected.hasHome ? text(locale, "yes") : text(locale, "noData")}</strong></div>
              <div className={styles.fact}><span>{text(locale, "workplace")}</span><strong>{selected.hasWorkplace ? text(locale, "yes") : text(locale, "noData")}</strong></div>
              <div className={styles.fact}><span>{text(locale, "location")}</span><strong>{selected.positionAvailable ? text(locale, "available") : text(locale, "unavailable")}</strong></div>
              <div className={styles.fact}><span>{text(locale, "status")}</span><strong>{lifeText(locale, selected.currentLifeStatus)}</strong></div>
            </div>

            <Button variant="default" className={`${styles.control} ${styles.findButton}`}
              disabled={!canLocate(selected)} data-disabled={!canLocate(selected)}
              onSelect={() => focusCitizen(selected.twitchUserId)}>{text(locale, "find")}</Button>
            <div className={styles.cameraActions}>
              <Button variant="default" className={`${styles.control} ${styles.secondaryButton}`}
                disabled={!canLocate(selected)} data-disabled={!canLocate(selected)}
                onSelect={() => followCitizen(selected.twitchUserId)}>{text(locale, "follow")}</Button>
              <Button variant="default" className={`${styles.control} ${styles.secondaryButton}`}
                tooltipLabel={text(locale, "stopFollow")}
                onSelect={() => stopFollowing(selected.twitchUserId)}>
                {text(locale, "stopFollow")}
              </Button>
            </div>
            <div className={styles.cameraFeedback} data-error={isCameraError(selectedFeedback)}>
              {focusText(locale, selectedFeedback)}
            </div>
            </>}
          </div>}
        </>}
        <div className={styles.footer}>{text(locale, "cameraNote")}</div>
      </div>
    </Scrollable>
  </Panel>;
};

export class CommandTabBoundary extends React.Component<{ children: React.ReactNode; locale: Locale },
  { failed: boolean; recoveryOpen: boolean; confirmReset: boolean }> {
  state = { failed: false, recoveryOpen: false, confirmReset: false };
  static getDerivedStateFromError() { return { failed: true }; }
  componentDidCatch(error: Error) { console.error("[CS2TwitchCitizens] Commands tab render failed", error); }
  render() {
    if (!this.state.failed) return this.props.children;
    return <div className={styles.settings}>
      <p className={styles.authError}>{this.props.locale === "ru"
        ? "Не удалось открыть настройки команд. Сбросьте их или вернитесь на другую вкладку."
        : "Could not open command settings. Reset them or switch to another tab."}</p>
      <Button variant="default" className={`${styles.control} ${styles.expandButton}`}
        onSelect={() => this.setState({ recoveryOpen: !this.state.recoveryOpen, confirmReset: false })}>
        {this.props.locale === "ru" ? "Дополнительные настройки восстановления" : "Advanced recovery settings"}</Button>
      {this.state.recoveryOpen && <div className={styles.advancedContent}>
        {this.state.confirmReset ? <>
          <span>{this.props.locale === "ru" ? "Сбросить настройки команд?" : "Reset command settings?"}</span>
          <Button variant="default" className={styles.control} onSelect={() => {
            updateCommands(JSON.stringify(defaultCommands())); this.setState({ failed: false, recoveryOpen: false, confirmReset: false });
          }}>{this.props.locale === "ru" ? "Да, сбросить" : "Yes, reset"}</Button>
          <Button variant="default" className={styles.control} onSelect={() => this.setState({ confirmReset: false })}>
            {this.props.locale === "ru" ? "Отмена" : "Cancel"}</Button>
        </> : <Button variant="default" className={styles.control}
          onSelect={() => this.setState({ confirmReset: true })}>
          {this.props.locale === "ru" ? "Сбросить настройки команд" : "Reset command settings"}</Button>}
      </div>}
    </div>;
  }
}
