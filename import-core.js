(() => {
  'use strict';

  function readScriptAttribute(attributes, name) {
    const pattern = name === 'id'
      ? /\bid\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'=<>`]+))/i
      : /\btype\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'=<>`]+))/i;
    const match = pattern.exec(attributes);
    return match ? String(match[1] ?? match[2] ?? match[3] ?? '') : '';
  }

  function parseJson(text, errorPrefix) {
    try {
      return JSON.parse(text);
    } catch (error) {
      throw new Error(`${errorPrefix}：${error.message}`);
    }
  }

  function extractReadonlyGraph(html) {
    const scriptPattern = /<script\b([^>]*)>([\s\S]*?)<\/script\s*>/gi;
    let match;
    while ((match = scriptPattern.exec(html))) {
      const id = readScriptAttribute(match[1], 'id');
      const type = readScriptAttribute(match[1], 'type').toLowerCase();
      if (id === 'graphData' && type === 'application/json') {
        const graphText = String(match[2] || '').trim();
        if (!graphText) throw new Error('只读交互版中没有关系图数据。');
        return parseJson(graphText, '只读交互版内嵌数据错误');
      }
    }
    throw new Error('该 HTML 不是本工具生成的只读交互版，未找到可导入的关系图数据。');
  }

  function parseGraphFileText(text, fileName = '', contentType = '') {
    const normalizedText = String(text ?? '').replace(/^\uFEFF/, '');
    const trimmed = normalizedText.trimStart();
    const normalizedName = String(fileName || '').toLowerCase();
    const normalizedType = String(contentType || '').toLowerCase();
    const isHtml = /\.html?$/.test(normalizedName) || normalizedType.includes('text/html') || /^<(?:!doctype\s+html|html)\b/i.test(trimmed);
    const isJson = /\.json$/.test(normalizedName) || normalizedType.includes('application/json') || /^[\[{]/.test(trimmed);

    if (isHtml) {
      return { graph: extractReadonlyGraph(normalizedText), format: 'readonly-html' };
    }
    if (isJson) {
      return { graph: parseJson(normalizedText, 'JSON 语法错误'), format: 'json' };
    }
    throw new Error('不支持该文件格式。请选择 JSON 或本工具导出的只读交互版 HTML。');
  }

  globalThis.GraphImport = {
    version: 1,
    parseGraphFileText,
    extractReadonlyGraph
  };
})();
