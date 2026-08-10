import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const toolDirectory = path.dirname(fileURLToPath(import.meta.url));
const projectDirectory = path.dirname(toolDirectory);
const repositoryRoot = path.dirname(projectDirectory);
const graph = JSON.parse(fs.readFileSync(path.join(repositoryRoot, 'system-function-graph.json'), 'utf8'));
await import(pathToFileURL(path.join(projectDirectory, 'routing-core.js')).href);

const routing = globalThis.GraphRouting;
const issues = [];
if (!routing?.calculateFocusedEdgeGeometries || !routing?.createWorkerSource) {
  issues.push('routing-core.js 未暴露完整布线接口');
}

function computeFocus(rootId, depth) {
  const nodeIds = new Set([rootId]);
  const edgeIds = new Set();
  const visited = new Map([[rootId, 0]]);
  const queue = [{ id: rootId, level: 0 }];
  while (queue.length) {
    const current = queue.shift();
    if (current.level >= depth) continue;
    graph.edges.forEach(edge => {
      let neighbor = null;
      if (edge.source === current.id) neighbor = edge.target;
      else if (edge.target === current.id) neighbor = edge.source;
      if (!neighbor) return;
      edgeIds.add(edge.id);
      nodeIds.add(neighbor);
      const level = current.level + 1;
      if (!visited.has(neighbor) || visited.get(neighbor) > level) {
        visited.set(neighbor, level);
        queue.push({ id: neighbor, level });
      }
    });
  }
  return { nodeIds, edgeIds };
}

function samplePath(pathData) {
  const tokens = String(pathData).match(/[MLCQ]|-?(?:\d+\.?\d*|\.\d+)(?:e[-+]?\d+)?/gi) || [];
  const points = [];
  let index = 0;
  let current = { x: 0, y: 0 };
  const number = () => Number(tokens[index++]);
  while (index < tokens.length) {
    const command = tokens[index++].toUpperCase();
    if (command === 'M') {
      current = { x: number(), y: number() };
      points.push(current);
    } else if (command === 'L') {
      const end = { x: number(), y: number() };
      const steps = Math.max(1, Math.ceil(Math.hypot(end.x - current.x, end.y - current.y) / 7));
      for (let step = 1; step <= steps; step += 1) {
        points.push({
          x: current.x + (end.x - current.x) * step / steps,
          y: current.y + (end.y - current.y) * step / steps
        });
      }
      current = end;
    } else if (command === 'C') {
      const start = current;
      const first = { x: number(), y: number() };
      const second = { x: number(), y: number() };
      const end = { x: number(), y: number() };
      for (let step = 1; step <= 36; step += 1) {
        const t = step / 36;
        const mt = 1 - t;
        points.push({
          x: mt ** 3 * start.x + 3 * mt * mt * t * first.x + 3 * mt * t * t * second.x + t ** 3 * end.x,
          y: mt ** 3 * start.y + 3 * mt * mt * t * first.y + 3 * mt * t * t * second.y + t ** 3 * end.y
        });
      }
      current = end;
    } else if (command === 'Q') {
      const start = current;
      const control = { x: number(), y: number() };
      const end = { x: number(), y: number() };
      for (let step = 1; step <= 24; step += 1) {
        const t = step / 24;
        const mt = 1 - t;
        points.push({
          x: mt * mt * start.x + 2 * mt * t * control.x + t * t * end.x,
          y: mt * mt * start.y + 2 * mt * t * control.y + t * t * end.y
        });
      }
      current = end;
    } else {
      throw new Error(`不支持的 SVG 路径命令：${command}`);
    }
  }
  return points;
}

const segments = points => points.slice(1).map((point, index) => ({ a: points[index], b: point }));

function segmentIntersection(first, second) {
  const rx = first.b.x - first.a.x;
  const ry = first.b.y - first.a.y;
  const sx = second.b.x - second.a.x;
  const sy = second.b.y - second.a.y;
  const denominator = rx * sy - ry * sx;
  if (Math.abs(denominator) < 0.0001) return null;
  const qx = second.a.x - first.a.x;
  const qy = second.a.y - first.a.y;
  const t = (qx * sy - qy * sx) / denominator;
  const u = (qx * ry - qy * rx) / denominator;
  if (t <= 0.001 || t >= 0.999 || u <= 0.001 || u >= 0.999) return null;
  return { x: first.a.x + t * rx, y: first.a.y + t * ry };
}

function collinearOverlap(first, second) {
  const ax = first.b.x - first.a.x;
  const ay = first.b.y - first.a.y;
  const bx = second.b.x - second.a.x;
  const by = second.b.y - second.a.y;
  if (Math.abs(ax * by - ay * bx) > 0.08) return 0;
  if (Math.abs((second.a.x - first.a.x) * ay - (second.a.y - first.a.y) * ax) > 0.8) return 0;
  const useX = Math.abs(ax) >= Math.abs(ay);
  const a1 = useX ? first.a.x : first.a.y;
  const a2 = useX ? first.b.x : first.b.y;
  const b1 = useX ? second.a.x : second.a.y;
  const b2 = useX ? second.b.x : second.b.y;
  return Math.max(0, Math.min(Math.max(a1, a2), Math.max(b1, b2)) - Math.max(Math.min(a1, a2), Math.min(b1, b2)));
}

