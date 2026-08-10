import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const toolDirectory = path.dirname(fileURLToPath(import.meta.url));
const projectDirectory = path.dirname(toolDirectory);
const source = fs.readFileSync(path.join(projectDirectory, 'project-store.js'), 'utf8');
const seedGraph = JSON.parse(fs.readFileSync(path.join(projectDirectory, 'system-function-graph.json'), 'utf8'));
const values = new Map();
const localStorage = {
  getItem: key => values.has(key) ? values.get(key) : null,
  setItem: (key, value) => values.set(key, String(value)),
  removeItem: key => values.delete(key)
};
const context = {
  window: {},
  localStorage,
  console: { ...console, warn: () => {} },
  structuredClone,
  crypto: globalThis.crypto,
  setTimeout,
  clearTimeout
};
vm.createContext(context);
vm.runInContext(source, context);
const store = context.window.GraphProjectStore;
const issues = [];

const first = await store.initialize(seedGraph);
if (!first?.id || first.graph.nodes.length !== seedGraph.nodes.length) issues.push('初始化项目失败');
if (!store.fallback) issues.push('无 IndexedDB 测试环境未进入兼容存储');

const blank = {
  version: 2,
  meta: { title: '测试项目', canvasWidth: 800, canvasHeight: 600 },
  groups: [{ id: 'main', label: '主要内容', x: 0, y: 0, w: 800, h: 600 }],
  nodes: [],
  edges: []
};
const created = await store.createProject('测试项目', blank);
await store.saveProject(created.id, { ...blank, nodes: [{ id: 'a', label: 'A' }] });
const saved = await store.getProject(created.id);
if (saved.graph.nodes.length !== 1) issues.push('保存项目失败');

const renamed = await store.renameProject(created.id, '已重命名');
if (renamed.name !== '已重命名' || renamed.graph.meta.title !== '已重命名') issues.push('重命名项目失败');

const duplicate = await store.duplicateProject(created.id);
if (duplicate.id === created.id || duplicate.graph.nodes.length !== 1) issues.push('复制项目失败');

const beforeDelete = await store.listProjects();
await store.deleteProject(duplicate.id);
const afterDelete = await store.listProjects();
if (afterDelete.length !== beforeDelete.length - 1) issues.push('删除项目失败');

function requestFor(result) {
  const request = { result, error: null, onsuccess: null, onerror: null };
  queueMicrotask(() => request.onsuccess?.());
  return request;
}

function createIndexedDbDouble(projects, snapshots) {
  return {
    transaction() {
      const transaction = {
        error: null,
        oncomplete: null,
        onerror: null,
        onabort: null,
        aborted: false,
        abort() {
          this.aborted = true;
          queueMicrotask(() => this.onabort?.());
        },
        objectStore(name) {
          if (name === 'projects') {
            return {
              get: id => requestFor(structuredClone(projects.get(id))),
              put: project => {
                projects.set(project.id, structuredClone(project));
                return requestFor(project.id);
              }
            };
          }
          if (name === 'snapshots') {
            return {
              get: id => requestFor(structuredClone(snapshots.get(id))),
              add: snapshot => {
                snapshots.set(snapshot.id, structuredClone(snapshot));
                return requestFor(snapshot.id);
              },
              delete: id => {
                snapshots.delete(id);
                return requestFor(undefined);
              },
              index: indexName => ({
                getAll: projectId => requestFor(
                  [...snapshots.values()]
                    .filter(snapshot => indexName !== 'projectId' || snapshot.projectId === projectId)
                    .map(snapshot => structuredClone(snapshot))
                )
              })
            };
          }
          throw new Error(`未知测试存储：${name}`);
        }
      };
      setTimeout(() => {
        if (!transaction.aborted) transaction.oncomplete?.();
      }, 0);
      return transaction;
    }
  };
}

const indexedProjects = new Map();
const indexedSnapshots = new Map();
const indexedProjectId = 'project_restore';
const currentGraph = structuredClone(seedGraph);
currentGraph.meta.title = '恢复前当前版本';
currentGraph.nodes[0].note = '必须保留的当前内容';
const targetGraph = structuredClone(seedGraph);
targetGraph.meta.title = '目标历史版本';
targetGraph.nodes[0].note = '历史内容';
indexedProjects.set(indexedProjectId, {
  id: indexedProjectId,
  name: currentGraph.meta.title,
  schemaVersion: currentGraph.version,
  createdAt: '2026-01-01T00:00:00.000Z',
  updatedAt: '2026-01-02T00:00:00.000Z',
  graph: currentGraph
});
indexedSnapshots.set('snapshot_target', {
  id: 'snapshot_target',
  projectId: indexedProjectId,
  reason: '目标快照',
  createdAt: '2026-01-01T12:00:00.000Z',
  graph: targetGraph
});

store.fallback = false;
store.db = createIndexedDbDouble(indexedProjects, indexedSnapshots);
const restored = await store.restoreSnapshot('snapshot_target');
if (restored.graph.meta.title !== targetGraph.meta.title || restored.graph.nodes[0].note !== '历史内容') {
  issues.push('恢复快照没有写入目标历史版本');
}
const recoveryBackup = [...indexedSnapshots.values()].find(snapshot => snapshot.reason === '恢复前自动备份');
if (!recoveryBackup || recoveryBackup.graph.meta.title !== currentGraph.meta.title ||
    recoveryBackup.graph.nodes[0].note !== '必须保留的当前内容') {
  issues.push('恢复快照没有保留恢复前的当前版本');
}

const summary = {
  fallback: true,
  projects: afterDelete.length,
  storedKeys: [...values.keys()].length,
  indexedRestore: Boolean(recoveryBackup),
  issues
};
console.log(JSON.stringify(summary, null, 2));
if (issues.length) process.exitCode = 1;
