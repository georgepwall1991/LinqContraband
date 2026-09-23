// Renders the docs site images from their HTML sources:
//   social-card.html -> docs/assets/img/social-card.png (1280x640, link previews)
//   hero.html        -> docs/assets/img/linqcontraband-diagnostics.png (1600x900, home page hero)
// Usage: node tools/SocialCard/render.mjs [path-to-chromium]
import { chromium } from 'playwright';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(here, '..', '..');
const images = [
  { source: 'social-card.html', output: 'social-card.png', width: 1280, height: 640 },
  { source: 'hero.html', output: 'linqcontraband-diagnostics.png', width: 1600, height: 900 },
];

const browser = await chromium.launch(process.argv[2] ? { executablePath: process.argv[2] } : {});
for (const image of images) {
  const page = await browser.newPage({ viewport: { width: image.width, height: image.height } });
  await page.goto(pathToFileURL(path.join(here, image.source)).href);
  await page.screenshot({ path: path.join(repoRoot, 'docs', 'assets', 'img', image.output) });
  await page.close();
}
await browser.close();
