using PoeRedux.Patches;
using PoeRedux.Services;

namespace PoeRedux;

/// <summary>Danh sách patch theo game — dùng chung cho giao diện WPF và bản dòng lệnh.</summary>
public static class PatchCatalog
{
    public static IPatch[] For(PoeGame game) => game == PoeGame.PoE2
        ?
        [
            new Camera(),
            new Minimap(),
            new ColorMods2(),
            new MonsterHP(),
            new MonsterIcons(),
            new AtlasFog(),
            new Fog(),
            new Rain(),
            new Clouds(),
            new EnvironmentParticles2(),
            new Shadow(),
            new Light(),
            new Delirium(),
            new ReduceEffects(),
            new Particles(),
            new Effects(),
            new BlackScreen(),
        ]
        :
        [
            new Camera(),
            new Minimap(),
            new ColorMods(),
            new MonsterHP(),
            new MonsterIcons(),
            new Fog(),
            new EnvironmentParticles(),
            new Shadow(),
            new Light(),
            new Corpse(),
            new Delirium(),
            new ReduceEffects(),
            new Particles(),
            new Effects(),
            new BlackScreen(),
        ];

    /// <summary>Tên ngắn cho dòng lệnh: "Monster HP Patch" → "monster-hp", "Black Screen Patch (Experimental)" → "black-screen".</summary>
    public static string Slug(IPatch patch)
    {
        var name = patch.Name;
        var paren = name.IndexOf('(');
        if (paren >= 0) name = name[..paren];
        name = name.Replace("Patch", "", StringComparison.OrdinalIgnoreCase).Trim();
        return string.Join('-', name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
