import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const toolDirectory = path.dirname(fileURLToPath(import.meta.url));
const projectDirectory = path.dirname(toolDirectory);
const repositoryRoot = path.dirname(projectDirectory);
const repositoryFiles = new Set([
  'CHANGELOG.md',
  'system-function-graph.json'
]);
const resolveFile = file => path.join(repositoryFiles.has(file) ? repositoryRoot : projectDirectory, file);
const read = file => fs.readFileSync(resolveFile(file), 'utf8');

const html = read('index.html');
const app = read('app.js');
const css = read('app.css');
const routingCore = read('routing-core.js');
const projectStore = read('project-store.js');
const nodeTypeCore = read('node-type-core.js');
const importCore = read('import-core.js');
const graph = JSON.parse(read('system-function-graph.json'));
const ungroupedFixture = JSON.parse(read('tests/fixtures/import-ungrouped.json'));
const groupRelationFixture = JSON.parse(read('tests/fixtures/import-group-relations.json'));
const issues = [];

if (ungroupedFixture.groups.length !== 0) issues.push('未分组导入样本不应包含分组');
if (!ungroupedFixture.nodes.length) issues.push('未分组导入样本缺少节点');
if (ungroupedFixture.nodes.some(node => node.group && node.group.trim())) {
  issues.push('未分组导入样本包含已分组节点');
}
const groupRelationNodeIds = new Set(groupRelationFixture.nodes.map(node => node.id));
const groupRelationGroupIds = new Set(groupRelationFixture.groups.map(group => group.id));
for (const edge of groupRelationFixture.edges) {
  const sourceIds = edge.sourceType === 'group' ? groupRelationGroupIds : groupRelationNodeIds;
  const targetIds = edge.targetType === 'group' ? groupRelationGroupIds : groupRelationNodeIds;
  if (!sourceIds.has(edge.source) || !targetIds.has(edge.target)) {
    issues.push(`分组关系导入样本端点无效：${edge.id}`);
  }
}
if (!groupRelationFixture.edges.some(edge => edge.sourceType === 'group' && edge.targetType === 'group')) {
  issues.push('分组关系导入样本缺少分组到分组关系');
}

