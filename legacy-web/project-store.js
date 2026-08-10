(() => {
  'use strict';

  const DB_NAME = 'relationship-graph-editor';
  const DB_VERSION = 1;
  const PROJECT_STORE = 'projects';
  const SNAPSHOT_STORE = 'snapshots';
  const ACTIVE_PROJECT_KEY = 'relationship-graph-editor:active-project';
  const FALLBACK_PROJECTS_KEY = 'relationship-graph-editor:projects';
  const MAX_SNAPSHOTS = 30;

  const clone = value => typeof structuredClone === 'function'
    ? structuredClone(value)
    : JSON.parse(JSON.stringify(value));

  function createId(prefix) {
    if (globalThis.crypto?.randomUUID) return `${prefix}_${crypto.randomUUID()}`;
    return `${prefix}_${Date.now().toString(36)}_${Math.random().toString(36).slice(2, 10)}`;
  }

  function requestResult(request) {
    return new Promise((resolve, reject) => {
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error || new Error('数据库操作失败。'));
    });
  }

  function transactionDone(transaction) {
    return new Promise((resolve, reject) => {
      transaction.oncomplete = () => resolve();
      transaction.onerror = () => reject(transaction.error || new Error('数据库事务失败。'));
      transaction.onabort = () => reject(transaction.error || new Error('数据库事务已取消。'));
    });
  }

  class GraphProjectStore {
    constructor() {
      this.db = null;
      this.fallback = false;
    }

    async initialize(seedGraph) {
      try {
        this.db = await this.openDatabase();
      } catch (error) {
        console.warn('IndexedDB 不可用，已切换为兼容存储。', error);
        this.fallback = true;
      }

      let projects = await this.listProjects();
      if (!projects.length) {
        const created = await this.createProject(
          seedGraph?.meta?.title || '甜品店功能关系图',
          seedGraph
        );
        this.setActiveProjectId(created.id);
        projects = [created];
      }

      const requestedId = this.getActiveProjectId();
      const active = projects.find(project => project.id === requestedId) || projects[0];
      this.setActiveProjectId(active.id);
      return this.getProject(active.id);
    }

    openDatabase() {
      return new Promise((resolve, reject) => {
        if (!globalThis.indexedDB) {
          reject(new Error('浏览器不支持 IndexedDB。'));
          return;
        }
        const request = indexedDB.open(DB_NAME, DB_VERSION);
        request.onupgradeneeded = () => {
          const database = request.result;
          if (!database.objectStoreNames.contains(PROJECT_STORE)) {
            const projects = database.createObjectStore(PROJECT_STORE, { keyPath: 'id' });
            projects.createIndex('updatedAt', 'updatedAt');
          }
          if (!database.objectStoreNames.contains(SNAPSHOT_STORE)) {
            const snapshots = database.createObjectStore(SNAPSHOT_STORE, { keyPath: 'id' });
            snapshots.createIndex('projectId', 'projectId');
            snapshots.createIndex('createdAt', 'createdAt');
          }
        };
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error || new Error('无法打开项目数据库。'));
        request.onblocked = () => reject(new Error('项目数据库被其他页面占用，请关闭旧页面后重试。'));
      });
    }

    getActiveProjectId() {
      try {
        return localStorage.getItem(ACTIVE_PROJECT_KEY) || '';
      } catch {
        return '';
      }
    }

    setActiveProjectId(projectId) {
      try {
        localStorage.setItem(ACTIVE_PROJECT_KEY, projectId);
      } catch {
        // The active project remains usable for this page even when storage is unavailable.
      }
    }

    readFallbackProjects() {
      try {
        const parsed = JSON.parse(localStorage.getItem(FALLBACK_PROJECTS_KEY) || '[]');
        return Array.isArray(parsed) ? parsed : [];
      } catch {
        return [];
      }
    }

    writeFallbackProjects(projects) {
      localStorage.setItem(FALLBACK_PROJECTS_KEY, JSON.stringify(projects));
    }

    async listProjects() {
      let projects;
      if (this.fallback) {
        projects = this.readFallbackProjects();
      } else {
        const transaction = this.db.transaction(PROJECT_STORE, 'readonly');
        projects = await requestResult(transaction.objectStore(PROJECT_STORE).getAll());
      }
      return projects
        .map(project => ({
          id: project.id,
          name: project.name,
          createdAt: project.createdAt,
          updatedAt: project.updatedAt,
          schemaVersion: project.schemaVersion || 1
        }))
        .sort((first, second) => String(second.updatedAt).localeCompare(String(first.updatedAt)));
    }

    async getProject(projectId) {
      if (this.fallback) {
        const project = this.readFallbackProjects().find(item => item.id === projectId);
        return project ? clone(project) : null;
      }
      const transaction = this.db.transaction(PROJECT_STORE, 'readonly');
      const project = await requestResult(transaction.objectStore(PROJECT_STORE).get(projectId));
      return project ? clone(project) : null;
    }

    async createProject(name, graph) {
      const now = new Date().toISOString();
      const project = {
        id: createId('project'),
        name: String(name || '未命名关系图').trim().slice(0, 80) || '未命名关系图',
        schemaVersion: Math.max(1, Number(graph?.version) || 1),
        createdAt: now,
        updatedAt: now,
        graph: clone(graph)
      };
      if (this.fallback) {
        const projects = this.readFallbackProjects();
        projects.push(project);
        this.writeFallbackProjects(projects);
      } else {
        const transaction = this.db.transaction(PROJECT_STORE, 'readwrite');
        transaction.objectStore(PROJECT_STORE).add(project);
        await transactionDone(transaction);
      }
      return clone(project);
    }

    async saveProject(projectId, graph, options = {}) {
      const existing = await this.getProject(projectId);
      if (!existing) throw new Error('当前项目不存在，无法保存。');
      const updated = {
        ...existing,
        name: String(graph?.meta?.title || existing.name || '未命名关系图').slice(0, 80),
        schemaVersion: Math.max(1, Number(graph?.version) || existing.schemaVersion || 1),
        graph: clone(graph),
        updatedAt: new Date().toISOString()
      };
      if (this.fallback) {
        const projects = this.readFallbackProjects();
        const index = projects.findIndex(project => project.id === projectId);
        if (index < 0) throw new Error('当前项目不存在，无法保存。');
        projects[index] = updated;
        this.writeFallbackProjects(projects);
      } else {
        const transaction = this.db.transaction(PROJECT_STORE, 'readwrite');
        transaction.objectStore(PROJECT_STORE).put(updated);
        await transactionDone(transaction);
      }
      if (options.snapshot) await this.createSnapshot(projectId, updated.graph, options.reason || '自动保存');
      return clone(updated);
    }

    async duplicateProject(projectId) {
      const source = await this.getProject(projectId);
      if (!source) throw new Error('找不到需要复制的项目。');
      const duplicate = await this.createProject(`${source.name} 副本`, source.graph);
      await this.createSnapshot(duplicate.id, duplicate.graph, '创建项目副本');
      return duplicate;
    }

    async renameProject(projectId, name) {
      const project = await this.getProject(projectId);
      if (!project) throw new Error('找不到需要重命名的项目。');
      project.name = String(name || '').trim().slice(0, 80);
      if (!project.name) throw new Error('项目名称不能为空。');
      project.graph.meta.title = project.name;
      return this.saveProject(projectId, project.graph, { snapshot: true, reason: '项目重命名' });
    }

    async deleteProject(projectId) {
      const projects = await this.listProjects();
      if (projects.length <= 1) throw new Error('至少需要保留一个项目。');
      if (this.fallback) {
        this.writeFallbackProjects(this.readFallbackProjects().filter(project => project.id !== projectId));
      } else {
        const transaction = this.db.transaction([PROJECT_STORE, SNAPSHOT_STORE], 'readwrite');
        transaction.objectStore(PROJECT_STORE).delete(projectId);
        const snapshotStore = transaction.objectStore(SNAPSHOT_STORE);
        const snapshotIndex = snapshotStore.index('projectId');
        const snapshots = await requestResult(snapshotIndex.getAll(projectId));
        snapshots.forEach(snapshot => snapshotStore.delete(snapshot.id));
        await transactionDone(transaction);
      }
      const remaining = (await this.listProjects())[0];
      this.setActiveProjectId(remaining.id);
      return this.getProject(remaining.id);
    }

    async createSnapshot(projectId, graph, reason) {
      if (this.fallback) return null;
      const snapshot = {
        id: createId('snapshot'),
        projectId,
        reason: String(reason || '自动保存').slice(0, 80),
        createdAt: new Date().toISOString(),
        graph: clone(graph)
      };
      const transaction = this.db.transaction(SNAPSHOT_STORE, 'readwrite');
      transaction.objectStore(SNAPSHOT_STORE).add(snapshot);
      await transactionDone(transaction);
      await this.pruneSnapshots(projectId);
      return clone(snapshot);
    }

    async listSnapshots(projectId) {
      if (this.fallback) return [];
      const transaction = this.db.transaction(SNAPSHOT_STORE, 'readonly');
      const snapshots = await requestResult(
        transaction.objectStore(SNAPSHOT_STORE).index('projectId').getAll(projectId)
      );
      return snapshots.sort((first, second) =>
        String(second.createdAt).localeCompare(String(first.createdAt))
      );
    }

    async pruneSnapshots(projectId) {
      if (this.fallback) return;
      const snapshots = await this.listSnapshots(projectId);
      const stale = snapshots.slice(MAX_SNAPSHOTS);
      if (!stale.length) return;
      const transaction = this.db.transaction(SNAPSHOT_STORE, 'readwrite');
      stale.forEach(snapshot => transaction.objectStore(SNAPSHOT_STORE).delete(snapshot.id));
      await transactionDone(transaction);
    }

    async restoreSnapshot(snapshotId) {
      if (this.fallback) throw new Error('当前浏览器不支持恢复快照。');
      const transaction = this.db.transaction([PROJECT_STORE, SNAPSHOT_STORE], 'readwrite');
      const completion = transactionDone(transaction);
      const projectStore = transaction.objectStore(PROJECT_STORE);
      const snapshotStore = transaction.objectStore(SNAPSHOT_STORE);
      let restored = null;
      let restoredProjectId = '';

      try {
        const snapshot = await requestResult(snapshotStore.get(snapshotId));
        if (!snapshot) throw new Error('恢复快照不存在。');
        const current = await requestResult(projectStore.get(snapshot.projectId));
        if (!current) throw new Error('恢复快照对应的项目不存在。');

        const restoredAt = new Date().toISOString();
        snapshotStore.add({
          id: createId('snapshot'),
          projectId: snapshot.projectId,
          reason: '恢复前自动备份',
          createdAt: restoredAt,
          graph: clone(current.graph)
        });
        restored = {
          ...current,
          name: String(snapshot.graph?.meta?.title || current.name || '未命名关系图').slice(0, 80),
          schemaVersion: Math.max(1, Number(snapshot.graph?.version) || current.schemaVersion || 1),
          graph: clone(snapshot.graph),
          updatedAt: restoredAt
        };
        projectStore.put(restored);
        await completion;
        restoredProjectId = snapshot.projectId;
      } catch (error) {
        try {
          transaction.abort();
        } catch {
          // The transaction may already have completed or aborted.
        }
        await completion.catch(() => undefined);
        throw error;
      }
      try {
        await this.pruneSnapshots(restoredProjectId);
      } catch (error) {
        console.warn('恢复已完成，但旧恢复记录清理失败。', error);
      }
      this.setActiveProjectId(restoredProjectId);
      return clone(restored);
    }
  }

  window.GraphProjectStore = new GraphProjectStore();
})();
