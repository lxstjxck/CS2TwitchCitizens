export type Locale = "ru" | "en";

const ru = {
  title: "Twitch Citizens", subtitle: "Управление зрителями города", open: "Twitch Citizens", name: "Имя",
  residents: "Жители", settings: "Настройки", search: "Поиск зрителя...",
  searchLabel: "Поиск по Twitch-имени или имени жителя", empty: "Зрители не найдены",
  loadCity: "Загрузите город, чтобы увидеть жителей.", connection: "Twitch",
  bound: "Привязано", totalLives: "Всего жизней", life: "жизнь", lives: "жизни",
  age: "Возраст", status: "Статус", home: "Домашний адрес", homeBuilding: "Тип домашнего здания",
  currentBuilding: "Текущее здание", currentAddress: "Адрес текущего здания",
  addressUnknown: "Адрес неизвестен", locationUnknown: "Точное местоположение неизвестно",
  positionAvailable: "Положение в мире доступно", inTransport: "В транспорте",
  ageDays: "игровых дней", workplace: "Работа", location: "Местоположение",
  ageModelHelp: "Условный возраст персонажа. Один день жизни в симуляции соответствует одному году. Не связан с календарём города.",
  atHome: "Дома", atWork: "На работе", employed: "Работает", student: "Учится", employmentUnknown: "Неизвестно",
  unbind: "Отвязать", deleteViewer: "Удалить все данные", confirmUnbind: "Отвязать зрителя?",
  confirmDelete: "Удалить все данные зрителя?", unbindDetail: "История жизней сохранится. Житель останется в городе.",
  deleteDetail: "Привязка и история жизней будут удалены без возможности восстановления через интерфейс.",
  cancel: "Отмена", confirm: "Подтвердить", lifeUnbound: "Отвязан",
  history: "История", citizenName: "Имя жителя", login: "Логин Twitch",
  displayName: "Имя Twitch",
  lifeNumber: "Жизнь №", infoTab: "Информация", lifeHistory: "История жизней",
  emptyHistory: "История жизней пуста", startDate: "Начало", endDate: "Конец", causeOfDeath: "Причина смерти",
  noData: "Нет данных", yes: "Есть", available: "Доступно", unavailable: "Недоступно",
  find: "Найти в городе", follow: "Следить", stopFollow: "Остановить слежение",
  cameraIdle: "Камера ожидает действия.",
  channel: "ID канала", channelMissing: "Не настроен", settingsNote: "Авторизация действует для пользователя Windows. Привязки жителей сохраняются в городе.",
  connectTwitch: "Подключить Twitch", reconnectTwitch: "Переподключить", disconnectTwitch: "Отключить Twitch",
  sendPermission: "Отправка сообщений", permissionAllowed: "Разрешено",
  permissionRequired: "Требуется авторизация", permissionChecking: "Проверяется",
  updateTwitchPermissions: "Обновить разрешения Twitch",
  cancelAuth: "Отмена", openTwitch: "Открыть Twitch", waitingAuth: "Ожидание подтверждения в Twitch",
  requestingAuth: "Запрос авторизации...", codeExpires: "Код действует ещё",
  clientIdMissing: "Не задан Client ID приложения. Обратитесь к владельцу сборки.",
  storageError: "Не удалось сохранить или прочитать учётные данные. Авторизуйтесь снова.",
  codeExpired: "Срок действия кода истёк. Повторите подключение.", authDenied: "Авторизация отклонена.",
  reauthorize: "Доступ отозван. Подключите Twitch снова.", networkError: "Ошибка сети. Повторите попытку.",
  legacyNotice: "Обнаружена старая конфигурация. Её токен не используется и не удаляется. Подключите Twitch через эту вкладку.",
  language: "Язык интерфейса", languageAuto: "Выбран по языку системы; можно переключить здесь.",
  connectionDisabled: "Twitch выключен", connectionConnecting: "Подключение к Twitch",
  connectionConnected: "Twitch подключён", connectionReconnecting: "Переподключение к Twitch",
  connectionAuthenticationError: "Ошибка авторизации Twitch", connectionDisconnected: "Twitch отключён",
  lifeActive: "Жив", lifeDeceased: "Умер", lifeMissing: "Недоступен", lifeUnknown: "Нет данных",
  ageChild: "Ребёнок", ageTeen: "Подросток", ageAdult: "Взрослый", ageElderly: "Пожилой",
  focusCitizenNotFound: "Житель не найден", focusPositionUnavailable: "Положение жителя недоступно",
  focusCameraUnavailable: "Камера сейчас недоступна", focusFocusRequested: "Перемещение камеры запрошено",
  focusFocusConfirmed: "Камера находится рядом с жителем", focusFocusFailed: "Не удалось переместить камеру",
  focusFollowRequested: "Слежение запрошено; проверьте камеру в игре", focusFollowStopped: "Слежение остановлено",
} as const;

