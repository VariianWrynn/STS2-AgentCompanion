namespace Sts2LlmCoop.HostTest;

internal readonly record struct HostIdentityFacts(
    bool ConnectedLoopbackHost, ulong Host, ulong? Local, ulong? ResolvedLocal, ulong Sender,
    int ParticipantCount, int HostCount, int AiCount, int ControllerCount,
    bool HostParticipantMatches, bool AiParticipantMatches, bool HostHasController,
    bool RunPlayersMatch);

internal static class HostIdentityPolicy
{
    internal static bool Allows(HostIdentityFacts facts) =>
        facts.ConnectedLoopbackHost && facts.Host != 0 &&
        facts.Local == facts.Host && facts.ResolvedLocal == facts.Host && facts.Sender == facts.Host &&
        facts.ParticipantCount == 2 && facts.HostCount == 1 && facts.AiCount == 1 &&
        facts.ControllerCount == 1 && facts.HostParticipantMatches && facts.AiParticipantMatches &&
        !facts.HostHasController && facts.RunPlayersMatch;
}
