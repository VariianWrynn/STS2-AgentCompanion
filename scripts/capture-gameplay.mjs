import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';

export async function saveFrame(runDir, state, outputDir, name, note) {
  if (!/^[a-z0-9-]+$/.test(name)) throw Error('Use a simple evidence name.');
  const session = JSON.parse(await fs.readFile(path.join(runDir, 'session.json'), 'utf8'));
  const doctor = JSON.parse(await fs.readFile(path.join(runDir, 'doctor.json'), 'utf8'));
  if (session.scope !== 'local-ai-gameplay' || !doctor.sameOwnedProcess || state.window.id !== doctor.windowHandle) {
    throw Error('Screenshot must belong to the owned gameplay instance.');
  }
  const shot = state.screenshots?.[0];
  if (!shot?.url?.startsWith('data:image/jpeg;base64,')) throw Error('Expected an actual JPEG screenshot.');
  const bytes = Buffer.from(shot.url.split(',')[1], 'base64');
  await fs.mkdir(outputDir, {recursive: true});
  const screenshot = path.join(outputDir, `${name}.jpg`);
  const metadata = path.join(outputDir, `${name}.json`);
  await fs.writeFile(screenshot, bytes);
  await fs.writeFile(metadata, JSON.stringify({
    scope: session.scope, pid: session.process.pid, windowId: state.window.id,
    screenshotId: shot.id, sha256: crypto.createHash('sha256').update(bytes).digest('hex'),
    capturedUtc: new Date().toISOString(), note
  }, null, 2));
  const q = value => "'" + value.replaceAll("'", "''") + "'";
  return `Copy-Item -LiteralPath ${q(screenshot)},${q(metadata)} -Destination ${q(runDir)}`;
}
