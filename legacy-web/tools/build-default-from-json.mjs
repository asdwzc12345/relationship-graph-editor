import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const toolDirectory = path.dirname(fileURLToPath(import.meta.url));
const legacyDirectory = path.dirname(toolDirectory);
const repositoryRoot = path.dirname(legacyDirectory);
const sourcePath = process.argv[2]
  ? path.resolve(process.argv[2])
  : path.join(repositoryRoot, 'system-function-graph.json');
const destinationPath = process.argv[3]
  ? path.resolve(process.argv[3])
  : path.join(legacyDirectory, 'system-function-default.js');
const graph = JSON.parse(fs.readFileSync(sourcePath, 'utf8'));
const output = `window.DEFAULT_GRAPH = ${JSON.stringify(graph, null, 2)};\n`;

fs.writeFileSync(destinationPath, output, 'utf8');
console.log(`已生成 ${destinationPath}`);
console.log(`节点 ${graph.nodes.length}，关系 ${graph.edges.length}`);
