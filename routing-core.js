(() => {
'use strict';

let routingCanvasMeta = { canvasWidth: 1440, canvasHeight: 1030 };

function calculateEdgeGeometry(source, target, index, edge = null) {
  const sourceCenterX = source.x + source.w / 2;
  const sourceCenterY = source.y + source.h / 2;
  const targetCenterX = target.x + target.w / 2;
  const targetCenterY = target.y + target.h / 2;
  const dx = targetCenterX - sourceCenterX;
  const dy = targetCenterY - sourceCenterY;
  const horizontal = Math.abs(dx) / Math.max(1, (source.w + target.w) / 2) >=
    Math.abs(dy) / Math.max(1, (source.h + target.h) / 2);
  const allowedSides = ['top', 'right', 'bottom', 'left'];
  const requestedSourceSide = String(edge?.sourceSide || '');
  const requestedTargetSide = String(edge?.targetSide || '');
  const sourceSide = allowedSides.includes(requestedSourceSide)
    ? requestedSourceSide
    : (horizontal ? (dx >= 0 ? 'right' : 'left') : (dy >= 0 ? 'bottom' : 'top'));
  const targetSide = allowedSides.includes(requestedTargetSide)
    ? requestedTargetSide
    : (horizontal ? (dx >= 0 ? 'left' : 'right') : (dy >= 0 ? 'top' : 'bottom'));
  const start = calculateNodePort(source, sourceSide, 0, 1);
  const end = calculateNodePort(target, targetSide, 0, 1);
  const sourceVector = sideVector(sourceSide);
  const targetVector = sideVector(targetSide);
  const distance = Math.hypot(end.x - start.x, end.y - start.y);
  const requestedLineType = String(edge?.lineType || '').toLowerCase();
  const lineType = ['straight', 'polyline', 'curve'].includes(requestedLineType)
    ? requestedLineType
    : 'curve';

  if (lineType === 'straight') {
    return {
      d: `M ${start.x} ${start.y} L ${end.x} ${end.y}`,
      lx: (start.x + end.x) / 2,
      ly: (start.y + end.y) / 2
    };
  }

  if (lineType === 'polyline') {
    const stub = Math.max(18, Math.min(34, distance * 0.18));
    const startOut = {
      x: start.x + sourceVector.x * stub,
      y: start.y + sourceVector.y * stub
    };
    const endOut = {
      x: end.x + targetVector.x * stub,
      y: end.y + targetVector.y * stub
    };
    const sourceHorizontal = sourceSide === 'left' || sourceSide === 'right';
    const targetHorizontal = targetSide === 'left' || targetSide === 'right';
    let points;
    if (sourceHorizontal && targetHorizontal) {
      const middleX = (startOut.x + endOut.x) / 2;
      points = [start, startOut, { x: middleX, y: startOut.y }, { x: middleX, y: endOut.y }, endOut, end];
    } else if (!sourceHorizontal && !targetHorizontal) {
      const middleY = (startOut.y + endOut.y) / 2;
      points = [start, startOut, { x: startOut.x, y: middleY }, { x: endOut.x, y: middleY }, endOut, end];
    } else if (sourceHorizontal) {
      points = [start, startOut, { x: endOut.x, y: startOut.y }, endOut, end];
    } else {
      points = [start, startOut, { x: startOut.x, y: endOut.y }, endOut, end];
    }
    const simplified = points.filter((point, pointIndex) => {
      const previous = points[pointIndex - 1];
      return !previous || Math.hypot(point.x - previous.x, point.y - previous.y) > 0.1;
    });
    const segments = simplified.slice(1).map((point, pointIndex) => ({
      start: simplified[pointIndex],
      end: point,
      length: Math.hypot(point.x - simplified[pointIndex].x, point.y - simplified[pointIndex].y)
    }));
    let remaining = segments.reduce((sum, segment) => sum + segment.length, 0) / 2;
    let labelPoint = { x: (start.x + end.x) / 2, y: (start.y + end.y) / 2 };
    for (const segment of segments) {
      if (remaining <= segment.length) {
        const ratio = segment.length ? remaining / segment.length : 0;
        labelPoint = {
          x: segment.start.x + (segment.end.x - segment.start.x) * ratio,
          y: segment.start.y + (segment.end.y - segment.start.y) * ratio
        };
        break;
      }
      remaining -= segment.length;
    }
    return {
      d: simplified.map((point, pointIndex) => `${pointIndex ? 'L' : 'M'} ${point.x} ${point.y}`).join(' '),
      lx: labelPoint.x,
      ly: labelPoint.y
    };
  }

  const bend = Math.max(42, Math.min(145, distance * 0.4));
  const jitter = ((index % 5) - 2) * 6;
  const normalLength = Math.max(1, distance);
  const normal = {
    x: -(end.y - start.y) / normalLength,
    y: (end.x - start.x) / normalLength
  };
  const c1 = {
    x: start.x + sourceVector.x * bend + normal.x * jitter,
    y: start.y + sourceVector.y * bend + normal.y * jitter
  };
  const c2 = {
    x: end.x + targetVector.x * bend + normal.x * jitter,
    y: end.y + targetVector.y * bend + normal.y * jitter
  };
  return {
    d: `M ${start.x} ${start.y} C ${c1.x} ${c1.y}, ${c2.x} ${c2.y}, ${end.x} ${end.y}`,
    lx: (start.x + end.x) / 2 + normal.x * jitter,
    ly: (start.y + end.y) / 2 + normal.y * jitter
  };
}

function calculateFocusedEdgeGeometriesInternal(edges, nodeMap, blockingNodeIds) {
  const descriptors = [];
  const portGroups = new Map();
  const blockingNodes = new Map();
  nodeMap.forEach((node, nodeId) => {
    if (blockingNodeIds.has(nodeId)) blockingNodes.set(nodeId, node);
  });

  edges.forEach((edge, order) => {
    const source = nodeMap.get(edge.source);
    const target = nodeMap.get(edge.target);
    if (!source || !target) return;
    const sourceCenter = {
      x: source.x + source.w / 2,
      y: source.y + source.h / 2
    };
    const targetCenter = {
      x: target.x + target.w / 2,
      y: target.y + target.h / 2
    };
    const dx = targetCenter.x - sourceCenter.x;
    const dy = targetCenter.y - sourceCenter.y;
    const horizontal = Math.abs(dx) / Math.max(1, (source.w + target.w) / 2) >=
      Math.abs(dy) / Math.max(1, (source.h + target.h) / 2);
    const allowedSides = ['top', 'right', 'bottom', 'left'];
    const requestedSourceSide = String(edge.sourceSide || '');
    const requestedTargetSide = String(edge.targetSide || '');
    const sourceSide = allowedSides.includes(requestedSourceSide)
      ? requestedSourceSide
      : (horizontal ? (dx >= 0 ? 'right' : 'left') : (dy >= 0 ? 'bottom' : 'top'));
    const targetSide = allowedSides.includes(requestedTargetSide)
      ? requestedTargetSide
      : (horizontal ? (dx >= 0 ? 'left' : 'right') : (dy >= 0 ? 'top' : 'bottom'));
    const descriptor = {
      edge,
      source,
      target,
      sourceCenter,
      targetCenter,
      horizontal,
      sourceSide,
      targetSide,
      order,
      sourcePort: null,
      targetPort: null,
      congestion: 0
    };
    descriptors.push(descriptor);
    addFocusedPort(portGroups, source, sourceSide, descriptor, 'source');
    addFocusedPort(portGroups, target, targetSide, descriptor, 'target');
  });

  portGroups.forEach(entries => {
    entries.sort((a, b) => {
      const coordinateDifference = a.oppositeCoordinate - b.oppositeCoordinate;
      if (Math.abs(coordinateDifference) > 0.5) return coordinateDifference;
      return a.descriptor.order - b.descriptor.order;
    });
    entries.forEach((entry, index) => {
      const point = calculateNodePort(entry.node, entry.side, index, entries.length);
      if (entry.role === 'source') entry.descriptor.sourcePort = point;
      else entry.descriptor.targetPort = point;
      entry.descriptor.congestion += entries.length;
    });
  });

  descriptors.sort((a, b) => {
    if (a.congestion !== b.congestion) return b.congestion - a.congestion;
    const aHorizontalSpan = Math.abs(a.targetCenter.x - a.sourceCenter.x);
    const bHorizontalSpan = Math.abs(b.targetCenter.x - b.sourceCenter.x);
    if (aHorizontalSpan !== bHorizontalSpan) return bHorizontalSpan - aHorizontalSpan;
    return a.order - b.order;
  });

  const chosenRoutes = [];
  const labelBoxes = [];
  const geometryMap = new Map();
  const relaxedEdgeIds = [];

  const canvasWidth = Number(routingCanvasMeta.canvasWidth) || 1440;
  const canvasHeight = Number(routingCanvasMeta.canvasHeight) || 1030;
  const reservedArrows = descriptors.map(descriptor => ({
    edgeId: descriptor.edge.id,
    triangle: portArrowTriangle(descriptor.targetPort, descriptor.targetSide)
  }));

  function samplePolyline(points) {
    const samples = [points[0]];
    for (let index = 1; index < points.length; index += 1) {
      const start = points[index - 1];
      const end = points[index];
      const steps = Math.max(1, Math.ceil(Math.hypot(end.x - start.x, end.y - start.y) / 8));
      for (let step = 1; step <= steps; step += 1) {
        samples.push({
          x: start.x + (end.x - start.x) * step / steps,
          y: start.y + (end.y - start.y) * step / steps
        });
      }
    }
    return samples;
  }

  function sampleSegments(samples) {
    const segments = [];
    for (let index = 1; index < samples.length; index += 1) {
      segments.push({ a: samples[index - 1], b: samples[index] });
    }
    return segments;
  }

  function simplifyPoints(points) {
    const result = [];
    points.forEach(point => {
      const previous = result[result.length - 1];
      if (!previous || Math.hypot(previous.x - point.x, previous.y - point.y) > 0.1) {
        result.push(point);
      }
    });
    return result;
  }

  function pointInsideNode(point, node, padding) {
    return point.x > node.x - padding && point.x < node.x + node.w + padding &&
      point.y > node.y - padding && point.y < node.y + node.h + padding;
  }

  function routeHitsNode(route, descriptor) {
    return route.samples.slice(1, -1).some(point => {
      let hit = false;
      blockingNodes.forEach(node => {
        const isEndpoint = node.id === descriptor.source.id || node.id === descriptor.target.id;
        if (pointInsideNode(point, node, isEndpoint ? -0.5 : 10)) hit = true;
      });
      return hit;
    });
  }

  function generalCollinearOverlap(first, second) {
    const ax = first.b.x - first.a.x;
    const ay = first.b.y - first.a.y;
    const bx = second.b.x - second.a.x;
    const by = second.b.y - second.a.y;
    if (Math.abs(ax * by - ay * bx) > 0.05) return 0;
    if (Math.abs((second.a.x - first.a.x) * ay - (second.a.y - first.a.y) * ax) > 0.5) return 0;
    const useX = Math.abs(ax) >= Math.abs(ay);
    const a1 = useX ? first.a.x : first.a.y;
    const a2 = useX ? first.b.x : first.b.y;
    const b1 = useX ? second.a.x : second.a.y;
    const b2 = useX ? second.b.x : second.b.y;
    return Math.max(0,
      Math.min(Math.max(a1, a2), Math.max(b1, b2)) -
      Math.max(Math.min(a1, a2), Math.min(b1, b2))
    );
  }

  function routeOverlapsExisting(route) {
    return chosenRoutes.some(existing => {
      return route.segments.some(segment =>
        existing.segments.some(other => generalCollinearOverlap(segment, other) > 1)
      );
    });
  }

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
    if (t < -0.0001 || t > 1.0001 || u < -0.0001 || u > 1.0001) return null;
    return { x: first.a.x + t * rx, y: first.a.y + t * ry };
  }

  function crossingCount(first, second) {
    const crossings = [];
    for (const segment of first.segments) {
      for (const other of second.segments) {
        const point = segmentIntersection(segment, other);
        if (point && !crossings.some(existing => Math.hypot(existing.x - point.x, existing.y - point.y) < 3)) {
          crossings.push(point);
          if (crossings.length > 1) return crossings.length;
        }
      }
    }
    return crossings.length;
  }

  function routeIntersectionPoints(first, second) {
    const crossings = [];
    for (const segment of first.segments) {
      for (const other of second.segments) {
        const point = segmentIntersection(segment, other);
        if (
          point &&
          !crossings.some(existing => Math.hypot(existing.x - point.x, existing.y - point.y) < 3)
        ) {
          crossings.push(point);
        }
      }
    }
    return crossings;
  }

  function routeCrossesExistingMoreThanOnce(route) {
    return chosenRoutes.some(existing => crossingCount(route, existing) > 1);
  }

  function routeCrossingTotal(route) {
    return chosenRoutes.reduce((total, existing) => total + crossingCount(route, existing), 0);
  }

  function pointInsideTriangle(point, triangle) {
    const sign = (first, second, third) =>
      (first.x - third.x) * (second.y - third.y) -
      (second.x - third.x) * (first.y - third.y);
    const first = sign(point, triangle.tip, triangle.left);
    const second = sign(point, triangle.left, triangle.right);
    const third = sign(point, triangle.right, triangle.tip);
    const hasNegative = first < -0.01 || second < -0.01 || third < -0.01;
    const hasPositive = first > 0.01 || second > 0.01 || third > 0.01;
    return !(hasNegative && hasPositive);
  }

  function routeArrowTriangle(route) {
    const tip = route.samples[route.samples.length - 1];
    let cursor = tip;
    let baseDirectionPoint = route.samples[Math.max(0, route.samples.length - 2)];
    let remaining = 8;
    for (let index = route.samples.length - 2; index >= 0; index -= 1) {
      const previous = route.samples[index];
      const length = Math.hypot(cursor.x - previous.x, cursor.y - previous.y);
      if (length >= remaining && length > 0.001) {
        baseDirectionPoint = {
          x: cursor.x + (previous.x - cursor.x) * remaining / length,
          y: cursor.y + (previous.y - cursor.y) * remaining / length
        };
        break;
      }
      remaining -= length;
      cursor = previous;
      baseDirectionPoint = previous;
    }
    const dx = tip.x - baseDirectionPoint.x;
    const dy = tip.y - baseDirectionPoint.y;
    const length = Math.hypot(dx, dy) || 1;
    const ux = dx / length;
    const uy = dy / length;
    const base = { x: tip.x - ux * 8, y: tip.y - uy * 8 };
    const front = { x: tip.x + ux, y: tip.y + uy };
    const left = { x: base.x - uy * 5, y: base.y + ux * 5 };
    const right = { x: base.x + uy * 5, y: base.y - ux * 5 };
    return {
      tip: front,
      left,
      right,
      minX: Math.min(front.x, left.x, right.x),
      maxX: Math.max(front.x, left.x, right.x),
      minY: Math.min(front.y, left.y, right.y),
      maxY: Math.max(front.y, left.y, right.y)
    };
  }

  function portArrowTriangle(port, side) {
    const outward = sideVector(side);
    const ux = -outward.x;
    const uy = -outward.y;
    const base = { x: port.x - ux * 8, y: port.y - uy * 8 };
    const tip = { x: port.x + ux, y: port.y + uy };
    const left = { x: base.x - uy * 5, y: base.y + ux * 5 };
    const right = { x: base.x + uy * 5, y: base.y - ux * 5 };
    return {
      tip,
      left,
      right,
      minX: Math.min(tip.x, left.x, right.x),
      maxX: Math.max(tip.x, left.x, right.x),
      minY: Math.min(tip.y, left.y, right.y),
      maxY: Math.max(tip.y, left.y, right.y)
    };
  }

  function segmentIntersectsTriangle(segment, triangle) {
    if (Math.max(segment.a.x, segment.b.x) < triangle.minX - 0.01 ||
      Math.min(segment.a.x, segment.b.x) > triangle.maxX + 0.01 ||
      Math.max(segment.a.y, segment.b.y) < triangle.minY - 0.01 ||
      Math.min(segment.a.y, segment.b.y) > triangle.maxY + 0.01) return false;
    if (pointInsideTriangle(segment.a, triangle) || pointInsideTriangle(segment.b, triangle)) {
      return true;
    }
    return [
      { a: triangle.tip, b: triangle.left },
      { a: triangle.left, b: triangle.right },
      { a: triangle.right, b: triangle.tip }
    ].some(side => Boolean(segmentIntersection(segment, side)));
  }

  function routeHasArrowConflict(route) {
    const arrow = routeArrowTriangle(route);
    return chosenRoutes.some(existing => {
      const existingArrow = routeArrowTriangle(existing);
      return existing.segments.some(segment =>
        segmentIntersectsTriangle(segment, arrow)
      ) || route.segments.some(segment =>
        segmentIntersectsTriangle(segment, existingArrow)
      );
    });
  }

  function routeHitsReservedArrow(route, descriptor) {
    const bounds = {
      minX: Number.POSITIVE_INFINITY,
      maxX: Number.NEGATIVE_INFINITY,
      minY: Number.POSITIVE_INFINITY,
      maxY: Number.NEGATIVE_INFINITY
    };
    route.samples.forEach(point => {
      bounds.minX = Math.min(bounds.minX, point.x);
      bounds.maxX = Math.max(bounds.maxX, point.x);
      bounds.minY = Math.min(bounds.minY, point.y);
      bounds.maxY = Math.max(bounds.maxY, point.y);
    });
    return reservedArrows.some(reserved => {
      const triangle = reserved.triangle;
      if (reserved.edgeId === descriptor.edge.id || bounds.maxX < triangle.minX ||
        bounds.minX > triangle.maxX || bounds.maxY < triangle.minY ||
        bounds.minY > triangle.maxY) return false;
      return route.segments.some(segment => segmentIntersectsTriangle(segment, triangle));
    });
  }

  function routeInsideCanvas(route) {
    return route.samples.every(point =>
      point.x >= 4 && point.x <= canvasWidth - 4 &&
      point.y >= 4 && point.y <= canvasHeight - 4
    );
  }

  function routeLength(route) {
    let total = 0;
    for (let index = 1; index < route.samples.length; index += 1) {
      total += Math.hypot(
        route.samples[index].x - route.samples[index - 1].x,
        route.samples[index].y - route.samples[index - 1].y
      );
    }
    return total;
  }

  function routePath(route) {
    if (route.type === 'curve') return buildFocusedCurvePath(route.curve);
    return route.points.reduce((path, point, index) =>
      `${path}${index ? ' L' : 'M'} ${point.x} ${point.y}`, '');
  }

  function segmentIntersectsNodeRect(start, end, node, padding) {
    const left = node.x - padding;
    const right = node.x + node.w + padding;
    const top = node.y - padding;
    const bottom = node.y + node.h + padding;
    const dx = end.x - start.x;
    const dy = end.y - start.y;
    let minimum = 0;
    let maximum = 1;
    const clips = [[-dx, start.x - left], [dx, right - start.x],
      [-dy, start.y - top], [dy, bottom - start.y]];
    for (const [direction, distance] of clips) {
      if (Math.abs(direction) < 0.0001) {
        if (distance < 0) return false;
        continue;
      }
      const ratio = distance / direction;
      if (direction < 0) minimum = Math.max(minimum, ratio);
      else maximum = Math.min(maximum, ratio);
      if (minimum > maximum) return false;
    }
    return true;
  }

  function segmentClear(descriptor, start, end) {
    if (start.x < 4 || start.x > canvasWidth - 4 || start.y < 4 || start.y > canvasHeight - 4 ||
      end.x < 4 || end.x > canvasWidth - 4 || end.y < 4 || end.y > canvasHeight - 4) return false;
    let blocked = false;
    blockingNodes.forEach(node => {
      const isEndpoint = node.id === descriptor.source.id || node.id === descriptor.target.id;
      if (!blocked && segmentIntersectsNodeRect(start, end, node, isEndpoint ? -0.5 : 10)) {
        blocked = true;
      }
    });
    if (!blocked) {
      const segment = { a: start, b: end };
      blocked = reservedArrows.some(reserved =>
        reserved.edgeId !== descriptor.edge.id &&
        segmentIntersectsTriangle(segment, reserved.triangle)
      );
    }
    return !blocked;
  }

  function segmentViolatesExisting(start, end) {
    const samples = samplePolyline([start, end]);
    const route = { type: 'polyline', samples, segments: sampleSegments(samples) };
    return routeOverlapsExisting(route) || routeCrossesExistingMoreThanOnce(route);
  }

  function buildCrossingAwareVisibilityRoute(vertices, descriptor, start, end) {
    const transitionCache = new Map();
    const stateLists = Array.from({ length: vertices.length }, () => []);
    const heap = [];

    function bitCount(mask) {
      let count = 0;
      for (let value = mask; value; value &= value - 1n) count += 1;
      return count;
    }

    function transitionInfo(from, to, cacheKey = '') {
      const key = cacheKey || `${from.x},${from.y}:${to.x},${to.y}`;
      if (transitionCache.has(key)) return transitionCache.get(key);
      if (!segmentClear(descriptor, from, to)) {
        const blocked = { valid: false, mask: 0n };
        transitionCache.set(key, blocked);
        return blocked;
      }
      const probe = {
        samples: [from, to],
        segments: [{ a: from, b: to }]
      };
      if (routeOverlapsExisting(probe)) {
        const blocked = { valid: false, mask: 0n };
        transitionCache.set(key, blocked);
        return blocked;
      }
      let mask = 0n;
      for (let index = 0; index < chosenRoutes.length; index += 1) {
        const count = crossingCount(probe, chosenRoutes[index]);
        if (count > 1) {
          const blocked = { valid: false, mask: 0n };
          transitionCache.set(key, blocked);
          return blocked;
        }
        if (count === 1) mask |= 1n << BigInt(index);
      }
      const info = { valid: true, mask };
      transitionCache.set(key, info);
      return info;
    }

    function heapPush(state) {
      heap.push(state);
      let index = heap.length - 1;
      while (index > 0) {
        const parent = Math.floor((index - 1) / 2);
        if (heap[parent].cost <= state.cost) break;
        heap[index] = heap[parent];
        index = parent;
      }
      heap[index] = state;
    }

    function heapPop() {
      const first = heap[0];
      const last = heap.pop();
      if (heap.length && last) {
        let index = 0;
        while (true) {
          let child = index * 2 + 1;
          if (child >= heap.length) break;
          if (child + 1 < heap.length && heap[child + 1].cost < heap[child].cost) child += 1;
          if (heap[child].cost >= last.cost) break;
          heap[index] = heap[child];
          index = child;
        }
        heap[index] = last;
      }
      return first;
    }

    function addState(state) {
      const list = stateLists[state.vertex];
      if (list.some(existing =>
        existing.active && existing.cost <= state.cost &&
        (existing.mask & state.mask) === existing.mask
      )) return false;
      list.forEach(existing => {
        if (existing.active && state.cost <= existing.cost &&
          (state.mask & existing.mask) === state.mask) existing.active = false;
      });
      const active = list.filter(existing => existing.active);
      if (active.length >= 16) {
        active.sort((first, second) => first.cost - second.cost);
        if (active[15].cost <= state.cost) return false;
        active.slice(15).forEach(existing => { existing.active = false; });
      }
      list.push(state);
      heapPush(state);
      return true;
    }

    const prefix = transitionInfo(start, vertices[0], 'prefix');
    const suffix = transitionInfo(vertices[1], end, 'suffix');
    if (!prefix.valid || !suffix.valid || (prefix.mask & suffix.mask) !== 0n) return null;
    addState({
      vertex: 0,
      mask: prefix.mask,
      cost: bitCount(prefix.mask) * 2500 + Math.hypot(vertices[0].x - start.x, vertices[0].y - start.y),
      previous: null,
      active: true
    });

    let explored = 0;
    while (heap.length && explored < 12000) {
      const state = heapPop();
      if (!state?.active) continue;
      explored += 1;
      if (state.vertex === 1) {
        const middle = [];
        for (let cursor = state; cursor; cursor = cursor.previous) {
          middle.push(vertices[cursor.vertex]);
        }
        middle.reverse();
        const points = simplifyPoints([start, ...middle, end]);
        const samples = samplePolyline(points);
        const route = { type: 'polyline', points, samples, segments: sampleSegments(points) };
        if (!routeHitsNode(route, descriptor) && !routeHitsReservedArrow(route, descriptor) &&
          !routeOverlapsExisting(route) && !routeCrossesExistingMoreThanOnce(route) &&
          !routeHasArrowConflict(route)) return route;
        continue;
      }
      for (let next = 0; next < vertices.length; next += 1) {
        if (next === state.vertex) continue;
        const low = Math.min(state.vertex, next);
        const high = Math.max(state.vertex, next);
        const transition = transitionInfo(vertices[state.vertex], vertices[next], `${low}:${high}`);
        if (!transition.valid || (state.mask & transition.mask) !== 0n) continue;
        let combinedMask = state.mask | transition.mask;
        let extraLength = Math.hypot(
          vertices[next].x - vertices[state.vertex].x,
          vertices[next].y - vertices[state.vertex].y
        );
        if (next === 1) {
          if ((combinedMask & suffix.mask) !== 0n) continue;
          combinedMask |= suffix.mask;
          extraLength += Math.hypot(end.x - vertices[1].x, end.y - vertices[1].y);
        }
        const addedCrossings = bitCount(combinedMask) - bitCount(state.mask);
        addState({
          vertex: next,
          mask: combinedMask,
          cost: state.cost + addedCrossings * 2500 + extraLength + (state.previous ? 10 : 0),
          previous: state,
          active: true
        });
      }
    }
    return null;
  }

  function buildVisibilityRoute(
    descriptor,
    start,
    end,
    startOut,
    endOut
  ) {
    const vertices = [startOut, endOut];
    blockingNodes.forEach(node => {
      const left = Math.max(6, node.x - 12);
      const right = Math.min(canvasWidth - 6, node.x + node.w + 12);
      const top = Math.max(6, node.y - 12);
      const bottom = Math.min(canvasHeight - 6, node.y + node.h + 12);
      vertices.push(
        { x: left, y: top }, { x: right, y: top },
        { x: right, y: bottom }, { x: left, y: bottom }
      );
    });
    const canvasVertices = [
      { x: 6, y: 6 }, { x: canvasWidth - 6, y: 6 },
      { x: canvasWidth - 6, y: canvasHeight - 6 }, { x: 6, y: canvasHeight - 6 }
    ];
    const crossingAwareVertices = [...vertices, ...canvasVertices];
    vertices.push(...canvasVertices);
    const distances = new Array(vertices.length).fill(Number.POSITIVE_INFINITY);
    const previous = new Array(vertices.length).fill(-1);
    const visited = new Array(vertices.length).fill(false);
    distances[0] = 0;
    for (let turn = 0; turn < vertices.length; turn += 1) {
      let current = -1;
      for (let index = 0; index < vertices.length; index += 1) {
        if (!visited[index] && (current < 0 || distances[index] < distances[current])) current = index;
      }
      if (current < 0 || !Number.isFinite(distances[current]) || current === 1) break;
      visited[current] = true;
      for (let next = 0; next < vertices.length; next += 1) {
        if (next === current || visited[next]) continue;
        const from = vertices[current];
        const to = vertices[next];
        if (
          !segmentClear(descriptor, from, to) ||
          segmentViolatesExisting(from, to)
        ) continue;
        const probeSamples = samplePolyline([from, to]);
        const probe = { samples: probeSamples, segments: sampleSegments(probeSamples) };
        const score = distances[current] + routeCrossingTotal(probe) * 2500 +
          Math.hypot(to.x - from.x, to.y - from.y) +
          (previous[current] < 0 ? 0 : 10);
        if (score < distances[next]) {
          distances[next] = score;
          previous[next] = current;
        }
      }
    }
    if (!Number.isFinite(distances[1])) return null;
    const middle = [];
    for (let cursor = 1; cursor >= 0; cursor = previous[cursor]) middle.push(vertices[cursor]);
    middle.reverse();
    const points = simplifyPoints([start, ...middle, end]);
    const samples = samplePolyline(points);
    const route = { type: 'polyline', points, samples, segments: sampleSegments(points) };
    const visibilityChecks = {
      node: routeHitsNode(route, descriptor),
      reservedArrow: routeHitsReservedArrow(route, descriptor),
      overlap: routeOverlapsExisting(route),
      multiple: routeCrossesExistingMoreThanOnce(route),
      arrow: routeHasArrowConflict(route)
    };
    if (!visibilityChecks.node && !visibilityChecks.reservedArrow &&
      !visibilityChecks.overlap && visibilityChecks.multiple && !visibilityChecks.arrow) {
      const crossingAwareRoute = buildCrossingAwareVisibilityRoute(
        crossingAwareVertices,
        descriptor,
        start,
        end
      );
      if (crossingAwareRoute) return crossingAwareRoute;
    }
    return visibilityChecks.node || visibilityChecks.reservedArrow || visibilityChecks.overlap ||
      visibilityChecks.multiple || visibilityChecks.arrow ? null : route;
  }

  function crossingPenalty(route) {
    let penalty = 0;
    chosenRoutes.forEach(existing => {
      const count = crossingCount(route, existing);
      if (count > 1) penalty += (count - 1) * 100000;
      penalty += count * 100;
    });
    return penalty;
  }

  function routePriority(route) {
    return routeCrossingTotal(route) * 100000 + routeLength(route) +
      (route.type === 'curve' ? 0 : 18);
  }

  descriptors.forEach(descriptor => {
    const start = descriptor.sourcePort;
    const end = descriptor.targetPort;
    const sourceDirection = sideVector(descriptor.sourceSide);
    const targetDirection = sideVector(descriptor.targetSide);
    const startOut = { x: start.x + sourceDirection.x * 8, y: start.y + sourceDirection.y * 8 };
    const endOut = { x: end.x + targetDirection.x * 8, y: end.y + targetDirection.y * 8 };
    const candidates = buildFocusedCurveCandidates(descriptor).map(curve => {
      const samples = sampleFocusedCurve(curve);
      return { type: 'curve', curve, samples, segments: sampleSegments(samples) };
    });
    const xLanes = [6, canvasWidth - 6, (startOut.x + endOut.x) / 2];
    const yLanes = [6, canvasHeight - 6, (startOut.y + endOut.y) / 2];
    blockingNodes.forEach(node => {
      xLanes.push(node.x - 16, node.x + node.w + 16);
      yLanes.push(node.y - 16, node.y + node.h + 16);
    });
    const centerX = (startOut.x + endOut.x) / 2;
    const centerY = (startOut.y + endOut.y) / 2;
    const compactXLanes = [...new Set(xLanes.map(value => Math.round(value)))]
      .sort((a, b) => Math.abs(a - centerX) - Math.abs(b - centerX)).slice(0, 15);
    const compactYLanes = [...new Set(yLanes.map(value => Math.round(value)))]
      .sort((a, b) => Math.abs(a - centerY) - Math.abs(b - centerY)).slice(0, 15);
    const polylinePoints = [[start, startOut, endOut, end]];
    compactXLanes.forEach(x => polylinePoints.push([
      start, startOut, { x, y: startOut.y }, { x, y: endOut.y }, endOut, end
    ]));
    compactYLanes.forEach(y => polylinePoints.push([
      start, startOut, { x: startOut.x, y }, { x: endOut.x, y }, endOut, end
    ]));
    polylinePoints.forEach(rawPoints => {
      const points = simplifyPoints(rawPoints);
      const samples = samplePolyline(points);
      candidates.push({ type: 'polyline', points, samples, segments: sampleSegments(points) });
    });
    const isUsableRoute = route =>
      routeInsideCanvas(route) && !routeHitsNode(route, descriptor) &&
      !routeHitsReservedArrow(route, descriptor) &&
      !routeOverlapsExisting(route) && !routeCrossesExistingMoreThanOnce(route) &&
      !routeHasArrowConflict(route);
    let validRoutes = candidates.filter(isUsableRoute);

    // Dense focus views sometimes need more than one shared X or Y lane.
    // Generate a bounded set of multi-lane doglegs only when no compact route
    // satisfies the one-crossing rule.
    let extendedCandidates = [];
    if (!validRoutes.some(route => routeCrossingTotal(route) === 0)) {
      compactXLanes.forEach(x => {
        compactYLanes.forEach(y => {
          [
            [
              start,
              startOut,
              { x, y: startOut.y },
              { x, y },
              { x: endOut.x, y },
              endOut,
              end
            ],
            [
              start,
              startOut,
              { x: startOut.x, y },
              { x, y },
              { x, y: endOut.y },
              endOut,
              end
            ]
          ].forEach(rawPoints => {
            const points = simplifyPoints(rawPoints);
            const samples = samplePolyline(points);
            extendedCandidates.push({
              type: 'polyline',
              points,
              samples,
              segments: sampleSegments(points)
            });
          });
        });
      });
      validRoutes.push(...extendedCandidates.filter(isUsableRoute));
    }
    validRoutes.sort((first, second) => routePriority(first) - routePriority(second));
    let chosen = validRoutes[0];
    let routeQuality = 'strict';
    if (!chosen || routeCrossingTotal(chosen) > 0) {
      const visibilityRoute = buildVisibilityRoute(descriptor, start, end, startOut, endOut);
      if (visibilityRoute && (!chosen || routePriority(visibilityRoute) < routePriority(chosen))) {
        chosen = visibilityRoute;
      }
    }
    if (!chosen) {
      console.warn(`无法在画布内为关系生成避开节点的路径：${descriptor.edge.id}`);
      return;
    }
    const label = descriptor.edge.label
      ? chooseFocusedCurveLabelPosition(
        chosen.samples,
        descriptor.edge.label,
        labelBoxes,
        nodeMap,
        descriptor
      )
      : chosen.samples[Math.floor(chosen.samples.length / 2)];
    chosen.edgeId = descriptor.edge.id;
    chosenRoutes.push(chosen);
    geometryMap.set(descriptor.edge.id, {
      d: routePath(chosen),
      lx: label.x,
      ly: label.y,
      routeQuality,
      routeOrder: chosenRoutes.length - 1
    });
  });

  const crossingGaps = [];
  for (let firstIndex = 0; firstIndex < chosenRoutes.length; firstIndex += 1) {
    for (let secondIndex = firstIndex + 1; secondIndex < chosenRoutes.length; secondIndex += 1) {
      const crossings = routeIntersectionPoints(
        chosenRoutes[firstIndex],
        chosenRoutes[secondIndex]
      );
      crossings.slice(1).forEach(point => {
        if (!crossingGaps.some(existing => Math.hypot(existing.x - point.x, existing.y - point.y) < 3)) {
          crossingGaps.push(point);
        }
      });
    }
  }
  geometryMap.relaxedEdgeIds = relaxedEdgeIds;
  geometryMap.crossingGaps = crossingGaps;
  return geometryMap;
}

function sideVector(side) {
  if (side === 'left') return { x: -1, y: 0 };
  if (side === 'right') return { x: 1, y: 0 };
  if (side === 'top') return { x: 0, y: -1 };
  return { x: 0, y: 1 };
}

function buildFocusedCurveCandidates(descriptor) {
  const start = descriptor.sourcePort;
  const end = descriptor.targetPort;
  const sourceVector = sideVector(descriptor.sourceSide);
  const targetVector = sideVector(descriptor.targetSide);
  const distance = Math.hypot(end.x - start.x, end.y - start.y);
  const handleLengths = [
    Math.max(46, Math.min(190, distance * 0.38)),
    Math.max(70, Math.min(250, distance * 0.55)),
    Math.max(34, Math.min(130, distance * 0.27))
  ];
  const normal = distance > 0
    ? { x: -(end.y - start.y) / distance, y: (end.x - start.x) / distance }
    : { x: 0, y: 1 };
  const delta = { x: end.x - start.x, y: end.y - start.y };
  const bows = [0, 28, -28, 56, -56, 92, -92, 132, -132, 190, -190, 260, -260,
    340, -340, 440, -440];
  const candidates = [];

  // Simple one-bow arcs stay close to the direct source-target corridor and
  // are far less likely than S-curves to meet the same line more than once.
  [0.26, 0.38].forEach(handleRatio => {
    [10, -10, 20, -20, 38, -38, 64, -64, 96, -96, 140, -140, 200, -200,
      280, -280, 360, -360, 480, -480]
      .forEach(bow => {
        candidates.push({
          start,
          c1: {
            x: start.x + delta.x * handleRatio + normal.x * bow,
            y: start.y + delta.y * handleRatio + normal.y * bow
          },
          c2: {
            x: end.x - delta.x * handleRatio + normal.x * bow,
            y: end.y - delta.y * handleRatio + normal.y * bow
          },
          end
        });
      });
  });
  handleLengths.forEach(handle => {
    bows.forEach(bow => {
      candidates.push({
        start,
        c1: {
          x: start.x + sourceVector.x * handle + normal.x * bow,
          y: start.y + sourceVector.y * handle + normal.y * bow
        },
        c2: {
          x: end.x + targetVector.x * handle + normal.x * bow,
          y: end.y + targetVector.y * handle + normal.y * bow
        },
        end
      });
    });
  });
  return candidates;
}

function sampleFocusedCurve(curve) {
  const samples = [];
  const steps = 32;
  for (let index = 0; index <= steps; index += 1) {
    const t = index / steps;
    const mt = 1 - t;
    samples.push({
      x: mt * mt * mt * curve.start.x + 3 * mt * mt * t * curve.c1.x +
        3 * mt * t * t * curve.c2.x + t * t * t * curve.end.x,
      y: mt * mt * mt * curve.start.y + 3 * mt * mt * t * curve.c1.y +
        3 * mt * t * t * curve.c2.y + t * t * t * curve.end.y
    });
  }
  return samples;
}

function scoreFocusedCurve(samples, descriptor, chosenCurves, nodeMap) {
  let score = 0;
  for (let index = 1; index < samples.length; index += 1) {
    score += Math.hypot(
      samples[index].x - samples[index - 1].x,
      samples[index].y - samples[index - 1].y
    ) * 0.004;
  }
  samples.slice(2, -2).forEach(point => {
    nodeMap.forEach(node => {
      if (node.id === descriptor.source.id || node.id === descriptor.target.id) return;
      if (point.x >= node.x - 10 && point.x <= node.x + node.w + 10 &&
        point.y >= node.y - 10 && point.y <= node.y + node.h + 10) score += 1800;
    });
  });
  chosenCurves.forEach(existing => {
    let closeSamples = 0;
    samples.slice(2, -2).forEach(point => {
      let minimum = Number.POSITIVE_INFINITY;
      existing.slice(2, -2).forEach(other => {
        minimum = Math.min(minimum, Math.hypot(point.x - other.x, point.y - other.y));
      });
      if (minimum < 5) closeSamples += 1;
      else if (minimum < 11) score += 3;
    });
    // A crossing creates only one or two close samples; a shared/overlapping path
    // stays close for many samples and is therefore rejected aggressively.
    if (closeSamples > 3) score += 900 + closeSamples * 180;
    else score += closeSamples * 8;
  });
  return score;
}

function chooseFocusedCurveLabelPosition(samples, text, labelBoxes, nodeMap, descriptor) {
  const width = Math.max(48, Math.min(190, String(text || '').length * 11 + 18));
  const candidates = [0.5, 0.38, 0.62, 0.28, 0.72].flatMap(ratio => {
    const index = Math.round((samples.length - 1) * ratio);
    const point = samples[index];
    const before = samples[Math.max(0, index - 1)];
    const after = samples[Math.min(samples.length - 1, index + 1)];
    const length = Math.max(1, Math.hypot(after.x - before.x, after.y - before.y));
    const normal = { x: -(after.y - before.y) / length, y: (after.x - before.x) / length };
    return [-1, 1].map(side => ({
      x: point.x + normal.x * side * 16,
      y: point.y + normal.y * side * 16 + 4
    }));
  });
  let best = null;
  let bestScore = Number.POSITIVE_INFINITY;
  candidates.forEach((candidate, preference) => {
    const box = { x: candidate.x - width / 2, y: candidate.y - 14, w: width, h: 20 };
    let score = preference;
    labelBoxes.forEach(existing => { if (rectanglesOverlap(box, existing)) score += 1200; });
    nodeMap.forEach(node => {
      const nodeBox = { x: node.x - 5, y: node.y - 5, w: node.w + 10, h: node.h + 10 };
      if (rectanglesOverlap(box, nodeBox)) score +=
        node.id === descriptor.source.id || node.id === descriptor.target.id ? 760 : 620;
    });
    if (score < bestScore) {
      bestScore = score;
      best = { x: candidate.x, y: candidate.y, box };
    }
  });
  labelBoxes.push(best.box);
  return best;
}

function buildFocusedCurvePath(curve) {
  return `M ${curve.start.x} ${curve.start.y} C ${curve.c1.x} ${curve.c1.y}, ` +
    `${curve.c2.x} ${curve.c2.y}, ${curve.end.x} ${curve.end.y}`;
}

function addFocusedPort(portGroups, node, side, descriptor, role) {
  const key = `${node.id}:${side}`;
  if (!portGroups.has(key)) portGroups.set(key, []);
  const oppositeCenter = role === 'source'
    ? descriptor.targetCenter
    : descriptor.sourceCenter;
  portGroups.get(key).push({
    node,
    side,
    descriptor,
    role,
    oppositeCoordinate: side === 'left' || side === 'right'
      ? oppositeCenter.y
      : oppositeCenter.x
  });
}

function calculateNodePort(node, side, index, count) {
  const margin = 9;
  if (side === 'left' || side === 'right') {
    const available = Math.max(1, node.h - margin * 2);
    return {
      x: side === 'left' ? node.x : node.x + node.w,
      y: node.y + margin + available * (index + 1) / (count + 1)
    };
  }
  const available = Math.max(1, node.w - margin * 2);
  return {
    x: node.x + margin + available * (index + 1) / (count + 1),
    y: side === 'top' ? node.y : node.y + node.h
  };
}

function buildFocusedRouteCandidates(descriptor) {
  const start = descriptor.sourcePort;
  const end = descriptor.targetPort;
  const candidates = [];
  if (descriptor.horizontal) {
    const low = Math.min(start.x, end.x);
    const high = Math.max(start.x, end.x);
    const span = high - low;
    const center = (start.x + end.x) / 2;
    const offsets = span > 260
      ? [0, -72, 72, -36, 36, -108, 108]
      : span > 140
        ? [0, -42, 42, -21, 21]
        : [0, -14, 14];
    const minLane = low + Math.min(22, span / 3);
    const maxLane = high - Math.min(22, span / 3);
    const lanes = [...new Set(offsets.map(offset =>
      Math.round(Math.max(minLane, Math.min(maxLane, center + offset)))
    ))];
    lanes.forEach(laneX => {
      candidates.push([
        start,
        { x: laneX, y: start.y },
        { x: laneX, y: end.y },
        end
      ]);
    });
  } else {
    const low = Math.min(start.y, end.y);
    const high = Math.max(start.y, end.y);
    const span = high - low;
    const center = (start.y + end.y) / 2;
    const offsets = span > 220
      ? [0, -60, 60, -30, 30, -90, 90]
      : span > 120
        ? [0, -36, 36, -18, 18]
        : [0, -14, 14];
    const minLane = low + Math.min(22, span / 3);
    const maxLane = high - Math.min(22, span / 3);
    const lanes = [...new Set(offsets.map(offset =>
      Math.round(Math.max(minLane, Math.min(maxLane, center + offset)))
    ))];
    lanes.forEach(laneY => {
      candidates.push([
        start,
        { x: start.x, y: laneY },
        { x: end.x, y: laneY },
        end
      ]);
    });
  }
  return candidates;
}

function normalizeRoutePoints(points) {
  const unique = [];
  points.forEach(point => {
    const previous = unique[unique.length - 1];
    if (!previous || Math.abs(previous.x - point.x) > 0.1 || Math.abs(previous.y - point.y) > 0.1) {
      unique.push({ x: point.x, y: point.y });
    }
  });
  if (unique.length <= 2) return unique;
  const simplified = [unique[0]];
  for (let index = 1; index < unique.length - 1; index += 1) {
    const previous = simplified[simplified.length - 1];
    const current = unique[index];
    const next = unique[index + 1];
    const collinear = (
      Math.abs(previous.x - current.x) < 0.1 &&
      Math.abs(current.x - next.x) < 0.1
    ) || (
      Math.abs(previous.y - current.y) < 0.1 &&
      Math.abs(current.y - next.y) < 0.1
    );
    if (!collinear) simplified.push(current);
  }
  simplified.push(unique[unique.length - 1]);
  return simplified;
}

function routeSegments(points) {
  const segments = [];
  for (let index = 1; index < points.length; index += 1) {
    segments.push({
      a: points[index - 1],
      b: points[index]
    });
  }
  return segments;
}

function scoreFocusedRoute(segments, descriptor, chosenSegments, nodeMap) {
  let score = 0;
  segments.forEach(segment => {
    score += segmentLength(segment) * 0.004;
    nodeMap.forEach(node => {
      if (node.id === descriptor.source.id || node.id === descriptor.target.id) return;
      if (segmentIntersectsNode(segment, node, 7)) score += 1200;
    });
    chosenSegments.forEach(existing => {
      const overlap = collinearOverlap(segment, existing);
      if (overlap > 0) score += 500 + overlap * 4;
      else if (orthogonalSegmentsIntersect(segment, existing)) score += 75;
    });
  });
  return score;
}

function segmentLength(segment) {
  return Math.abs(segment.b.x - segment.a.x) + Math.abs(segment.b.y - segment.a.y);
}

function segmentIntersectsNode(segment, node, padding) {
  const left = node.x - padding;
  const right = node.x + node.w + padding;
  const top = node.y - padding;
  const bottom = node.y + node.h + padding;
  if (Math.abs(segment.a.y - segment.b.y) < 0.1) {
    const minX = Math.min(segment.a.x, segment.b.x);
    const maxX = Math.max(segment.a.x, segment.b.x);
    return segment.a.y >= top &&
      segment.a.y <= bottom &&
      maxX >= left &&
      minX <= right;
  }
  const minY = Math.min(segment.a.y, segment.b.y);
  const maxY = Math.max(segment.a.y, segment.b.y);
  return segment.a.x >= left &&
    segment.a.x <= right &&
    maxY >= top &&
    minY <= bottom;
}

function collinearOverlap(first, second) {
  const firstHorizontal = Math.abs(first.a.y - first.b.y) < 0.1;
  const secondHorizontal = Math.abs(second.a.y - second.b.y) < 0.1;
  if (firstHorizontal !== secondHorizontal) return 0;
  if (firstHorizontal) {
    if (Math.abs(first.a.y - second.a.y) > 1) return 0;
    return Math.max(0,
      Math.min(Math.max(first.a.x, first.b.x), Math.max(second.a.x, second.b.x)) -
      Math.max(Math.min(first.a.x, first.b.x), Math.min(second.a.x, second.b.x))
    );
  }
  if (Math.abs(first.a.x - second.a.x) > 1) return 0;
  return Math.max(0,
    Math.min(Math.max(first.a.y, first.b.y), Math.max(second.a.y, second.b.y)) -
    Math.max(Math.min(first.a.y, first.b.y), Math.min(second.a.y, second.b.y))
  );
}

function orthogonalSegmentsIntersect(first, second) {
  const firstHorizontal = Math.abs(first.a.y - first.b.y) < 0.1;
  const secondHorizontal = Math.abs(second.a.y - second.b.y) < 0.1;
  if (firstHorizontal === secondHorizontal) return false;
  const horizontal = firstHorizontal ? first : second;
  const vertical = firstHorizontal ? second : first;
  const minX = Math.min(horizontal.a.x, horizontal.b.x);
  const maxX = Math.max(horizontal.a.x, horizontal.b.x);
  const minY = Math.min(vertical.a.y, vertical.b.y);
  const maxY = Math.max(vertical.a.y, vertical.b.y);
  return vertical.a.x >= minX &&
    vertical.a.x <= maxX &&
    horizontal.a.y >= minY &&
    horizontal.a.y <= maxY;
}

function chooseFocusedLabelPosition(points, text, labelBoxes, nodeMap, descriptor) {
  const width = Math.max(48, Math.min(190, String(text || '').length * 11 + 18));
  const height = 20;
  const segments = routeSegments(points)
    .map((segment, index) => ({ segment, index, length: segmentLength(segment) }))
    .sort((a, b) => b.length - a.length);
  const candidates = [];
  segments.forEach((entry, segmentRank) => {
    [0.5, 0.34, 0.66].forEach((ratio, ratioIndex) => {
      const segment = entry.segment;
      const point = {
        x: segment.a.x + (segment.b.x - segment.a.x) * ratio,
        y: segment.a.y + (segment.b.y - segment.a.y) * ratio
      };
      const horizontal = Math.abs(segment.a.y - segment.b.y) < 0.1;
      [-1, 1].forEach((side, sideIndex) => {
        candidates.push({
          x: horizontal ? point.x : point.x + side * (width / 2 + 10),
          y: horizontal ? point.y + side * 12 : point.y + 4,
          preference: segmentRank * 8 + ratioIndex * 2 + sideIndex
        });
      });
    });
  });

  let best = null;
  let bestScore = Number.POSITIVE_INFINITY;

  candidates.forEach(candidate => {
    const x = candidate.x;
    const y = candidate.y;
    const box = {
      x: x - width / 2,
      y: y - 14,
      w: width,
      h: height
    };
    let score = candidate.preference;
    labelBoxes.forEach(existing => {
      if (rectanglesOverlap(box, existing)) score += 1200;
    });
    nodeMap.forEach(node => {
      const nodeBox = { x: node.x - 4, y: node.y - 4, w: node.w + 8, h: node.h + 8 };
      if (rectanglesOverlap(box, nodeBox)) {
        score += node.id === descriptor.source.id || node.id === descriptor.target.id
          ? 720
          : 520;
      }
    });
    if (score < bestScore) {
      bestScore = score;
      best = { x, y, box };
    }
  });

  labelBoxes.push(best.box);
  return best;
}

function rectanglesOverlap(first, second) {
  return first.x < second.x + second.w &&
    first.x + first.w > second.x &&
    first.y < second.y + second.h &&
    first.y + first.h > second.y;
}

function buildRoundedRoutePath(points) {
  if (!points.length) return '';
  if (points.length === 1) return `M ${points[0].x} ${points[0].y}`;
  let path = `M ${points[0].x} ${points[0].y}`;
  for (let index = 1; index < points.length - 1; index += 1) {
    const previous = points[index - 1];
    const corner = points[index];
    const next = points[index + 1];
    const incoming = Math.hypot(corner.x - previous.x, corner.y - previous.y);
    const outgoing = Math.hypot(next.x - corner.x, next.y - corner.y);
    const radius = Math.min(11, incoming / 2, outgoing / 2);
    const before = {
      x: corner.x + (previous.x - corner.x) / Math.max(1, incoming) * radius,
      y: corner.y + (previous.y - corner.y) / Math.max(1, incoming) * radius
    };
    const after = {
      x: corner.x + (next.x - corner.x) / Math.max(1, outgoing) * radius,
      y: corner.y + (next.y - corner.y) / Math.max(1, outgoing) * radius
    };
    path += ` L ${before.x} ${before.y} Q ${corner.x} ${corner.y} ${after.x} ${after.y}`;
  }
  const last = points[points.length - 1];
  return `${path} L ${last.x} ${last.y}`;
}



function calculateFocusedEdgeGeometries(edges, nodeMap, blockingNodeIds, canvasMeta = {}) {
  routingCanvasMeta = {
    canvasWidth: Number(canvasMeta.canvasWidth) || 1440,
    canvasHeight: Number(canvasMeta.canvasHeight) || 1030
  };
  return calculateFocusedEdgeGeometriesInternal(edges, nodeMap, blockingNodeIds);
}

function serializeGeometryMap(geometryMap) {
  return {
    entries: [...geometryMap.entries()],
    crossingGaps: geometryMap.crossingGaps || [],
    relaxedEdgeIds: geometryMap.relaxedEdgeIds || []
  };
}

function installRoutingWorkerHandler() {
  self.addEventListener('message', event => {
    const message = event.data || {};
    if (message.type !== 'route') return;
    try {
      const nodeMap = new Map((message.nodes || []).map(node => [node.id, node]));
      const blockingNodeIds = new Set(message.blockingNodeIds || []);
      const geometryMap = calculateFocusedEdgeGeometries(
        message.edges || [],
        nodeMap,
        blockingNodeIds,
        message.canvasMeta || {}
      );
      self.postMessage({
        type: 'route-result',
        requestId: message.requestId,
        result: serializeGeometryMap(geometryMap)
      });
    } catch (error) {
      self.postMessage({
        type: 'route-error',
        requestId: message.requestId,
        error: error?.message || String(error)
      });
    }
  });
}

function createWorkerSource() {
  const dependencies = [
    calculateEdgeGeometry,
    calculateFocusedEdgeGeometriesInternal,
    sideVector,
    buildFocusedCurveCandidates,
    sampleFocusedCurve,
    scoreFocusedCurve,
    chooseFocusedCurveLabelPosition,
    buildFocusedCurvePath,
    addFocusedPort,
    calculateNodePort,
    buildFocusedRouteCandidates,
    normalizeRoutePoints,
    routeSegments,
    scoreFocusedRoute,
    segmentLength,
    segmentIntersectsNode,
    collinearOverlap,
    orthogonalSegmentsIntersect,
    chooseFocusedLabelPosition,
    rectanglesOverlap,
    buildRoundedRoutePath,
    calculateFocusedEdgeGeometries,
    serializeGeometryMap,
    installRoutingWorkerHandler
  ];
  return `'use strict';\nlet routingCanvasMeta={canvasWidth:1440,canvasHeight:1030};\n` +
    dependencies.map(dependency => dependency.toString()).join('\n') +
    '\ninstallRoutingWorkerHandler();\n';
}

const api = {
  version: 1,
  calculateEdgeGeometry,
  calculateFocusedEdgeGeometries,
  serializeGeometryMap,
  createWorkerSource
};

globalThis.GraphRouting = api;

if (typeof WorkerGlobalScope !== 'undefined' && self instanceof WorkerGlobalScope) {
  installRoutingWorkerHandler();
}
})();