const mappedIds = [...app.matchAll(/document\.getElementById\('([^']+)'\)/g)]
  .map(match => match[1]);
const htmlIdList = [...html.matchAll(/\bid="([^"]+)"/g)].map(match => match[1]);
const htmlIds = new Set(htmlIdList);
mappedIds.filter(id => !htmlIds.has(id)).forEach(id => issues.push(`入口页缺少元素 #${id}`));

const mappedNames = new Set(
  [...app.matchAll(/^\s{4}([A-Za-z0-9_]+): document\./gm)].map(match => match[1])
);
const usedNames = new Set([...app.matchAll(/\bdom\.([A-Za-z0-9_]+)/g)].map(match => match[1]));
[...usedNames]
  .filter(name => !mappedNames.has(name))
  .forEach(name => issues.push(`脚本使用了未映射的 dom.${name}`));

const duplicateHtmlIds = htmlIdList.filter((id, index) => htmlIdList.indexOf(id) !== index);
duplicateHtmlIds.forEach(id => issues.push(`入口页存在重复 ID：${id}`));

const nodeIds = new Set();
const groupIds = new Set();
const edgeIds = new Set();
const edgePairs = new Set();
graph.groups.forEach(group => {
  if (groupIds.has(group.id)) issues.push(`重复分组 ID：${group.id}`);
  groupIds.add(group.id);
  if (group.x < 0 || group.y < 0 || group.x + group.w > graph.meta.canvasWidth ||
    group.y + group.h > graph.meta.canvasHeight) issues.push(`分组超出画布：${group.id}`);
});
graph.nodes.forEach(node => {
  if (nodeIds.has(node.id)) issues.push(`重复节点 ID：${node.id}`);
  nodeIds.add(node.id);
  if (node.group && !groupIds.has(node.group)) issues.push(`节点引用不存在的分组：${node.id}`);
  if (node.x < 0 || node.y < 0 || node.x + node.w > graph.meta.canvasWidth ||
    node.y + node.h > graph.meta.canvasHeight) issues.push(`节点超出画布：${node.id}`);
});
graph.edges.forEach(edge => {
  if (edgeIds.has(edge.id)) issues.push(`重复关系 ID：${edge.id}`);
  edgeIds.add(edge.id);
  const sourceType = edge.sourceType === 'group' ? 'group' : 'node';
  const targetType = edge.targetType === 'group' ? 'group' : 'node';
  const sourceIds = sourceType === 'group' ? groupIds : nodeIds;
  const targetIds = targetType === 'group' ? groupIds : nodeIds;
  if (!sourceIds.has(edge.source) || !targetIds.has(edge.target)) {
    issues.push(`关系引用不存在的节点或分组：${edge.id}`);
  }
  if (sourceType === targetType && edge.source === edge.target) issues.push(`关系自连：${edge.id}`);
  const pair = `${sourceType}:${edge.source}\u0000${targetType}:${edge.target}`;
  if (edgePairs.has(pair)) issues.push(`重复同方向关系：${edge.source} -> ${edge.target}`);
  edgePairs.add(pair);
});

for (let first = 0; first < graph.nodes.length; first += 1) {
  for (let second = first + 1; second < graph.nodes.length; second += 1) {
    const a = graph.nodes[first];
    const b = graph.nodes[second];
    if (a.x < b.x + b.w && a.x + a.w > b.x && a.y < b.y + b.h && a.y + a.h > b.y) {
      issues.push(`初始节点重叠：${a.id} / ${b.id}`);
    }
  }
}

const defaultContext = { window: {} };
vm.createContext(defaultContext);
vm.runInContext(read('system-function-default.js'), defaultContext);
if (JSON.stringify(defaultContext.window.DEFAULT_GRAPH) !== JSON.stringify(graph)) {
  issues.push('system-function-default.js 与 JSON 源数据不同步');
}

const exportContext = { window: {} };
vm.createContext(exportContext);
vm.runInContext(routingCore, exportContext);
exportContext.window.GraphRouting = exportContext.GraphRouting;
vm.runInContext(read('readonly-export.js'), exportContext);
const standaloneGraph = structuredClone(graph);
standaloneGraph.settings = {
  theme: 'dark',
  colors: { primary: '#3366ff' },
  relationTypes: [
    { id: 'core', label: '核心操作', color: '#e8963e' },
    { id: 'review', label: '评审关系', color: '#3366ff' }
  ]
};
const standaloneHtml = exportContext.window.buildReadonlyGraphHtml(standaloneGraph);
const groupRelationHtml = exportContext.window.buildReadonlyGraphHtml(groupRelationFixture);
const fixtureOutputIndex = process.argv.indexOf('--write-readonly-fixture');
if (fixtureOutputIndex >= 0 && process.argv[fixtureOutputIndex + 1]) {
  fs.writeFileSync(
    path.resolve(process.argv[fixtureOutputIndex + 1]),
    exportContext.window.buildReadonlyGraphHtml(graph),
    'utf8'
  );
}
const executableScripts = [...standaloneHtml.matchAll(/<script(?:[^>]*)>([\s\S]*?)<\/script>/g)]
  .map(match => match[1])
  .filter(script => !script.trim().startsWith('{'));
executableScripts.forEach(script => new vm.Script(script));
const groupRelationScripts = [...groupRelationHtml.matchAll(/<script(?:[^>]*)>([\s\S]*?)<\/script>/g)]
  .map(match => match[1])
  .filter(script => !script.trim().startsWith('{'));
groupRelationScripts.forEach(script => new vm.Script(script));
if (!standaloneHtml.includes('id="edgeGaps"')) issues.push('只读版缺少交叉断桥图层');
if (!standaloneHtml.includes('评审关系')) issues.push('只读版未写入自定义关系类型');
if (!standaloneHtml.includes('installRoutingWorkerHandler')) issues.push('只读版未嵌入后台布线 Worker');
if (!standaloneHtml.includes('name="relationship-graph-format" content="readonly-v1"')) issues.push('只读版缺少可识别的导入格式标记');

const importContext = {};
vm.createContext(importContext);
vm.runInContext(importCore, importContext);
const parsedReadonly = importContext.GraphImport?.parseGraphFileText(standaloneHtml, '评审关系图.html', 'text/html');
if (parsedReadonly?.format !== 'readonly-html' ||
    JSON.stringify(parsedReadonly.graph) !== JSON.stringify(standaloneGraph)) {
  issues.push('只读版 HTML 无法无损提取并重新导入关系图');
}
const parsedJson = importContext.GraphImport?.parseGraphFileText(JSON.stringify(standaloneGraph), '评审关系图.json', 'application/json');
if (parsedJson?.format !== 'json' || JSON.stringify(parsedJson.graph) !== JSON.stringify(standaloneGraph)) {
  issues.push('JSON 导入解析回归');
}
const parsedGroupRelationReadonly = importContext.GraphImport?.parseGraphFileText(
  groupRelationHtml,
  '分组关系图.html',
  'text/html'
);
if (parsedGroupRelationReadonly?.format !== 'readonly-html' ||
    JSON.stringify(parsedGroupRelationReadonly.graph) !== JSON.stringify(groupRelationFixture)) {
  issues.push('包含分组端点的只读 HTML 无法无损重新导入');
}
try {
  importContext.GraphImport?.parseGraphFileText('<!doctype html><title>普通网页</title>', '普通网页.html', 'text/html');
  issues.push('普通 HTML 被错误识别为本工具只读版');
} catch {
  // Expected: arbitrary HTML must not be accepted as a graph.
}

const routingContext = {};
vm.createContext(routingContext);
vm.runInContext(routingCore, routingContext);
if (!routingContext.GraphRouting?.createWorkerSource) issues.push('布线核心缺少 Worker 源码接口');
else new vm.Script(routingContext.GraphRouting.createWorkerSource());
new vm.Script(projectStore);
const nodeTypeContext = {};
vm.createContext(nodeTypeContext);
vm.runInContext(nodeTypeCore, nodeTypeContext);
if (!nodeTypeContext.GraphNodeTypes?.ensureInSettings) issues.push('节点类型核心缺少项目标签复用接口');
if (html.indexOf('node-type-core.js') > html.indexOf('app.js')) issues.push('node-type-core.js 必须先于 app.js 载入');
if (html.indexOf('routing-core.js') > html.indexOf('app.js')) issues.push('routing-core.js 必须先于 app.js 载入');
if (html.indexOf('import-core.js') > html.indexOf('app.js')) issues.push('import-core.js 必须先于 app.js 载入');

const textFiles = [
  'index.html',
  'app.js',
  'app.css',
  'project-store.js',
  'node-type-core.js',
  'routing-core.js',
  'import-core.js',
  'route-worker.js',
  'README.md',
  'CHANGELOG.md',
  'desktop/RelationshipGraphLauncher.cs',
  'tools/test-desktop-experience.mjs',
  'readonly-export.js',
  'tests/fixtures/import-ungrouped.json',
  'tests/fixtures/import-group-relations.json',
  'system-function-default.js',
  'system-function-graph.json'
];
textFiles.filter(file => read(file).includes('\uFFFD'))
  .forEach(file => issues.push(`文件包含损坏字符：${file}`));
if (html.includes('id="addEdgeButton"')) issues.push('入口页仍包含新增关系按钮');
if (!css.includes('.graph-edge-crossing-gap')) issues.push('主程序缺少交叉断桥样式');
if (!html.includes('id="minimapCanvas"')) issues.push('入口页缺少小地图');
if (!html.includes('id="importPreviewDialog"')) issues.push('入口页缺少导入预览');
if (!html.includes('id="shortcutsDialog"')) issues.push('入口页缺少快捷键设置');
if (!html.includes('id="nodeTypeSuggestions"')) issues.push('入口页缺少项目节点类型建议列表');
if (/<select id="(?:node|batch)KindInput">/.test(html)) issues.push('节点类型仍被固定下拉框限制');

const summary = {
  groups: graph.groups.length,
  nodes: graph.nodes.length,
  edges: graph.edges.length,
  standaloneBytes: Buffer.byteLength(standaloneHtml),
  issues
};
console.log(JSON.stringify(summary, null, 2));
if (issues.length) process.exitCode = 1;
