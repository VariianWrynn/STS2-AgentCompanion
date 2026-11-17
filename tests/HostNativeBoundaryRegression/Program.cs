using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using Sts2LlmCoop.HostTest;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        _checks++;
        Console.WriteLine("PASS " + message);
    }

    private static (Func<CardModel> New, TargetType Expected)[] Admitted() =>
    [
        (() => new StrikeIronclad(), TargetType.AnyEnemy),
        (() => new Bash(), TargetType.AnyEnemy),
        (() => new DefendIronclad(), TargetType.Self),
        (() => new TwinStrike(), TargetType.AnyEnemy),
        (() => new Thunderclap(), TargetType.AllEnemies),
        (() => new Uppercut(), TargetType.AnyEnemy),
        (() => new Inflame(), TargetType.Self),
        (() => new ShrugItOff(), TargetType.Self),
        (() => new PommelStrike(), TargetType.AnyEnemy)
    ];

    public static void Main()
    {
        Console.WriteLine("Actual complete production HostTestBridge, HostCardPolicy and HostTurnCommands linked against explicit test stubs.");
        Console.WriteLine("Only actual NativeBoundary.Cards/CanPlay/InOriginalHand run. Read/Dispatch are never invoked on the null-bridge fixture.");
        Console.WriteLine("Native CanPlayTargeting is a configurable recorded stub. No engine resources, hooks, card effects, animation, queue, settlement or gameplay are verified here.");
        PolicyAndEnumeration();
        TargetValidation();
        BoundaryGuards();
        SubmissionRechecks();
        Console.WriteLine($"PASS {_checks} focused behavior checks");
    }

    private static void PolicyAndEnumeration()
    {
        var f = new Fixture();
        foreach (var (create, expected) in Admitted())
        {
            var card = create();
            f.Add(card);
            Check(HostCardPolicy.TargetFor(card) == expected, card.GetType().Name + " expected target capability");
            card.Upgraded = true;
            Check(HostCardPolicy.TargetFor(card) == expected, card.GetType().Name + " upgrade preserves exact-type admission");
        }
        var choices = f.Boundary.Cards(f.Context);
        Check(choices.Length == 14, "nine cards with two enemies offer ten enemy choices and four null choices");
        foreach (var card in f.Host.PlayerCombatState!.Hand.Cards)
        {
            var offered = choices.Where(c => ReferenceEquals(c.Card, card)).ToArray();
            var expected = Admitted().Single(row => row.New().GetType() == card.GetType()).Expected;
            Check(offered.Length == (expected == TargetType.AnyEnemy ? 2 : 1), card.GetType().Name + " exact choice multiplicity");
            Check(offered.All(c => f.Boundary.CanPlay(f.Context, c)), card.GetType().Name + " offered commands are playable");
            Check(offered.All(c => ReferenceEquals(c.OriginalHand, f.Host.PlayerCombatState.Hand) && ReferenceEquals(c.Holder, f.Hand.GetCardHolder(card))), card.GetType().Name + " offer binds original hand and holder");
        }
        var thunder = choices.Single(c => c.Card is Thunderclap);
        Check(thunder.Target is null && thunder.TargetCombatId is null && thunder.Label.EndsWith(" on all enemies"), "Thunderclap offers exactly one labelled null target and null ID");
        var self = choices.Single(c => c.Card is Inflame);
        Check(self.Target is null && self.TargetCombatId is null && self.Label.EndsWith(" on self"), "Inflame offers labelled null self target");

        foreach (var (name, change) in new (string, Action<CardModel>)[]
        {
            ("enchantment", c => c.Enchantment = new()),
            ("affliction", c => c.Affliction = new()),
            ("energy X", c => c.EnergyCost.CostsX = true),
            ("star X", c => c.HasStarCostX = true),
            ("AnyAlly", c => c.TargetType = TargetType.AnyAlly),
            ("unknown category", c => c.TargetType = (TargetType)999)
        })
        {
            foreach (var (create, _) in Admitted())
            {
                var denied = new Fixture();
                var card = create();
                var command = denied.Add(card);
                change(card);
                Check(HostCardPolicy.TargetFor(card) is null && denied.Boundary.Cards(denied.Context).Length == 0 && !denied.Boundary.CanPlay(denied.Context, command), card.GetType().Name + " denies " + name + " in policy, offer and validation");
            }
        }
        foreach (var (create, expected) in Admitted())
        {
            var denied = new Fixture();
            var card = create();
            var command = denied.Add(card);
            card.TargetType = expected == TargetType.AnyEnemy ? TargetType.Self : TargetType.AnyEnemy;
            Check(HostCardPolicy.TargetFor(card) is null && denied.Boundary.Cards(denied.Context).Length == 0 && !denied.Boundary.CanPlay(denied.Context, command), card.GetType().Name + " rejects changed native target category");
        }
        foreach (var card in new CardModel[] { new Unlisted(TargetType.AnyEnemy), new Unlisted(TargetType.Self), new Unlisted(TargetType.AllEnemies), new Armaments(), new DerivedTwinStrike(), new Unlisted(TargetType.AnyEnemy) { Id = "TwinStrike" } })
        {
            var denied = new Fixture();
            var command = denied.Add(card);
            Check(HostCardPolicy.TargetFor(card) is null && denied.Boundary.Cards(denied.Context).Length == 0 && !denied.Boundary.CanPlay(denied.Context, command), card.GetType().Name + " unlisted or derived exact type denied, ID=" + card.Id);
        }
        var empty = new Fixture();
        var aoe = new Thunderclap();
        empty.Add(aoe);
        empty.Combat.HittableEnemies.Clear();
        Check(empty.Boundary.Cards(empty.Context).Length == 1 && aoe.NativeTargets.Single() is null, "AllEnemies with no enumerated enemies offers one choice when native targeting allows");
        aoe.NativeAllows = false;
        Check(empty.Boundary.Cards(empty.Context).Length == 0, "native targeting decides AllEnemies legality with no enumerated enemies");
        var capped = new Fixture();
        for (var i = 0; i < 130; i++) capped.Add(new TwinStrike());
        var bounded = capped.Boundary.Cards(capped.Context);
        Check(bounded.Length == 255 && bounded.Count(c => ReferenceEquals(c.Card, capped.Host.PlayerCombatState!.Hand.Cards[127])) == 1, "255 cap applies inside enemy enumeration without a 256th choice");
    }

    private static void TargetValidation()
    {
        foreach (var create in new Func<CardModel>[] { () => new Inflame(), () => new Thunderclap() })
        {
            var f = new Fixture();
            var card = create();
            var command = f.Add(card);
            Check(f.Boundary.CanPlay(f.Context, command) && card.NativeTargets.Single() is null, card.GetType().Name + " valid null target reaches native targeting with null");
            foreach (var invalid in new[]
            {
                command with { Target = f.Enemy },
                command with { TargetCombatId = 1 },
                command with { Target = f.Enemy, TargetCombatId = 1 }
            }) Check(!f.Boundary.CanPlay(f.Context, invalid), card.GetType().Name + " requires both target and ID null");
            Check(card.NativeTargets.Count == 1, card.GetType().Name + " invalid target pairs stop before native targeting");
        }
        var enemyFixture = new Fixture();
        var targeted = new Uppercut();
        var valid = enemyFixture.Add(targeted);
        Check(enemyFixture.Boundary.CanPlay(enemyFixture.Context, valid) && ReferenceEquals(targeted.NativeTargets.Single(), enemyFixture.Enemy), "AnyEnemy native targeting receives exact living hittable reference");
        foreach (var (name, change) in new (string, Func<Fixture, HostCommand.PlayCard, HostCommand.PlayCard>)[]
        {
            ("null target", (f,c) => c with { Target = null }),
            ("null ID", (f,c) => c with { TargetCombatId = null }),
            ("wrong ID", (f,c) => c with { TargetCombatId = 99 }),
            ("dead enemy", (f,c) => { f.Enemy.IsAlive = false; return c; }),
            ("missing enemy ID", (f,c) => { f.Enemy.CombatId = null; return c; }),
            ("foreign combat", (f,c) => { f.Enemy.CombatState = new CombatState(); return c; }),
            ("foreign same-ID object", (f,c) => c with { Target = new Creature { CombatId = f.Enemy.CombatId, CombatState = f.Combat } }),
            ("non-hittable enemy", (f,c) => { f.Combat.HittableEnemies.Remove(f.Enemy); return c; })
        })
        {
            var f = new Fixture();
            var card = new TwinStrike();
            var invalid = change(f, f.Add(card));
            Check(!f.Boundary.CanPlay(f.Context, invalid) && card.NativeTargets.Count == 0, "AnyEnemy rejects " + name + " before native targeting");
        }
        foreach (var (create, _) in Admitted())
        {
            var f = new Fixture();
            var card = create();
            var command = f.Add(card);
            card.NativeAllows = false;
            Check(!f.Boundary.CanPlay(f.Context, command) && card.NativeTargets.Count == 1 && f.Boundary.Cards(f.Context).Length == 0, card.GetType().Name + " final configured native denial rejects validation and offer");
        }
    }

    private static void BoundaryGuards()
    {
        foreach (var (name, change) in new (string, Action<Fixture, CardModel, NCardHolder>)[]
        {
            ("ineligible context", (f,c,h) => f.Context = f.Context with { Eligible = false }),
            ("peeking", (f,c,h) => f.Hand.PeekButton.IsPeeking = true),
            ("wrong owner", (f,c,h) => c.Owner = new Player()),
            ("replaced hand", (f,c,h) => f.Host.PlayerCombatState!.Hand = new CardPile()),
            ("missing combat state", (f,c,h) => f.Host.PlayerCombatState = null),
            ("departed card", (f,c,h) => f.Host.PlayerCombatState!.Hand.Cards.Clear()),
            ("invalid holder", (f,c,h) => h.Valid = false),
            ("detached holder", (f,c,h) => h.InsideTree = false),
            ("invalid card node", (f,c,h) => h.CardNode.Valid = false),
            ("detached card node", (f,c,h) => h.CardNode.InsideTree = false),
            ("wrong holder card", (f,c,h) => h.CardModel = new Inflame()),
            ("changed holder lookup", (f,c,h) => f.Hand.Holders[c] = new NCardHolder { CardModel = c }),
            ("missing holder lookup", (f,c,h) => f.Hand.Holders.Remove(c))
        })
        {
            var f = new Fixture();
            var card = new Inflame();
            var command = f.Add(card);
            change(f, card, (NCardHolder)command.Holder);
            Check(!f.Boundary.CanPlay(f.Context, command) && card.NativeTargets.Count == 0, "actual boundary rejects " + name + " before native targeting");
            if (name == "changed holder lookup")
                Check(ReferenceEquals(f.Boundary.Cards(f.Context).Single().Holder, f.Hand.GetCardHolder(card)), "fresh enumeration binds replacement holder while old command is rejected");
            else if (f.Host.PlayerCombatState is not null)
                Check(f.Boundary.Cards(f.Context).Length == 0, "actual enumeration excludes " + name);
        }
        var original = new Fixture();
        var c = original.Add(new Inflame());
        Check(!original.Boundary.CanPlay(original.Context, c with { OriginalHand = new CardPile() }), "actual boundary rejects foreign original hand even with live card holder");
    }

    private static void SubmissionRechecks()
    {
        foreach (var (name, change) in new (string, Action<CardModel>)[]
        {
            ("enchantment", c => c.Enchantment = new()),
            ("affliction", c => c.Affliction = new()),
            ("energy X", c => c.EnergyCost.CostsX = true),
            ("star X", c => c.HasStarCostX = true),
            ("native playability", c => c.NativeAllows = false)
        })
        {
            var f = new Fixture();
            var card = new Thunderclap();
            f.Add(card);
            var wrapper = new SubmissionBoundary(f);
            using var commands = new HostTurnCommands(wrapper);
            var offer = commands.Observe(1);
            var choice = offer.Choices.Single(c => c.Kind == "PlayCard");
            change(card);
            Check(commands.Submit(new HostSubmission(1, offer.Observation, choice.Key)) == "stale" && wrapper.Dispatches == 0, "actual command submission rejects changed " + name + " with zero dispatch");
        }
        var baseline = new Fixture();
        baseline.Add(new Thunderclap());
        var positive = new SubmissionBoundary(baseline);
        using var accepted = new HostTurnCommands(positive);
        var validOffer = accepted.Observe(1);
        Check(accepted.Submit(new HostSubmission(1, validOffer.Observation, validOffer.Choices.Single(c => c.Kind == "PlayCard").Key)) == "submitted" && positive.Dispatches == 1, "unchanged valid actual-boundary offer dispatches exactly once through wrapper");
    }

    private sealed class Unlisted(TargetType target) : CardModel(target);
    private sealed class DerivedTwinStrike : TwinStrike;

    private sealed class Fixture
    {
        internal Player Host { get; } = new();
        internal NPlayerHand Hand { get; } = new();
        internal CombatState Combat { get; } = new();
        internal Creature Enemy { get; }
        internal HostTurnContext Context { get; set; }
        internal IHostTurnBoundary Boundary { get; }
        internal Fixture()
        {
            var nested = typeof(HostTestBridge).GetNestedType("NativeBoundary", BindingFlags.NonPublic)!;
            Boundary = (IHostTurnBoundary)Activator.CreateInstance(nested, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [null], null)!;
            Host.Creature.CombatState = Combat;
            Enemy = new Creature { CombatId = 1, CombatState = Combat };
            Combat.HittableEnemies.Add(Enemy);
            Combat.HittableEnemies.Add(new Creature { CombatId = 2, CombatState = Combat });
            Context = new HostTurnContext(new(), new(), Host, Combat, Hand, new(), 1, true, false, true, "same native fixture stamp");
        }
        internal HostCommand.PlayCard Add(CardModel card)
        {
            card.Owner = Host;
            Host.PlayerCombatState!.Hand.Cards.Add(card);
            var holder = new NCardHolder { CardModel = card };
            Hand.Holders.Add(card, holder);
            var target = card.TargetType == TargetType.AnyEnemy ? Enemy : null;
            return new HostCommand.PlayCard(card, target, holder, Host.PlayerCombatState.Hand, "fixture", target?.CombatId);
        }
    }
    private sealed class SubmissionBoundary(Fixture fixture) : IHostTurnBoundary
    {
        internal int Dispatches { get; private set; }
        public HostTurnContext Read() => fixture.Context;
        public HostCommand.PlayCard[] Cards(HostTurnContext context) => fixture.Boundary.Cards(context);
        public bool CanPlay(HostTurnContext context, HostCommand.PlayCard card) => fixture.Boundary.CanPlay(context, card);
        public bool InOriginalHand(HostCommand.PlayCard card) => fixture.Boundary.InOriginalHand(card);
        public IHostTurnAction Dispatch(HostTurnContext context, HostCommand command) { Dispatches++; return new StubAction(); }
    }
    private sealed class StubAction : IHostTurnAction
    {
        public bool Completed => false;
        public bool Failed => false;
        public void Dispose() { }
    }
}
