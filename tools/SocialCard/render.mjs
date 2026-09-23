// Renders social-card.html to docs/assets/img/social-card.png at 1280x640.
// Usage: node tools/SocialCard/render.mjs [path-to-chromium]
import { chromium } from 'playwright';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(here, '..', '..');
const browser = await chromium.launch(process.argv[2] ? { executablePath: process.argv[2] } : {});
const page = await browser.newPage({ viewport: { width: 1280, height: 640 } });
await page.goto(pathToFileURL(path.join(here, 'social-card.html')).href);
await page.screenshot({ path: path.join(repoRoot, 'docs', 'assets', 'img', 'social-card.png') });
await browser.close();
