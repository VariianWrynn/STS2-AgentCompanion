import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';

// Call from node_repl with an actual WindowState returned by @oai/sky.
export async function saveScreenshot(runDir, state, outputDir = runDir) {
  const session = JSON.parse(await fs.readFile(path.join(runDir, 'session.json'), 'utf8'));
  const doctor = JSON.parse(await fs.readFile(path.join(runDir, 'doctor.json'), 'utf8'));
  if (!doctor.sameOwnedProcess || !doctor.responding || state.window.id !== doctor.windowHandle) {
    throw new Error('Captured window does not match the doctor-verified owned process.');
  }
  const shot = state.screenshots?.[0];
  if (!shot?.url?.startsWith('data:image/jpeg;base64,')) throw new Error('Expected a JPEG screenshot from Computer Use.');
  const bytes = Buffer.from(shot.url.split(',')[1], 'base64');
  if (bytes.subarray(0,3).toString('hex') !== 'ffd8ff') throw new Error('Invalid JPEG.');
  await fs.mkdir(outputDir, {recursive:true});
  await fs.writeFile(path.join(outputDir, 'main-menu.jpg'), bytes);
  const metadata = {
    pid: session.process.pid, windowId: state.window.id, doctorWindowHandle: doctor.windowHandle,
    windowTitle: state.window.title, screenshotId: shot.id,
    screenshotSha256: crypto.createHash('sha256').update(bytes).digest('hex'),
    capturedUtc: new Date().toISOString(), mainMenuVisible: false,
    note: 'Awaiting agent visual inspection; screenshot alone does not prove readiness.'
  };
  await fs.writeFile(path.join(outputDir, 'visual-review.json'), JSON.stringify(metadata,null,2));
  return { path:path.join(outputDir,'main-menu.jpg'), ...metadata };
}

export async function recordVisualReview(runDir, mainMenuVisible, note) {
  if (typeof mainMenuVisible !== 'boolean' || typeof note !== 'string' || !note.trim()) throw new Error('Supply the actual observed result and a note.');
  const target = path.join(runDir, 'visual-review.json');
  const metadata = JSON.parse(await fs.readFile(target,'utf8'));
  const bytes = await fs.readFile(path.join(runDir,'main-menu.jpg'));
  if (crypto.createHash('sha256').update(bytes).digest('hex') !== metadata.screenshotSha256) throw new Error('Screenshot changed since capture.');
  metadata.mainMenuVisible=mainMenuVisible;
  metadata.note=note;
  metadata.reviewedUtc=new Date().toISOString();
  await fs.writeFile(target,JSON.stringify(metadata,null,2));
  return metadata;
}
