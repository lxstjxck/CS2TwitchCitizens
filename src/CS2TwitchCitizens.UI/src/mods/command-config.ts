export type CommandName = "join" | "me" | "find" | "history";
export type CommandOption = { enabled: boolean; viewerCooldownSeconds: number; responseEnabled: boolean;
  permission: string; selectedFields: string[]; maxResponseLength: number;
  previousLivesLimit: number; alreadyJoinedResponse: boolean };
export type CommandConfig = { version: number; responsesEnabled: boolean; language: string; missingData: string;
  sharedCooldownSeconds: number; outgoingIntervalSeconds: number; maxOutgoingQueue: number;
  join: CommandOption; me: CommandOption; find: CommandOption; history: CommandOption };
export type Field = { id: string; en: string; ru: string };
export type FieldGroup = { category: string; fields: Field[] };

const defaultOption = (cooldown: number, fields: string[]): CommandOption => ({ enabled: true, viewerCooldownSeconds: cooldown,
  responseEnabled: true, permission: "everyone", selectedFields: fields, maxResponseLength: 450,
  previousLivesLimit: 3, alreadyJoinedResponse: true });
export const defaultCommands = (): CommandConfig => ({ version: 1, responsesEnabled: true, language: "ru", missingData: "omit",
  sharedCooldownSeconds: 0, outgoingIntervalSeconds: 2, maxOutgoingQueue: 50,
  join: defaultOption(30, []), me: defaultOption(15, ["name", "ageGroup", "status", "home", "work"]),
  find: defaultOption(15, ["locationType", "currentBuilding"]),
  history: defaultOption(30, ["totalLives", "currentLife", "status", "previousLives"]) });

export const fieldGroups: Record<CommandName, FieldGroup[]> = {
  join: [],
  me: [
    { category: "Personal details / Личные данные", fields: [{ id: "name", en: "Citizen name", ru: "Имя жителя" }, { id: "ageGroup", en: "Age group", ru: "Возрастная группа" }, { id: "status", en: "Life status", ru: "Статус жизни" }] },
    { category: "Family / Семья", fields: [{ id: "householdSize", en: "Household size", ru: "Размер семьи" }] },
    { category: "Home / Дом", fields: [{ id: "home", en: "Home building", ru: "Дом" }] },
    { category: "Work / Работа", fields: [{ id: "employment", en: "Employment", ru: "Занятость" }, { id: "work", en: "Workplace", ru: "Место работы" }] },
    { category: "Education / Образование", fields: [{ id: "school", en: "School", ru: "Учебное заведение" }] },
    { category: "Location / Местоположение", fields: [{ id: "currentBuilding", en: "Current building", ru: "Текущее здание" }] },
  ],
  find: [{ category: "Location / Местоположение", fields: [{ id: "locationType", en: "Location type", ru: "Тип местоположения" },
    { id: "currentBuilding", en: "Current building", ru: "Текущее здание" }, { id: "coordinates", en: "Coordinates", ru: "Координаты" }] }],
  history: [{ category: "Life history / История жизней", fields: [
    { id: "totalLives", en: "Total lives", ru: "Всего жизней" }, { id: "currentLife", en: "Current life number", ru: "Номер текущей жизни" },
    { id: "status", en: "Status", ru: "Статус" }, { id: "startDate", en: "Start date", ru: "Дата начала" },
    { id: "endDate", en: "End date", ru: "Дата окончания" }, { id: "causeOfDeath", en: "Cause of death", ru: "Причина смерти" },
    { id: "previousLives", en: "Previous lives", ru: "Прошлые жизни" }] }],
};

const record = (value: unknown): Record<string, unknown> | null =>
  value !== null && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : null;
const bool = (value: unknown, fallback: boolean) => typeof value === "boolean" ? value : fallback;
const integer = (value: unknown, fallback: number, min: number, max: number) =>
  typeof value === "number" && Number.isFinite(value) ? Math.min(max, Math.max(min, Math.round(value))) : fallback;
const stringChoice = (value: unknown, choices: string[], fallback: string) =>
  typeof value === "string" && choices.includes(value) ? value : fallback;

export function safeFieldGroups(name: CommandName, catalog: unknown = fieldGroups): FieldGroup[] {
  const groups = record(catalog)?.[name];
  if (!Array.isArray(groups)) return [];
  return groups.reduce<FieldGroup[]>((result, group: unknown) => {
    const source = record(group);
    if (!source || typeof source.category !== "string" || !Array.isArray(source.fields)) return result;
    const fields = source.fields.reduce<Field[]>((items: Field[], field: unknown) => {
      const item = record(field);
      if (item && typeof item.id === "string" && typeof item.en === "string" && typeof item.ru === "string")
        items.push({ id: item.id, en: item.en, ru: item.ru });
      return items;
    }, []);
    result.push({ category: source.category, fields });
    return result;
  }, []);
}

