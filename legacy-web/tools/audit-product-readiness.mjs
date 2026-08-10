import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const toolDirectory = path.dirname(fileURLToPath(import.meta.url));
const projectDirectory = path.dirname(toolDirectory);
const read = file => fs.readFileSync(path.join(projectDirectory, file), 'utf8');
const html = read('index.html');
const app = read('app.js');
const css = read('app.css');
const store = read('project-store.js');
const routing = read('routing-core.js');
const readonly = read('readonly-export.js');
const issues = [];

const requireText = (source, text, message) => {
  if (!source.includes(text)) issues.push(message);
};

requireText(store, "this.db.transaction([PROJECT_STORE, SNAPSHOT_STORE], 'readwrite')", '恢复操作没有使用跨存储读写事务');
requireText(store, "graph: clone(current.graph)", '恢复操作没有备份恢复前的当前项目');
for (const functionName of ['renameCurrentProject', 'openRecoveryDialog', 'restoreProjectSnapshot']) {
  const start = app.indexOf(`function ${functionName}`);
  const end = app.indexOf('\n  }', start);
  const body = app.slice(start, end);
  if (!body.includes('await projectSaveChain')) issues.push(`${functionName} 没有等待项目保存队列`);
}

requireText(html, 'id="exportMenu"', '电脑端缺少聚合导出菜单');
requireText(css, '.export-menu-popover', '导出菜单缺少弹层样式');
requireText(html, 'id="editToolsMenu"', '编辑工具栏缺少排列与层级菜单');
requireText(css, 'flex: 1 0 100%', '编辑操作没有使用独立电脑端工具栏行');
const restoreIndex = html.indexOf('id="restoreButton"');
const projectToolbarStart = html.indexOf('class="project-toolbar"');
const projectToolbarEnd = html.indexOf('class="focus-toolbar"');
if (restoreIndex < projectToolbarStart || restoreIndex > projectToolbarEnd) issues.push('恢复初始操作没有归入项目工具栏');

requireText(app, 'function renderViewportOnly()', '缺少轻量视窗渲染');
requireText(app, 'scheduleViewportRender();', '画布连续操作没有动画帧合并');
requireText(app, 'MAX_FOCUSED_ROUTE_CACHE', '聚焦布线缺少多项缓存');

for (const constant of [
  'MIN_CANVAS_WIDTH',
  'MIN_CANVAS_HEIGHT',
  'MAX_CANVAS_SIZE',
  'MAX_IMPORT_FILE_BYTES',
  'MAX_GRAPH_GROUPS',
  'MAX_GRAPH_NODES',
  'MAX_GRAPH_EDGES',
  'MAX_ENTITY_ID_LENGTH'
]) requireText(app, `const ${constant}`, `缺少导入边界 ${constant}`);
requireText(app, 'file.size > MAX_IMPORT_FILE_BYTES', '导入前没有限制文件大小');
requireText(app, 'boundedText(node.note', '导入节点说明没有长度限制');
requireText(html, '.json,.html,.htm,application/json,text/html', '文件选择器没有同时支持 JSON 和只读 HTML');
requireText(app, 'IMPORT_API.parseGraphFileText(text, file.name, file.type)', '导入流程没有调用安全的多格式解析核心');
requireText(html, '<option value="">未分组</option>', '单节点编辑缺少未分组选项');
requireText(html, '<option value="">设为未分组</option>', '批量编辑缺少设为未分组选项');
requireText(app, 'groups: [],\n      nodes: []', '新项目仍然强制创建默认分组');
const findGroupSource = app.slice(app.indexOf('function findGroupForPoint'), app.indexOf('function saveGraph'));
if (!findGroupSource.includes("return '';")) issues.push('节点移出所有分组后没有保持未分组状态');
requireText(app, "const targetGroupId = groupMap.get(sourceNode.group) || '';", '复制未分组节点时仍会被强制放入分组');
requireText(app, ".forEach(node => {\n            node.group = '';", '删除分组后没有将组内节点设为未分组');
if (app.includes('关系图至少需要一个分组')) issues.push('关系图仍然强制至少一个分组');
if (app.includes('至少需要保留一个分组')) issues.push('界面仍然阻止删除最后一个分组');
if (app.includes("if (!raw.groups.length) errors.push")) issues.push('导入预检仍然拒绝零分组关系图');

