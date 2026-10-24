import React, { useEffect, useState } from "react";
import { Button } from "cs2/ui";
import styles from "./twitch-citizens.module.scss";
import { Locale } from "./ui-text";

type Reward = { id: string; title: string; prompt: string; cost: number; background_color: string;
  is_enabled: boolean; is_paused: boolean; is_user_input_required: boolean;
  max_per_stream_setting?: { is_enabled: boolean; max_per_stream: number };
  max_per_user_per_stream_setting?: { is_enabled: boolean; max_per_user_per_stream: number };
  global_cooldown_setting?: { is_enabled: boolean; global_cooldown_seconds: number };
  should_redemptions_skip_request_queue: boolean; image?: { url_1x: string };
  default_image?: { url_1x: string } };
type Redemption = { id: string; broadcaster_id: string; user_id: string; user_name: string;
  status: string; user_input?: string; reward?: { id: string } };
type RewardRule = { rewardId: string; action: string; enabled: boolean;
  maxTextLength: number; cooldownSeconds: number; displaySeconds: number; autoFulfill?: boolean };
export type RewardSnapshot = { broadcasterId: string; items: Reward[]; manageableIds: string[];
  redemptions: Redemption[]; selectedRewardId: string; busy: boolean; status: string };
const empty: RewardSnapshot = { broadcasterId: "", items: [], manageableIds: [],
  redemptions: [], selectedRewardId: "", busy: false, status: "" };
export function parseRewardSnapshot(json: string): RewardSnapshot {
  try {
    const value = JSON.parse(json) as Partial<RewardSnapshot>;
    return { broadcasterId: typeof value.broadcasterId === "string" ? value.broadcasterId : "",
      items: Array.isArray(value.items) ? value.items.filter(r => r && typeof r.id === "string") : [],
      manageableIds: Array.isArray(value.manageableIds) ? value.manageableIds.filter(id => typeof id === "string") : [],
      redemptions: Array.isArray(value.redemptions) ? value.redemptions.filter(r => r && typeof r.id === "string") : [],
      selectedRewardId: value.selectedRewardId || "", busy: !!value.busy, status: value.status || "" };
  } catch { return empty; }
}

type Edit = { title: string; prompt: string; cost: number; background_color: string;
  is_enabled: boolean; is_paused: boolean; is_user_input_required: boolean;
  is_max_per_stream_enabled: boolean; max_per_stream: number;
  is_max_per_user_per_stream_enabled: boolean; max_per_user_per_stream: number;
  is_global_cooldown_enabled: boolean; global_cooldown_seconds: number;
  should_redemptions_skip_request_queue: boolean };
const blank = (): Edit => ({ title: "", prompt: "", cost: 100, background_color: "#00E5CB",
  is_enabled: true, is_paused: false, is_user_input_required: true,
  is_max_per_stream_enabled: false, max_per_stream: 1,
  is_max_per_user_per_stream_enabled: false, max_per_user_per_stream: 1,
  is_global_cooldown_enabled: false, global_cooldown_seconds: 60,
  should_redemptions_skip_request_queue: false });
