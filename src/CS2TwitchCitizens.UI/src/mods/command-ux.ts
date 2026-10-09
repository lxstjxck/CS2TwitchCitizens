import { CommandConfig, CommandName } from "./command-config";

export type CommandLocale = "ru" | "en";
type Localized = { ru: string; en: string };
const choose = (value: Localized, locale: CommandLocale) => value[locale];

export const commandPurpose: Record<CommandName, Localized> = {
  join: { ru: "Привязать зрителя к жителю города", en: "Bind a viewer to a city resident" },
  me: { ru: "Информация о жителе", en: "Information about a resident" },
  find: { ru: "Где сейчас находится житель", en: "Where the resident is now" },
  history: { ru: "Текущая и прошлые жизни", en: "Current and previous lives" },
};

export const settingHelp: Record<string, Localized> = {
  responses: { ru: "Разрешает отправлять ответы на команды в Twitch-чат. Сами команды продолжают работать, когда ответы выключены.",
    en: "Allows command replies in Twitch chat. Commands still run when replies are off." },
  language: { ru: "Язык исходящих сообщений. Язык этой панели меняется отдельно в настройках.",
    en: "Language of outgoing messages. The panel language is changed separately in Settings." },
  sharedCooldown: { ru: "Пауза для одного зрителя между любыми двумя его командами. Команды разных зрителей друг друга не задерживают.",
    en: "Delay for one viewer between any two of their commands. Different viewers do not delay each other." },
  viewerCooldown: { ru: "Сколько секунд один зритель ждёт перед повтором этой же команды. Например, 15 секунд между двумя !me.",
    en: "Seconds one viewer waits before repeating this command. For example, 15 seconds between two !me calls." },
  sendInterval: { ru: "Минимальная пауза между исходящими ответами бота в Twitch-чате. Ограничение Twitch 429 даёт паузу 30 секунд.",
    en: "Minimum delay between outgoing bot replies in Twitch chat. Twitch 429 causes a 30-second pause." },
  queue: { ru: "Сколько ответов может ждать отправки. Когда очередь заполнена, новый ответ пропускается.",
    en: "Maximum replies waiting to be sent. A new reply is skipped when the queue is full." },
  missing: { ru: "Если выбранного свойства нет, пропустить его или написать «нет данных». Например, у жителя может не быть места работы.",
    en: "When a selected value is missing, omit it or write ‘no data’. For example, a resident may have no workplace." },
  permission: { ru: "Ограничивает, кто может вызвать команду. Права определяются значками Twitch и ID стримера.",
    en: "Limits who can invoke this command. Permissions use Twitch badges and the broadcaster ID." },
  enabled: { ru: "Выключенная команда не выполняется и не запускает ожидание между командами.",
    en: "A disabled command does not run or start a cooldown." },
  response: { ru: "Отключает ответ только этой команды. Глобальный переключатель ответов тоже должен быть включён.",
    en: "Disables replies for this command only. The global reply switch must also be on." },
  maxLength: { ru: "Максимальная длина одного ответа, от 1 до 500 символов. Длинный текст обрезается с многоточием.",
    en: "Maximum length of one reply, from 1 to 500 characters. Longer text is cut with an ellipsis." },
  alreadyJoined: { ru: "Если зритель уже привязан, отправить ему короткий ответ. Повторный !join не меняет привязку.",
    en: "Send a short reply when the viewer is already bound. Repeating !join does not change the binding." },
  previousLives: { ru: "Сколько прошлых жизней показать в !history. Значение 0 скрывает список прошлых жизней.",
    en: "How many previous lives !history can show. Zero hides the previous-lives list." },
};

export const fieldHelp: Record<string, Localized> = {
  name: { ru: "Имя жителя из игровой системы имён; после !join мод назначает имя Twitch.", en: "Citizen name from the game's name system; !join assigns the Twitch name." },
  ageGroup: { ru: "Возрастная группа из компонента Citizen: ребёнок, подросток, взрослый или пожилой.", en: "Age group from Citizen: child, teen, adult, or elderly." },
  status: { ru: "Статус текущей жизни из сохранённой истории: жив, умер или недоступен.", en: "Current life status from the saved journal: alive, deceased, or missing." },
  householdSize: { ru: "Число жителей в буфере семьи, если игровая семья доступна.", en: "Number of residents in the household buffer, when available." },
  home: { ru: "Название домашнего здания через игровую систему имён. Это не адрес улицы.", en: "Home building name from the game's name system. This is not a street address." },
  employment: { ru: "Наличие компонента Worker у активного жителя; не показывает профессию.", en: "Whether the active citizen has a Worker component; it is not a profession." },
  work: { ru: "Название рабочего здания из Worker и игровой системы имён, если работа есть.", en: "Workplace building name from Worker and the game's name system, if employed." },
  school: { ru: "Название школы из Student и игровой системы имён, если житель учится.", en: "School name from Student and the game's name system, if enrolled." },
  currentBuilding: { ru: "Название здания, где житель находится сейчас; оно может отличаться от дома.", en: "Name of the building where the resident is now; it may differ from home." },
  locationType: { ru: "Сравнение текущего здания с домом и работой: дома, на работе или в другом здании.", en: "Compares the current building with home and work: at home, at work, or in another building." },
  coordinates: { ru: "Текущая игровая позиция жителя, если система UI вернула координаты.", en: "Current game position of the citizen, if the UI system provides coordinates." },
  totalLives: { ru: "Число жизней этого Twitch-зрителя в городской истории.", en: "Number of this Twitch viewer's lives in the city journal." },
  currentLife: { ru: "Порядковый номер текущей жизни по городской истории.", en: "Current life number from the city journal." },
  startDate: { ru: "Дата начала текущей жизни из городской истории, если записана.", en: "Start date of the current life from the city journal, if recorded." },
  endDate: { ru: "Дата окончания текущей жизни. Для живого жителя её обычно нет.", en: "End date of the current life. It is usually absent for a living resident." },
  causeOfDeath: { ru: "Причина смерти из истории. Сейчас игра часто не даёт её, поэтому поле может быть пустым.", en: "Cause of death from the journal. It is often unavailable and may be empty." },
  previousLives: { ru: "Статусы прошлых жизней из городской истории; число записей ограничивается настройкой ниже.", en: "Statuses of previous lives from the city journal; the count is limited by the setting below." },
};

