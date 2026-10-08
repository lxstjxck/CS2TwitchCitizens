import React, { useEffect, useMemo, useState } from "react";
import { bindLocalValue, bindTriggerWithArgs, bindValue, useValue } from "cs2/api";
import { Button, Panel, Scrollable } from "cs2/ui";
import styles from "./twitch-citizens.module.scss";
import { filterViewers } from "./viewer-search";

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
  previousLives: Array<{ lifeId: string; status: string; startGameDate: string; endGameDate: string }>;
};
type Snapshot = {
  twitchStatus: string;
  channelId: string;
  gameLoaded: boolean;
  viewers: Viewer[];
};
type FocusFeedback = { viewerId: string; result: string; sequence: number };

const group = "cs2twitchcitizens";
const snapshotBinding = bindValue<string>(group, "snapshot", "{}");
const focusResultBinding = bindValue<string>(group, "focusResult", "");
const focusCitizen = bindTriggerWithArgs<[string]>(group, "focusCitizen");
const followCitizen = bindTriggerWithArgs<[string]>(group, "followCitizen");
const stopFollowing = bindTriggerWithArgs<[string]>(group, "stopFollowing");
const panelOpen = bindLocalValue(false);

const statusText: Record<string, string> = {
  Disabled: "Выключен",
  Connecting: "Подключение",
  Connected: "Подключён",
  Reconnecting: "Переподключение",
  AuthenticationError: "Ошибка авторизации",
  Disconnected: "Отключён",
};
const focusText: Record<string, string> = {
  CitizenNotFound: "Житель не найден",
  PositionUnavailable: "Положение жителя недоступно",
  CameraUnavailable: "Камера сейчас недоступна",
  FocusRequested: "Перемещение камеры запрошено",
  FocusConfirmed: "Камера находится рядом с жителем",
  FocusFailed: "Не удалось переместить камеру",
  FollowRequested: "Слежение запрошено; проверьте камеру в игре",
  FollowStopped: "Слежение остановлено",
};
const lifeStatusText: Record<string, string> = {
  Active: "Привязан",
  Deceased: "Умер",
  Missing: "Местонахождение неизвестно",
};

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

export const TwitchCitizensButton = () => {
  const open = useValue(panelOpen);
  const snapshotJson = useValue(snapshotBinding);
  const snapshot = useMemo(() => parseSnapshot(snapshotJson), [snapshotJson]);

  return <div className={styles.root}>
    <Button variant="default" className={styles.openButton} onSelect={() => {
      console.info(`[CS2TwitchCitizens.UI] toggle panel open=${!open}`);
      panelOpen.update(!open);
    }}>
      Twitch Citizens {snapshot.viewers.length > 0 ? `(${snapshot.viewers.length})` : ""}
    </Button>
  </div>;
};

export const TwitchCitizensPanel = () => {
  const open = useValue(panelOpen);
  const [settings, setSettings] = useState(false);
  const [search, setSearch] = useState("");
  const [selectedId, setSelectedId] = useState("");
  const snapshotJson = useValue(snapshotBinding);
  const snapshot = useMemo(() => parseSnapshot(snapshotJson), [snapshotJson]);
  const focusResultJson = useValue(focusResultBinding);
  const focusResult = useMemo(() => {
    try { return JSON.parse(focusResultJson) as FocusFeedback; }
    catch { return null; }
  }, [focusResultJson]);
  const viewers = useMemo(() => filterViewers(snapshot.viewers, search), [snapshot.viewers, search]);
  const selected = snapshot.viewers.find((viewer) => viewer.twitchUserId === selectedId);
  useEffect(() => {
    if (open) console.info("[CS2TwitchCitizens.UI] panel mounted");
  }, [open]);

  if (!open) return null;
  return <Panel className={styles.panel} header={<span>Twitch Citizens</span>} onClose={() => panelOpen.update(false)}>
      <div className={styles.content}>
        <div className={styles.summary}>
          <span className={styles.status} data-status={snapshot.twitchStatus}>
            Twitch: {statusText[snapshot.twitchStatus] ?? snapshot.twitchStatus}
          </span>
          <span>Зрителей: {snapshot.viewers.length}</span>
        </div>
        <div className={styles.channel}>Канал ID: {snapshot.channelId || "не настроен"}</div>
        <div className={styles.tabs}>
          <Button variant={settings ? "text" : "primary"} onSelect={() => setSettings(false)}>Жители</Button>
          <Button variant={settings ? "primary" : "text"} onSelect={() => setSettings(true)}>Настройки</Button>
        </div>
        {settings ? <div className={styles.settings}>
          <div>Канал: {snapshot.channelId || "не настроен"}</div>
          <div>Состояние: {statusText[snapshot.twitchStatus] ?? snapshot.twitchStatus}</div>
          <div>Настройки доступны только для чтения. Измените локальный конфигурационный файл и перезапустите игру.</div>
          <div>Авторизация через браузер будет добавлена в будущем.</div>
        </div> : <>
          {!snapshot.gameLoaded && <div className={styles.notice}>Загрузите город, чтобы видеть жителей.</div>}
          <input className={styles.search} type="text" value={search} onChange={(event) => setSearch(event.target.value)}
            placeholder="Поиск по Twitch-имени" aria-label="Поиск зрителя" />
          <Scrollable className={styles.list} vertical trackVisibility="scrollable">
            {viewers.length === 0 && <div className={styles.empty}>Зрители не найдены</div>}
            {viewers.map((viewer) => <div className={styles.row} key={viewer.twitchUserId}>
              <Button variant="text" className={styles.viewerName} onSelect={() => setSelectedId(viewer.twitchUserId)}>
                {viewer.displayName || viewer.login || viewer.twitchUserId}
              </Button>
              <div className={styles.rowMeta}>{viewer.age || "Возраст неизвестен"} · {lifeStatusText[viewer.currentLifeStatus] ?? "Устарел"} · Жизней: {viewer.totalLives || 0}</div>
              <Button variant="default" disabled={!viewer.isValid || !viewer.positionAvailable}
                onSelect={() => { setSelectedId(viewer.twitchUserId); focusCitizen(viewer.twitchUserId); }}>Найти</Button>
            </div>)}
          </Scrollable>
          {selected && <div className={styles.card}>
            <strong>{selected.displayName || selected.login}</strong>
            <div>Логин: {selected.login || "—"}</div>
            <div>Имя в городе: {selected.citizenName || "—"}</div>
            <div>Возраст: {selected.age || "—"}</div>
            <div>Дом: {selected.hasHome ? "есть" : "нет"} · Работа: {selected.hasWorkplace ? "есть" : "нет"}</div>
            <div>Местоположение: {selected.positionAvailable ? "доступно" : "недоступно"}</div>
            <div>Жизней: {selected.totalLives || 0} · Статус: {lifeStatusText[selected.currentLifeStatus] ?? "неизвестен"}</div>
            <Button variant="primary" disabled={!selected.isValid || !selected.positionAvailable}
              onSelect={() => focusCitizen(selected.twitchUserId)}>Найти в городе</Button>
            <Button variant="default" disabled={!selected.isValid || !selected.positionAvailable}
              onSelect={() => followCitizen(selected.twitchUserId)}>Следить</Button>
            <Button variant="default" onSelect={() => stopFollowing(selected.twitchUserId)}>Остановить слежение</Button>
          </div>}
          {focusResult?.viewerId === selectedId && <div className={styles.notice}>
            {focusText[focusResult.result] ?? focusResult.result}
          </div>}
        </>}
      </div>
    </Panel>;
};