const fromReward = (reward: Reward): Edit => ({
  title: reward.title || "", prompt: reward.prompt || "", cost: reward.cost || 1,
  background_color: reward.background_color || "#00E5CB", is_enabled: !!reward.is_enabled,
  is_paused: !!reward.is_paused, is_user_input_required: !!reward.is_user_input_required,
  is_max_per_stream_enabled: !!reward.max_per_stream_setting?.is_enabled,
  max_per_stream: reward.max_per_stream_setting?.max_per_stream || 1,
  is_max_per_user_per_stream_enabled: !!reward.max_per_user_per_stream_setting?.is_enabled,
  max_per_user_per_stream: reward.max_per_user_per_stream_setting?.max_per_user_per_stream || 1,
  is_global_cooldown_enabled: !!reward.global_cooldown_setting?.is_enabled,
  global_cooldown_seconds: reward.global_cooldown_setting?.global_cooldown_seconds || 60,
  should_redemptions_skip_request_queue: !!reward.should_redemptions_skip_request_queue,
});
const actions = ["ShowMessage", "CityAnnouncement", "CityCelebration", "EmergencyAlert"];
const tr = (locale: Locale, en: string, ru: string) => locale === "ru" ? ru : en;
const statusText = (locale: Locale, status: string): string => {
  const messages: Record<string, [string, string]> = {
    Synced: ["Synced with Twitch", "Синхронизировано с Twitch"],
    Created: ["Reward created in Twitch", "Награда создана в Twitch"],
    Updated: ["Twitch confirmed the update", "Twitch подтвердил изменение"],
    Deleted: ["Twitch confirmed deletion", "Twitch подтвердил удаление"],
    Saved: ["Action binding saved", "Привязка сохранена"],
    FULFILLED: ["Twitch confirmed fulfillment", "Twitch подтвердил выполнение"],
    CANCELED: ["Twitch confirmed cancellation and refund", "Twitch подтвердил отмену и возврат"],
    ServerChangedSyncRequired: ["Reward changed in Twitch. Sync before saving.", "Награда изменилась в Twitch. Синхронизируйте перед сохранением."],
    CreateOutcomeUnknownCheckTwitch: ["Creation may have succeeded. Check Twitch and sync; another create is blocked.",
      "Создание могло завершиться. Проверьте Twitch и синхронизируйте; повторное создание заблокировано."],
    CreateNeedsReconciliation: ["Sync the uncertain creation before creating another reward.",
      "Сначала сверьте неопределённое создание с Twitch."],
    NotDisplayed: ["The game has not confirmed showing this event.", "Игра ещё не подтвердила показ события."],
    AuthorizationRequired: ["Twitch authorization or app ownership required.", "Требуется авторизация Twitch или владение наградой."],
    RewardLimit: ["The channel has reached 50 custom rewards.", "Достигнут лимит 50 пользовательских наград."],
  };
  if (status.startsWith("NeedsSync:")) return tr(locale,
    "Twitch changed this reward; local save needs recovery. Sync using Reward ID ",
    "Награда изменена в Twitch; локальная запись требует восстановления. Синхронизируйте ID ") + status.slice(10);
  return messages[status] ? tr(locale, ...messages[status]) : status;
};
const valid = (edit: Edit) => edit.title.trim().length > 0 && edit.title.length <= 45 &&
  edit.prompt.length <= 200 && Number.isInteger(edit.cost) && edit.cost >= 1 &&
  /^#[0-9a-fA-F]{6}$/.test(edit.background_color) &&
  (!edit.is_max_per_stream_enabled || Number.isInteger(edit.max_per_stream) && edit.max_per_stream > 0) &&
  (!edit.is_max_per_user_per_stream_enabled || Number.isInteger(edit.max_per_user_per_stream) && edit.max_per_user_per_stream > 0) &&
  (!edit.is_global_cooldown_enabled || Number.isInteger(edit.global_cooldown_seconds) && edit.global_cooldown_seconds > 0 && edit.global_cooldown_seconds <= 604800);

