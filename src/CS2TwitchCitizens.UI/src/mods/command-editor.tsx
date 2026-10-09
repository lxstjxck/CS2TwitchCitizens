import React, { useState } from "react";
import { Button } from "cs2/ui";
import styles from "./twitch-citizens.module.scss";
import { CommandConfig, CommandName, CommandOption, defaultCommands, safeFieldGroups } from "./command-config";
import { categoryText, commandPreview, commandPurpose, demoFieldValue, helpText, joinRepeatPreview, nextAdvanced, nextHelp } from "./command-ux";
import { Locale } from "./ui-text";

type Props = { config: CommandConfig; recovered: boolean; saveFailed?: boolean; locale: Locale;
  initialCommand?: CommandName; initialAdvanced?: "global" | "command" | null; initialGroup?: string | null;
  initialHelp?: string | null; initialReset?: "all" | "command" | null;
  onSave?: (config: CommandConfig) => void };

export function CommandEditor({ config, recovered, saveFailed = false, locale, initialCommand = "join",
  initialAdvanced = null, initialGroup = null, initialHelp = null, initialReset = null, onSave }: Props) {
  const [selected, setSelected] = useState<CommandName>(initialCommand);
  const [advanced, setAdvanced] = useState<"global" | "command" | null>(initialAdvanced);
  const [openGroup, setOpenGroup] = useState<string | null>(initialGroup);
  const [pinnedHelp, setPinnedHelp] = useState<string | null>(initialHelp);
  const [hoverHelp, setHoverHelp] = useState<string | null>(null);
  const [confirmReset, setConfirmReset] = useState<"all" | "command" | null>(initialReset);
  const ru = locale === "ru";
  const option = config[selected];
  const save = (next: CommandConfig) => onSave?.(next);
  const updateGlobal = (patch: Partial<CommandConfig>) => save({ ...config, ...patch });
  const updateOption = (patch: Partial<CommandOption>) => save({ ...config, [selected]: { ...option, ...patch } });
  const switchCommand = (name: CommandName) => {
    setSelected(name); setAdvanced(null); setOpenGroup(null); setPinnedHelp(null); setHoverHelp(null); setConfirmReset(null);
  };
  const toggleAdvanced = (section: "global" | "command") => {
    setAdvanced(value => nextAdvanced(value, section)); setPinnedHelp(null); setConfirmReset(null);
  };

  const help = (key: string) => <span className={styles.helpWrap}
    onMouseEnter={() => setHoverHelp(key)} onMouseLeave={() => setHoverHelp(null)}>
    <Button variant="default" className={`${styles.control} ${styles.helpButton}`}
      aria-label={ru ? "Подсказка" : "Help"} aria-expanded={pinnedHelp === key}
      onSelect={() => setPinnedHelp(value => nextHelp(value, key))}>?</Button>
  </span>;
  const helpNote = (key: string) => (pinnedHelp === key || hoverHelp === key) &&
    <div className={styles.helpText} role="note">{helpText(key, locale)}</div>;
  const row = (label: string, key: string, control: React.ReactNode) =>
    <div className={styles.commandSetting}>
      <div className={styles.settingHeading}><span>{label}</span>{help(key)}</div>
      {control}{helpNote(key)}
    </div>;
  const toggle = (label: string, checked: boolean, action: (value: boolean) => void, key?: string) =>
    <div className={styles.toggleBlock}><div className={styles.toggleRow}>
      <Button variant="default" className={`${styles.control} ${styles.commandToggle}`} selected={checked}
        data-selected={checked} onSelect={() => action(!checked)}>
        <span className={styles.toggleMark} aria-hidden="true" /><span>{label}</span>
      </Button>{key && help(key)}
    </div>{key && helpNote(key)}</div>;
  const number = (label: string, key: string, value: number, min: number, max: number,
    action: (value: number) => void, unit?: string) => row(label, key,
      <div className={styles.numberRow}><input type="text" inputMode="numeric" aria-label={label}
        value={String(value)} onChange={event => {
          const next = Number(event.target.value);
          if (event.target.value.trim() && Number.isFinite(next)) action(Math.max(min, Math.min(max, Math.round(next))));
        }} />{unit && <span>{unit}</span>}</div>);
  const reset = (kind: "all" | "command") => <div className={styles.resetArea}>
    {confirmReset === kind ? <>
      <span>{ru ? "Сбросить сохранённые настройки?" : "Reset saved settings?"}</span>
      <div className={styles.commandChoices}>
        <Button variant="default" className={`${styles.control} ${styles.dangerButton}`} onSelect={() => {
          if (kind === "all") save(defaultCommands());
          else updateOption(defaultCommands()[selected]);
          setConfirmReset(null);
        }}>{ru ? "Да, сбросить" : "Yes, reset"}</Button>
        <Button variant="default" className={styles.control} onSelect={() => setConfirmReset(null)}>
          {ru ? "Отмена" : "Cancel"}</Button>
      </div>
    </> : <Button variant="default" className={`${styles.control} ${styles.resetButton}`}
      onSelect={() => setConfirmReset(kind)}>{kind === "all" ? (ru ? "Сбросить все настройки" : "Reset all settings") :
        (ru ? "Сбросить эту команду" : "Reset this command")}</Button>}
  </div>;

  return <div className={styles.commandEditor}>
    {saveFailed && <div className={styles.commandNotice} role="alert">{ru
      ? "Не удалось сохранить настройки команд. Проверьте доступ к файлу и попробуйте снова."
      : "Could not save command settings. Check file access and try again."}</div>}
    {recovered && <div className={styles.commandNotice} role="alert">
      <span>{ru ? "Настройки команд получены не полностью. Используются безопасные значения."
        : "Command settings were incomplete. Safe values are shown."}</span>
      <Button variant="default" className={styles.control} onSelect={() => {
        setAdvanced("global"); setConfirmReset("all");
      }}>
        {ru ? "Восстановить настройки" : "Restore defaults"}</Button>
    </div>}

    <div className={styles.commandHeader}>
      <strong>{ru ? "Команды Twitch" : "Twitch commands"}</strong>
      <span className={styles.replyStatus} data-enabled={config.responsesEnabled}>
        {config.responsesEnabled ? (ru ? "Ответы включены" : "Replies on") : (ru ? "Ответы выключены" : "Replies off")}
      </span>
    </div>
    <div className={styles.commandSection}>
      {toggle(ru ? "Отвечать в Twitch-чате" : "Reply in Twitch chat", config.responsesEnabled,
        value => updateGlobal({ responsesEnabled: value }), "responses")}
      {row(ru ? "Язык ответов" : "Reply language", "language", <div className={styles.commandChoices}>
        {(["ru", "en"] as const).map(language => <Button key={language} variant="default"
          className={`${styles.control} ${styles.choiceButton}`} selected={config.language === language}
          data-selected={config.language === language} onSelect={() => updateGlobal({ language })}>
          {language === "ru" ? (ru ? "Русский" : "Russian") : (ru ? "Английский" : "English")}</Button>)}
      </div>)}
      <Button variant="default" className={`${styles.control} ${styles.expandButton}`}
        aria-expanded={advanced === "global"} onSelect={() => toggleAdvanced("global")}>
        {ru ? "Дополнительные настройки ответов" : "Advanced reply settings"}<span>{advanced === "global" ? "⌄" : "›"}</span>
      </Button>
      {advanced === "global" && <div className={styles.advancedContent}>
        {number(ru ? "Общий кулдаун" : "Shared cooldown", "sharedCooldown", config.sharedCooldownSeconds,
          0, 3600, value => updateGlobal({ sharedCooldownSeconds: value }), ru ? "секунд" : "seconds")}
        {number(ru ? "Интервал отправки" : "Send interval", "sendInterval", config.outgoingIntervalSeconds,
          1, 60, value => updateGlobal({ outgoingIntervalSeconds: value }), ru ? "секунд" : "seconds")}
        {number(ru ? "Очередь ответов" : "Reply queue", "queue", config.maxOutgoingQueue,
          1, 200, value => updateGlobal({ maxOutgoingQueue: value }))}
        {row(ru ? "Если данных нет" : "If data is missing", "missing", <div className={styles.commandChoices}>
          {(["omit", "label"] as const).map(choice => <Button key={choice} variant="default"
            className={`${styles.control} ${styles.choiceButton}`} selected={config.missingData === choice}
            data-selected={config.missingData === choice} onSelect={() => updateGlobal({ missingData: choice })}>
            {choice === "omit" ? (ru ? "Пропустить" : "Omit") : (ru ? "Нет данных" : "No data")}</Button>)}
        </div>)}
        {reset("all")}
      </div>}
    </div>

    <div className={styles.commandTabs}>{(["join", "me", "find", "history"] as CommandName[]).map(name =>
      <Button variant="default" key={name} className={`${styles.control} ${styles.commandTab}`}
        selected={selected === name} data-selected={selected === name} onSelect={() => switchCommand(name)}>
        <span className={styles.commandTabName}>!{name}</span><span className={styles.tabDot} data-enabled={config[name].enabled} /></Button>)}</div>
    <div className={styles.commandTitle}>
      <div className={styles.commandTitleText}><strong className={styles.commandName}>!{selected}</strong><p>{commandPurpose[selected][locale]}</p></div>
      <span className={styles.commandState} data-enabled={option.enabled}>
        {option.enabled ? (ru ? "Включена" : "Enabled") : (ru ? "Выключена" : "Disabled")}</span>
    </div>
    <div className={styles.commandSection}>
      {toggle(ru ? "Команда включена" : "Command enabled", option.enabled,
        value => updateOption({ enabled: value }), "enabled")}
      {row(ru ? "Кто может использовать?" : "Who can use it?", "permission", <div className={styles.permissionChoices}>
        {(["everyone", "moderators", "broadcaster"] as const).map(permission => <Button key={permission}
          variant="default" className={`${styles.control} ${styles.permissionButton}`}
          selected={option.permission === permission} data-selected={option.permission === permission}
          onSelect={() => updateOption({ permission })}>
          <span className={styles.radioMark} />{permission === "everyone" ? (ru ? "Все" : "Everyone") :
            permission === "moderators" ? (ru ? "Модераторы и стример" : "Moderators and broadcaster") :
              (ru ? "Только стример" : "Broadcaster only")}</Button>)}
      </div>)}
      {number(ru ? "Задержка между командами" : "Delay between commands", "viewerCooldown",
        option.viewerCooldownSeconds, 0, 3600, value => updateOption({ viewerCooldownSeconds: value }),
        ru ? "секунд" : "seconds")}
      <Button variant="default" className={`${styles.control} ${styles.expandButton}`}
        aria-expanded={advanced === "command"} onSelect={() => toggleAdvanced("command")}>
        {ru ? "Дополнительные настройки команды" : "Advanced command settings"}<span>{advanced === "command" ? "⌄" : "›"}</span>
      </Button>
      {advanced === "command" && <div className={styles.advancedContent}>
        {toggle(ru ? "Отвечать в чат" : "Reply in chat", option.responseEnabled,
          value => updateOption({ responseEnabled: value }), "response")}
        {number(ru ? "Максимальная длина ответа" : "Maximum reply length", "maxLength",
          option.maxResponseLength, 1, 500, value => updateOption({ maxResponseLength: value }),
          ru ? "символов" : "characters")}
        {selected === "join" && toggle(ru ? "Отвечать при повторном !join" : "Reply when already joined",
          option.alreadyJoinedResponse, value => updateOption({ alreadyJoinedResponse: value }), "alreadyJoined")}
        {selected === "history" && number(ru ? "Показывать прошлых жизней" : "Previous lives to show",
          "previousLives", option.previousLivesLimit, 0, 10, value => updateOption({ previousLivesLimit: value }))}
        {safeFieldGroups(selected).map(group => <div key={group.category} className={styles.fieldGroup}>
          <Button variant="default" className={`${styles.control} ${styles.expandButton}`}
            aria-expanded={openGroup === group.category}
            onSelect={() => setOpenGroup(value => value === group.category ? null : group.category)}>
            {categoryText(group.category, locale)}<span>{openGroup === group.category ? "⌄" : "›"}</span>
          </Button>
          {openGroup === group.category && <div className={styles.fieldList}>{group.fields.map(field => {
            const missing = !demoFieldValue(field.id, config);
            return <div key={field.id} className={styles.fieldRow}>
              {toggle(locale === "ru" ? field.ru : field.en, option.selectedFields.includes(field.id), checked =>
                updateOption({ selectedFields: checked ? [...option.selectedFields, field.id] :
                  option.selectedFields.filter(id => id !== field.id) }), field.id)}
              {missing && <span className={styles.fieldUnavailable}>{ru ? "В примере: нет данных" : "No sample data"}</span>}
            </div>;
          })}</div>}
        </div>)}
        <div className={styles.commandPreview}>
          <strong>{ru ? "Пример ответа" : "Reply preview"}</strong>
          <span>{ru ? "Демонстрационные данные · не из игры" : "Demo data · not from the game"}</span>
          <p>{commandPreview(selected, config)}</p>
          {selected === "join" && <p>{joinRepeatPreview(config)}</p>}
        </div>
        {reset("command")}
      </div>}
    </div>
  </div>;
}
