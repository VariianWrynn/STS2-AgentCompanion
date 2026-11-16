namespace Sts2LlmCoop.HostTest;

internal sealed record HostProcessIdentity(int Pid, DateTime StartTimeUtc, string? Executable,
    string AssemblyPath, string AssemblySha256, string ReleaseVersion);
internal sealed record HostPlayerObservation(ulong NetId, string? Phase, bool? Ready,
    int? Round, decimal Hp, decimal MaxHp, decimal Gold, decimal? Energy, int? HandCount);
internal sealed record HostObservation(string Capability, string Compatibility, HostProcessIdentity Process,
    string Scope, string? Room = null, int? Floor = null,
    HostPlayerObservation? Host = null, HostPlayerObservation? Ai = null);
