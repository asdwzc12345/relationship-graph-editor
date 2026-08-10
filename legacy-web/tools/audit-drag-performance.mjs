import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const toolDirectory = path.dirname(fileURLToPath(import.meta.url));
const projectDirectory = path.dirname(toolDirectory);
const source = fs.readFileSync(path.join(projectDirectory, 'app.js'), 'utf8');
const issues = [];

const moveBranchStart = source.indexOf('if (dragState && event.pointerId === dragState.pointerId) {', source.indexOf('function handleGlobalPointerMove'));
const moveBranchEnd = source.indexOf('if (panState && event.pointerId === panState.pointerId)', moveBranchStart);
const moveBranch = source.slice(moveBranchStart, moveBranchEnd);

if (!moveBranch.includes('scheduleNodeDrag(event)')) issues.push('节点拖动没有合并到动画帧');
if (moveBranch.includes('renderGraph()')) issues.push('节点拖动仍在每次鼠标事件中完整重绘画布');
if (!source.includes('event.getCoalescedEvents?.()')) issues.push('节点拖动没有读取最新合并指针事件');
if (!source.includes('requestAnimationFrame(() =>')) issues.push('节点拖动没有使用 requestAnimationFrame');
if (!source.includes('captureNodeDragElements()')) issues.push('节点拖动没有缓存画布元素引用');
if (!source.includes("'transform',\n        `translate(")) issues.push('节点拖动没有使用轻量坐标变换');
if (!source.includes('dragState.affectedEdgeIds.forEach')) issues.push('节点拖动没有增量更新相关关系线');
if (!source.includes('cancelScheduledNodeDrag();\n      const cancelled = event.type === \'pointercancel\';\n      if (!cancelled) applyNodeDragPointer(event);')) {
  issues.push('松开鼠标时没有同步应用最终指针位置');
}

const panBranchStart = source.indexOf('if (panState && event.pointerId === panState.pointerId)', moveBranchEnd);
const panBranchEnd = source.indexOf('\n    }\n  }', panBranchStart);
const panBranch = source.slice(panBranchStart, panBranchEnd);
if (!panBranch.includes('scheduleViewportRender()')) issues.push('画布平移没有合并到动画帧');
if (panBranch.includes('renderGraph()')) issues.push('画布平移仍在完整重建 SVG');
const zoomStart = source.indexOf('function zoomViewport');
const zoomEnd = source.indexOf('function fitViewport', zoomStart);
const zoomSource = source.slice(zoomStart, zoomEnd);
if (!zoomSource.includes('scheduleViewportRender()')) issues.push('滚轮缩放没有合并到动画帧');
if (zoomSource.includes('renderGraph()')) issues.push('滚轮缩放仍在完整重建 SVG');
if (!source.includes('function renderViewportOnly()')) issues.push('缺少轻量视窗更新路径');
if (!source.includes('MAX_FOCUSED_ROUTE_CACHE')) issues.push('聚焦布线结果没有多项缓存');

console.log(JSON.stringify({
  frameCoalescing: issues.length === 0,
  fullRenderDuringPointerMove: moveBranch.includes('renderGraph()'),
  lightweightPanZoom: !panBranch.includes('renderGraph()') && !zoomSource.includes('renderGraph()'),
  focusedRouteCache: source.includes('MAX_FOCUSED_ROUTE_CACHE'),
  issues
}, null, 2));

if (issues.length) process.exitCode = 1;
