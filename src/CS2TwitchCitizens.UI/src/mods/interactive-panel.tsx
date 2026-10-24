import React, { useEffect, useState } from "react";
import { Button } from "cs2/ui";
import styles from "./twitch-citizens.module.scss";
import type { Locale } from "./ui-text";

export type InteractiveEvent = {
  id: string; type: string; twitchUserId: string; displayName: string; redemptionId: string;
  state: string; receivedAt: string; text: string; result: string; localTest: boolean;
  citizenName: string; canFindCitizen: boolean; displaySeconds: number; rewardIconUrl: string;
};
export type InteractiveState = { events: InteractiveEvent[]; active: InteractiveEvent | null;
  queued: number; dropped: number; error: string; ledgerError: boolean };
type RewardRule = { rewardId: string; action: string; enabled: boolean; maxTextLength: number;
  cooldownSeconds: number; displaySeconds: number };
export type InteractiveSettings = { version: number; enabled: boolean; destructiveEnabled: boolean; rewards: RewardRule[] };
export const actions = ["ShowMessage", "CityAnnouncement", "CityCelebration", "EmergencyAlert", "GameDisaster"] as const;
const defaults: InteractiveSettings = { version: 1, enabled: false, destructiveEnabled: false, rewards: [] };

export function parseInteractiveState(json: string): InteractiveState {
  try {
    const value = JSON.parse(json) as Partial<InteractiveState>;
    return { events: Array.isArray(value.events) ? value.events.slice(0, 20) : [],
      active: value.active && typeof value.active.text === "string" ? value.active : null,
      queued: Number.isFinite(value.queued) ? Math.max(0, value.queued!) : 0,
      dropped: Number.isFinite(value.dropped) ? Math.max(0, value.dropped!) : 0,
      error: typeof value.error === "string" ? value.error : "", ledgerError: value.ledgerError === true };
  } catch { return { events: [], active: null, queued: 0, dropped: 0, error: "", ledgerError: false }; }
}
export function parseInteractiveSettings(json: string): InteractiveSettings {
  try {
    const value = JSON.parse(json) as Partial<InteractiveSettings>;
    return { version: 1, enabled: value.enabled === true,
      destructiveEnabled: value.destructiveEnabled === true,
      rewards: Array.isArray(value.rewards) ? value.rewards.filter(r => r && typeof r.rewardId === "string" &&
        actions.includes(r.action as typeof actions[number])) : [] };
  } catch { return defaults; }
}

const label = (locale: Locale, en: string, ru: string) => locale === "ru" ? ru : en;
const actionLabel = (action: string, locale: Locale) => ({
  ShowMessage: label(locale, "Viewer message", "Сообщение зрителя"),
  CityAnnouncement: label(locale, "City announcement", "Городское объявление"),
  CityCelebration: label(locale, "City celebration", "Праздник города"),
  EmergencyAlert: label(locale, "Emergency alert", "Тревожное объявление"),
  GameDisaster: label(locale, "Game disaster: unavailable", "Игровая катастрофа: недоступна"),
  CitizenMessage: label(locale, "Citizen message", "Сообщение жителя"),
} as Record<string, string>)[action] || action;

export function InteractiveNotice({ event, locale, onDismiss, onFind }: { event: InteractiveEvent | null;
  locale: Locale; onDismiss: () => void; onFind: (id: string) => void }) {
  if (!event) return null;
  return <div className={styles.interactiveNotice} role="status" data-type={event.type}>
    <div className={styles.interactiveNoticeHead}>
      <strong><span className={styles.rewardMark} aria-hidden="true">★</span>
        {event.rewardIconUrl?.startsWith("https://static-cdn.jtvnw.net/") &&
          <img className={styles.rewardIcon} src={event.rewardIconUrl} alt="" />}
        {actionLabel(event.type, locale)}</strong>
      <Button variant="default" className={styles.control} onSelect={onDismiss}
        aria-label={label(locale, "Dismiss", "Скрыть")}>×</Button>
    </div>
    <span>{event.citizenName || event.displayName || label(locale, "Viewer", "Зритель")}</span>
    <p>{event.text}</p>
    {event.localTest && <small>{label(locale, "Local test · no Channel Points spent", "Локальный тест · баллы не списаны")}</small>}
    {event.canFindCitizen && <Button variant="default" className={styles.control}
      onSelect={() => onFind(event.twitchUserId)}>{label(locale, "Find citizen", "Найти жителя")}</Button>}
  </div>;
}