requireText(html, 'id="createRelationDialog"', '缺少键盘可操作的新增关系窗口');
requireText(html, 'id="createRelationButton"', '编辑工具栏缺少新增关系入口');
requireText(app, "{ id: 'addRelation'", '新增关系没有快捷键定义');
requireText(app, "else if (action === 'addRelation')", '新增关系快捷键没有执行路径');
requireText(app, "const NODE_PORT_SIDES = ['top', 'right', 'bottom', 'left']", '节点没有提供四向连线入口');
requireText(app, "if (selectedNodeIds.has(node.id) || additive) startNodeDrag(event, node);", '已选节点拖动没有进入移动流程');
requireText(app, "else startLinkDrag(event, node, 'node');", '未选节点拖动没有进入拉线流程');
requireText(app, 'const LINK_DRAG_THRESHOLD_PX = 7;', '节点与分组拉线缺少防误触拖动阈值');
requireText(app, 'const didDrag = !cancelled && (completedLink.moved || finalDistance >= LINK_DRAG_THRESHOLD_PX);', '普通点击与拉线动作没有明确区分');
requireText(app, 'if (!didDrag) {', '普通点击仍可能创建意外关系');
requireText(app, 'collapseToSingleOnClick', '多选节点单击后没有收敛为单选聚焦');
requireText(app, "const cancelled = event.type === 'pointercancel';", '指针取消事件没有进入统一回滚流程');
requireText(app, 'graph = completedDrag.snapshot;', '节点拖动被系统取消后没有恢复原位置');
requireText(app, 'graph = completedResize.snapshot;', '分组缩放被系统取消后没有恢复原范围');
requireText(app, 'if (!cancelled && completedBox.moved)', '框选被系统取消后仍可能意外改变选择');
requireText(app, 'if (dragState) {\n      cancelScheduledNodeDrag();\n      graph = dragState.snapshot;', '切换模式时没有取消未完成的节点拖动');
requireText(app, 'for (let index = groups.length - 1; index >= 0; index -= 1)', '重叠分组区域没有优先识别视觉上层分组');
requireText(app, 'const participatingEndpointKeys = new Set();', '大图渲染仍会为每个节点重复扫描全部关系');
requireText(app, 'const adjacency = new Map();', '上下游聚焦仍会为每层节点重复扫描全部关系');
requireText(app, 'function refreshEntityIndexes()', '节点与分组查找仍会在大图中反复线性扫描');
requireText(app, 'const groupNodeCounts = new Map();', '搜索分组统计仍会为每个分组重复扫描全部节点');
requireText(readonly, 'participating=new Set()', '只读版大图渲染仍会重复扫描全部关系');
requireText(readonly, 'adjacent=new Map()', '只读版上下游聚焦仍会重复扫描全部关系');
requireText(app, 'const maximumPixels = 32000000;', '高清导出缺少总像素上限，超大画布可能耗尽内存');
requireText(app, "updateSaveState('正在保存…');", '项目数据库保存期间没有显示真实保存状态');
requireText(app, 'if (localMirrorError) {', '本地镜像写入失败时没有区分项目数据库是否可用');
const confirmImportIndex = app.indexOf('function confirmImport');
const restoreDefaultIndex = app.indexOf('function restoreDefault()', confirmImportIndex);
const importSource = app.slice(confirmImportIndex, restoreDefaultIndex);
if (!importSource.includes('invalidateFocusedGeometry();')) issues.push('导入新图后没有清除旧布线路径缓存');
const restoreSource = app.slice(restoreDefaultIndex, app.indexOf('function exportReadonly', restoreDefaultIndex));
if (!restoreSource.includes('invalidateFocusedGeometry();')) issues.push('恢复默认图后没有清除旧布线路径缓存');
requireText(app, "classNames.push('is-related')", '邻居节点没有明确高亮状态');
requireText(app, "groupClasses.push('is-related')", '邻居分组没有明确高亮状态');
requireText(css, '.graph-node.is-related:not(.is-selected)', '邻居节点缺少明确高亮样式');
requireText(css, '.graph-group.is-related:not(.is-selected)', '邻居分组缺少明确高亮样式');
requireText(readonly, '.node.related:not(.selected)', '只读版邻居节点缺少明确高亮样式');
requireText(readonly, '.group.related:not(.selected)', '只读版邻居分组缺少明确高亮样式');
requireText(app, "dom.svg.addEventListener('contextmenu', handleCanvasContextMenu)", '画布右键没有取消全部选择');
requireText(app, "document.addEventListener('contextmenu', preventNativeContextMenu, { capture: true })", '软件没有全局禁用网页右键菜单');
requireText(app, "document.addEventListener('auxclick', preventBrowserPointerGesture, { capture: true })", '软件没有拦截中键和鼠标侧键');
requireText(readonly, "document.addEventListener('contextmenu',function(ev){ev.preventDefault()})", '只读分享版没有禁用网页右键菜单');
requireText(readonly, "document.addEventListener('auxclick',function(ev){if(ev.button===2)ev.preventDefault()})", '只读分享版没有拦截右键辅助点击');
requireText(readonly, "if(ev.button!==0||ev.isPrimary===false", '只读分享版仍允许右键拖动画布');
requireText(app, "document.addEventListener('wheel', preventBrowserPageZoom, { capture: true, passive: false })", '软件没有拦截网页缩放手势');
requireText(app, "window.addEventListener('keydown', preventBrowserShortcut, { capture: true })", '软件没有拦截网页快捷键');
requireText(app, "event.stopImmediatePropagation();", '网页快捷键拦截没有阻止后续处理');
requireText(app, "sourceSide,\n        targetSide", '拖线创建关系没有保存四向端口');
requireText(app, "startLinkDrag(event, group, 'group', side, true);", '分组四向端口没有进入拉线流程');
requireText(app, "if (isSelected) startGroupDrag(event, group);", '已选分组拖动没有进入移动流程');
requireText(app, "else startLinkDrag(event, group, 'group');", '未选分组拖动没有进入拉线流程');
requireText(app, "wrapper.addEventListener('pointerdown', event => {", '分组名称区域不能执行选择、移动或拉线');
requireText(app, 'function applyGroupDragPointer(pointer)', '缺少分组整体移动流程');
requireText(app, 'member.x = position.x + deltaX;', '移动分组时没有同步移动组内节点');
requireText(app, 'startGroupResize(event, group, direction);', '已选分组不能调整大小');
requireText(app, "sourceType: completedLink.sourceType", '拖线创建关系没有保存分组端点类型');
requireText(app, "findEndpointAtPoint(finalPoint, completedLink.sourceType, completedLink.sourceId)", '拖线终点不能识别节点与分组');
requireText(app, "graph.edges = graph.edges.filter(edge => !edgeReferencesEndpoint(edge, 'group', group.id));", '删除分组后没有清理分组关系');
requireText(readonly, "function edgeEndpointKey(edge,role)", '只读分享版不能解析分组关系端点');
requireText(html, 'name="newEdgeLineType" value="straight"', '右侧缺少直线选项');
requireText(html, 'name="newEdgeLineType" value="polyline"', '右侧缺少折线选项');
requireText(html, 'name="newEdgeLineType" value="curve" checked', '右侧缺少默认曲线选项');
requireText(app, 'lineType: getSelectedNewEdgeLineType()', '表单新建关系没有使用右侧线型');
requireText(app, 'lineType: normalizeEdgeLineType(completedLink.lineType)', '拖线新建关系没有保存所选线型');
requireText(routing, "if (lineType === 'straight')", '正式连线渲染缺少直线实现');
requireText(routing, "if (lineType === 'polyline')", '正式连线渲染缺少折线实现');
requireText(app, "const focusedGeometry = selection.type === 'node'", '右侧线型错误影响了非节点聚焦布线边界');
requireText(readonly, 'geometry(a,b,edgeIndex.get(edge.id)||0,edge)', '只读分享版没有保留连线线型');
requireText(html, '关系名称（可选）', '关系名称没有标记为可选');
requireText(app, "label: boundedText(edge.label, '', 40)", '空关系名称仍会被替换为占位名称');
const deleteSelectionSource = app.slice(app.indexOf('function deleteSelection'), app.indexOf('function uniqueId', app.indexOf('function deleteSelection')));
if (deleteSelectionSource.includes('window.confirm')) issues.push('删除所选仍会弹出二次确认');
const deleteProjectSource = app.slice(app.indexOf('async function deleteCurrentProject'), app.indexOf('async function openRecoveryDialog'));
if (deleteProjectSource.includes('window.confirm')) issues.push('删除项目仍会弹出二次确认');
requireText(html, 'role="combobox"', '搜索框缺少组合框语义');
requireText(html, 'aria-autocomplete="list"', '搜索框缺少自动完成语义');
requireText(app, "aria-activedescendant", '搜索结果没有暴露当前活动项');
requireText(html, 'id="graphCanvas" viewBox="0 0 1380 760" role="group"', '交互画布使用了会吞并子控件的图片角色');