export function RewardManagerPanel({ locale, snapshot, rules, permission, onCommand, onReauthorize, onRuleChange }: {
  locale: Locale; snapshot: RewardSnapshot; rules: RewardRule[];
  permission: string; onCommand: (command: object) => void; onReauthorize: () => void;
  onRuleChange: (rewardId: string, patch: Partial<RewardRule>) => void }) {
  const [selectedId, setSelectedId] = useState("");
  const [draft, setDraft] = useState<Edit>(blank);
  const [baseline, setBaseline] = useState("");
  const [action, setAction] = useState("ShowMessage");
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [confirmRedemption, setConfirmRedemption] = useState<{ id: string; operation: string } | null>(null);
  const selected = snapshot.items.find(r => r.id === selectedId);
  const serverEdit = selected ? fromReward(selected) : null;
  const serverSignature = serverEdit ? JSON.stringify(serverEdit) : "";
  const serverChanged = !!selected && baseline !== serverSignature;
  const owned = !!selected && snapshot.manageableIds.includes(selected.id);
  const rule = rules.find(r => r.rewardId === selectedId);
  const choose = (id: string) => {
    const reward = snapshot.items.find(r => r.id === id);
    setSelectedId(id); setDraft(reward ? fromReward(reward) : blank());
    setBaseline(reward ? JSON.stringify(fromReward(reward)) : "");
    setAction(rules.find(r => r.rewardId === id)?.action || "ShowMessage");
    setConfirmDelete(false); setConfirmRedemption(null);
  };
  useEffect(() => {
    if (selectedId && !snapshot.items.some(r => r.id === selectedId)) choose("");
  }, [snapshot.items, selectedId]);
  useEffect(() => {
    if (selected && snapshot.status === "Updated" && serverEdit) {
      setDraft(serverEdit); setBaseline(serverSignature);
    }
  }, [snapshot.status, serverSignature]);
  const update = (patch: Partial<Edit>) => setDraft({ ...draft, ...patch });
  const number = (key: keyof Edit, value: string) => {
    const n = Number(value); if (Number.isInteger(n) && n >= 0) update({ [key]: n } as Partial<Edit>);
  };
  const toggle = (key: keyof Edit, caption: string) =>
    <Button variant="default" className={styles.control} onSelect={() => update({ [key]: !draft[key] } as Partial<Edit>)}>
      {caption}: {draft[key] ? tr(locale, "On", "Да") : tr(locale, "Off", "Нет")}</Button>;
  const input = (caption: string, key: keyof Edit, limit?: number) =>
    <label>{caption}<input className={styles.search} type="text" value={String(draft[key])}
      maxLength={limit} onChange={e => typeof draft[key] === "number"
        ? number(key, e.target.value) : update({ [key]: e.target.value } as Partial<Edit>)} /></label>;
  const canCreate = permission === "Allowed" && snapshot.items.length < 50 && valid(draft) && !snapshot.busy &&
    !snapshot.items.some(r => r.title === draft.title);
  return <div className={styles.interactiveCard}>
    <div className={styles.interactiveButtons}>
      <Button variant="default" className={styles.control} disabled={snapshot.busy}
        onSelect={() => onCommand({ operation: "Sync" })}>{tr(locale, "Sync with Twitch", "Синхронизировать с Twitch")}</Button>
      <Button variant="default" className={styles.control} disabled={snapshot.busy || permission !== "Allowed"}
        onSelect={() => choose("")}>{tr(locale, "Create reward", "Создать награду")}</Button>
    </div>
    {permission !== "Allowed" && <div className={styles.authError}>
      {tr(locale, "Reward management permission required.", "Требуется право управления наградами.")}
      <Button variant="default" className={styles.control} onSelect={onReauthorize}>
        {tr(locale, "Reauthorize", "Повторная авторизация")}</Button></div>}
    {snapshot.status && <p className={snapshot.status.startsWith("HTTP") || snapshot.status.includes("Error") ||
      snapshot.status.startsWith("NeedsSync") ? styles.authError : styles.helper}>{statusText(locale, snapshot.status)}</p>}
    {snapshot.busy && <p>{tr(locale, "Contacting Twitch…", "Запрос к Twitch…")}</p>}
    <p>{snapshot.items.length}/50 {tr(locale, "custom rewards", "пользовательских наград")}</p>
    {snapshot.items.map(reward => {
      const linked = rules.find(r => r.rewardId === reward.id);
      const icon = reward.image?.url_1x || reward.default_image?.url_1x;
      return <Button key={reward.id} variant="default" className={`${styles.control} ${styles.rewardRow}`}
        selected={selectedId === reward.id} onSelect={() => choose(reward.id)}>
        {icon?.startsWith("https://static-cdn.jtvnw.net/") && <img className={styles.rewardIcon} src={icon} alt="" />}
        {reward.title} · {reward.cost} · {linked?.action || tr(locale, "Unlinked", "Без действия")} ·
        {snapshot.manageableIds.includes(reward.id) ? tr(locale, "Managed", "Управляемая") : tr(locale, "External", "Внешняя")}
      </Button>;
    })}
    {(selected || permission === "Allowed") && <div className={`${styles.interactiveCard} ${styles.rewardEditor}`}>
      <strong>{selected ? selected.title : tr(locale, "New reward", "Новая награда")}</strong>
      <p>{owned ? tr(locale, "Created by this app", "Создана этим приложением") : selected
        ? tr(locale, "External reward: linking only", "Внешняя награда: только привязка") : ""}</p>
      {(!selected || owned) && <>
        {serverChanged && <div className={styles.authError}>
          {tr(locale, "Twitch changed this reward. Reload its current values before saving.",
            "Награда изменилась в Twitch. Загрузите актуальные параметры перед сохранением.")}
          <Button variant="default" className={styles.control}
            onSelect={() => { if (serverEdit) { setDraft(serverEdit); setBaseline(serverSignature); } }}>
            {tr(locale, "Reload from Twitch", "Загрузить из Twitch")}</Button></div>}
        {input(tr(locale, "Title", "Название"), "title", 45)}
        {input(tr(locale, "Description", "Описание"), "prompt", 200)}
        {input(tr(locale, "Cost", "Стоимость"), "cost")}
        {input(tr(locale, "Color HEX", "Цвет HEX"), "background_color", 7)}
        <div className={styles.interactiveButtons}>
          {toggle("is_enabled", tr(locale, "Enabled", "Включена"))}
          {toggle("is_paused", tr(locale, "Paused", "Приостановлена"))}
          {toggle("is_user_input_required", tr(locale, "User input", "Ввод текста"))}
        </div>
        <div className={styles.interactiveButtons}>
          {toggle("is_max_per_stream_enabled", tr(locale, "Stream limit", "Лимит трансляции"))}
          {draft.is_max_per_stream_enabled && input(tr(locale, "Per stream", "За трансляцию"), "max_per_stream")}
          {toggle("is_max_per_user_per_stream_enabled", tr(locale, "Viewer limit", "Лимит зрителя"))}
          {draft.is_max_per_user_per_stream_enabled && input(tr(locale, "Per viewer", "На зрителя"), "max_per_user_per_stream")}
          {toggle("is_global_cooldown_enabled", tr(locale, "Twitch cooldown", "Кулдаун Twitch"))}
          {draft.is_global_cooldown_enabled && input(tr(locale, "Seconds", "Секунды"), "global_cooldown_seconds")}
        </div>
        {toggle("should_redemptions_skip_request_queue", tr(locale, "Skip request queue", "Без очереди запросов"))}
      </>}
      <p>{tr(locale, "Mod action", "Действие мода")}</p>
      <div className={styles.interactiveButtons}>{actions.map(value =>
        <Button key={value} variant="default" className={styles.control} selected={action === value}
          onSelect={() => { setAction(value); if (value === "ShowMessage" || value === "CityAnnouncement")
            update({ is_user_input_required: true }); }}>{value}</Button>)}</div>
      {selected ? <>
        {owned && <Button variant="default" className={styles.control} disabled={!valid(draft) || snapshot.busy || serverChanged}
          onSelect={() => onCommand({ operation: "Update", rewardId: selected.id, edit: draft,
            expected: JSON.parse(baseline) })}>
          {tr(locale, "Save to Twitch", "Сохранить в Twitch")}</Button>}
        <Button variant="default" className={styles.control} disabled={snapshot.busy}
          onSelect={() => onCommand({ operation: "Bind", rewardId: selected.id, action })}>
          {rule ? tr(locale, "Save mod action", "Сохранить действие мода") : tr(locale, "Link action", "Привязать действие")}</Button>
        {rule && <Button variant="default" className={styles.control} disabled={snapshot.busy}
          onSelect={() => onCommand({ operation: "Unbind", rewardId: selected.id })}>
          {tr(locale, "Unlink action", "Отвязать действие")}</Button>}
        {rule && <div className={styles.interactiveLimits}>
          <Button variant="default" className={styles.control}
            onSelect={() => onRuleChange(selected.id, { enabled: !rule.enabled })}>
            {tr(locale, "Mod action", "Действие мода")}: {rule.enabled ? tr(locale, "On", "Включено") : tr(locale, "Off", "Отключено")}</Button>
          {([ ["maxTextLength", tr(locale, "Max text", "Макс. текст"), 1, 300],
            ["cooldownSeconds", tr(locale, "Mod cooldown (s)", "Пауза мода (с)"), 0, 3600],
            ["displaySeconds", tr(locale, "Display (s)", "Показ (с)"), 2, 60] ] as const).map(([key, caption, min, max]) =>
            <label key={key}>{caption}<input type="text" value={rule[key]}
              onChange={e => { const n = Number(e.target.value); if (Number.isInteger(n) && n >= min && n <= max)
                onRuleChange(selected.id, { [key]: n }); }} /></label>)}</div>}
        {rule && owned && actions.includes(rule.action) && !selected.should_redemptions_skip_request_queue &&
          <div className={styles.interactiveCard}>
            <Button variant="default" className={styles.control}
              onSelect={() => onRuleChange(selected.id, { autoFulfill: !rule.autoFulfill })}>
              {tr(locale, "Fulfillment mode", "Режим подтверждения")}: {rule.autoFulfill
                ? tr(locale, "Automatic after display", "Автоматически после показа")
                : tr(locale, "Manual (default)", "Вручную (по умолчанию)")}</Button>
            <p>{tr(locale, "Automatic mode sends FULFILLED only after the UI displays the event and Twitch still reports UNFULFILLED. Network failures stay for manual review. Live verification is pending.",
              "Автоматический режим отправляет FULFILLED только после показа события и проверки статуса UNFULFILLED в Twitch. Сетевые ошибки остаются для ручной проверки. Проверка в игре ещё требуется.")}</p>
          </div>}
        {owned && <>
          <Button variant="default" className={styles.control} disabled={snapshot.busy}
            onSelect={() => onCommand({ operation: "Redemptions", rewardId: selected.id })}>
            {tr(locale, "View redemptions", "Посмотреть погашения")}</Button>
          <Button variant="default" className={styles.control} onSelect={() => setConfirmDelete(true)}>
            {tr(locale, "Delete reward", "Удалить награду")}</Button>
          {confirmDelete && <div role="dialog" className={styles.confirmBox}>
            <p>{tr(locale, "Twitch may fulfill pending redemptions when this reward is deleted. Delete?",
              "При удалении Twitch может отметить ожидающие погашения выполненными. Удалить?")}</p>
            <Button variant="default" className={styles.control} disabled={snapshot.busy}
              onSelect={() => { onCommand({ operation: "Delete", rewardId: selected.id }); setConfirmDelete(false); }}>
              {tr(locale, "Confirm delete", "Подтвердить удаление")}</Button>
            <Button variant="default" className={styles.control} onSelect={() => setConfirmDelete(false)}>
              {tr(locale, "Cancel", "Отмена")}</Button>
          </div>}
        </>}
      </> : <Button variant="default" className={styles.control} disabled={!canCreate}
        onSelect={() => onCommand({ operation: "Create", edit: draft, action })}>
        {tr(locale, "Create in Twitch", "Создать в Twitch")}</Button>}
    </div>}
    {selected && snapshot.selectedRewardId === selected.id && owned &&
      <div className={styles.interactiveCard}>
        <strong>{tr(locale, "Pending redemptions", "Ожидающие погашения")}</strong>
        {selected.should_redemptions_skip_request_queue && <p>{tr(locale,
          "This reward skips Twitch's request queue. Refund and status changes may be unavailable.",
          "Эта награда минует очередь Twitch. Возврат и изменение статуса могут быть недоступны.")}</p>}
        {snapshot.redemptions.map(item => <div key={item.id} className={styles.interactiveEvent}>
          <span>{item.user_name || item.user_id} · {item.status}</span>
          {item.user_input && <p>{item.user_input}</p>}
          {item.status === "UNFULFILLED" && !selected.should_redemptions_skip_request_queue && <>
            <Button variant="default" className={styles.control} disabled={snapshot.busy}
              onSelect={() => setConfirmRedemption({ id: item.id, operation: "Fulfill" })}>
              {tr(locale, "Fulfill", "Подтвердить выполнение")}</Button>
            <Button variant="default" className={styles.control} disabled={snapshot.busy}
              onSelect={() => setConfirmRedemption({ id: item.id, operation: "Cancel" })}>
              {tr(locale, "Cancel and refund", "Отменить и вернуть баллы")}</Button>
          </>}
        </div>)}
        {confirmRedemption && <div role="dialog" className={styles.confirmBox}>
          <p>{confirmRedemption.operation === "Cancel" ? tr(locale, "Cancel and refund points?", "Отменить и вернуть баллы?") :
            tr(locale, "Confirm this event was displayed in game?", "Подтвердить показ события в игре?")}</p>
          <Button variant="default" className={styles.control} disabled={snapshot.busy}
            onSelect={() => { onCommand({ operation: confirmRedemption.operation, rewardId: selected.id,
              redemptionId: confirmRedemption.id }); setConfirmRedemption(null); }}>
            {tr(locale, "Confirm", "Подтвердить")}</Button>
          <Button variant="default" className={styles.control} onSelect={() => setConfirmRedemption(null)}>
            {tr(locale, "Back", "Назад")}</Button>
        </div>}
      </div>}
  </div>;
}
