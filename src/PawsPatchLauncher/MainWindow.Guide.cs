using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace PawsPatchLauncher;

public partial class MainWindow
{
    private string _guideSubject = "general";
    private string _guideVariant = "mod";
    private const string EnsrickGitHubUrl = "https://github.com/Ensrick";

    private string GuideIdentity() => System.Text.Json.JsonSerializer.Serialize(new
    {
        Patch = PatchGuide.Resolve(GuideChannel()), Mods = GuideChannel()?.ModGuides
    });

    private void GuideSubject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string subject } || subject == _guideSubject) return;
        _guideSubject = subject;
        _guideVariant = "mod";
        RefreshAboutPage(); MainOptionsScroll.ScrollToTop(); Motion.Reveal(AboutEntriesPanel);
    }

    private void GuideVariant_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string variant } || variant == _guideVariant) return;
        _guideVariant = variant;
        RefreshAboutPage(); MainOptionsScroll.ScrollToTop(); Motion.Reveal(AboutEntriesPanel);
    }

    private void SyncGuideSubjects()
    {
        GuideTitleText.Text = T("Справка", "Guide");
        GuideGeneralTab.Content = T("Общее", "Overview");
        GuideVanillaTab.Content = GameMod.Name(GameMod.Vanilla);
        GuideModTab.Content = T("О моде", "About the mod");
        foreach (var button in new[] { GuideGeneralTab, GuideVanillaTab, GuideArcaneTab, GuideImmortalsTab })
            SetChangelogTabState(button, (string)button.Tag == _guideSubject);
        var patch = _guideSubject == GameMod.ArcaneWars || PatchGuide.IsValid(GuideCatalog.Resolve(GuideChannel(), _guideSubject)?.PatchGuide);
        GuideVariantTabs.Visibility = patch ? Visibility.Visible : Visibility.Collapsed;
        SetChangelogTabState(GuideModTab, _guideVariant == "mod");
        SetChangelogTabState(GuidePatchTab, _guideVariant == "patch");
    }

    private void RenderGeneralOrModGuide()
    {
        AboutTabsPanel.Visibility = AboutCategoryText.Visibility = Visibility.Collapsed;
        AboutEntriesPanel.Children.Clear();
        _renderedGuideIdentity = GuideIdentity();
        if (_guideSubject == "general")
        {
            AboutTitleText.Text = T("Kohan II, моды и Paw's Patch", "Kohan II, mods and Paw's Patch");
            AboutIntroText.Text = T(
                "Во вкладке «Компоненты» выберите Vanilla, Arcane Wars или Immortals. Новый мод устанавливается один раз вместе со всеми его компонентами. Языковые пакеты скачиваются только для выбранных текста и озвучки. Сохранённые моды переключаются кнопкой «Применить настройки», в том числе без интернета. Пока настройки не применены, запуск игры недоступен. Для каждого режима доступен свой выключаемый Paw's Patch. Vanilla без него возвращает оригинальную игру с выбранным языком.",
                "Choose Vanilla, Arcane Wars or Immortals in Components. A new mod is installed once with all its components. Language packages are downloaded only for the selected text and speech. Switch between stored mods with Apply settings, including offline. Launch is unavailable until your settings are applied. Each mode has its own optional Paw's Patch. Vanilla with the patch disabled restores the original game with your selected language.");
            AddGuideCard("language", T("Язык игры и лаунчера", "Game and launcher languages"), T(
                "Текст и озвучка выбираются независимо в карточке модов и сохраняются при переключении режимов и каналов. Перевод текста работает в оригинальной игре и модах независимо от включения Paw's Patch. Доступные языки озвучки могут отличаться от языков текста. Загруженные языки сохраняются для повторного применения без интернета. Язык самого лаунчера выбирается отдельно в настройках.",
                "Text and speech are selected independently in the mod card and survive mode and channel changes. Text translations work in the original game and mods independently of Paw's Patch. Available speech languages may differ from text languages. Downloaded languages are retained for offline reuse. The launcher's language is selected separately in Settings."));
            AddGuideCard("compatibility", T("Версии и совместимость", "Versions and compatibility"), T(
                "Версия установленной игры указана внизу слева. Если EXE игры не поддерживается, изменения исполняемого файла отключаются. Доступные файловые компоненты можно применить отдельно. Если подходящий патч уже вышел, красное окно предлагает его установить. Для сетевого матча нужны одинаковые мод, версия и игровые настройки у всех участников.",
                "The installed game version is shown at the lower left. If its executable is unsupported, executable changes are disabled. Available file components can be applied separately. A red dialog offers a compatible patch when one is available. Multiplayer participants need matching mods, versions and gameplay settings."));
            var credits = AddGuideCard("authors", T("Авторы и благодарности", "Authors and acknowledgements"), T(
                "Arcane Wars — Darquan Mortis. Immortals — MartialDoctor. Оба мода самостоятельны и не являются частью Paw's Patch. Общий сервер авторов открывается значком Discord справа на верхней панели. Описания модов доступны на вкладках этой справки.",
                "Arcane Wars is by Darquan Mortis. Immortals is by MartialDoctor. Both are independent mods and are not part of Paw's Patch. The Discord icon at the top right opens the authors' shared server. Mod descriptions are available in this guide's tabs."));
            var contribution = (TextBlock)credits.Children[1];
            contribution.Inlines.Add(new LineBreak());
            contribution.Inlines.Add(new LineBreak());
            var authorLink = new Hyperlink(new Run("Ensrick"))
            {
                NavigateUri = new Uri(EnsrickGitHubUrl), ToolTip = EnsrickGitHubUrl,
                Foreground = (Brush)FindResource("GoldBrightBrush"), FontWeight = FontWeights.SemiBold
            };
            authorLink.RequestNavigate += async (_, e) =>
            {
                e.Handled = true;
                if (!authorLink.IsEnabled) return;
                authorLink.IsEnabled = false;
                try { await OpenConfirmedLinkAsync(EnsrickGitHubUrl); }
                catch (Exception error) { ActivityStore.Log(error); ShowToast(BrowserOpenError, true); }
                finally { authorLink.IsEnabled = true; }
            };
            contribution.Inlines.Add(authorLink);
            contribution.Inlines.Add(new Run(T(
                " — работа с архивами RWD, сетевые автосохранения и основа восстановления матча после отключения.",
                " — RWD archive tools, multiplayer autosaves and the foundation of match recovery after a disconnection.")));
            contribution.Inlines.Add(new LineBreak());
            contribution.Inlines.Add(new Run(T(
                "Адаптация и интеграция в Paw’s Launcher / Paw’s Patch — Paw. Спасибо за вклад в проект!",
                "Adaptation and integration into Paw’s Launcher / Paw’s Patch — Paw. Thank you for contributing!")));
            return;
        }
        var guide = GuideCatalog.Resolve(GuideChannel(), _guideSubject);
        AboutTitleText.Text = GameMod.Name(_guideSubject, _text.Language == "ru");
        if (guide is null)
        {
            AboutIntroText.Text = _guideSubject == GameMod.Vanilla
                ? T("Оригинальная Kohan II: Kings of War с выбранным языком игры.", "The original Kohan II: Kings of War with your selected game language.")
                : T("Описание этого мода пока не получено. Проверьте обновления.", "The guide for this mod has not been received. Check for updates.");
            return;
        }
        var introduction = GuideCatalog.IntroductionSection(guide);
        AboutIntroText.Text = guide.Description.Get(_text.Language)
            + (introduction is null ? "" : "\n\n" + introduction.Body.Get(_text.Language))
            + "\n\n" + T("Версия: ", "Version: ") + guide.Version
            + (guide.Author.Length > 0 ? T(" · Автор: ", " · Author: ") + guide.Author : "");
        foreach (var section in guide.Sections.Where(section => !ReferenceEquals(section, introduction)))
            AddGuideCard(section.Id, section.Title.Get(_text.Language), section.Body.Get(_text.Language));
    }

    private StackPanel AddGuideCard(string id, string title, string body)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("CardTitle") });
        content.Children.Add(new TextBlock { Text = body, Style = (Style)FindResource("CardDescription") });
        AboutEntriesPanel.Children.Add(new Border { Tag = id, Style = (Style)FindResource("Card"), Margin = new(0, 0, 0, 14), Child = content });
        return content;
    }
}
