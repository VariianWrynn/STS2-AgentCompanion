using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;

internal static class Program
{
    private static Assembly _mod = null!;
    private static Assembly _game = null!;
    private static object _score = null!;
    private static int _checks;
    private static int _failures;

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: PlannerPotionVulnerableRegression <LocalAiTeammate.dll> <game-data-directory>");
            return 2;
        }
        string modPath = Path.GetFullPath(args[0]);
        string gameDirectory = Path.GetFullPath(args[1]);
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            foreach (string directory in new[] { Path.GetDirectoryName(modPath)!, gameDirectory })
            {
                string candidate = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(candidate)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
            }
            return null;
        };
        _mod = AssemblyLoadContext.Default.LoadFromAssemblyPath(modPath);
        _game = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameDirectory, "sts2.dll"));
        return Run();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run()
    {
        _score = New("CombatActionScore");
        new Harmony("localai.tests.potion-vulnerable").Patch(ModType("CombatActionScorer").GetMethod("Score")!,
            prefix: new HarmonyMethod(typeof(Program).GetMethod(nameof(SkipScore), BindingFlags.Static | BindingFlags.NonPublic)!));
        Console.WriteLine("Scorer.Score is replaced with a neutral score to avoid Godot logging. Planner methods are unmodified.");

        foreach (string name in new[] { "FirePotion", "PoisonPotion", "WeakPotion", "BlockPotion" })
        {
            string id = NativeId("Potions." + name);
            var (context, planned) = PlanPotion(id, "enemy_a");
            Equal(id + " vulnerable amount", 0, Get<int>(planned, "Vulnerable"));
            Equal(id + " applies vulnerable", false, Get<bool>(planned, "AppliesVulnerable"));
            Equal(id + " then 6-damage attack", 6, FollowUpDamage(context, planned));
        }

        string vulnerableId = NativeId("Potions.VulnerablePotion");
        Equal("native single-target vulnerable amount", 3, NativePowerAmount("VulnerablePotion", "VulnerablePower"));
        Equal("native single-target vulnerable target", "AnyEnemy", NativeTarget("VulnerablePotion"));
        var (singleContext, singlePotion) = PlanPotion(vulnerableId, "enemy_a");
        Equal(vulnerableId + " vulnerable amount", 3, Get<int>(singlePotion, "Vulnerable"));
        Equal(vulnerableId + " applies vulnerable", true, Get<bool>(singlePotion, "AppliesVulnerable"));
        Equal(vulnerableId + " then 6-damage attack", 9, FollowUpDamage(singleContext, singlePotion));

        string bindingId = NativeId("Potions.PotionOfBinding");
        Equal("native binding vulnerable amount", 1, NativePowerAmount("PotionOfBinding", "VulnerablePower"));
        Equal("native binding target", "AllEnemies", NativeTarget("PotionOfBinding"));
        var (bindingContext, bindingPotion) = PlanPotion(bindingId, null);
        Equal(bindingId + " vulnerable amount", 1, Get<int>(bindingPotion, "Vulnerable"));
        Equal(bindingId + " applies vulnerable", true, Get<bool>(bindingPotion, "AppliesVulnerable"));
        Equal(bindingId + " no arbitrary target bonus", 6, FollowUpDamage(bindingContext, bindingPotion));

        object cardContext = Context();
        object bash = CardAction(cardContext, "bash", NativeId("Cards.Bash"), 8, 2);
        object bashPlan = Plan(cardContext, bash);
        Equal("BASH vulnerable amount", 2, Get<int>(bashPlan, "Vulnerable"));
        Equal("BASH applies vulnerable", true, Get<bool>(bashPlan, "AppliesVulnerable"));
        Equal("BASH retains follow-up vulnerable bonus", 9, FollowUpDamage(cardContext, bashPlan));

        Equal("BASH does not amplify its own first hit", 8, DamageSequence(cardContext, bashPlan));
        object secondBash = Plan(cardContext, CardAction(cardContext, "second_bash", NativeId("Cards.Bash"), 8, 2));
        Equal("BASH then BASH amplifies only the second hit", 20, DamageSequence(cardContext, bashPlan, secondBash));
        object strikePlan = Plan(cardContext, CardAction(cardContext, "timing_strike", "STRIKE", 6, 0));
        Equal("strike then BASH does not apply vulnerable backward", 14, DamageSequence(cardContext, strikePlan, bashPlan));
        Equal("BASH then strike totals native-order damage", 17, DamageSequence(cardContext, bashPlan, strikePlan));
        Equal("prior vulnerable potion still amplifies BASH", 12, DamageSequence(cardContext, singlePotion, bashPlan));

        foreach (var card in new[] { (Name: "BeamCell", Damage: 3, Vulnerable: 1), (Name: "Break", Damage: 20, Vulnerable: 5) })
        {
            object context = Context();
            string id = NativeId("Cards." + card.Name);
            object attack = Plan(context, CardAction(context, "timing_attack", id, card.Damage, card.Vulnerable));
            object followUp = Plan(context, CardAction(context, "timing_follow_up", "STRIKE", 6, 0));
            Equal(id + " does not amplify its own first hit", card.Damage, DamageSequence(context, attack));
            Equal(id + " still amplifies the following attack", card.Damage + 9, DamageSequence(context, attack, followUp));
        }
        object unknown = Plan(cardContext, CardAction(cardContext, "unknown_timing", "UNVERIFIED_CARD", 8, 2));
        Equal("unverified card timing retains existing behavior", 12, DamageSequence(cardContext, unknown));

        Console.WriteLine($"Production planner potion checks: {_checks - _failures} passed, {_failures} failed.");
        return _failures == 0 ? 0 : 1;
    }

    private static bool SkipScore(ref object __result)
    {
        __result = _score;
        return false;
    }

    private static Type ModType(string name) => _mod.GetType("Sts2LlmCoop." + name, throwOnError: true)!;
    private static Type GameType(string name) => _game.GetType("MegaCrit.Sts2.Core." + name, throwOnError: true)!;
    private static object New(string name) => Activator.CreateInstance(ModType(name), nonPublic: true)!;
    private static void Set(object instance, string property, object? value) => instance.GetType().GetProperty(property)!.SetValue(instance, value);
    private static T Get<T>(object instance, string property) => (T)instance.GetType().GetProperty(property)!.GetValue(instance)!;
    private static object List(Type element) => Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;
    private static object Dictionary(Type value) => Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), value))!;

    private static string NativeId(string name)
    {
        object id = GameType("Models.ModelDb").GetMethod("GetId", [typeof(Type)])!
            .Invoke(null, [GameType("Models." + name)])!;
        string entry = Get<string>(id, "Entry");
        Console.WriteLine($"Native model {name} ID={entry}");
        return entry;
    }

    private static object NativePotion(string name) => RuntimeHelpers.GetUninitializedObject(GameType("Models.Potions." + name));
    private static string NativeTarget(string name)
    {
        object potion = NativePotion(name);
        return potion.GetType().GetProperty("TargetType")!.GetValue(potion)!.ToString()!;
    }

    private static int NativePowerAmount(string name, string variable)
    {
        object potion = NativePotion(name);
        var variables = (System.Collections.IEnumerable)potion.GetType()
            .GetProperty("CanonicalVars", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(potion)!;
        return variables.Cast<object>().Where(value => Get<string>(value, "Name") == variable)
            .Select(value => Get<int>(value, "IntValue")).Single();
    }

    private static object Context()
    {
        object context = New("DeterministicCombatContext");
        Set(context, "HandCardsByInstanceId", Dictionary(ModType("ResolvedCardView")));
        Set(context, "LegalActions", List(ModType("AiLegalActionOption")));
        Set(context, "EnemiesById", Dictionary(ModType("DeterministicEnemyState")));
        Set(context, "CombatConfig", ModType("AiCharacterCombatConfig").GetMethod("CreateBuiltInDefault")!
            .Invoke(null, ["default", "Default"]));
        return context;
    }

    private static (object Context, object Planned) PlanPotion(string id, string? target)
    {
        object context = Context();
        object action = New("AiLegalActionOption");
        Set(action, "ActionId", "potion_" + id);
        Set(action, "ActionType", "UsePotion");
        Set(action, "CardId", id);
        Set(action, "TargetId", target);
        return (context, Plan(context, action));
    }

    private static object CardAction(object context, string instance, string id, int damage, int vulnerable)
    {
        object card = New("ResolvedCardView");
        Set(card, "CardInstanceId", instance);
        Set(card, "CardId", id);
        object effects = List(ModType("NormalizedEffectDescriptor"));
        AddEffect("DealDamage", damage, null);
        if (vulnerable > 0) AddEffect("ApplyPower", vulnerable, "Vulnerable");
        Set(card, "Effects", effects);
        object cards = context.GetType().GetProperty("HandCardsByInstanceId")!.GetValue(context)!;
        cards.GetType().GetMethod("Add")!.Invoke(cards, [instance, card]);
        object action = New("AiLegalActionOption");
        Set(action, "ActionId", instance);
        Set(action, "ActionType", "PlayCard");
        Set(action, "CardId", id);
        Set(action, "CardInstanceId", instance);
        Set(action, "TargetId", "enemy_a");
        return action;

        void AddEffect(string kind, int amount, string? power)
        {
            object effect = New("NormalizedEffectDescriptor");
            Set(effect, "Kind", Enum.Parse(ModType("EffectKind"), kind));
            Set(effect, "TargetScope", Enum.Parse(ModType("TargetScope"), "SingleEnemy"));
            Set(effect, "Amount", amount);
            Set(effect, "AppliedPowerId", power);
            effects.GetType().GetMethod("Add")!.Invoke(effects, [effect]);
        }
    }

    private static object Plan(object context, object action)
    {
        object planner = Activator.CreateInstance(ModType("CombatTurnLinePlanner"), [New("CombatActionScorer")])!;
        return planner.GetType().GetMethod("BuildPlannableAction", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(planner, [context, action])!;
    }

    private static int FollowUpDamage(object context, object first)
    {
        Type nodeType = ModType("CombatTurnLinePlanner").GetNestedType("LineNode", BindingFlags.NonPublic)!;
        MethodInfo apply = nodeType.GetMethod("Apply")!;
        object node = Activator.CreateInstance(nodeType, [10])!;
        node = apply.Invoke(node, [context, first])!;
        int damageBeforeFollowUp = Get<int>(node, "TotalDamageDealt");
        object followUp = Plan(context, CardAction(context, "strike", "STRIKE", 6, 0));
        node = apply.Invoke(node, [context, followUp])!;
        return Get<int>(node, "TotalDamageDealt") - damageBeforeFollowUp;
    }

    private static int DamageSequence(object context, params object[] plannedActions)
    {
        Type nodeType = ModType("CombatTurnLinePlanner").GetNestedType("LineNode", BindingFlags.NonPublic)!;
        MethodInfo apply = nodeType.GetMethod("Apply")!;
        object node = Activator.CreateInstance(nodeType, [10])!;
        foreach (object planned in plannedActions)
        {
            node = apply.Invoke(node, [context, planned])!;
        }
        return Get<int>(node, "TotalDamageDealt");
    }

    private static void Equal<T>(string label, T expected, T actual)
    {
        _checks++;
        bool passed = EqualityComparer<T>.Default.Equals(expected, actual);
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {label}: expected={expected}, actual={actual}");
        if (!passed) _failures++;
    }
}
