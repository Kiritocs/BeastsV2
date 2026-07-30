using System;
using System.Collections.Generic;
using System.Linq;

namespace BeastsV2;

/// <summary>
/// Maps each red beast to the talisman base type it is associated with, along with that
/// talisman's implicit modifier and drop level.
/// </summary>
internal static class BeastsV2TalismanData
{
    public static readonly TalismanInfo[] AllTalismans =
    [
        // Craicic (The Deep)
        new("Craicic Croaker",      "Croaker Talisman",      "+1 to Level of all Skill Gems",                                    36),
        new("Craicic Spider Crab",  "Spider Crab Talisman",  "+15% to Quality of all Skill Gems",                                36),
        new("Craicic Maw",          "Great Maw Talisman",    "15% increased Attributes",                                         36),
        new("Craicic Sand Spitter", "Sand Spitter Talisman", "12% increased Movement Speed",                                      0),
        new("Craicic Savage Crab",  "Savage Crab Talisman",  "Damage Penetrates 15% Cold Resistance",                             0),
        new("Craicic Shield Crab",  "Shield Crab Talisman",  "30% increased Global Defences",                                    28),
        new("Craicic Squid",        "Squid Talisman",        "30% increased maximum Mana",                                        0),
        new("Craicic Vassal",       "Octopus Talisman",      "20% chance to Freeze Enemies for 1 second when they Hit you",      36),
        new("Craicic Watcher",      "Watcher Talisman",      "Damage Penetrates 15% Lightning Resistance",                        0),

        // Farric (The Wilds)
        new("Farric Tiger Alpha",         "Tiger Talisman",         "8% increased Action Speed",                            36),
        new("Farric Wolf Alpha",          "Wolf Alpha Talisman",    "+40% to Global Critical Strike Multiplier",            28),
        new("Farric Lynx Alpha",          "Lynx Talisman",          "+4% to maximum Lightning Resistance",                  28),
        new("Farric Flame Hellion Alpha", "Flame Hellion Talisman", "+4% to maximum Fire Resistance",                       28),
        new("Farric Frost Hellion Alpha", "Frost Hellion Talisman", "+4% to maximum Cold Resistance",                       28),
        new("Farric Magma Hound",         "Magma Hound Talisman",   "Unaffected by Ignite",                                 28),
        new("Farric Pit Hound",           "Pitbull Talisman",       "Warcries Exert 1 additional Attacks",                  28),
        new("Farric Chieftain",           "Chieftain Talisman",     "16% increased Area of Effect",                          0),
        new("Farric Ape",                 "Ape Talisman",           "+1 to Minimum Endurance, Frenzy and Power Charges",    28),
        new("Farric Goliath",             "Goliath Talisman",       "Projectiles Pierce 3 additional Targets",               0),
        new("Farric Goatman",             "Goatman Talisman",       "Hits ignore Enemy Physical Damage Reduction",           0),
        new("Farric Gargantuan",          "Gargantuan Talisman",    "15% increased maximum Life",                            0),
        new("Farric Taurus",              "Taurus Talisman",        "+1 to Maximum Endurance Charges",                      28),
        new("Farric Ursa",                "Ursa Talisman",          "30% increased Effect of your Marks",                   28),

        // Fenumal (The Caverns)
        new("Fenumal Hybrid Arachnid",  "Hybrid Arachnid Talisman",  "Minions have +30% to Damage over Time Multiplier",   36),
        new("Fenumal Plagued Arachnid", "Plagued Arachnid Talisman", "35% increased Effect of Withered",                     0),
        new("Fenumal Devourer",         "Devourer Talisman",         "Damage Penetrates 15% Fire Resistance",                0),
        new("Fenumal Queen",            "Carrion Queen Talisman",    "+1 to maximum number of Spectres",                    28),
        new("Fenumal Widow",            "Black Widow Talisman",      "Utility Flasks gain 2 Charges every 3 seconds",       28),
        new("Fenumal Scorpion",         "Scorpion Talisman",         "+20% to Damage over Time Multiplier",                 28),
        new("Fenumal Scrabbler",        "Scrabbler Talisman",        "+2 to Level of all Herald Skill Gems",                28),

        // Saqawine (The Sands)
        new("Saqawine Rhex",        "Rhex Talisman",        "100% of Cold and Lightning Damage from Hits taken as Fire Damage", 36),
        new("Saqawine Vulture",     "Vulture Talisman",     "Skills fire 1 additional Projectiles",                             36),
        new("Saqawine Cobra",       "Cobra Talisman",       "+1 to Maximum Frenzy Charges",                                     28),
        new("Saqawine Blood Viper", "Blood Viper Talisman", "20% increased Cooldown Recovery Rate",                              0),
        new("Saqawine Retch",       "Retch Talisman",       "+1 to Maximum Power Charges",                                      28),
        new("Saqawine Rhoa",        "Rhoa Talisman",        "Gain 15% of Maximum Life as Extra Armour",                          0),
        new("Saqawine Chimeral",    "Chimeral Talisman",    "30% increased Projectile Speed",                                    0),

        // Spirit Bosses
        new("Saqawal, First of the Sky",    "Saqawine Talisman", "100% increased Aspect of the Avian Buff Effect",    45),
        new("Craiceann, First of the Deep", "Craicic Talisman",  "100% increased Aspect of the Crab Buff Effect",     45),
        new("Farrul, First of the Plains",  "Farric Talisman",   "100% increased Aspect of the Cat Buff Effect",      45),
        new("Fenumus, First of the Night",  "Fenumal Talisman",  "100% increased Aspect of the Spider Debuff Effect", 45),
    ];

