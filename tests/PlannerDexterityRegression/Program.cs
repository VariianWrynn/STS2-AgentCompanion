using System.Reflection;
using System.Runtime.Loader;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: PlannerDexterityRegression <LocalAiTeammate.dll> <game-data-directory>");
    return 2;
}

string modPath = Path.GetFullPath(args[0]);
string gameDirectory = Path.GetFullPath(args[1]);
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    foreach (string directory in new[] { Path.GetDirectoryName(modPath)!, gameDirectory })
    {
        string candidate = Path.Combine(directory, name.Name + ".dll");
        if (File.Exists(candidate))
        {
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
        }
    }
    return null;
};
Assembly mod = AssemblyLoadContext.Default.LoadFromAssemblyPath(modPath);
Type plannerType = ModType("CombatTurnLinePlanner");
Type nodeType = plannerType.GetNestedType("LineNode", BindingFlags.NonPublic)!;
Type plannedActionType = plannerType.GetNestedType("PlannableAction", BindingFlags.NonPublic)!;
MethodInfo apply = nodeType.GetMethod("Apply")!;
MethodInfo getBlock = ModType("ResolvedCardViewExtensions").GetMethod("GetEstimatedBlock")!;
int failures = 0;

Check("persistent dexterity then attack", 0, ("setup", 0, 2, 0), ("attack", null, 0, 0));
Check("persistent dexterity then utility", 0, ("setup", 0, 2, 0), ("utility", null, 0, 0));
Check("persistent dexterity then potion", 0, ("setup", 0, 2, 0), ("potion", null, 0, 0));
Check("persistent dexterity then two attacks", 0, ("setup", 0, 2, 0), ("attack", null, 0, 0), ("attack", null, 0, 0));
Check("persistent dexterity then block", 7, ("setup", 0, 2, 0), ("block", 5, 0, 0));
Check("persistent dexterity then zero-amount block effect", 2, ("setup", 0, 2, 0), ("block", 0, 0, 0));
Check("temporary dexterity then attack", 0, ("setup", 0, 0, 3), ("attack", null, 0, 0));
Check("temporary dexterity then block", 8, ("setup", 0, 0, 3), ("block", 5, 0, 0));
Check("block without added dexterity", 5, ("block", 5, 0, 0));

Console.WriteLine($"Production LineNode.Apply regression checks: {9 - failures} passed, {failures} failed.");
return failures == 0 ? 0 : 1;

Type ModType(string name) => mod.GetType("Sts2LlmCoop." + name, throwOnError: true)!;
object New(string name) => Activator.CreateInstance(ModType(name), nonPublic: true)!;
void Set(object instance, string property, object? value) => instance.GetType().GetProperty(property)!.SetValue(instance, value);
object EmptyList(Type element) => Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;
object Dictionary(Type value) => Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), value))!;

void Check(string label, int expected, params (string Kind, int? Block, int Dexterity, int TemporaryDexterity)[] actions)
{
    object context = New("DeterministicCombatContext");
    object cards = Dictionary(ModType("ResolvedCardView"));
    Set(context, "HandCardsByInstanceId", cards);
    Set(context, "LegalActions", EmptyList(ModType("AiLegalActionOption")));
    Set(context, "EnemiesById", Dictionary(ModType("DeterministicEnemyState")));
    Set(context, "CombatConfig", ModType("AiCharacterCombatConfig").GetMethod("CreateBuiltInDefault")!
        .Invoke(null, ["default", "Default"]));
    object node = Activator.CreateInstance(nodeType, [10])!;
    int index = 0;
    foreach (var specification in actions)
    {
        string id = "fixture_" + index++;
        object action = New("AiLegalActionOption");
        Set(action, "ActionId", id);
        Set(action, "ActionType", specification.Kind == "potion" ? "UsePotion" : "PlayCard");
        object? card = null;
        if (specification.Kind != "potion")
        {
            card = New("ResolvedCardView");
            Set(card, "CardInstanceId", id);
            Set(action, "CardInstanceId", id);
            object effects = EmptyList(ModType("NormalizedEffectDescriptor"));
            if (specification.Kind == "block")
            {
                object effect = New("NormalizedEffectDescriptor");
                Set(effect, "Kind", Enum.Parse(ModType("EffectKind"), "GainBlock"));
                Set(effect, "TargetScope", Enum.Parse(ModType("TargetScope"), "Self"));
                Set(effect, "Amount", specification.Block!.Value);
                effects.GetType().GetMethod("Add")!.Invoke(effects, [effect]);
            }
            Set(card, "Effects", effects);
            cards.GetType().GetMethod("Add")!.Invoke(cards, [id, card]);
        }
        object planned = Activator.CreateInstance(plannedActionType, nonPublic: true)!;
        Set(planned, "Action", action);
        Set(planned, "ConsumptionKey", id);
        Set(planned, "ImmediateScore", New("CombatActionScore"));
        Set(planned, "Block", getBlock.Invoke(null, [card]));
        Set(planned, "SelfDexterity", specification.Dexterity);
        Set(planned, "SelfTemporaryDexterity", specification.TemporaryDexterity);
        node = apply.Invoke(node, [context, planned])!;
    }
    int actual = (int)nodeType.GetProperty("TotalBlockGained")!.GetValue(node)!;
    bool passed = actual == expected;
    Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {label}: expected block={expected}, actual block={actual}");
    if (!passed) failures++;
}
