import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const toolDirectory = path.dirname(fileURLToPath(import.meta.url));
const projectDirectory = path.dirname(toolDirectory);
const source = fs.readFileSync(path.join(projectDirectory, 'node-type-core.js'), 'utf8');
const context = {};
vm.createContext(context);
vm.runInContext(source, context);

const api = context.GraphNodeTypes;
const issues = [];
if (!api) issues.push('节点类型核心未导出');

if (api) {
  if (api.normalizeLabel('  玩法   规则  ') !== '玩法 规则') issues.push('自由标签空白字符没有规范化');
  const catalog = api.normalizeCatalog([
    { label: '玩法规则', kind: 'content' },
    { label: '玩法规则', kind: 'staff' },
    { label: '数值验证', kind: 'invalid' }
  ]);
  if (catalog.length !== 2) issues.push('项目历史标签没有正确合并');
  if (catalog[0]?.kind !== 'content') issues.push('已有标签的配色没有保留');

  const settings = { nodeTypes: catalog };
  const first = api.ensureInSettings(settings, '制作流程');
  const reused = api.ensureInSettings(settings, '制作流程');
  if (first !== reused || settings.nodeTypes.length !== 3) issues.push('相同项目标签没有复用');
  if (api.styleKind('制作流程') !== api.styleKind('制作流程')) issues.push('自由标签配色不稳定');

  const legacySettings = { nodeTypes: [] };
  const legacy = api.ensureInSettings(legacySettings, '经营功能', 'system');
  if (legacy.kind !== 'system') issues.push('旧项目节点类型迁移没有保留配色');
  if (api.labelForNode({ type: '玩家决策', kind: 'system' }) !== '玩家决策') issues.push('自由类型没有优先显示');
  if (api.labelForNode({ kind: 'staff' }) !== '员工能力') issues.push('旧节点类型没有自动迁移');
}

console.log(JSON.stringify({
  freeform: true,
  reusableProjectLabels: true,
  issues
}, null, 2));

if (issues.length) process.exitCode = 1;
