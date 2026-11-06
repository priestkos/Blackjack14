import { chromium } from "playwright";
import { mkdir } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const outDir = path.resolve(__dirname, "..");

const scenes = [
  { html: "01-table-your-move.html", png: "01-table-your-move.png" },
  { html: "02-split-round-over.html", png: "02-split-round-over.png" },
  { html: "03-settings.html", png: "03-settings.png" },
  { html: "04-help.html", png: "04-help.png" },
  { html: "05-random-chat-result.html", png: "05-random-chat-result.png" },
  { html: "06-dice-party-dealing.html", png: "06-dice-party-dealing.png" },
];

await mkdir(outDir, { recursive: true });

const browser = await chromium.launch();
const page = await browser.newPage();
await page.setViewportSize({ width: 1280, height: 720 });

for (const { html, png } of scenes) {
  const fileUrl = `file://${path.join(__dirname, html)}`;
  await page.goto(fileUrl, { waitUntil: "load" });
  await page.waitForTimeout(100);
  await page.screenshot({
    path: path.join(outDir, png),
    type: "png",
  });
  console.log("Wrote", png);
}

await browser.close();
