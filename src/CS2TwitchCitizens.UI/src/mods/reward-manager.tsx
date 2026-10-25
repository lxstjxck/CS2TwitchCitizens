import React, { useEffect, useState } from "react";
import { Button } from "cs2/ui";
import styles from "./twitch-citizens.module.scss";
import type { Locale } from "./ui-text";

type Reward = { id: string; title: string; is_enabled: boolean;
  should_redemptions_skip_request_queue: boolean };
type Redemption = { id: string; user_id: string; user_name: string; status: string; user_input?: string };
type FixedReward = { templateId: string; rewardId: string; cost: number; enabled: boolean;
  status: string; iconUrl: string };
export type RewardSnapshot = { broadcasterId: string; items: Reward[]; manageableIds: string[];
  fixedRewards: FixedReward[]; legacyActive: string[]; redemptions: Redemption[];
  selectedRewardId: string; busy: boolean; status: string; fresh: boolean };
const empty: RewardSnapshot = { broadcasterId: "", items: [], manageableIds: [],
  fixedRewards: [], legacyActive: [], redemptions: [], selectedRewardId: "", busy: false, status: "", fresh: false };
export function parseRewardSnapshot(json: string): RewardSnapshot {
  try {
    const value = JSON.parse(json) as Partial<RewardSnapshot>;
    return { broadcasterId: typeof value.broadcasterId === "string" ? value.broadcasterId : "",
      items: Array.isArray(value.items) ? value.items.filter(r => r && typeof r.id === "string") : [],
      manageableIds: Array.isArray(value.manageableIds) ? value.manageableIds.filter(id => typeof id === "string") : [],
      fixedRewards: Array.isArray(value.fixedRewards) ? value.fixedRewards.filter(r => r &&
        (r.templateId === "ShowMessage" || r.templateId === "CityAnnouncement")) : [],
      legacyActive: Array.isArray(value.legacyActive) ? value.legacyActive.filter(id => typeof id === "string") : [],
      redemptions: Array.isArray(value.redemptions) ? value.redemptions.filter(r => r && typeof r.id === "string") : [],
      selectedRewardId: value.selectedRewardId || "", busy: !!value.busy, status: value.status || "",
      fresh: value.fresh === true };
  } catch { return empty; }
}

