# Paw’s Launcher 1.0.7

- Пользовательские RWD-моды вынесены на отдельную вкладку «Моды»: добавление, включение, порядок загрузки, замена архивов, отображение совпадающих файлов и применение/отмена изменений. Выбор игры и установка компонентов остаются во вкладке «Компоненты».
- Несохранённый список модов сохраняется при повторном открытии вкладки; перед уходом с неё или запуском игры нужно применить либо отменить изменения.
- Для файлового режима, в том числе на Kohan II 1.3.68, показано понятное обозначение. Предупреждение больше не выглядит как запрет запуска всей игры: функции изменения памяти по-прежнему требуют совместимой версии игры.
- Список друзей загружается, даже если сервер отклонил дополнительные сведения о конфигурации в статусе игрока.

Спасибо Ensrick за предложение #17. Файловый режим проверен автоматическими сценариями установки, переключения без интернета и подключения RWD. Нагрузка до 100% одного ядра в наших тестах не воспроизвелась; исправление этой проблемы не заявляется. Игровые пакеты и беты не изменены.

---

# Paw’s Launcher 1.0.7

- Custom RWD mods now have a dedicated Mods page: import, enable/disable, load order, archive replacement, overlapping-file indicators and apply/undo controls. Game selection and component installation remain under Components.
- Pending mod edits survive reopening the page; apply or undo them before leaving it or launching the game.
- Clearer file-only mode labeling, including Kohan II 1.3.68. The compatibility warning no longer suggests that the entire game cannot run: memory-patching features still require a compatible game version.
- The friends list can load even if the server rejects optional configuration metadata in player presence.

Thanks to Ensrick for proposal #17. Automated checks covered file-only installation, offline switching and RWD mount configuration. We could not reproduce a CPU core being fully saturated in our tests and are not claiming a fix for it. Game packages and betas are unchanged.