export function InteractivePanel({ locale, state, config, status, pointsStatus, pointsPermission,
  onSave, onTest }: { locale: Locale; state: InteractiveState; config: InteractiveSettings; status: string;
  pointsStatus: string; pointsPermission: string; onSave: (value: InteractiveSettings) => void;
  onTest: (action: string) => void }) {
  const [draftIds, setDraftIds] = useState<Record<string, string>>({});
  const [section, setSection] = useState<"messages" | "rewards" | "events" | "queue" | "settings">("messages");
  useEffect(() => setDraftIds(config.rewards.reduce<Record<string, string>>((ids, rule) => {
    ids[rule.action] = rule.rewardId; return ids;
  }, {})), [config.rewards]);
  const update = (change: Partial<InteractiveSettings>) => onSave({ ...config, ...change });
  const setRule = (action: string, patch: Partial<RewardRule>) => {
    const old = config.rewards.find(r => r.action === action);
    const next = { rewardId: "", action, enabled: true, maxTextLength: 180,
      cooldownSeconds: 15, displaySeconds: 8, ...old, ...patch };
    onSave({ ...config, rewards: [...config.rewards.filter(r => r.action !== action), next] });
  };
  return <div className={styles.interactivePanel}>
    <div className={styles.interactiveSections}>
      {(["messages", "rewards", "events", "queue", "settings"] as const).map(name =>
        <Button key={name} variant="default" className={styles.control} selected={section === name}
          data-selected={section === name} onSelect={() => setSection(name)}>
          {({ messages: label(locale, "Messages", "Сообщения"), rewards: label(locale, "Twitch rewards", "Награды Twitch"),
            events: label(locale, "Events", "События"), queue: label(locale, "Queue", "Очередь"),
            settings: label(locale, "Settings", "Настройки") })[name]}</Button>)}
    </div>
    {section === "messages" && <div className={styles.interactiveCard}>
      <strong>{label(locale, "Message previews", "Предпросмотр сообщений")}</strong>
      <p>{label(locale, "Local tests show sample text in the city without using Twitch or spending points.",
        "Локальные тесты показывают пример текста в городе без Twitch и списания баллов.")}</p>
      <div className={styles.interactiveButtons}>{actions.filter(a => a !== "GameDisaster").map(action =>
        <Button key={action} variant="default" className={styles.control} onSelect={() => onTest(action)}>
          {actionLabel(action, locale)}</Button>)}</div>
    </div>}
    {section === "settings" && <>
    <strong>{label(locale, "Interactive", "Интерактив")}</strong>
    <div className={styles.settingsRow}><span>{label(locale, "Channel Points", "Баллы канала")}</span>
      <strong>{pointsPermission === "Allowed" ? pointsStatus : label(locale, "Authorization required", "Требуется авторизация")}</strong></div>
    <div className={styles.settingsRow}><span>{label(locale, "Interactive events", "Интерактивные события")}</span>
      <Button variant="default" className={styles.control} onSelect={() => update({ enabled: !config.enabled })}>
        {config.enabled ? label(locale, "On", "Включено") : label(locale, "Off", "Выключено")}</Button></div>
    <div className={styles.settingsRow}><span>{label(locale, "Destructive events (future)", "Разрушительные события (позже)")}</span>
      <Button variant="default" className={styles.control} disabled data-disabled="true">
        {label(locale, "Unavailable", "Недоступно")}</Button></div>
    <p className={styles.helper}>{label(locale,
      "Create custom rewards in Twitch, then paste each exact Reward ID here. Redemptions are shown in game; they are not marked fulfilled by this mod.",
      "Создайте награды в Twitch и вставьте точные Reward ID. Награды показываются в игре; мод не отмечает их выполненными в Twitch.")}</p>
    {status && <p className={styles.authError}>{status}</p>}
    {state.ledgerError && <p className={styles.authError}>{label(locale, "Redemption history could not be read; rewards are blocked.",
      "Не удалось прочитать историю наград; выполнение заблокировано.")}</p>}
    </>}
    {section === "rewards" && actions.map(action => {
      const rule = config.rewards.find(r => r.action === action);
      return <div key={action} className={styles.interactiveCard}>
        <strong>{actionLabel(action, locale)}</strong>
        {action === "GameDisaster" ? <p>{label(locale, "Unavailable until game API verification.",
          "Недоступно до проверки игрового API.")}</p> : <>
          <label>{label(locale, "Reward ID", "Reward ID")}</label>
          <input className={styles.search} type="text" value={draftIds[action] ?? rule?.rewardId ?? ""}
            onChange={e => setDraftIds({ ...draftIds, [action]: e.target.value })} />
          <div className={styles.interactiveButtons}>
            <Button variant="default" className={styles.control} onSelect={() => {
              const id = (draftIds[action] ?? rule?.rewardId ?? "").trim();
              if (id) setRule(action, { rewardId: id });
            }}>{label(locale, "Save ID", "Сохранить ID")}</Button>
            {rule && <Button variant="default" className={styles.control}
              onSelect={() => setRule(action, { enabled: !rule.enabled })}>
              {rule.enabled ? label(locale, "Enabled", "Включено") : label(locale, "Disabled", "Выключено")}</Button>}
            <Button variant="default" className={styles.control} onSelect={() => onTest(action)}>
              {label(locale, "Local test", "Локальный тест")}</Button>
          </div>
          {rule && <div className={styles.interactiveLimits}>
            <label>{label(locale, "Max text", "Макс. текст")}
              <input type="text" value={rule.maxTextLength} onChange={e => {
                const n = Number(e.target.value); if (Number.isInteger(n) && n >= 1 && n <= 300)
                  setRule(action, { maxTextLength: n }); }} /></label>
            <label>{label(locale, "Cooldown (s)", "Пауза (с)")}
              <input type="text" value={rule.cooldownSeconds} onChange={e => {
                const n = Number(e.target.value); if (Number.isInteger(n) && n >= 0 && n <= 3600)
                  setRule(action, { cooldownSeconds: n }); }} /></label>
            <label>{label(locale, "Display (s)", "Показ (с)")}
              <input type="text" value={rule.displaySeconds} onChange={e => {
                const n = Number(e.target.value); if (Number.isInteger(n) && n >= 2 && n <= 60)
                  setRule(action, { displaySeconds: n }); }} /></label>
          </div>}
        </>}
      </div>;
    })}
    {section === "queue" && <div className={styles.settingsRow}><span>{label(locale, "Queue", "Очередь")}</span><strong>{state.queued}</strong></div>}
    {section === "queue" && <div className={styles.settingsRow}><span>{label(locale, "Dropped (queue full)", "Пропущено (очередь заполнена)")}</span>
      <strong>{state.dropped}</strong></div>}
    {section === "events" && <>
    {state.error && <p className={styles.authError}>{label(locale, "Last result: ", "Последний результат: ")}{state.error}</p>}
    <strong>{label(locale, "Recent events", "Последние события")}</strong>
    {state.events.length === 0 && <p>{label(locale, "No events yet", "Событий пока нет")}</p>}
    {state.events.map(event => <div key={event.id} className={styles.interactiveEvent}>
      <strong>{actionLabel(event.type, locale)}</strong> · {event.displayName || "Twitch"}
      {event.localTest && <small> {label(locale, "(local test)", "(локальный тест)")}</small>}
      <p>{event.text}</p>
      <small>{event.result}</small>
    </div>)}
    </>}
  </div>;
}