export const helpText = (key: string, locale: CommandLocale): string =>
  choose(settingHelp[key] || fieldHelp[key] || { ru: "Описание недоступно.", en: "Description unavailable." }, locale);

export const nextAdvanced = (current: "global" | "command" | null, section: "global" | "command") =>
  current === section ? null : section;
export const nextHelp = (current: string | null, key: string) => current === key ? null : key;

export const categoryText = (category: string, locale: CommandLocale): string => {
  const parts = category.split(" / ");
  return parts[locale === "ru" ? 1 : 0] || category;
};

const labels: Record<string, Localized> = {
  name: { ru: "житель", en: "citizen" }, ageGroup: { ru: "возраст", en: "age" },
  status: { ru: "статус", en: "status" }, householdSize: { ru: "членов семьи", en: "household members" },
  home: { ru: "дом", en: "home" }, employment: { ru: "занятость", en: "employment" },
  work: { ru: "работа", en: "work" }, school: { ru: "учёба", en: "school" },
  currentBuilding: { ru: "здание", en: "building" }, locationType: { ru: "место", en: "location" },
  coordinates: { ru: "координаты", en: "coordinates" }, totalLives: { ru: "жизней", en: "lives" },
  currentLife: { ru: "текущая жизнь", en: "current life" }, startDate: { ru: "начало", en: "start" },
  endDate: { ru: "конец", en: "end" }, causeOfDeath: { ru: "причина смерти", en: "cause of death" },
  previousLives: { ru: "прошлые жизни", en: "previous lives" },
};

export function demoFieldValue(id: string, config: CommandConfig): string {
  const ru = config.language === "ru";
  const values: Record<string, string> = {
    name: ru ? "Алекс" : "Alex", ageGroup: ru ? "взрослый" : "adult",
    status: ru ? "жив" : "alive", householdSize: "3", home: ru ? "Жилой дом" : "Residential building",
    employment: ru ? "работает" : "employed", work: ru ? "Офис" : "Office",
    school: "", currentBuilding: ru ? "Магазин" : "Shop",
    locationType: ru ? "в здании" : "in a building", coordinates: "124, 15, 239",
    totalLives: "3", currentLife: "3", startDate: "2026-01-01", endDate: "", causeOfDeath: "",
    previousLives: config.history.previousLivesLimit === 0 ? "" :
      (ru ? "#1 умер, #2 умер" : "#1 deceased, #2 deceased")
        .split(", ").slice(-config.history.previousLivesLimit).join(", "),
  };
  return values[id] || "";
}

export function commandPreview(name: CommandName, config: CommandConfig): string {
  const option = config[name];
  const ru = config.language === "ru";
  if (!option.enabled) return ru ? "Команда выключена." : "Command is disabled.";
  if (!config.responsesEnabled || !option.responseEnabled) return ru ? "Ответы в чат выключены." : "Chat replies are off.";
  const prefix = ru ? "@Зритель" : "@Viewer";
  let reply: string;
  if (name === "join") reply = prefix + (ru ? ", вы стали жителем города!" : ", you joined the city!");
  else {
    const fields = (Array.isArray(option.selectedFields) ? option.selectedFields : [])
      .filter(id => labels[id])
      .map(id => {
        const value = demoFieldValue(id, config) || (config.missingData === "label" ? (ru ? "нет данных" : "no data") : "");
        return value ? choose(labels[id], config.language as CommandLocale) + ": " + value : "";
      }).filter(Boolean);
    reply = prefix + (fields.length ? ", " + fields.join(". ") + "." : "");
  }
  const limit = Math.max(1, option.maxResponseLength);
  return reply.length <= limit ? reply : reply.slice(0, Math.max(0, limit - 1)).trim() + "…";
}

export function joinRepeatPreview(config: CommandConfig): string {
  const ru = config.language === "ru";
  if (!config.join.enabled || !config.responsesEnabled || !config.join.responseEnabled || !config.join.alreadyJoinedResponse)
    return ru ? "Повторный !join: без ответа." : "Repeated !join: no reply.";
  const message = ru ? "@Зритель, вы уже житель города." : "@Viewer, you already joined the city.";
  const limit = Math.max(1, config.join.maxResponseLength);
  return (ru ? "Повторный !join: " : "Repeated !join: ") +
    (message.length <= limit ? message : message.slice(0, Math.max(0, limit - 1)).trim() + "…");
}
