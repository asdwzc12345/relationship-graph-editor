(() => {
  'use strict';

  const LEGACY_KIND_NAMES = Object.freeze({
    resource: '资源/输入',
    system: '经营功能',
    output: '产出/成长',
    content: '收集内容',
    staff: '员工能力',
    commercial: '商业化入口'
  });
  const STYLE_KINDS = Object.freeze(Object.keys(LEGACY_KIND_NAMES));

  function normalizeLabel(value, fallback = '未分类') {
    return String(value ?? '').trim().replace(/\s+/g, ' ').slice(0, 30) || fallback;
  }

  function key(value) {
    return normalizeLabel(value).toLocaleLowerCase('zh-CN');
  }

  function styleKind(label) {
    const text = normalizeLabel(label);
    let hash = 0;
    for (let index = 0; index < text.length; index += 1) {
      hash = ((hash * 31) + text.charCodeAt(index)) >>> 0;
    }
    return STYLE_KINDS[hash % STYLE_KINDS.length];
  }

  function normalizeCatalog(rawNodeTypes) {
    const nodeTypes = [];
    const usedKeys = new Set();
    if (!Array.isArray(rawNodeTypes)) return nodeTypes;
    rawNodeTypes.forEach(type => {
      const label = normalizeLabel(type?.label, '');
      if (!label) return;
      const normalizedKey = key(label);
      if (usedKeys.has(normalizedKey)) return;
      usedKeys.add(normalizedKey);
      nodeTypes.push({
        label,
        kind: LEGACY_KIND_NAMES[type?.kind] ? type.kind : styleKind(label)
      });
    });
    return nodeTypes;
  }

  function ensureInSettings(settings, label, preferredKind = '') {
    if (!Array.isArray(settings.nodeTypes)) settings.nodeTypes = [];
    const normalizedLabel = normalizeLabel(label);
    const normalizedKey = key(normalizedLabel);
    const existing = settings.nodeTypes.find(type => key(type.label) === normalizedKey);
    if (existing) return existing;
    const definition = {
      label: normalizedLabel,
      kind: LEGACY_KIND_NAMES[preferredKind] ? preferredKind : styleKind(normalizedLabel)
    };
    settings.nodeTypes.push(definition);
    return definition;
  }

  function labelForNode(node) {
    return normalizeLabel(node?.type ?? LEGACY_KIND_NAMES[node?.kind]);
  }

  globalThis.GraphNodeTypes = Object.freeze({
    version: 1,
    LEGACY_KIND_NAMES,
    STYLE_KINDS,
    normalizeLabel,
    key,
    styleKind,
    normalizeCatalog,
    ensureInSettings,
    labelForNode
  });
})();