const en: Record<keyof typeof ru, string> = {
  title: "Twitch Citizens", subtitle: "Manage your city's viewers", open: "Twitch Citizens", name: "Name",
  residents: "Residents", settings: "Settings", search: "Search viewers...",
  searchLabel: "Search Twitch or citizen names", empty: "No viewers found",
  loadCity: "Load a city to see residents.", connection: "Twitch",
  bound: "Bound", totalLives: "Total lives", life: "life", lives: "lives",
  age: "Age", status: "Status", home: "Home address", homeBuilding: "Home building type",
  currentBuilding: "Current building", currentAddress: "Current building address",
  addressUnknown: "Address unknown", locationUnknown: "Exact location unknown",
  positionAvailable: "Position available in world", inTransport: "In transport",
  ageDays: "game days", workplace: "Work", location: "Location",
  ageModelHelp: "Estimated character age. One simulation day corresponds to one year. It is unrelated to the city calendar.",
  atHome: "At home", atWork: "At work", employed: "Employed", student: "Student", employmentUnknown: "Unknown",
  unbind: "Unbind", deleteViewer: "Delete all data", confirmUnbind: "Unbind viewer?",
  confirmDelete: "Delete all viewer data?", unbindDetail: "Life history will be kept. The citizen stays in the city.",
  deleteDetail: "The binding and all life history will be removed and cannot be restored in the UI.",
  cancel: "Cancel", confirm: "Confirm", lifeUnbound: "Unbound",
  history: "History", citizenName: "Citizen name", login: "Twitch login",
  displayName: "Twitch name",
  lifeNumber: "Life ", infoTab: "Information", lifeHistory: "Life history",
  emptyHistory: "No life history", startDate: "Start", endDate: "End", causeOfDeath: "Cause of death",
  noData: "No data", yes: "Available", available: "Available", unavailable: "Unavailable",
  find: "Find in city", follow: "Follow", stopFollow: "Stop following",
  cameraIdle: "Camera is waiting for an action.",
  channel: "Channel ID", channelMissing: "Not configured", settingsNote: "Authorization belongs to this Windows user. Citizen bindings remain in the city save.",
  connectTwitch: "Connect Twitch", reconnectTwitch: "Reconnect", disconnectTwitch: "Disconnect Twitch",
  sendPermission: "Send chat messages", permissionAllowed: "Allowed",
  permissionRequired: "Authorization required", permissionChecking: "Checking",
  updateTwitchPermissions: "Update Twitch permissions",
  cancelAuth: "Cancel", openTwitch: "Open Twitch", waitingAuth: "Waiting for Twitch authorization",
  requestingAuth: "Requesting authorization...", codeExpires: "Code expires in",
  clientIdMissing: "The application Client ID is missing. Contact the build owner.",
  storageError: "Could not read or save credentials. Authorize again.",
  codeExpired: "The code expired. Try connecting again.", authDenied: "Authorization was denied.",
  reauthorize: "Access was revoked. Connect Twitch again.", networkError: "Network error. Try again.",
  legacyNotice: "An old configuration was found. Its token is not used or deleted. Connect Twitch here.",
  language: "Interface language", languageAuto: "Initially follows the system language; you can switch it here.",
  connectionDisabled: "Twitch disabled", connectionConnecting: "Connecting to Twitch",
  connectionConnected: "Twitch connected", connectionReconnecting: "Reconnecting to Twitch",
  connectionAuthenticationError: "Twitch authentication error", connectionDisconnected: "Twitch disconnected",
  lifeActive: "Active", lifeDeceased: "Deceased", lifeMissing: "Missing", lifeUnknown: "No data",
  ageChild: "Child", ageTeen: "Teen", ageAdult: "Adult", ageElderly: "Elderly",
  focusCitizenNotFound: "Citizen not found", focusPositionUnavailable: "Citizen position unavailable",
  focusCameraUnavailable: "Camera unavailable", focusFocusRequested: "Camera move requested",
  focusFocusConfirmed: "Camera is near the citizen", focusFocusFailed: "Could not move the camera",
  focusFollowRequested: "Follow requested; check the camera in game", focusFollowStopped: "Following stopped",
};

export type TextKey = keyof typeof ru;
export const text = (locale: Locale, key: TextKey): string => (locale === "ru" ? ru : en)[key];

export const showLegacyNotice = (legacyConfig: boolean, modernCredentials: boolean, state: string): boolean =>
  legacyConfig && !modernCredentials && (state === "Disconnected" || state === "Error");

export function ageDetailText(locale: Locale, age: string, ageYears?: number | null): string {
  const category = ageText(locale, age);
  if (typeof ageYears !== "number" || !Number.isFinite(ageYears) || ageYears < 0) return category;
  const years = Math.floor(ageYears);
  if (locale === "en") return `${years} ${years === 1 ? "year" : "years"} old`;
  const lastTwo = years % 100;
  const last = years % 10;
  const noun = lastTwo >= 11 && lastTwo <= 14 ? "лет" :
    last === 1 ? "год" : last >= 2 && last <= 4 ? "года" : "лет";
  return `${years} ${noun}`;
}

