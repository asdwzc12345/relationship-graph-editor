import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const toolDir = path.dirname(fileURLToPath(import.meta.url));
const projectDir = path.dirname(toolDir);
const graphPath = path.join(projectDir, 'system-function-graph.json');
const exporterPath = path.join(projectDir, 'readonly-export.js');
const safeTitle = String(graph.meta?.title || '关系图').replace(/[<>:"/\\|?*]/g, '_');
const outputPath = path.join(projectDir, `${safeTitle}.html`);

const graph = JSON.parse(fs.readFileSync(graphPath, 'utf8'));
const sandbox = { window: {} };
vm.createContext(sandbox);
vm.runInContext(fs.readFileSync(exporterPath, 'utf8'), sandbox);

let html = sandbox.window.buildReadonlyGraphHtml(graph);
html = html
  .replaceAll('关系图－只读版', `${graph.meta.title}－只读版`)
  .replaceAll('viewBox="0 0 1440 1030"', `viewBox="0 0 ${graph.meta.canvasWidth} ${graph.meta.canvasHeight}"`)
  .replaceAll('aria-label="关系图"', `aria-label="${graph.meta.title}"`);

fs.writeFileSync(outputPath, html, 'utf8');
console.log(outputPath);
