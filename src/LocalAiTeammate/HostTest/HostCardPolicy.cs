using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace Sts2LlmCoop.HostTest;

internal static class HostCardPolicy
{
    internal static TargetType? TargetFor(CardModel card)
    {
        if (card.Enchantment is not null || card.Affliction is not null ||
            card.EnergyCost.CostsX || card.HasStarCostX) return null;
        var type = card.GetType();
        TargetType? expected =
            type == typeof(StrikeIronclad) || type == typeof(Bash) || type == typeof(TwinStrike) ||
            type == typeof(Uppercut) || type == typeof(PommelStrike) ? TargetType.AnyEnemy :
            type == typeof(DefendIronclad) || type == typeof(Inflame) || type == typeof(ShrugItOff) ? TargetType.Self :
            type == typeof(Thunderclap) ? TargetType.AllEnemies : null;
        return card.TargetType == expected ? expected : null;
    }
}