    /// <summary>
    /// Unique talismans. Unlike the base talismans above these do not drop from the beast matching
    /// their base type - Natural Hierarchy sits on the Rhex Talisman base but does not come from
    /// Saqawine Rhex. They drop from Spirit Beasts, or from the Beastcrafting recipe that creates a
    /// new Talisman, which requires The Black Morrigan.
    /// </summary>
    public static readonly UniqueTalismanInfo[] UniqueTalismans =
    [
        new("Natural Hierarchy",     "Rhex Talisman",       44),
        new("Eyes of the Greatwolf", "Greatwolf Talisman",  52),
        new("Blightwell",            "Shield Crab Talisman", 28),
        new("Rigwald's Curse",       "Wolf Alpha Talisman",  28),
        new("Night's Hold",          "Black Maw Talisman",   12),
    ];

    /// <summary>
    /// The beasts that can yield a unique talisman: the four Spirit Beasts
    /// </summary>
    public static readonly string[] UniqueTalismanSourceBeasts =
    [
        "Saqawal, First of the Sky",
        "Craiceann, First of the Deep",
        "Farrul, First of the Plains",
        "Fenumus, First of the Night",
    ];

    private static readonly HashSet<string> UniqueTalismanSourceBeastSet =
        new(UniqueTalismanSourceBeasts, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, TalismanInfo> TalismansByBeast =
        AllTalismans.ToDictionary(x => x.BeastName, x => x, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, TalismanInfo> TalismansByTalismanName =
        AllTalismans.ToDictionary(x => x.TalismanName, x => x, StringComparer.OrdinalIgnoreCase);

    public static bool TryGetByBeast(string beastName, out TalismanInfo talisman)
    {
        if (!string.IsNullOrWhiteSpace(beastName))
        {
            return TalismansByBeast.TryGetValue(beastName, out talisman);
        }

        talisman = default;
        return false;
    }

    public static bool IsUniqueTalismanSourceBeast(string beastName)
    {
        return !string.IsNullOrWhiteSpace(beastName) && UniqueTalismanSourceBeastSet.Contains(beastName);
    }

    public static bool TryGetByTalismanName(string talismanName, out TalismanInfo talisman)
    {
        if (!string.IsNullOrWhiteSpace(talismanName))
        {
            return TalismansByTalismanName.TryGetValue(talismanName, out talisman);
        }

        talisman = default;
        return false;
    }
}

/// <param name="BeastName">The red beast this talisman is associated with.</param>
/// <param name="TalismanName">The talisman base type name, matching the poe.ninja BaseType feed.</param>
/// <param name="Implicit">The talisman's implicit modifier text.</param>
/// <param name="DropLevel">
/// The base's drop level: the minimum item level it can roll at, which for a dropped item equals the
/// area's monster level. It is also the character level needed to equip it, since both come from the
/// same value. 0 means no restriction (poedb drop level 1). In practice this never binds, because
/// these talismans come off red-tier beasts and show up at item level 82-86.
/// </param>
public readonly record struct TalismanInfo(string BeastName, string TalismanName, string Implicit, int DropLevel);

/// <param name="Name">The unique talisman's name, matching the poe.ninja UniqueAccessory feed.</param>
/// <param name="BaseTypeName">The talisman base type it rolls on.</param>
/// <param name="RequiredLevel">The character level needed to equip it.</param>
public readonly record struct UniqueTalismanInfo(string Name, string BaseTypeName, int RequiredLevel);