const tr = (locale: Locale, en: string, ru: string) => locale === "ru" ? ru : en;
export const rewardPrimaryCommand = (templateId: string, state: string, cost: number) => ({
  operation: state === "Missing" || state === "Disabled" ? "FixedEnable" : "FixedCost",
  action: templateId, cost,
});
const catalog = [
  { id: "ShowMessage", icon: "✉", en: "Message to the city", ru: "Сообщение в город",
    descriptionEn: "Upper right · 8 sec", descriptionRu: "Справа сверху · 8 сек.", cost: 500 },
  { id: "CityAnnouncement", icon: "◈", en: "City announcement", ru: "Городское объявление",
    descriptionEn: "Center screen · up to 30 sec", descriptionRu: "По центру · до 30 сек.", cost: 1000 },
] as const;
const statusText = (locale: Locale, status: string) => {
  const messages: Record<string, [string, string]> = {
    Missing: ["Not created", "Не создана"],
    Synced: ["Active", "Активна"],
    Disabled: ["Off", "Выключена"],
    Conflict: ["Conflicting or unverified reward. Check Twitch before connecting.",
      "Конфликт или неподтверждённая награда. Проверьте Twitch перед подключением."],
    Updated: ["Twitch confirmed the price", "Twitch подтвердил стоимость"],
    Enabled: ["Twitch confirmed activation", "Twitch подтвердил включение"],
    FULFILLED: ["Twitch confirmed fulfillment", "Twitch подтвердил выполнение"],
    CANCELED: ["Twitch confirmed refund", "Twitch подтвердил возврат"],
    AuthorizationRequired: ["Update Twitch permissions to manage Channel Points.",
      "Для управления баллами канала обновите разрешения Twitch."],
    RewardConflict: ["A previous reward needs manual review. No duplicate was created.",
      "Ранее созданную награду нужно проверить вручную. Дубликат не создан."],
    CreateOutcomeUnknownCheckTwitch: ["Twitch may have created this reward. Check your channel rewards before trying again.",
      "Twitch мог создать награду. Проверьте награды канала перед повторной попыткой."],
    NotDisplayed: ["The game has not confirmed displaying this event.",
      "Игра ещё не подтвердила показ события."],
    HTTP403: ["Twitch denied Channel Points access. Check channel eligibility, broadcaster identity and authorization.",
      "Twitch отклонил доступ к баллам канала. Проверьте доступность функции, ID канала и авторизацию."],
    HTTP401: ["Twitch authorization expired. Reconnect Twitch if it does not recover automatically.",
      "Срок авторизации Twitch истёк. Если доступ не восстановится, подключите Twitch снова."],
    HTTP429: ["Twitch is limiting requests. Try again shortly.", "Twitch ограничил запросы. Повторите позже."],
    Checking: ["Checking Channel Points…", "Проверяем баллы канала…"],
    NetworkFailure: ["Could not reach Twitch. Try again.", "Не удалось связаться с Twitch. Повторите попытку."],
    Canceled: ["Request was canceled. Try again.", "Запрос отменён. Повторите попытку."],
    Created: ["Reward created in Twitch.", "Награда создана в Twitch."],
    RewardInternalError: ["Could not process Twitch rewards. Try again. If this continues, share the mod log with the developer.",
      "Не удалось обработать награды Twitch. Повторите попытку. Если ошибка сохранится, передайте журнал разработчику."],
  };
  if (messages[status]) return tr(locale, ...messages[status]);
  if (status === "RewardStorageError" || status === "CreateRecoveryCorrupt")
    return tr(locale, "Reward settings could not be saved or read. Check disk access, then restart the game.",
      "Не удалось прочитать или сохранить настройки наград. Проверьте доступ к диску и перезапустите игру.");
  if (status === "RewardResponseError")
    return tr(locale, "Twitch returned an unreadable reward response. Refresh rewards; if it repeats, share the mod log.",
      "Twitch вернул нечитаемый ответ о наградах. Обновите список; если ошибка повторится, передайте лог мода.");
  if (status === "RewardTlsError")
    return tr(locale, "Secure connection to Twitch failed. Check the system clock and network, then refresh rewards.",
      "Не удалось установить защищённое соединение с Twitch. Проверьте время системы и сеть, затем обновите список.");
  if (status === "RewardDnsError")
    return tr(locale, "Twitch address could not be resolved. Check DNS and network access, then refresh rewards.",
      "Не удалось определить адрес Twitch. Проверьте DNS и сеть, затем обновите список наград.");
  if (status === "RewardTimeout" || status === "RewardNetworkError" || status.startsWith("HTTP5"))
    return tr(locale, "Twitch did not complete the reward request. Check your connection and refresh rewards. If creation timed out, check Twitch before trying again.",
      "Запрос наград Twitch не завершился. Проверьте соединение и обновите список. Если создание прервалось, проверьте награды в Twitch перед повтором.");
  if (status.startsWith("NetworkOrStorageError"))
    return tr(locale, "Could not check rewards. Review the mod log, then refresh rewards.",
      "Не удалось проверить награды. Посмотрите лог мода и обновите список.");
  return tr(locale, "Check the Twitch connection and try again.", "Проверьте соединение с Twitch и повторите попытку.");
};

