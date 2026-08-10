import fs from 'node:fs';
import path from 'node:path';

const sourcePath = path.resolve(process.argv[2] || 'system-function-graph.json');
const destinationPath = path.resolve(process.argv[3] || 'system-function-default.js');
const graph = JSON.parse(fs.readFileSync(sourcePath, 'utf8'));
const output = `window.DEFAULT_GRAPH = ${JSON.stringify(graph, null, 2)};\n`;

fs.writeFileSync(destinationPath, output, 'utf8');
console.log(`已生成 ${destinationPath}`);
console.log(`节点 ${graph.nodes.length}，关系 ${graph.edges.length}`);