function arrowTriangle(points) {
  const tip = points.at(-1);
  let base = points.at(-2) || tip;
  let remaining = 8;
  for (let index = points.length - 2; index >= 0; index -= 1) {
    const point = points[index];
    const length = Math.hypot(tip.x - point.x, tip.y - point.y);
    if (length >= remaining) {
      base = { x: tip.x + (point.x - tip.x) * remaining / length, y: tip.y + (point.y - tip.y) * remaining / length };
      break;
    }
  }
  const dx = tip.x - base.x;
  const dy = tip.y - base.y;
  const length = Math.hypot(dx, dy) || 1;
  const ux = dx / length;
  const uy = dy / length;
  const center = { x: tip.x - ux * 8, y: tip.y - uy * 8 };
  return [
    { x: tip.x + ux, y: tip.y + uy },
    { x: center.x - uy * 5, y: center.y + ux * 5 },
    { x: center.x + uy * 5, y: center.y - ux * 5 }
  ];
}

function pointInTriangle(point, triangle) {
  const sign = (a, b, c) => (a.x - c.x) * (b.y - c.y) - (b.x - c.x) * (a.y - c.y);
  const values = [sign(point, triangle[0], triangle[1]), sign(point, triangle[1], triangle[2]), sign(point, triangle[2], triangle[0])];
  return !(values.some(value => value < -0.01) && values.some(value => value > 0.01));
}

function segmentHitsTriangle(segment, triangle) {
  if (pointInTriangle(segment.a, triangle) || pointInTriangle(segment.b, triangle)) return true;
  return [0, 1, 2].some(index => segmentIntersection(segment, {
    a: triangle[index],
    b: triangle[(index + 1) % 3]
  }));
}

const focusNode = graph.nodes.find(node => node.id === 'make_plan')?.id || graph.nodes[0]?.id;
const focus = computeFocus(focusNode, 3);
const focusEdges = graph.edges.filter(edge => focus.edgeIds.has(edge.id));
const nodeMap = new Map(graph.nodes.map(node => [node.id, node]));
const startedAt = performance.now();
const geometry = routing.calculateFocusedEdgeGeometries(focusEdges, nodeMap, focus.nodeIds, graph.meta);
const elapsedMs = Math.round(performance.now() - startedAt);

if (geometry.size !== focusEdges.length) issues.push(`布线缺失：期望 ${focusEdges.length}，实际 ${geometry.size}`);
const sampledRoutes = [];
focusEdges.forEach(edge => {
  const route = geometry.get(edge.id);
  if (!route) return;
  let points;
  try {
    points = samplePath(route.d);
  } catch (error) {
    issues.push(`${edge.id} 路径无法解析：${error.message}`);
    return;
  }
  if (points.some(point => point.x < 0 || point.y < 0 || point.x > graph.meta.canvasWidth || point.y > graph.meta.canvasHeight)) {
    issues.push(`${edge.id} 超出画布`);
  }
  graph.nodes.forEach(node => {
    if (!focus.nodeIds.has(node.id) || node.id === edge.source || node.id === edge.target) return;
    if (points.slice(1, -1).some(point => point.x > node.x && point.x < node.x + node.w && point.y > node.y && point.y < node.y + node.h)) {
      issues.push(`${edge.id} 穿过点亮节点 ${node.id}`);
    }
  });
  sampledRoutes.push({ edge, route, points, segments: segments(points) });
});

for (let firstIndex = 0; firstIndex < sampledRoutes.length; firstIndex += 1) {
  for (let secondIndex = firstIndex + 1; secondIndex < sampledRoutes.length; secondIndex += 1) {
    const first = sampledRoutes[firstIndex];
    const second = sampledRoutes[secondIndex];
    const crossings = [];
    for (const segment of first.segments) {
      for (const other of second.segments) {
        if (collinearOverlap(segment, other) > 1) {
          issues.push(`${first.edge.id} 与 ${second.edge.id} 存在线段重叠`);
          break;
        }
        const point = segmentIntersection(segment, other);
        if (point && !crossings.some(existing => Math.hypot(existing.x - point.x, existing.y - point.y) < 3)) crossings.push(point);
      }
    }
    if (crossings.length > 1) issues.push(`${first.edge.id} 与 ${second.edge.id} 相交 ${crossings.length} 次`);
  }
}

sampledRoutes.forEach(route => {
  const triangle = arrowTriangle(route.points);
  sampledRoutes.forEach(other => {
    if (route === other) return;
    if (other.segments.some(segment => segmentHitsTriangle(segment, triangle))) {
      issues.push(`${other.edge.id} 与 ${route.edge.id} 的箭头相交`);
    }
  });
});

const curveCount = sampledRoutes.filter(route => route.route.d.includes(' C ')).length;
const polylineCount = sampledRoutes.length - curveCount;
const expectsCurves = focusEdges.some(edge => !edge.lineType || String(edge.lineType).toLowerCase() === 'curve');
const expectsLinearRoutes = focusEdges.some(edge => ['straight', 'polyline'].includes(String(edge.lineType || '').toLowerCase()));
if (expectsCurves && !curveCount) issues.push('深度三路线缺少默认曲线路径');
if (expectsLinearRoutes && !polylineCount) issues.push('深度三路线缺少已声明的直线或折线路径');
if (elapsedMs > 15000) issues.push(`布线耗时过长：${elapsedMs}ms`);

const summary = {
  focusNode,
  depth: 3,
  expectedEdges: focusEdges.length,
  routedEdges: geometry.size,
  curveCount,
  polylineCount,
  crossingGaps: geometry.crossingGaps?.length || 0,
  elapsedMs,
  issues: [...new Set(issues)]
};

console.log(JSON.stringify(summary, null, 2));
if (summary.issues.length) process.exitCode = 1;