function RewardCard({ template, reward, locale, fresh, busy, checking, permitted, onCommand }: {
  template: typeof catalog[number]; reward: FixedReward | undefined; locale: Locale;
  fresh: boolean; busy: boolean; checking: boolean; permitted: boolean; onCommand: (command: object) => void }) {
  const [cost, setCost] = useState(String(reward?.cost ?? template.cost));
  useEffect(() => setCost(String(reward?.cost ?? template.cost)), [reward?.cost, template.cost]);
  const valid = /^[1-9][0-9]*$/.test(cost) && Number.isSafeInteger(Number(cost));
  const disabled = busy || !permitted || !fresh || reward?.status === "Conflict";
  const state = reward?.status ?? "Missing";
  const changed = reward?.rewardId && Number(cost) !== reward.cost;
  const primaryVisible = state !== "Conflict" && (state === "Missing" || state === "Disabled" || !!changed);
  const primaryLabel = busy && !checking && state === "Missing"
    ? tr(locale, "Creating reward…", "Создаём награду…") : busy
    ? tr(locale, "Working…", "Выполняется…") : state === "Missing"
    ? tr(locale, "Create reward", "Создать награду") : state === "Disabled"
    ? tr(locale, "Enable reward", "Включить награду")
    : tr(locale, "Save cost", "Сохранить стоимость");
  return <article className={styles.fixedRewardCard}>
    <div className={styles.fixedRewardHeading}>
      {reward?.iconUrl?.startsWith("https://static-cdn.jtvnw.net/")
        ? <img className={styles.rewardIcon} src={reward.iconUrl} alt="" />
        : <span className={styles.rewardMark} aria-hidden="true">{template.icon}</span>}
      <div className={styles.rewardIdentity}><strong>{tr(locale, template.en, template.ru)}</strong>
        <span>{tr(locale, template.descriptionEn, template.descriptionRu)}</span></div>
      <small className={styles.rewardState}>{fresh ? statusText(locale, state) :
        tr(locale, busy ? "Checking…" : "Needs refresh", busy ? "Проверяем…" : "Нужно обновить")}</small>
    </div>
    <div className={styles.rewardControls}><label>{tr(locale, "Cost", "Стоимость")}
      <input className={styles.search} type="text" inputMode="numeric" value={cost} maxLength={10}
        onChange={e => setCost(e.target.value)} /></label>
      {primaryVisible && <Button variant="default" className={styles.control}
        disabled={disabled || !valid || state === "Conflict"}
        onSelect={() => onCommand(rewardPrimaryCommand(template.id, state, Number(cost)))}>
        {primaryLabel}</Button>}
      {state === "Synced" && <Button variant="default" className={styles.rewardSecondary}
        disabled={disabled} onSelect={() => onCommand({ operation: "FixedDisable", action: template.id })}>
        {tr(locale, "Disable", "Отключить")}</Button>}
    </div>
  </article>;
}

export function RewardManagerPanel({ locale, snapshot, permission, connectionState, reauthorizationState,
  onCommand, onReauthorize }: {
  locale: Locale; snapshot: RewardSnapshot; rules?: object[]; permission: string;
  connectionState?: string; reauthorizationState?: string;
  onCommand: (command: object) => void; onReauthorize: () => void;
  onRuleChange?: (rewardId: string, patch: object) => void }) {
  const permitted = permission === "Allowed";
  const connected = connectionState === undefined || connectionState === "Connected";
  const connecting = connectionState === "Restoring" || connectionState === "Requesting" ||
    connectionState === "Pending" || reauthorizationState === "Requesting" ||
    reauthorizationState === "Pending";
  const error = snapshot.status === "RewardInternalError" || snapshot.status === "RewardResponseError" ||
    snapshot.status === "RewardStorageError" || snapshot.status.startsWith("HTTP") ||
    snapshot.status.includes("Conflict") || snapshot.status.includes("Timeout") ||
    snapshot.status.includes("NetworkError");
  return <div className={styles.rewardDashboard}>
    <header className={styles.rewardDashboardHead}><div><h3>{tr(locale, "Channel Points", "Баллы канала")}</h3>
      <p>{tr(locale, "Set reward costs for viewers. Rewards appear on your Twitch channel after creation.",
        "Настройте стоимость наград для зрителей. Они появятся на вашем Twitch-канале после создания.")}</p></div>
      <span className={styles.connectionBadge}>{permitted ? tr(locale, "Twitch connected", "Twitch подключён") :
        tr(locale, "Twitch unavailable", "Twitch недоступен")}</span></header>
    {!permitted && <div className={styles.authError}>
      {connecting ? tr(locale, "Connecting to Twitch…", "Подключаемся к Twitch…") :
        connected ? statusText(locale, "AuthorizationRequired") :
        tr(locale, "Connect Twitch in Settings to use Channel Points.",
          "Подключите Twitch в настройках, чтобы использовать баллы канала.")}
      {connected && !connecting && <Button variant="default" className={styles.control} onSelect={onReauthorize}>
        {tr(locale, "Update Twitch permissions", "Обновить разрешения Twitch")}</Button>}</div>}
    {permitted && snapshot.status && snapshot.status !== "Synced" && snapshot.status !== "Checking" &&
      <p className={error ? styles.authError : styles.helper}>{statusText(locale, snapshot.status)}</p>}
    {permitted && snapshot.busy && <p role="status">{statusText(locale, "Checking")}</p>}
    <div className={styles.rewardList}>{catalog.map(template => <RewardCard key={template.id} template={template}
      reward={snapshot.fixedRewards.find(r => r.templateId === template.id)} locale={locale}
      fresh={snapshot.fresh} busy={snapshot.busy} checking={snapshot.status === "Checking"}
      permitted={permitted} onCommand={onCommand} />)}</div>
    {snapshot.legacyActive.length > 0 && <p className={styles.authError}>
      {tr(locale, "Older rewards remain enabled in Twitch but no longer trigger mod actions. Review them on your channel.",
        "Старые награды остаются включёнными в Twitch, но мод больше не выполняет их действия. Проверьте их на канале.")}</p>}
    {permitted && !snapshot.busy && <Button variant="default" className={styles.rewardRefresh}
      onSelect={() => onCommand({ operation: "Sync" })}>{tr(locale, "Refresh", "Обновить")}</Button>}
  </div>;
}

