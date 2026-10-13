using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media.Imaging;

namespace PawsPatchLauncher;

public sealed record ChatGlyph(string Id, string Ru, string En)
{
    public string Token => ":" + Id + ":";
}

public static class ChatGlyphs
{
    public static IReadOnlyList<ChatGlyph> All { get; } = new ChatGlyph[] {
        new("ch_mana_crystal","Мана","Mana"), new("ch_treasure_chest","Золото","Gold"),
        new("ch_wood_pile","Дерево","Wood"), new("ch_rock_pile","Камень","Stone"),
        new("ch_anvil","Железо","Iron"), new("ch_sword_on_shield","Атака и защита","Attack and defense"),
        new("ch_eye","Обзор","Vision"), new("ch_crate","Снабжение","Supply"),
        new("ch_man","Человек","Man"), new("ch_man_with_spear","Копейщик","Spearman"),
        new("ch_man_with_bow","Лучник","Archer"), new("ch_gem","Самоцвет","Gem"),
        new("ch_boot","Скорость","Speed"), new("ch_sword","Меч","Sword"),
        new("ch_bow","Лук","Bow"), new("ch_shield","Щит","Shield"),
        new("ch_mana_flame","Пламя маны","Mana flame"), new("ch_pickaxe","Кирка","Pickaxe"),
        new("ch_rank2","Ранг 2","Rank 2"), new("ch_rank3","Ранг 3","Rank 3"),
        new("ch_rank4","Ранг 4","Rank 4"), new("ch_rank5","Ранг 5","Rank 5"),
        new("ch_fire","Огонь","Fire"), new("ch_siege","Осада","Siege"),
        new("ch_khaldunite","Халдунит","Khaldunite"), new("ch_magic","Магия","Magic")
    };
    private static readonly Dictionary<string, ChatGlyph> ByToken = All.ToDictionary(g => g.Token, StringComparer.Ordinal);
    private static readonly Regex Tokens = new(":ch_[a-z0-9_]{1,40}:", RegexOptions.CultureInvariant);
    private static readonly Dictionary<string, BitmapImage> Images = new();

    public static Image CreateImage(ChatGlyph glyph, bool russian = false) => CreateImage(glyph,russian?"ru":"en");
    public static Image CreateImage(ChatGlyph glyph, string language)
    {
        if (!Images.TryGetValue(glyph.Id, out var bitmap))
        {
            // Local test builds have a different assembly name; resolve resources from their owner.
            var assemblyName = typeof(ChatGlyphs).Assembly.GetName().Name;
            bitmap = new BitmapImage(new Uri($"pack://application:,,,/{assemblyName};component/Assets/ChatGlyphs/{glyph.Id}.png"));
            bitmap.Freeze(); Images[glyph.Id] = bitmap;
        }
        var label = UiLanguages.Text(language,glyph.Ru,glyph.En);
        var image = new Image { Source = bitmap, Width = 22, Height = 22, ToolTip = label, Margin = new(1, 0, 1, 0) };
        System.Windows.Automation.AutomationProperties.SetName(image, label);
        return image;
    }

    public static IEnumerable<Inline> Inlines(string text, bool russian = false) => Inlines(text,russian?"ru":"en");
    public static IEnumerable<Inline> Inlines(string text, string language)
    {
        var offset = 0;
        foreach (Match match in Tokens.Matches(text))
        {
            if (!ByToken.TryGetValue(match.Value, out var glyph)) continue;
            if (match.Index > offset) yield return new Run(text[offset..match.Index]);
            yield return new InlineUIContainer(CreateImage(glyph, language)) { Tag = glyph.Token, BaselineAlignment = BaselineAlignment.Center };
            offset = match.Index + match.Length;
        }
        if (offset < text.Length) yield return new Run(text[offset..]);
    }

    public static void Render(TextBlock target, string text, bool russian = false) => Render(target,text,russian?"ru":"en");
    public static void Render(TextBlock target, string text, string language)
    {
        target.Inlines.Clear();
        target.Inlines.AddRange(Inlines(text, language));
    }
}