function normalizeOption(raw: unknown, fallback: CommandOption, allowed: string[]): CommandOption {
  const value = record(raw);
  const source = value ?? {};
  const selectedFields = Array.isArray(source.selectedFields)
    ? source.selectedFields.filter((id): id is string => typeof id === "string" && allowed.includes(id))
    : [];
  return {
    enabled: bool(source.enabled, fallback.enabled),
    viewerCooldownSeconds: integer(source.viewerCooldownSeconds, fallback.viewerCooldownSeconds, 0, 3600),
    responseEnabled: bool(source.responseEnabled, fallback.responseEnabled),
    permission: stringChoice(source.permission, ["everyone", "moderators", "broadcaster"], fallback.permission),
    selectedFields: [...new Set(selectedFields)],
    maxResponseLength: integer(source.maxResponseLength, fallback.maxResponseLength, 1, 500),
    previousLivesLimit: integer(source.previousLivesLimit, fallback.previousLivesLimit, 0, 10),
    alreadyJoinedResponse: bool(source.alreadyJoinedResponse, fallback.alreadyJoinedResponse),
  };
}

export function normalizeCommandConfig(raw: unknown, catalog: unknown = fieldGroups): { config: CommandConfig; recovered: boolean } {
  const defaults = defaultCommands();
  const source = record(raw);
  if (!source || source.version !== 1) return { config: defaults, recovered: true };
  const config: CommandConfig = {
    version: 1,
    responsesEnabled: bool(source.responsesEnabled, defaults.responsesEnabled),
    language: stringChoice(source.language, ["ru", "en"], defaults.language),
    missingData: stringChoice(source.missingData, ["omit", "label"], defaults.missingData),
    sharedCooldownSeconds: integer(source.sharedCooldownSeconds, defaults.sharedCooldownSeconds, 0, 3600),
    outgoingIntervalSeconds: integer(source.outgoingIntervalSeconds, defaults.outgoingIntervalSeconds, 1, 60),
    maxOutgoingQueue: integer(source.maxOutgoingQueue, defaults.maxOutgoingQueue, 1, 200),
    join: normalizeOption(source.join, defaults.join, []),
    me: normalizeOption(source.me, defaults.me, safeFieldGroups("me").reduce<string[]>((ids, group) => ids.concat(group.fields.map(field => field.id)), [])),
    find: normalizeOption(source.find, defaults.find, safeFieldGroups("find").reduce<string[]>((ids, group) => ids.concat(group.fields.map(field => field.id)), [])),
    history: normalizeOption(source.history, defaults.history, safeFieldGroups("history").reduce<string[]>((ids, group) => ids.concat(group.fields.map(field => field.id)), [])),
  };
  const validShape = ["join", "me", "find", "history"].every(name => {
    const option = record(source[name]);
    if (!option) return false;
    const allowed = name === "join" ? [] : safeFieldGroups(name as CommandName)
      .reduce<string[]>((ids, group) => ids.concat(group.fields.map(field => field.id)), []);
    return typeof option.enabled === "boolean" && typeof option.responseEnabled === "boolean" &&
      typeof option.alreadyJoinedResponse === "boolean" &&
      ["everyone", "moderators", "broadcaster"].includes(option.permission as string) &&
      validInt(option.viewerCooldownSeconds, 0, 3600) && validInt(option.maxResponseLength, 1, 500) &&
      validInt(option.previousLivesLimit, 0, 10) && Array.isArray(option.selectedFields) &&
      option.selectedFields.every((id: unknown) => typeof id === "string" && allowed.includes(id));
  });
  const validGlobal = typeof source.responsesEnabled === "boolean" && ["ru", "en"].includes(source.language as string) &&
    ["omit", "label"].includes(source.missingData as string) &&
    validInt(source.sharedCooldownSeconds, 0, 3600) && validInt(source.outgoingIntervalSeconds, 1, 60) &&
    validInt(source.maxOutgoingQueue, 1, 200);
  const validCatalog = ["join", "me", "find", "history"].every(name => Array.isArray(record(catalog)?.[name])) &&
    (["me", "find", "history"] as CommandName[]).every(name => safeFieldGroups(name, catalog).length > 0);
  return { config, recovered: !validShape || !validGlobal || !validCatalog };
}

const validInt = (value: unknown, min: number, max: number): boolean =>
  typeof value === "number" && Number.isInteger(value) && value >= min && value <= max;

export function parseCommandConfig(json: unknown, catalog: unknown = fieldGroups): { config: CommandConfig; recovered: boolean } {
  if (typeof json !== "string") return { config: defaultCommands(), recovered: true };
  try { return normalizeCommandConfig(JSON.parse(json), catalog); }
  catch { return { config: defaultCommands(), recovered: true }; }
}

export function previewFields(name: CommandName, config: CommandConfig, catalog: unknown = fieldGroups): Field[] {
  const option = config[name];
  const ids = Array.isArray(option?.selectedFields) ? option.selectedFields : [];
  return safeFieldGroups(name, catalog).reduce<Field[]>((fields, group) => fields.concat(group.fields), [])
    .filter(field => ids.includes(field.id));
}