export function RewardHistory({ locale, snapshot, onCommand }: { locale: Locale;
  snapshot: RewardSnapshot; onCommand: (command: object) => void }) {
  const [confirm, setConfirm] = useState<{ id: string; operation: string } | null>(null);
  const selected = snapshot.items.find(r => r.id === snapshot.selectedRewardId);
  return <section className={styles.interactivePanel}>
    <h3>{tr(locale, "Pending redemptions", "Ожидающие погашения")}</h3>
    <div className={styles.interactiveButtons}>
      {snapshot.fixedRewards.filter(r => !!r.rewardId).map(r => <Button key={r.templateId}
        variant="default" className={styles.control} disabled={snapshot.busy}
        onSelect={() => onCommand({ operation: "Redemptions", rewardId: r.rewardId })}>
        {tr(locale, "Load", "Загрузить")} {tr(locale,
          catalog.find(item => item.id === r.templateId)?.en || r.templateId,
          catalog.find(item => item.id === r.templateId)?.ru || r.templateId)}</Button>)}
      {snapshot.items.filter(item => snapshot.manageableIds.includes(item.id) &&
        !snapshot.fixedRewards.some(r => r.rewardId === item.id)).map(item =>
        <Button key={item.id} variant="default" className={styles.control} disabled={snapshot.busy}
          onSelect={() => onCommand({ operation: "Redemptions", rewardId: item.id })}>
          {tr(locale, "Load older reward", "Загрузить старую награду")}: {item.title}</Button>)}
    </div>
    {selected && snapshot.redemptions.map(item => <div key={item.id} className={styles.interactiveEvent}>
      <span>{item.user_name || item.user_id} · {item.status}</span>
      {item.user_input && <p>{item.user_input}</p>}
      {item.status === "UNFULFILLED" && !selected.should_redemptions_skip_request_queue &&
        <div className={styles.interactiveButtons}>
          <Button variant="default" className={styles.control} disabled={snapshot.busy}
            onSelect={() => setConfirm({ id: item.id, operation: "Fulfill" })}>
            {tr(locale, "Fulfill", "Подтвердить")}</Button>
          <Button variant="default" className={styles.control} disabled={snapshot.busy}
            onSelect={() => setConfirm({ id: item.id, operation: "Cancel" })}>
            {tr(locale, "Cancel and refund", "Отменить и вернуть баллы")}</Button>
        </div>}
    </div>)}
    {confirm && selected && <div className={styles.confirmBox}>
      <p>{confirm.operation === "Fulfill" ? tr(locale, "Confirm that this event appeared in game?",
        "Подтвердить показ события в игре?") : tr(locale, "Cancel and refund points?",
        "Отменить и вернуть баллы?")}</p>
      <Button variant="default" className={styles.control} disabled={snapshot.busy}
        onSelect={() => { onCommand({ operation: confirm.operation, rewardId: selected.id,
          redemptionId: confirm.id }); setConfirm(null); }}>{tr(locale, "Confirm", "Подтвердить")}</Button>
      <Button variant="default" className={styles.control} onSelect={() => setConfirm(null)}>
        {tr(locale, "Back", "Назад")}</Button>
    </div>}
  </section>;
}
