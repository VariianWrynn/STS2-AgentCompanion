from pathlib import Path
import hashlib
import json
import re

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'artifacts/verification/issued-turn-red'
OUT.mkdir(parents=True, exist_ok=True)
CONTROLLER = ROOT / 'src/LocalAiTeammate/ActionLayer/Controller/AiTeammateDummyController.cs'
ACTIONS = CONTROLLER.with_name('AiTeammateDummyController.CombatActions.cs')
MODELS = ROOT / 'src/LocalAiTeammate/ActionLayer/Common/AiTeammateActionModels.cs'
provenance = []


def source_slice(path, start, end, label):
    text = path.read_text(encoding='utf-8-sig')
    sliced = text[start:end]
    provenance.append(dict(label=label, path=str(path),
        source_sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
        start_line=text[:start].count('\n') + 1,
        end_line=text[:end].count('\n') + 1,
        extracted_sha256=hashlib.sha256(sliced.encode()).hexdigest()))
    return sliced


def block(marker):
    text = CONTROLLER.read_text(encoding='utf-8-sig')
    start = text.index(marker)
    opening = text.index('{', start)
    depth = 0
    for end in range(opening, len(text)):
        if text[end] == '{':
            depth += 1
        elif text[end] == '}':
            depth -= 1
            if depth == 0:
                return source_slice(CONTROLLER, start, end + 1, marker.strip())
    raise ValueError('Unterminated source block: ' + marker)


text = ACTIONS.read_text(encoding='utf-8-sig')
# Take the whole producer rather than reconstructing its lambda in the fixture.
kind = text.index('ActionType = AiTeammateActionKind.EndTurn.ToString()')
start = text.rfind('        IssuedPlayerTurn issued =', 0, kind)
if start < 0:
    raise ValueError('Combat EndTurn producer statement was not found.')
end = text.index('        return actions;', start)
producer = source_slice(ACTIONS, start, end, 'combat EndTurn producer')
text = CONTROLLER.read_text(encoding='utf-8-sig')
fields = []
for name in ['_nextDecisionAtUtc', '_lastFinishedEndTurn',
             '_lastCombatRoundWithInitialStagger', '_pendingIssuedActionSettlement',
             '_isExecutingAction', '_executionFailed', '_lastDeduplicationKey']:
    match = re.search(r'^    private [^\n]*\b' + name + r'\b[^\n]*;', text, re.M)
    if not match:
        raise ValueError('Review source extraction for changed field: ' + name)
    fields.append(source_slice(CONTROLLER, match.start(), match.end(), name))
methods = [block(marker) for marker in [
    '    private async Task ExecuteResolvedActionAsync(',
    '    private static string DescribeTrackedAction(',
    '    private bool ShouldSuppressRepeatedEndTurn(',
    '    private void ResetCompletedEndTurnTrackingIfNeeded(',
    '    private static bool IsCombatEndTurnAction(',
    '    private static bool IsEndTurnAction(',
    '    private static void ValidateEndTurnExecution(',
    '    private void BeginIssuedActionSettlement(',
    '    private bool TryWaitForIssuedActionSettlement()',
    '    private sealed class PendingIssuedActionSettlement']]
(OUT/'Extracted.cs').write_text('''using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Runs;
namespace Sts2LlmCoop;
internal sealed partial class AiTeammateDummyController {
public AiTeammateAvailableAction DiscoverEndTurn(Player player) {
List<AiTeammateAvailableAction> actions = [];
''' + producer + '''return actions.Single();
}
''' + '\n'.join(fields + methods) + '\n}\n', encoding='utf-8')
provenance.append(dict(label='complete linked action models', path=str(MODELS),
    source_sha256=hashlib.sha256(MODELS.read_bytes()).hexdigest()))
(OUT/'provenance.json').write_text(json.dumps(provenance, indent=2), encoding='utf-8')
project = f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework>
<ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
<ItemGroup><Compile Include="Extracted.cs" />
<Compile Include="{MODELS}" />
<Compile Include="{ROOT / 'tests/IssuedPlayerTurnRegression/Stubs.cs'}" />
<Compile Include="{ROOT / 'tests/IssuedPlayerTurnRegression/Program.cs'}" />
</ItemGroup></Project>'''
(OUT/'Regression.csproj').write_text(project, encoding='utf-8')
(OUT/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>', encoding='utf-8')
print('Generated production slices and SHA-256 provenance at ' + str(OUT))