export function missingReasonText(locale: Locale, reason: string): string {
  const ru = locale === "ru";
  switch (reason) {
    case "EntityAbsent": return ru ? "Жителя больше нет в текущем ECS-мире. Смерть не подтверждена." :
      "The citizen entity is absent from the current ECS world. Death is not confirmed.";
    case "EntityDeleted": return ru ? "Игровая сущность жителя помечена к удалению. Смерть не подтверждена." :
      "The citizen entity is marked for deletion. Death is not confirmed.";
    case "CitizenComponentMissing": return ru ? "Сущность больше не содержит компонент жителя. Смерть не подтверждена." :
      "The entity no longer has a Citizen component. Death is not confirmed.";
    default: return ru ? "Сохранённая привязка не разрешилась в текущем мире. Причина не была записана; история сохранена." :
      "The saved binding could not be resolved in this world. The reason was not recorded; history is preserved.";
  }
}

export function detectLocale(language?: string): Locale {
  const value = language ?? (typeof navigator === "undefined" ? "en" : navigator.language || "en");
  return value.toLowerCase().startsWith("ru") ? "ru" : "en";
}

export function ageText(locale: Locale, age: string): string {
  const key: Record<string, TextKey> = {
    Child: "ageChild", Teen: "ageTeen", Adult: "ageAdult", Elderly: "ageElderly",
  };
  return key[age] ? text(locale, key[age]) : text(locale, "noData");
}

export function valueText(locale: Locale, value?: string): string {
  const clean = value?.trim();
  return clean && clean !== "null" && clean !== "undefined" &&
    clean !== "home available" && clean !== "workplace available"
    ? clean : text(locale, "noData");
}

const ruMonths = ["январь", "февраль", "март", "апрель", "май", "июнь",
  "июль", "август", "сентябрь", "октябрь", "ноябрь", "декабрь"];
const enMonths = ["January", "February", "March", "April", "May", "June",
  "July", "August", "September", "October", "November", "December"];

export function dateText(locale: Locale, value?: string): string {
  if (!value) return text(locale, "noData");
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value);
  if (!match) return text(locale, "noData");
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  const date = new Date(Date.UTC(year, month - 1, day));
  if (date.getUTCFullYear() !== year || date.getUTCMonth() !== month - 1 || date.getUTCDate() !== day)
    return text(locale, "noData");
  return locale === "ru" ? `${ruMonths[month - 1]} ${year} г.` : `${enMonths[month - 1]} ${year}`;
}

export function lifeText(locale: Locale, status: string): string {
  const key: Record<string, TextKey> = {
    Active: "lifeActive", Deceased: "lifeDeceased", Missing: "lifeMissing", Unbound: "lifeUnbound",
  };
  return text(locale, key[status] ?? "lifeUnknown");
}

export function connectionText(locale: Locale, status: string): string {
  const key: Record<string, TextKey> = {
    Disabled: "connectionDisabled", Connecting: "connectionConnecting",
    Connected: "connectionConnected", Reconnecting: "connectionReconnecting",
    AuthenticationError: "connectionAuthenticationError", Disconnected: "connectionDisconnected",
  };
  return text(locale, key[status] ?? "connectionDisconnected");
}

export function focusText(locale: Locale, result?: string): string {
  if (!result) return text(locale, "cameraIdle");
  const key: Record<string, TextKey> = {
    CitizenNotFound: "focusCitizenNotFound", PositionUnavailable: "focusPositionUnavailable",
    CameraUnavailable: "focusCameraUnavailable", FocusRequested: "focusFocusRequested",
    FocusConfirmed: "focusFocusConfirmed", FocusFailed: "focusFocusFailed",
    FollowRequested: "focusFollowRequested", FollowStopped: "focusFollowStopped",
  };
  return text(locale, key[result] ?? "cameraIdle");
}

export function lifeCountText(locale: Locale, count: number): string {
  if (locale === "en") return `${count} ${count === 1 ? "life" : "lives"}`;
  const lastTwo = count % 100;
  const last = count % 10;
  const noun = lastTwo >= 11 && lastTwo <= 14 ? "жизней" :
    last === 1 ? "жизнь" : last >= 2 && last <= 4 ? "жизни" : "жизней";
  return `${count} ${noun}`;
}

export function lifeNumberText(locale: Locale, count: number): string {
  return Number.isFinite(count) && count > 0 ? `${text(locale, "lifeNumber")}${Math.floor(count)}` : "";
}

export function viewerMetaText(locale: Locale, age: string, lives: number, ageYears?: number | null): string {
  const parts: string[] = [];
  if (ageYears != null || age && ageText(locale, age) !== text(locale, "noData"))
    parts.push(ageDetailText(locale, age, ageYears));
  const life = lifeNumberText(locale, lives);
  if (life) parts.push(life);
  return parts.join(" · ");
}

export function viewerCountText(locale: Locale, count: number): string {
  if (locale === "en") return `${count} ${count === 1 ? "viewer" : "viewers"}`;
  const lastTwo = count % 100;
  const last = count % 10;
  const noun = lastTwo >= 11 && lastTwo <= 14 ? "зрителей" :
    last === 1 ? "зритель" : last >= 2 && last <= 4 ? "зрителя" : "зрителей";
  return `${count} ${noun}`;
}
