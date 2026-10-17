namespace PawsPatchLauncher;

public sealed class Localization
{
    private string _language;

    public Localization(string language) => _language = Normalize(language);

    public string Language => _language;
    public void SetLanguage(string language) => _language = Normalize(language);
    public string ColorText(string key, ChannelManifest? channel)
    {
        var count = channel?.PlayerColorCount is >= 16 and <= 64 ? channel.PlayerColorCount : PatchGuide.PlayerColorCount;
        return this[key].Replace(PatchGuide.PlayerColorCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            count.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    public static string? KeyFor(string? text) => text is null ? null
        : Strings.FirstOrDefault(pair => pair.Value.Ru == text || pair.Value.En == text).Key;

    public string this[string key]
        => Strings.TryGetValue(key, out var value) ? UiLanguages.Text(_language, value.Ru, value.En) : key;

    private static string Normalize(string language) => UiLanguages.Normalize(language);

    private static readonly Dictionary<string, (string Ru, string En)> Strings = new()
    {
        ["app.title"] = ("Paw's Launcher для Kohan II", "Paw's Launcher for Kohan II"),
        ["app.subtitle"] = ("Моды и обновления игры", "Game mods and updates"),
        ["nav.home"] = ("Главная", "Home"),
        ["nav.modules"] = ("Компоненты", "Components"),
        ["nav.settings"] = ("Настройки", "Settings"),
        ["nav.about"] = ("О патче", "About the patch"),
        ["status.ready"] = ("Готово к игре", "Ready to play"),
        ["status.notinstalled"] = ("Патч не установлен", "Patch is not installed"),
        ["status.notfound"] = ("Kohan II не найден", "Kohan II not found"),
        ["status.feedmissing"] = ("Сервер обновлений ещё не подключён", "Update server is not configured yet"),
        ["game.path"] = ("Папка игры", "Game folder"),
        ["game.version"] = ("Версия игры", "Game version"),
        ["patch.version"] = ("Версия патча", "Patch version"),
        ["modules.title"] = ("Компоненты патча", "Patch components"),
        ["modules.core"] = ("Paw's Patch", "Paw's Patch"),
        ["modules.core.desc"] = ("Большие карты, расширенные настройки матчей и исправления игры", "Larger maps, expanded match settings and game fixes"),
        ["modules.core.help"] = ("Базовые возможности Paw's Patch:\n\n• До 16 королевств и 8 команд.\n• Размеры карт 1024×1024 и 1152×1152.\n• Случайный тип карты и случайное время суток с сохранением настроек.\n• Камера на WASD и союзная метка на F в профиле Dvorak.\n• Версии модов в главном меню и исправление отображения «−0» в лимите рот.\n• Исправлено отображение цветов на значках рот.\n\nНа карте 1152×1152 возможны вылеты из-за ограничений игры.", "Base Paw's Patch features:\n\n• Up to 16 kingdoms and 8 teams.\n• 1024×1024 and 1152×1152 map sizes.\n• Random map type and time of day with persistent settings.\n• WASD camera and F allied map marker in the Dvorak profile.\n• Mod versions in the main menu and corrected negative-zero display in the company limit.\n• Fixed color display on company badges.\n\n1152×1152 maps may crash due to game limits."),
        ["modules.ru"] = ("Локализация игры", "Game language"),
        ["modules.ru.desc"] = ("Общий язык для Vanilla и всех модов", "Shared language for Vanilla and every mod"),
        ["modules.ru.help"] = ("Текст и озвучку можно выбрать отдельно. Оба выбора сохраняются при переключении модов и применяются вместе с настройками. Скачиваются только выбранные языки; уже загруженные файлы используются повторно, пока не выйдет их обновление.\n\nПеревод текста охватывает оригинальную игру, Immortals, Arcane Wars и Paw's Patch. Доступные языки озвучки могут отличаться от языков текста. Язык лаунчера настраивается отдельно. Локализация не меняет игровые характеристики.", "Choose text and speech separately. Both choices persist across mods and are applied with your settings. Only selected languages are downloaded; cached files are reused until an update is available.\n\nText translations cover the original game, Immortals, Arcane Wars and Paw's Patch. Available speech languages may differ from text languages. Launcher language is configured separately. Localization does not change gameplay statistics."),
        ["modules.colors"] = ("Расширенные цвета игроков", "Extended player colors"),
        ["modules.powers"] = ("Отключение Powers и Shards", "Disable Powers and Shards"),
        ["modules.powers.desc"] = ("Отключает глобальные способности и ресурс Shards из Arcane Wars", "Disables Arcane Wars global powers and the Shards resource"),
        ["modules.powers.help"] = ("Включено: глобальные способности, их меню, действия ИИ и производство Shards отключены. Выключено: обе механики Arcane Wars возвращаются вместе. Остальные настройки патча сохраняются. После переключения начните новый матч; сохранения с другим набором ресурсов могут быть несовместимы.", "Enabled: global powers, their menu, AI use and Shards production are disabled. Off: both Arcane Wars mechanics are restored together. Other patch settings are preserved. Start a new match after switching; saves using a different resource set may be incompatible."),
        ["modules.powers.unavailable"] = ("Для переключения нужен обновлённый пакет канала патча", "An updated patch-channel package is required to switch this option"),
        ["modules.colors.desc"] = ($"{PatchGuide.PlayerColorCount} цветов в лобби и случайный выбор", $"{PatchGuide.PlayerColorCount} lobby colors and random selection"),
        ["modules.colors.help"] = ($"Добавляет выбор из {PatchGuide.PlayerColorCount} цветов и вариант «Случайно». Хост может менять цвета всех, клиент только свой. В сохранениях цвета показаны без возможности изменения. В текущем Релизе цвета не включают другие компоненты: вражда независимых и пропуск рассинхрона настраиваются отдельно. Всем участникам нужны одинаковые версии патча и совместимые настройки. Закреплённые старые выпуски могут иметь ограничения.", $"Adds {PatchGuide.PlayerColorCount} colors plus Random. Hosts can change all colors, clients only their own. Saved-game colors are read-only. In the current Release colors do not enable other components: independent hostility and desync bypass are configured separately. All peers need matching patch versions and compatible settings. Pinned older releases may have restrictions."),
        ["channel.beta"] = ("Бета", "Beta"),
        ["patch.channel"] = ("Канал патча:", "Patch channel:"),
        ["patch.updates"] = ("Обновления патча", "Patch updates"),
        ["patch.channel.help"] = ("Релиз: проверенная версия патча. Бета: тестовые версии и новые функции. Выбор канала не влияет на обновления лаунчера.", "Release is the tested patch version. Beta offers test versions and new features. This choice does not affect launcher updates."),
        ["patch.release.tip"] = ("Релиз патча: последняя проверенная версия. Версия и обновления лаунчера общие для Релиза и Беты.", "Patch Release: the latest tested version. Both patch channels share the same launcher version and updates."),
        ["patch.beta.tip"] = ("Бета патча: тестовые версии и новые функции. Версия и обновления лаунчера общие для Релиза и Беты.", "Patch Beta: test versions and new features. Both patch channels share the same launcher version and updates."),
        ["launcher.updates.help"] = ("Лаунчер обновляется автоматически при запуске. Его обновления не зависят от канала патча.", "The launcher updates automatically at startup. Its updates are independent of the patch channel."),
        ["patch.ready"] = ("Патч готов к игре · {0}", "Patch ready to play · {0}"),
        ["home.welcome.title"] = ("Автоматические обновления", "Automatic updates"),
        ["home.welcome.body"] = ("Лаунчер следит за обновлениями активного канала в фоне. Проверка выполняется при запуске, при смене канала и раз в минуту.", "The launcher monitors the active channel in the background. It checks at startup, after switching channels, and once per minute."),
        ["settings.title"] = ("Настройки лаунчера", "Launcher settings"),
        ["settings.language"] = ("Язык интерфейса", "Interface language"),
        ["settings.language.desc"] = ("Выберите язык интерфейса лаунчера", "Choose the launcher interface language"),
        ["settings.repair"] = ("Проверка файлов", "File verification"),
        ["settings.repair.desc"] = ("Проверяет установленные файлы Paw's Patch и сообщает, если они повреждены, изменены или отсутствуют.", "Checks the installed Paw's Patch files and reports any damaged, modified or missing files."),
        ["settings.updates.desc"] = ("Обновления патча проверяются раз в минуту. Для выбранного канала заранее загружаются все варианты настроек.", "Patch updates are checked once per minute. Every settings variant for the selected channel is downloaded in advance."),
        ["button.checknow"] = ("Проверить обновления", "Check for updates"),
        ["button.launcherupdate"] = ("Обновить лаунчер до {0}", "Update launcher to {0}"),
        ["button.patchupdate"] = ("Обновить патч · {0}", "Update patch · {0}"),
        ["button.install"] = ("Установить", "Install"),
        ["button.installed"] = ("Установлено", "Installed"),
        ["install.patch.title"] = ("Paw's Patch готов к установке", "Paw's Patch is ready to install"),
        ["install.patch.body"] = ("Лаунчер загрузит Arcane Wars, Paw's Patch и все варианты настроек выбранного канала.", "The launcher will download Arcane Wars, Paw's Patch and every settings variant for the selected channel."),
        ["update.patch.title"] = ("Доступно обновление патча · {0}", "Patch update available · {0}"),
        ["update.patch.body"] = ("Будут загружены новые компоненты выбранного канала. Уже загруженные наборы Релиза и Беты сохраняются для локального переключения. Настройки можно применить отдельно или при запуске игры.", "New components for the selected channel will be downloaded. Cached Release and Beta packages remain available for local switching. Apply settings separately or when launching the game."),
        ["update.launcher.ready"] = ("Доступна новая версия лаунчера: {0}", "A new launcher version is available: {0}"),
        ["updates.current"] = ("Установлена актуальная версия патча · {0}", "The current patch version is installed · {0}"),
        ["updates.checked"] = ("Последняя проверка: {0}", "Last checked: {0}"),
        ["modules.oos"] = ("Игнорирование рассинхронов", "Ignore desyncs"),
        ["modules.oos.official"] = ("Официальная: остановить игру", "Official: stop the game"),
        ["modules.oos.continue"] = ("Продолжать игру", "Continue the game"),
        ["modules.oos.help"] = (PatchGuide.DesyncHelpRu, PatchGuide.DesyncHelpEn),
        ["modules.independent"] = ("Вражда независимых", "Independent faction hostility"),
        ["modules.independent.desc"] = ("Логова, лагеря и независимые города получают команды семейств", "Lairs, camps and independent towns receive family factions"),
        ["modules.independent.help"] = ("После генерации карты распределяет независимые объекты по командам семейств. Семейства враждуют между собой и с игроками; выбранные монстры также охотятся на животных. Одинаковая настройка обязательна у всех игроков.", "After map generation, assigns independent objects to family factions. Families fight each other and players; selected monsters also hunt wildlife. Every player must use the same setting."),
        ["modules.spawn"] = ("Частота блуждающих рот", "Roaming company frequency"),
        ["modules.spawn.standard"] = ("Стандартная", "Standard"),
        ["modules.spawn.x4"] = ("×4", "×4"),
        ["modules.spawn.help"] = ("Стандартный режим использует обычные интервалы и шансы. ×2 сокращает интервал на 25% и увеличивает шанс события на 50%, что даёт примерно удвоенную частоту. ×4 вдвое сокращает интервал и удваивает шанс. Исключение: на ×4 Тёмный лорд из Тёмного разлома (Врат теней) появляется с частотой ×2 - попытка каждые 6 игровых минут с шансом 30%. Требуется включить «Новые блуждающие роты». Конкретное появление остаётся случайным. Работает с включёнными и выключенными новыми ротами. Настройка должна совпадать у всех игроков.", "Standard uses normal intervals and chances. ×2 shortens the interval by 25% and raises the event chance by 50%, giving about twice the frequency. ×4 halves the interval and doubles the chance. Exception: at ×4, the Shadow Lord from the Dark Rift uses ×2 frequency: one attempt every 6 game minutes with a 30% chance. Additional roaming companies must be enabled. Individual appearances remain random. Works with additional companies on or off. Every player must use the same setting."),
        ["modules.roaming"] = ("Новые блуждающие роты", "Additional roaming companies"),
        ["modules.roaming.desc"] = ("Роты из лагерей поселений, фундаментов и выбранных логов", "Companies from settlement camps, foundation camps and selected lairs"),
        ["modules.roaming.help"] = ("Добавляет выход рот из лагерей бандитов, варваров, ракшасов и слаан на местах поселений и фундаментов, а также из логов пауков, скорпионов, Тёмного разлома и руин нежити. Настройка должна совпадать у всех игроков.", "Adds roaming companies from bandit, barbarian, Rhaksha and Slaan camps on settlement and foundation sites, plus spider and scorpion lairs, the Dark Rift and undead ruins. Every player must use the same setting."),
        ["modules.ai"] = ("Улучшения ботов", "AI improvements"),
        ["modules.ai.desc"] = ("Новая оценка найма, расширения и исправления поведения ИИ", "New recruitment and expansion evaluation, and AI behavior fixes"),
        ["modules.ai.help"] = ("Включает новые параметры и исправления ИИ: оценку затрат на найм и лимитов королевства, выбор мест расширения и восстановление застрявших истощённых рот. При выключении возвращается прежнее поведение ботов. Общие списки настройки ботов в лобби остаются доступны. Настройка должна совпадать у всех участников матча.", "Enables new AI parameters and fixes: recruitment cost and kingdom capacity evaluation, expansion site selection, and recovery of stuck exhausted companies. Turning it off restores previous AI behavior. Shared bot setup controls remain available in the lobby. All match participants must use the same setting."),
        ["modules.siege"] = ("Баланс осадных машин", "Siege engine balance"),
        ["modules.siege.desc"] = ("Пять осадных машин расходуют по 0,75 очка королевства", "Five siege engines consume 0.75 kingdom points each"),
        ["modules.siege.help"] = ("Добавляет расход 0,75 очка королевства за каждое орудие: Багровую катапульту, Чумную катапульту, Баллисту Драконьего огня и Ворпальную машину. В чистом Arcane Wars у этих четырёх машин такого расхода нет.\n\nИх стоимость в золоте, содержание в ресурсах и параметры атак остаются такими, как в Arcane Wars 0.82.1.8. При выключении компонента дополнительный расход очков королевства убирается. Для сетевой игры настройка должна совпадать у всех участников.", "Adds an upkeep of 0.75 kingdom points per engine to the Crimson Catapult, Plague Catapult, Dragonfire Ballista and Vorpal Engine. These four engines have no such upkeep in pure Arcane Wars.\n\nTheir gold cost, resource upkeep and attack parameters remain as defined in Arcane Wars 0.82.1.8. Turning this component off removes the additional kingdom-point upkeep. Every multiplayer participant must use the same setting."),
        ["modules.siege.beta.help"] = ("Добавляет расход 0,75 очка королевства за каждое орудие: Багровую катапульту, Чумную катапульту, Баллисту Драконьего огня, Ворпальную машину и разрушитель «Водоворот». В чистом Arcane Wars у этих пяти машин такого расхода нет.\n\nИх стоимость в золоте, содержание в ресурсах и параметры атак остаются такими, как в Arcane Wars 0.82.1.8. При выключении компонента дополнительный расход очков королевства убирается. Для сетевой игры настройка должна совпадать у всех участников.", "Adds an upkeep of 0.75 kingdom points per engine to the Crimson Catapult, Plague Catapult, Dragonfire Ballista, Vorpal Engine and Maelstrom Destroyer. These five engines have no such upkeep in pure Arcane Wars.\n\nTheir gold cost, resource upkeep and attack parameters remain as defined in Arcane Wars 0.82.1.8. Turning this component off removes the additional kingdom-point upkeep. Every multiplayer participant must use the same setting."),
        ["modules.multiplayer.note"] = ("Для сетевой игры у всех участников должны совпадать мод, его версия и игровые настройки.", "For multiplayer, all players need the same mod, version and gameplay settings."),
        ["configuration.title"] = ("Код конфигурации", "Configuration code"),
        ["configuration.desc"] = ("Отправьте этот код друзьям перед сетевой игрой", "Send this code to friends before a multiplayer match"),
        ["configuration.help"] = ("Короткая запись всех выбранных каналов и компонентов. Совпадающий код помогает убедиться, что у игроков одинаковые настройки, но не заменяет установку или обновление файлов.", "A compact record of every selected channel and component. A matching code helps confirm identical settings, but does not replace installing or updating files."),
        ["button.copyconfig"] = ("Скопировать код", "Copy code"),
        ["configuration.copied"] = ("Код конфигурации скопирован", "Configuration code copied"),
        ["diagnostics.title"] = ("Архив диагностики", "Diagnostic archive"),
        ["diagnostics.desc"] = ("Последние 3 журналов и дампов игры, история действий и сведения о системе", "Latest 3 game logs and dumps, action history and system information"),
        ["diagnostics.help"] = ("Собирает ZIP с историей действий в лаунчере, сведениями о Windows, процессоре, памяти, видеокарте и её драйвере, свободном месте, выбранных и применённых настройках, версиях модов, логах, синхлогах, дампах и хешах. В журнал действий не записываются пароли, коды входа, введённый текст и переписка. Архив сохраняется на компьютере; автоматической отправки нет. Дампы могут содержать фрагменты памяти; проверьте архив перед публичной отправкой.", "Creates a ZIP with launcher action history, Windows, CPU, memory, GPU and driver information, free disk space, selected and applied settings, mod versions, logs, sync logs, dumps and hashes. The action journal excludes passwords, login codes, typed text and conversations. The archive is saved locally and is not sent automatically. Dumps can contain memory fragments; review before sharing publicly."),
        ["button.diagnostics"] = ("Создать архив", "Create archive"),
        ["diagnostics.progress"] = ("Собираю архив диагностики…", "Creating diagnostic archive…"),
        ["diagnostics.ready"] = ("Архив диагностики создан: {0}", "Diagnostic archive created: {0}"),
        ["diagnostics.dialog"] = ("Сохранить архив диагностики", "Save diagnostic archive"),
        ["help.close"] = ("Понятно", "Got it"),
        ["news.title"] = ("История изменений", "Changelog"),
        ["news.tab.patch"] = ("Патч", "Patch"),
        ["news.tab.launcher"] = ("Лаунчер", "Launcher"),
        ["news.empty"] = ("После подключения сервера здесь появится история обновлений.", "Release notes will appear here after the update server is connected."),
        ["button.repair"] = ("Проверить файлы", "Verify files"),
        ["button.launch"] = ("Запустить игру", "Launch game"),
        ["button.browse"] = ("Выбрать", "Browse"),
        ["progress.checking"] = ("Проверяю обновления…", "Checking for updates…"),
        ["progress.downloading"] = ("Загрузка", "Downloading"),
        ["progress.installing"] = ("Установка и проверка файлов…", "Installing and verifying files…"),
        ["progress.beforelaunch"] = ("Применяю выбранные настройки перед запуском…", "Applying the selected settings before launch…"),
        ["error.title"] = ("Ошибка", "Error"),
        ["dialog.selectgame"] = ("Выберите папку Kohan II", "Select the Kohan II folder"),
    };
}
