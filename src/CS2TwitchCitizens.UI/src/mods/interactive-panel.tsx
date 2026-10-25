import React, { useEffect, useState } from "react";
import { RewardHistory, RewardManagerPanel, RewardSnapshot } from "./reward-manager";
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
  cooldownSeconds: number; displaySeconds: number; autoFulfill?: boolean };
export type InteractiveSettings = { version: number; enabled: boolean; destructiveEnabled: boolean; rewards: RewardRule[] };
export const actions = ["ShowMessage", "CityAnnouncement"] as const;
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
  ShowMessage: label(locale, "Message to the city", "Сообщение в город"),
  CityAnnouncement: label(locale, "City announcement", "Городское объявление"),
  CityCelebration: label(locale, "City celebration", "Праздник города"),
  EmergencyAlert: label(locale, "Emergency alert", "Тревожное объявление"),
  GameDisaster: label(locale, "Game disaster: unavailable", "Игровая катастрофа: недоступна"),
  CitizenMessage: label(locale, "Citizen message", "Сообщение жителя"),
} as Record<string, string>)[action] || action;

export function InteractiveNotice({ event, locale, onDismiss, onFind, onShown }: { event: InteractiveEvent | null;
  locale: Locale; onDismiss: () => void; onFind: (id: string) => void; onShown: (id: string) => void }) {
  useEffect(() => { if (event?.id) onShown(event.id); }, [event?.id]);
  if (!event) return null;
  if (event.type === "CityAnnouncement") return <div className={styles.cityAnnouncement}
      role="status" data-type={event.type}>
    <div className={styles.interactiveNoticeHead}>
      <strong><span className={styles.rewardMark} aria-hidden="true">◈</span>
        {label(locale, "CITY ANNOUNCEMENT", "ГОРОДСКОЕ ОБЪЯВЛЕНИЕ")}</strong>
      <Button variant="default" className={styles.control} onSelect={onDismiss}
        aria-label={label(locale, "Dismiss", "Скрыть")}>×</Button>
    </div>
    <p className={styles.announcementText}>{event.text}</p>
    <span>{label(locale, "Author: ", "Автор: ")}{event.displayName || label(locale, "Viewer", "Зритель")}</span>
    {event.localTest && <small>{label(locale, "Local test", "Локальный тест")}</small>}
    <Button variant="default" className={styles.control} onSelect={onDismiss}>
      {label(locale, "Close announcement", "Закрыть объявление")}</Button>
  </div>;
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
  rewardSnapshot, rewardManagePermission, connectionState, reauthorizationState, onRewardCommand, onReauthorize,
  onSave, onTest }: { locale: Locale; state: InteractiveState; config: InteractiveSettings; status: string;
  pointsStatus: string; pointsPermission: string; onSave: (value: InteractiveSettings) => void;
  onTest: (action: string) => void; rewardSnapshot: RewardSnapshot; rewardManagePermission: string;
  connectionState?: string; reauthorizationState?: string;
  onRewardCommand: (command: object) => void; onReauthorize: () => void }) {
  const [section, setSection] = useState<"rewards" | "preview" | "history" | "settings">("rewards");
  const update = (change: Partial<InteractiveSettings>) => onSave({ ...config, ...change });
  return <div className={styles.interactivePanel}>
    <div className={styles.interactiveSections}>
      {(["rewards", "preview", "history", "settings"] as const).map(name =>
        <Button key={name} variant="default" className={styles.control} selected={section === name}
          data-selected={section === name} onSelect={() => setSection(name)}>
          {({ preview: label(locale, "Preview", "Предпросмотр"), rewards: label(locale, "Channel Points", "Баллы канала"),
            history: label(locale, "History", "История"),
            settings: label(locale, "Settings", "Настройки") })[name]}</Button>)}
    </div>
    {section === "preview" && <div className={styles.previewPanel}>
      <strong>{label(locale, "Preview in your city", "Предпросмотр в городе")}</strong>
      <p>{label(locale, "Local previews do not spend Channel Points.",
        "Локальный предпросмотр не расходует баллы канала.")}</p>
      <div className={styles.interactiveButtons}>
        <Button variant="default" className={styles.control} onSelect={() => onTest("ShowMessage")}>
          {label(locale, "Preview message", "Проверить сообщение")}</Button>
        <Button variant="default" className={styles.control} onSelect={() => onTest("CityAnnouncement")}>
          {label(locale, "Preview announcement", "Проверить объявление")}</Button></div>
    </div>}
    {section === "settings" && <>
    <strong>{label(locale, "Interactive", "Интерактив")}</strong>
    <div className={styles.settingsRow}><span>{label(locale, "Channel Points", "Баллы канала")}</span>
      <strong>{pointsPermission === "Allowed" ? pointsStatus : label(locale, "Authorization required", "Требуется авторизация")}</strong></div>
    <div className={styles.settingsRow}><span>{label(locale, "Interactive events", "Интерактивные события")}</span>
      <Button variant="default" className={styles.control} onSelect={() => update({ enabled: !config.enabled })}>
        {config.enabled ? label(locale, "On", "Включено") : label(locale, "Off", "Выключено")}</Button></div>
    <p className={styles.helper}>{label(locale,
      "Create rewards in Channel Points. Redemption confirmation is manual until live testing is complete.",
      "Создавайте награды во вкладке «Баллы канала». Подтверждение погашений остаётся ручным до проверки в игре.")}</p>
    {status && <p className={styles.authError}>{status}</p>}
    {state.ledgerError && <p className={styles.authError}>{label(locale, "Redemption history could not be read; rewards are blocked.",
      "Не удалось прочитать историю наград; выполнение заблокировано.")}</p>}
    </>}
    {section === "rewards" && <RewardManagerPanel locale={locale} snapshot={rewardSnapshot}
      connectionState={connectionState} reauthorizationState={reauthorizationState}
      rules={config.rewards} permission={rewardManagePermission} onCommand={onRewardCommand}
      onReauthorize={onReauthorize} onRuleChange={(id, patch) => onSave({ ...config,
        rewards: config.rewards.map(rule => rule.rewardId === id ? { ...rule, ...patch } : rule) })} />}
    {section === "history" && <>
    <div className={styles.historyMetrics}>
      <div><span>{label(locale, "Queued", "В очереди")}</span><strong>{state.queued}</strong></div>
      <div><span>{label(locale, "Missed", "Пропущено")}</span><strong>{state.dropped}</strong></div>
    </div>
    {state.error && <p className={styles.authError}>{label(locale, "Last result: ", "Последний результат: ")}{state.error}</p>}
    <strong>{label(locale, "Recent events", "Последние события")}</strong>
    {state.events.length === 0 && <p>{label(locale, "No events yet", "Событий пока нет")}</p>}
    {state.events.map(event => <div key={event.id} className={styles.interactiveEvent}>
      <strong>{actionLabel(event.type, locale)}</strong> · {event.displayName || "Twitch"}
      {event.receivedAt && <small>{event.receivedAt}</small>}
      <small>{event.state}</small>
      {event.localTest && <small> {label(locale, "(local test)", "(локальный тест)")}</small>}
      <p>{event.text}</p>
      <small>{event.result}</small>
    </div>)}
    <RewardHistory locale={locale} snapshot={rewardSnapshot} onCommand={onRewardCommand} />
    </>}
  </div>;
}
