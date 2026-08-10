import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const toolDirectory = path.dirname(fileURLToPath(import.meta.url));
const legacyDirectory = path.dirname(toolDirectory);
const repositoryRoot = path.dirname(legacyDirectory);
const htmlPath = process.argv[2]
  ? path.resolve(process.argv[2])
  : path.join(legacyDirectory, '测试用图.html');
const graphPath = process.argv[3]
  ? path.resolve(process.argv[3])
  : path.join(repositoryRoot, 'system-function-graph.json');
const html = fs.readFileSync(htmlPath, 'utf8');
const graph = JSON.parse(fs.readFileSync(graphPath, 'utf8'));
const data = JSON.stringify(graph).replace(/</g, '\\u003c');
const pattern = /(<script id="graphData" type="application\/json">)[\s\S]*?(<\/script>)/;

if (!pattern.test(html)) throw new Error('未找到 graphData 数据块。');
fs.writeFileSync(htmlPath, html.replace(pattern, `$1${data}$2`), 'utf8');
console.log(`已同步 ${htmlPath}`);
console.log(`节点 ${graph.nodes.length}，关系 ${graph.edges.length}`);
