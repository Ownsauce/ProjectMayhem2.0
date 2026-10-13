using System;
using System.Linq;
using AO.Assets.ResourceDatabase;
using AO.Unity.Assets;

// Uses the production metadata/icon readers against an installed database, without Unity or a server.
namespace AO.Unity.Assets
{
    public static class AOInstallConfiguration
    {
        public static string InstallPath;
        public static AOInstallValidation GetConfiguredInstall() => AOInstallLocator.Validate(InstallPath);
    }
}

internal static class GameplayMetadataTests
{
    private static void Require(bool valid, string message)
    {
        if (!valid) throw new Exception(message);
    }

    private static int Main(string[] args)
    {
        AOInstallConfiguration.InstallPath = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("PROJECTMAYHEM_AO_INSTALL");
        Require(AOInstallConfiguration.GetConfiguredInstall().IsValid, "Supply the AO installation folder or PROJECTMAYHEM_AO_INSTALL.");
        Require(AOItemObjectResolver.TryResolveNano(82834, out var nano), "Resolve uploaded Pronouncement of Greatness from nano records.");
        Require(nano.DBType == 1040005 && nano.Name == "Pronouncement of Greatness", "Nano identity must not resolve a crystal or item.");
        Require(nano.StatValues.Single(value => value.Stat == 54).RawValue == 7, "Running nano requires seven NCU.");
        int icon = nano.StatValues.Single(value => value.Stat == 79).RawValue;
        Require(icon == 296675 && AOItemIconResolver.TryReadIcon(icon, out var png)
            && png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "Uploaded and running nano icon must be a loadable PNG payload.");
        Require(nano.SpellData.SelectMany(group => group.Items).Any(effect => effect.Stat == 164 && effect.Amount != 0),
            "Hover details contain Concealment effect data.");
        Require(nano.SpellData.SelectMany(group => group.Items).Any(effect => effect.SpellDescription == "MonsterShape"),
            "Hover details retain the morph effect.");
        Require(AOItemObjectResolver.TryResolveNano(287046, out var composite)
            && composite.StatValues.Any(value => value.Stat == 79 && value.RawValue > 0)
            && composite.SpellData.SelectMany(group => group.Items).Any(effect => effect.Stat > 0),
            "Composite Utility Expertise has icon and modifier metadata.");
        Require(composite.StatValues.Single(value => value.Stat == 54).RawValue == 4
            && composite.StatValues.Single(value => value.Stat == 30).RawValue == 0,
            "Composite Utility is a four-NCU buff with no explicit hostile targeting flags.");
        Require(AOItemObjectResolver.TryResolve(121567, 121567, 1, out var pistol)
            && pistol.AttackDefenseData.Attack.Any(skill => skill.Stat == 112 && skill.RawValue > 0), "Pistol attack skill needed to select one-handed ranged animations.");
        Require(AOItemObjectResolver.TryResolve(218404, 218404, 1, out var sword)
            && sword.AttackDefenseData.Attack.Any(skill => skill.Stat == 103 && skill.RawValue > 0), "Sword attack skill needed to select one-handed melee animations.");
        Require(AOItemObjectResolver.TryResolve(252158, 252158, 1, out var belt)
            && belt.SpellData.SelectMany(group => group.Items).Single(effect => effect.Stat == 181).Amount == 2,
            "Belt NCU contribution survives metadata hydration.");
        using var database = new AOResourceDatabase(AOInstallConfiguration.InstallPath);
        var names = AOResourceCatalog.Load(database).GetResources(AOResourceTypes.Animation).Values;
        foreach (var sample in new[] { (Skill: 112, Dual: true), (Skill: 103, Dual: false), (Skill: 105, Dual: false), (Skill: 116, Dual: false), (Skill: 113, Dual: false) })
        {
            int set = AOCombatAnimationCatalog.GetWeaponSet(sample.Skill, sample.Dual);
            foreach (string action in new[] { "attack-idle", "attack-hit" })
                Require(AOCombatAnimationCatalog.GetTokens(action, set).Any(token => names.Any(name => name.StartsWith("male_", StringComparison.Ordinal) && name.Contains(token))),
                    $"Installed animation exists for weapon skill {sample.Skill}, action {action}.");
        }
        foreach (string action in new[] { "special-burst", "special-fullauto", "attack-hit-left" })
            Require(AOCombatAnimationCatalog.GetTokens(action, 4).Any(token => names.Any(name => name.StartsWith("male_", StringComparison.Ordinal) && name.Contains(token))),
                $"Installed one-handed ranged animation exists for {action}.");
        Console.WriteLine("PASS installed nano icons/NCU/effects, belt capacity, weapon skills, and combat animation catalog");
        return 0;
    }
}