const categoryFunction = app.slice(
  app.indexOf('function populateCategoryFilterOptions'),
  app.indexOf('function populateRelationTypeOptions')
);
for (const redundant of ["['content',", "['staff',", "['commercial',"]) {
  if (categoryFunction.includes(redundant)) issues.push(`关系筛选仍包含重复预设 ${redundant.slice(2, -2)}`);
}

const summary = {
  atomicRecovery: !issues.some(issue => issue.includes('恢复')),
  desktopToolbar: !issues.some(issue => issue.includes('工具栏') || issue.includes('导出菜单')),
  guardedImport: !issues.some(issue => issue.includes('导入')),
  optionalGroups: !issues.some(issue => issue.includes('分组')),
  keyboardRelationCreation: !issues.some(issue => issue.includes('新增关系')),
  fourWayNodeInteraction: !issues.some(issue => issue.includes('节点拖动') || issue.includes('四向') || issue.includes('右键') || issue.includes('端口')),
  clickDragSeparation: !issues.some(issue => issue.includes('防误触') || issue.includes('普通点击') || issue.includes('收敛为单选')),
  neighborHighlighting: !issues.some(issue => issue.includes('邻居')),
  cancelledInteractionRollback: !issues.some(issue => issue.includes('系统取消') || issue.includes('未完成的节点拖动')),
  focusedRouteInvalidation: !issues.some(issue => issue.includes('旧布线路径')),
  largeGraphIndexing: !issues.some(issue => issue.includes('重复扫描全部关系') || issue.includes('反复线性扫描') || issue.includes('重复扫描全部节点')),
  boundedRasterExport: !issues.some(issue => issue.includes('总像素上限')),
  accurateSaveState: !issues.some(issue => issue.includes('真实保存状态') || issue.includes('本地镜像')),
  groupRelationEndpoints: !issues.some(issue => issue.includes('分组') && (issue.includes('端口') || issue.includes('关系') || issue.includes('终点'))),
  groupSelectionInteraction: !issues.some(issue => issue.includes('分组') && (issue.includes('移动') || issue.includes('拉线') || issue.includes('大小'))),
  newEdgeLineTypes: !issues.some(issue => issue.includes('线型') || issue.includes('直线') || issue.includes('折线') || issue.includes('曲线')),
  optionalRelationNames: !issues.some(issue => issue.includes('关系名称') || issue.includes('占位名称')),
  immediateDeletion: !issues.some(issue => issue.includes('二次确认')),
  accessibleSearch: !issues.some(issue => issue.includes('搜索')),
  browserActionsBlocked: !issues.some(issue => issue.includes('网页') || issue.includes('鼠标侧键') || issue.includes('中键')),
  issues
};

console.log(JSON.stringify(summary, null, 2));
if (issues.length) process.exitCode = 1;
