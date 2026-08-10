(() => {
  'use strict';

  const STORAGE_KEY = 'sweet-shop-graph-editor:v4';
  const CLIPBOARD_KEY = 'relationship-graph-editor:clipboard';
  const GRAPH_SCHEMA_VERSION = 2;
  const MAX_HISTORY = 60;
  const MAX_FOCUSED_ROUTE_CACHE = 8;
  const MIN_CANVAS_WIDTH = 600;
  const MIN_CANVAS_HEIGHT = 400;
  const MAX_CANVAS_SIZE = 20000;
  const MAX_IMPORT_FILE_BYTES = 10 * 1024 * 1024;
  const MAX_GRAPH_GROUPS = 400;
  const MAX_GRAPH_NODES = 1500;
  const MAX_GRAPH_EDGES = 7500;
  const MAX_ENTITY_ID_LENGTH = 100;
  const LINK_DRAG_THRESHOLD_PX = 7;
  const SVG_NS = 'http://www.w3.org/2000/svg';
  const NODE_PORT_SIDES = ['top', 'right', 'bottom', 'left'];
  const EDGE_LINE_TYPES = ['straight', 'polyline', 'curve'];
  const BATCH_GROUP_KEEP = '__keep__';

  const NODE_TYPE_API = window.GraphNodeTypes;
  if (!NODE_TYPE_API) throw new Error('节点类型核心未载入。');
  const IMPORT_API = window.GraphImport;
  if (!IMPORT_API) throw new Error('导入解析核心未载入。');
  const KIND_NAMES = NODE_TYPE_API.LEGACY_KIND_NAMES;

  const EDGE_CATEGORY_NAMES = {
    core: '核心操作',
    economy: '资源经济',
    tempo: '时间节奏',
    growth: '成长反馈',
    content: '内容收集',
    staff: '员工岗位',
    commercial: '商业化'
  };

  const DEFAULT_RELATION_TYPES = [
    { id: 'core', label: '核心操作', color: '#e8963e' },
    { id: 'economy', label: '资源经济', color: '#e8963e' },
    { id: 'tempo', label: '时间节奏', color: '#e8963e' },
    { id: 'growth', label: '成长反馈', color: '#54ad72' },
    { id: 'content', label: '内容收集', color: '#d765a4' },
    { id: 'staff', label: '员工岗位', color: '#8a70d6' },
    { id: 'commercial', label: '商业化', color: '#42aeb2' }
  ];

  const PROJECT_COLOR_DEFAULTS = {
    primary: '#1f6feb',
    bg: '#f5f6f8',
    surface: '#ffffff',
    resource: '#4b9eea',
    system: '#e8963e',
    output: '#54ad72',
    content: '#d765a4',
    staff: '#8a70d6',
    commercial: '#42aeb2'
  };

  const PROJECT_COLOR_VARIABLES = {
    primary: '--primary',
    bg: '--bg',
    surface: '--surface',
    resource: '--resource',
    system: '--system',
    output: '--output',
    content: '--content',
    staff: '--staff',
    commercial: '--commercial'
  };

  const SHORTCUT_STORAGE_KEY = 'relationship-graph-editor:shortcuts';
  const ONBOARDING_STORAGE_KEY = 'relationship-graph-editor:onboarding-seen';
  const SHORTCUT_DEFINITIONS = [
    { id: 'search', label: '搜索', description: '聚焦节点、分组和关系搜索', defaultValue: 'Control+f' },
    { id: 'locate', label: '定位所选', description: '将所选内容移到画布中央', defaultValue: 'l' },
    { id: 'fit', label: '适合画布', description: '显示完整画布', defaultValue: '0' },
    { id: 'toggleMode', label: '切换查看/编辑', description: '在两种工作模式间切换', defaultValue: 'e' },
    { id: 'addNode', label: '新增节点', description: '在当前视窗中心创建节点', defaultValue: 'n' },
    { id: 'addGroup', label: '新增分组', description: '在当前视窗中心创建分组', defaultValue: 'g' },
    { id: 'addRelation', label: '新增关系', description: '用表单选择起点和终点创建关系', defaultValue: 'r' },
    { id: 'editLabel', label: '编辑名称', description: '聚焦当前所选项的名称输入框', defaultValue: 'F2' },
    { id: 'copy', label: '复制所选', description: '复制节点、分组或关系', defaultValue: 'Control+c' },
    { id: 'paste', label: '粘贴', description: '粘贴并自动进入编辑模式', defaultValue: 'Control+v' },
    { id: 'selectAll', label: '全选节点', description: '选择当前项目所有节点', defaultValue: 'Control+a' },
    { id: 'delete', label: '删除所选', description: '在编辑模式删除当前选择', defaultValue: 'Delete' },
    { id: 'undo', label: '撤销', description: '撤销最近一次编辑', defaultValue: 'Control+z' },
    { id: 'redo', label: '重做', description: '恢复最近一次撤销', defaultValue: 'Control+y' },
    { id: 'toggleEdges', label: '显示/隐藏关系', description: '切换未聚焦关系线的显示', defaultValue: 'h' },
    { id: 'zoomIn', label: '放大画布', description: '以画布中心放大', defaultValue: ']' },
    { id: 'zoomOut', label: '缩小画布', description: '以画布中心缩小', defaultValue: '[' }
  ];

  const CATEGORY_VIEWS = {
    all: new Set(['core', 'economy', 'tempo', 'growth', 'content', 'staff', 'commercial']),
    economy: new Set(['core', 'economy', 'tempo', 'growth']),
    content: new Set(['content']),
    staff: new Set(['staff']),
    commercial: new Set(['commercial'])
  };

  const EDGE_COLORS = {
    core: 'var(--system)',
    economy: 'var(--system)',
    tempo: 'var(--system)',
    growth: 'var(--output)',
    content: 'var(--content)',
    staff: 'var(--staff)',
    commercial: 'var(--commercial)'
  };

  const dom = {
    appTitle: document.getElementById('appTitle'),
    graphTitle: document.getElementById('graphTitle'),
    graphDescription: document.getElementById('graphDescription'),
    saveState: document.getElementById('saveState'),
    projectSelect: document.getElementById('projectSelect'),
    projectNameInput: document.getElementById('projectNameInput'),
    renameProjectButton: document.getElementById('renameProjectButton'),
    newProjectButton: document.getElementById('newProjectButton'),
    duplicateProjectButton: document.getElementById('duplicateProjectButton'),
    recoveryButton: document.getElementById('recoveryButton'),
    projectSettingsButton: document.getElementById('projectSettingsButton'),
    shortcutsButton: document.getElementById('shortcutsButton'),
    helpButton: document.getElementById('helpButton'),
    deleteProjectButton: document.getElementById('deleteProjectButton'),
    projectMenu: document.getElementById('projectMenu'),
    projectStorageState: document.getElementById('projectStorageState'),
    graphSearch: document.getElementById('graphSearch'),
    graphSearchInput: document.getElementById('graphSearchInput'),
    graphSearchResults: document.getElementById('graphSearchResults'),
    locateSelectionButton: document.getElementById('locateSelectionButton'),
    recoveryDialog: document.getElementById('recoveryDialog'),
    closeRecoveryButton: document.getElementById('closeRecoveryButton'),
    recoveryList: document.getElementById('recoveryList'),
    projectSettingsDialog: document.getElementById('projectSettingsDialog'),
    projectSettingsForm: document.getElementById('projectSettingsForm'),
    closeProjectSettingsButton: document.getElementById('closeProjectSettingsButton'),
    cancelProjectSettingsButton: document.getElementById('cancelProjectSettingsButton'),
    canvasWidthInput: document.getElementById('canvasWidthInput'),
    canvasHeightInput: document.getElementById('canvasHeightInput'),
    projectThemeInput: document.getElementById('projectThemeInput'),
    customColorsToggle: document.getElementById('customColorsToggle'),
    colorSettingsFieldset: document.getElementById('colorSettingsFieldset'),
    colorPrimaryInput: document.getElementById('colorPrimaryInput'),
    colorBgInput: document.getElementById('colorBgInput'),
    colorSurfaceInput: document.getElementById('colorSurfaceInput'),
    colorResourceInput: document.getElementById('colorResourceInput'),
    colorSystemInput: document.getElementById('colorSystemInput'),
    colorOutputInput: document.getElementById('colorOutputInput'),
    colorContentInput: document.getElementById('colorContentInput'),
    colorStaffInput: document.getElementById('colorStaffInput'),
    colorCommercialInput: document.getElementById('colorCommercialInput'),
    addRelationTypeButton: document.getElementById('addRelationTypeButton'),
    relationTypeEditor: document.getElementById('relationTypeEditor'),
    createRelationDialog: document.getElementById('createRelationDialog'),
    createRelationForm: document.getElementById('createRelationForm'),
    closeCreateRelationButton: document.getElementById('closeCreateRelationButton'),
    cancelCreateRelationButton: document.getElementById('cancelCreateRelationButton'),
    createRelationSourceInput: document.getElementById('createRelationSourceInput'),
    createRelationTargetInput: document.getElementById('createRelationTargetInput'),
    createRelationLabelInput: document.getElementById('createRelationLabelInput'),
    createRelationCategoryInput: document.getElementById('createRelationCategoryInput'),
    importPreviewDialog: document.getElementById('importPreviewDialog'),
    closeImportPreviewButton: document.getElementById('closeImportPreviewButton'),
    cancelImportButton: document.getElementById('cancelImportButton'),
    confirmImportButton: document.getElementById('confirmImportButton'),
    importPreviewSummary: document.getElementById('importPreviewSummary'),
    importPreviewStats: document.getElementById('importPreviewStats'),
    importErrorCount: document.getElementById('importErrorCount'),
    importWarningCount: document.getElementById('importWarningCount'),
    importErrorList: document.getElementById('importErrorList'),
    importWarningList: document.getElementById('importWarningList'),
    shortcutsDialog: document.getElementById('shortcutsDialog'),
    closeShortcutsButton: document.getElementById('closeShortcutsButton'),
    restoreShortcutsButton: document.getElementById('restoreShortcutsButton'),
    saveShortcutsButton: document.getElementById('saveShortcutsButton'),
    shortcutEditor: document.getElementById('shortcutEditor'),
    onboardingDialog: document.getElementById('onboardingDialog'),
    closeOnboardingButton: document.getElementById('closeOnboardingButton'),
    onboardingProgress: document.getElementById('onboardingProgress'),
    previousOnboardingButton: document.getElementById('previousOnboardingButton'),
    nextOnboardingButton: document.getElementById('nextOnboardingButton'),
    finishOnboardingButton: document.getElementById('finishOnboardingButton'),
    viewModeButton: document.getElementById('viewModeButton'),
    editModeButton: document.getElementById('editModeButton'),
    undoButton: document.getElementById('undoButton'),
    redoButton: document.getElementById('redoButton'),
    exportMenu: document.getElementById('exportMenu'),
    exportJsonButton: document.getElementById('exportJsonButton'),
    exportSvgButton: document.getElementById('exportSvgButton'),
    exportPngButton: document.getElementById('exportPngButton'),
    exportPdfButton: document.getElementById('exportPdfButton'),
    importJsonButton: document.getElementById('importJsonButton'),
    exportReadonlyButton: document.getElementById('exportReadonlyButton'),
    restoreButton: document.getElementById('restoreButton'),
    importFileInput: document.getElementById('importFileInput'),
    categoryFilter: document.getElementById('categoryFilter'),
    directionFilter: document.getElementById('directionFilter'),
    depthFilter: document.getElementById('depthFilter'),
    edgeVisibilityToggle: document.getElementById('edgeVisibilityToggle'),
    zoomOutButton: document.getElementById('zoomOutButton'),
    zoomInButton: document.getElementById('zoomInButton'),
    fitButton: document.getElementById('fitButton'),
    autoLayoutButton: document.getElementById('autoLayoutButton'),
    addGroupButton: document.getElementById('addGroupButton'),
    addNodeButton: document.getElementById('addNodeButton'),
    createRelationButton: document.getElementById('createRelationButton'),
    duplicateNodeButton: document.getElementById('duplicateNodeButton'),
    copySelectionButton: document.getElementById('copySelectionButton'),
    pasteSelectionButton: document.getElementById('pasteSelectionButton'),
    snapToggle: document.getElementById('snapToggle'),
    arrangeAction: document.getElementById('arrangeAction'),
    editToolsMenu: document.getElementById('editToolsMenu'),
    applyArrangeButton: document.getElementById('applyArrangeButton'),
    layerAction: document.getElementById('layerAction'),
    applyLayerButton: document.getElementById('applyLayerButton'),
    deleteSelectionButton: document.getElementById('deleteSelectionButton'),
    canvasPanel: document.querySelector('.canvas-panel'),
    svg: document.getElementById('graphCanvas'),
    defs: document.getElementById('graphDefs'),
    groupLayer: document.getElementById('groupLayer'),
    edgeLayer: document.getElementById('edgeLayer'),
    edgeGapLayer: document.getElementById('edgeGapLayer'),
    edgeLabelLayer: document.getElementById('edgeLabelLayer'),
    linkPreviewLayer: document.getElementById('linkPreviewLayer'),
    nodeLayer: document.getElementById('nodeLayer'),
    groupHandleLayer: document.getElementById('groupHandleLayer'),
    alignmentGuideLayer: document.getElementById('alignmentGuideLayer'),
    selectionBoxLayer: document.getElementById('selectionBoxLayer'),
    canvasHint: document.getElementById('canvasHint'),
    canvasCounts: document.getElementById('canvasCounts'),
    routeState: document.getElementById('routeState'),
    minimapCanvas: document.getElementById('minimapCanvas'),
    newEdgeStraightInput: document.getElementById('newEdgeStraightInput'),
    newEdgePolylineInput: document.getElementById('newEdgePolylineInput'),
    newEdgeCurveInput: document.getElementById('newEdgeCurveInput'),
    emptyInspector: document.getElementById('emptyInspector'),
    batchInspector: document.getElementById('batchInspector'),
    batchInspectorTitle: document.getElementById('batchInspectorTitle'),
    batchSelectionCount: document.getElementById('batchSelectionCount'),
    batchKindInput: document.getElementById('batchKindInput'),
    batchGroupInput: document.getElementById('batchGroupInput'),
    nodeInspector: document.getElementById('nodeInspector'),
    nodeInspectorTitle: document.getElementById('nodeInspectorTitle'),
    nodeTypeChip: document.getElementById('nodeTypeChip'),
    nodeNoteView: document.getElementById('nodeNoteView'),
    nodeLabelInput: document.getElementById('nodeLabelInput'),
    nodeKindInput: document.getElementById('nodeKindInput'),
    nodeTypeSuggestions: document.getElementById('nodeTypeSuggestions'),
    nodeGroupInput: document.getElementById('nodeGroupInput'),
    nodeNoteInput: document.getElementById('nodeNoteInput'),
    incomingCount: document.getElementById('incomingCount'),
    outgoingCount: document.getElementById('outgoingCount'),
    incomingList: document.getElementById('incomingList'),
    outgoingList: document.getElementById('outgoingList'),
    groupInspector: document.getElementById('groupInspector'),
    groupInspectorTitle: document.getElementById('groupInspectorTitle'),
    groupLabelInput: document.getElementById('groupLabelInput'),
    groupIncomingCount: document.getElementById('groupIncomingCount'),
    groupOutgoingCount: document.getElementById('groupOutgoingCount'),
    groupIncomingList: document.getElementById('groupIncomingList'),
    groupOutgoingList: document.getElementById('groupOutgoingList'),
    edgeInspector: document.getElementById('edgeInspector'),
    edgeInspectorTitle: document.getElementById('edgeInspectorTitle'),
    edgeTypeChip: document.getElementById('edgeTypeChip'),
    edgeSourceView: document.getElementById('edgeSourceView'),
    edgeTargetView: document.getElementById('edgeTargetView'),
    edgeSourceInput: document.getElementById('edgeSourceInput'),
    edgeTargetInput: document.getElementById('edgeTargetInput'),
    edgeLabelInput: document.getElementById('edgeLabelInput'),
    edgeCategoryInput: document.getElementById('edgeCategoryInput'),
    toastRegion: document.getElementById('toastRegion')
  };

  let graph = loadInitialGraph();
  let mode = 'view';
  let selection = { type: null, id: null };
  let selectedNodeIds = new Set();
  let history = [];
  let future = [];
  let viewport = {
    x: 0,
    y: 0,
    w: Number(graph.meta.canvasWidth) || 1440,
    h: Number(graph.meta.canvasHeight) || 1030
  };
  let dragState = null;
  let dragAnimationFrame = 0;
  let pendingDragPointer = null;
  let viewportAnimationFrame = 0;
  let groupDragState = null;
  let groupResizeState = null;
  let linkState = null;
  let panState = null;
  let minimapPanState = null;
  let selectionBoxState = null;
  let alignmentGuides = [];
  let suppressCanvasClick = false;
  let activeSearchResultIndex = -1;
  let graphRevision = 0;
  const focusedGeometryCache = new Map();
  let routingWorker = null;
  let routingWorkerUrl = '';
  let routingRequestId = 0;
  let routingPendingKey = '';
  let routingDebounceTimer = 0;
  let routingTimeoutTimer = 0;
  let routingFailureNotified = false;
  let routingPendingPayload = null;
  let activeProjectId = '';
  let projectSaveChain = Promise.resolve();
  let projectStoreReady = false;
  let editingRelationTypes = [];
  let pendingImport = null;
  let shortcuts = loadShortcuts();
  let editingShortcuts = { ...shortcuts };
  let capturingShortcutAction = '';
  let onboardingStep = 0;
  let desktopHeartbeatTimer = 0;
  let indexedGraph = null;
  let indexedNodes = null;
  let indexedGroups = null;
  let indexedNodeCount = -1;
  let indexedGroupCount = -1;
  let nodeIndex = new Map();
  let groupIndex = new Map();

  initialize().catch(error => {
    console.error(error);
    updateSaveState('初始化失败');
    showToast(`编辑器初始化失败：${error.message}`, true);
  });

  async function initialize() {
    initializeDesktopHeartbeat();
    await initializeProjectStore();
    initializeRoutingWorker();
    applyProjectAppearance();
    buildMarkers();
    populateGroupOptions();
    populateNodeSelects();
    populateRelationTypeOptions();
    bindEvents();
    setMode('view');
    renderAll();
    updateSaveState(projectStoreReady ? '项目已安全载入' : '已自动载入');
    try {
      if (!localStorage.getItem(ONBOARDING_STORAGE_KEY)) requestAnimationFrame(() => openOnboarding());
    } catch {
      // The guide remains available from the toolbar.
    }
  }

  function initializeDesktopHeartbeat() {
    const params = new URLSearchParams(window.location.search);
    if (params.get('desktop') !== '1' || window.location.protocol !== 'http:') return;
    document.documentElement.dataset.desktop = 'true';

    const sendHeartbeat = () => {
      window.fetch('/__heartbeat', {
        method: 'POST',
        cache: 'no-store',
        keepalive: true
      }).catch(() => {
        // The launcher closes itself after the desktop window exits.
      });
    };

    sendHeartbeat();
    desktopHeartbeatTimer = window.setInterval(sendHeartbeat, 10000);
    window.addEventListener('pagehide', () => window.clearInterval(desktopHeartbeatTimer), { once: true });
  }

  function bindEvents() {
    document.addEventListener('contextmenu', preventNativeContextMenu, { capture: true });
    document.addEventListener('pointerdown', preventBrowserPointerGesture, { capture: true });
    document.addEventListener('pointermove', preventBrowserPointerGesture, { capture: true });
    document.addEventListener('pointerup', preventBrowserPointerGesture, { capture: true });
    document.addEventListener('mousedown', preventBrowserPointerGesture, { capture: true });
    document.addEventListener('mouseup', preventBrowserPointerGesture, { capture: true });
    document.addEventListener('auxclick', preventBrowserPointerGesture, { capture: true });
    document.addEventListener('dragstart', preventNativePageDrag, { capture: true });
    document.addEventListener('dragover', preventNativePageDrag, { capture: true });
    document.addEventListener('drop', preventNativePageDrag, { capture: true });
    document.addEventListener('wheel', preventBrowserPageZoom, { capture: true, passive: false });
    document.addEventListener('gesturestart', preventNativePageDrag, { capture: true });
    document.addEventListener('gesturechange', preventNativePageDrag, { capture: true });
    document.addEventListener('gestureend', preventNativePageDrag, { capture: true });
    window.addEventListener('keydown', preventBrowserShortcut, { capture: true });

    dom.viewModeButton.addEventListener('click', () => setMode('view'));
    dom.editModeButton.addEventListener('click', () => setMode('edit'));

    dom.categoryFilter.addEventListener('change', renderGraph);
    dom.directionFilter.addEventListener('change', renderGraph);
    dom.depthFilter.addEventListener('change', renderGraph);
    dom.edgeVisibilityToggle.addEventListener('change', renderGraph);

    dom.zoomOutButton.addEventListener('click', () => zoomViewport(1.22));
    dom.zoomInButton.addEventListener('click', () => zoomViewport(0.82));
    dom.fitButton.addEventListener('click', fitViewport);
    dom.locateSelectionButton.addEventListener('click', locateSelection);
    dom.graphSearchInput.addEventListener('input', renderSearchResults);
    dom.graphSearchInput.addEventListener('focus', renderSearchResults);
    dom.graphSearchInput.addEventListener('keydown', handleSearchKeydown);
    dom.minimapCanvas.addEventListener('pointerdown', handleMinimapPointerDown);
    dom.minimapCanvas.addEventListener('keydown', handleMinimapKeydown);
    dom.autoLayoutButton.addEventListener('click', autoLayout);
    dom.svg.addEventListener('wheel', handleWheel, { passive: false });
    dom.svg.addEventListener('pointerdown', handleCanvasPointerDown);
    dom.svg.addEventListener('click', handleCanvasClick);
    dom.svg.addEventListener('contextmenu', handleCanvasContextMenu);
    window.addEventListener('pointermove', handleGlobalPointerMove);
    window.addEventListener('pointerup', handleGlobalPointerUp);
    window.addEventListener('pointercancel', handleGlobalPointerUp);

    dom.addGroupButton.addEventListener('click', addGroup);
    dom.addNodeButton.addEventListener('click', addNode);
    dom.createRelationButton.addEventListener('click', openCreateRelationDialog);
    dom.duplicateNodeButton.addEventListener('click', duplicateSelectedNode);
    dom.copySelectionButton.addEventListener('click', copySelection);
    dom.pasteSelectionButton.addEventListener('click', pasteSelection);
    dom.arrangeAction.addEventListener('change', updateActionButtons);
    dom.applyArrangeButton.addEventListener('click', () => { applyArrangeAction(); dom.editToolsMenu.open = false; });
    dom.applyLayerButton.addEventListener('click', () => { applyLayerAction(); dom.editToolsMenu.open = false; });
    dom.deleteSelectionButton.addEventListener('click', deleteSelection);
    dom.undoButton.addEventListener('click', undo);
    dom.redoButton.addEventListener('click', redo);

    dom.nodeInspector.addEventListener('submit', applyNodeForm);
    dom.batchInspector.addEventListener('submit', applyBatchForm);
    dom.groupInspector.addEventListener('submit', applyGroupForm);
    dom.edgeInspector.addEventListener('submit', applyEdgeForm);
    dom.exportJsonButton.addEventListener('click', () => { dom.exportMenu.open = false; exportJson(); });
    dom.exportSvgButton.addEventListener('click', () => { dom.exportMenu.open = false; exportSvg(); });
    dom.exportPngButton.addEventListener('click', () => { dom.exportMenu.open = false; exportPng(); });
    dom.exportPdfButton.addEventListener('click', () => { dom.exportMenu.open = false; exportPdf(); });
    dom.importJsonButton.addEventListener('click', () => dom.importFileInput.click());
    dom.importFileInput.addEventListener('change', importGraphFile);
    dom.restoreButton.addEventListener('click', restoreDefault);
    dom.exportReadonlyButton.addEventListener('click', () => { dom.exportMenu.open = false; exportReadonly(); });

    dom.projectSelect.addEventListener('change', handleProjectChange);
    dom.renameProjectButton.addEventListener('click', renameCurrentProject);
    dom.newProjectButton.addEventListener('click', createNewProject);
    dom.duplicateProjectButton.addEventListener('click', duplicateCurrentProject);
    dom.deleteProjectButton.addEventListener('click', deleteCurrentProject);
    dom.recoveryButton.addEventListener('click', openRecoveryDialog);
    dom.projectSettingsButton.addEventListener('click', openProjectSettings);
    dom.shortcutsButton.addEventListener('click', openShortcutsDialog);
    dom.helpButton.addEventListener('click', openOnboarding);
    dom.projectMenu.addEventListener('click', event => {
      if (event.target.closest('button')) dom.projectMenu.open = false;
    });
    dom.closeRecoveryButton.addEventListener('click', () => dom.recoveryDialog.close());
    dom.recoveryDialog.addEventListener('click', event => {
      if (event.target === dom.recoveryDialog) dom.recoveryDialog.close();
    });
    dom.closeProjectSettingsButton.addEventListener('click', () => dom.projectSettingsDialog.close());
    dom.cancelProjectSettingsButton.addEventListener('click', () => dom.projectSettingsDialog.close());
    dom.projectSettingsDialog.addEventListener('click', event => {
      if (event.target === dom.projectSettingsDialog) dom.projectSettingsDialog.close();
    });
    dom.projectSettingsForm.addEventListener('submit', applyProjectSettings);
    dom.customColorsToggle.addEventListener('change', () => {
      dom.colorSettingsFieldset.disabled = !dom.customColorsToggle.checked;
    });
    dom.addRelationTypeButton.addEventListener('click', addRelationTypeRow);
    dom.createRelationForm.addEventListener('submit', createRelationFromDialog);
    dom.closeCreateRelationButton.addEventListener('click', () => dom.createRelationDialog.close());
    dom.cancelCreateRelationButton.addEventListener('click', () => dom.createRelationDialog.close());
    dom.createRelationDialog.addEventListener('click', event => {
      if (event.target === dom.createRelationDialog) dom.createRelationDialog.close();
    });
    dom.createRelationSourceInput.addEventListener('change', keepRelationEndpointsDistinct);
    dom.closeImportPreviewButton.addEventListener('click', closeImportPreview);
    dom.cancelImportButton.addEventListener('click', closeImportPreview);
    dom.confirmImportButton.addEventListener('click', confirmImport);
    dom.importPreviewDialog.addEventListener('click', event => {
      if (event.target === dom.importPreviewDialog) closeImportPreview();
    });
    dom.closeShortcutsButton.addEventListener('click', () => dom.shortcutsDialog.close());
    dom.restoreShortcutsButton.addEventListener('click', restoreDefaultShortcuts);
    dom.saveShortcutsButton.addEventListener('click', saveShortcuts);
    dom.shortcutsDialog.addEventListener('click', event => {
      if (event.target === dom.shortcutsDialog) dom.shortcutsDialog.close();
    });
    dom.closeOnboardingButton.addEventListener('click', finishOnboarding);
    dom.previousOnboardingButton.addEventListener('click', () => setOnboardingStep(onboardingStep - 1));
    dom.nextOnboardingButton.addEventListener('click', () => setOnboardingStep(onboardingStep + 1));
    dom.finishOnboardingButton.addEventListener('click', finishOnboarding);

    document.addEventListener('pointerdown', event => {
      if (!dom.graphSearch.contains(event.target)) closeSearchResults();
      if (!dom.exportMenu.contains(event.target)) dom.exportMenu.open = false;
      if (!dom.editToolsMenu.contains(event.target)) dom.editToolsMenu.open = false;
      if (!dom.projectMenu.contains(event.target)) dom.projectMenu.open = false;
    });

    window.addEventListener('keydown', handleKeyboardShortcuts);
    window.addEventListener('beforeunload', destroyRoutingWorker, { once: true });
  }

  function preventNativeContextMenu(event) {
    event.preventDefault();
  }

  function preventBrowserPointerGesture(event) {
    const button = Number(event.button);
    const buttons = Number(event.buttons);
    const blockedButton = button === 1 || button === 2 || button === 3 || button === 4;
    const blockedButtonsHeld = Boolean(buttons & (2 | 4 | 8 | 16));
    if (!blockedButton && !blockedButtonsHeld) return;
    event.preventDefault();
    if (button !== 2 && !(buttons & 2)) event.stopPropagation();
    if (event.type === 'pointermove' && blockedButtonsHeld) event.stopPropagation();
  }

  function preventNativePageDrag(event) {
    event.preventDefault();
  }

  function preventBrowserPageZoom(event) {
    if (!event.ctrlKey && !event.metaKey) return;
    event.preventDefault();
  }

  function preventBrowserShortcut(event) {
    const key = String(event.key || '').toLowerCase();
    const modified = event.ctrlKey || event.metaKey;
    const browserNavigation = key === 'browserback' || key === 'browserforward' || key === 'browserrefresh' ||
      (event.altKey && (key === 'arrowleft' || key === 'arrowright' || key === 'home'));
    const browserFunctionKey = key === 'f5' || key === 'f12';
    const browserCommand = modified && ['l', 'n', 'o', 'p', 'r', 's', 't', 'u', 'w'].includes(key);
    const developerCommand = modified && event.shiftKey && ['c', 'i', 'j'].includes(key);
    if (!browserNavigation && !browserFunctionKey && !browserCommand && !developerCommand) return;
    event.preventDefault();
    event.stopImmediatePropagation();
  }

  function deepClone(value) {
    return JSON.parse(JSON.stringify(value));
  }

  async function initializeProjectStore() {
    if (!window.GraphProjectStore) {
      dom.projectStorageState.textContent = '兼容存储';
      return;
    }
    try {
      const project = await window.GraphProjectStore.initialize(graph);
      if (project?.graph) graph = normalizeAndValidateGraph(project.graph);
      activeProjectId = project?.id || window.GraphProjectStore.getActiveProjectId();
      projectStoreReady = Boolean(activeProjectId);
      await refreshProjectOptions();
      syncProjectHeader();
      dom.projectStorageState.textContent = window.GraphProjectStore.fallback
        ? '兼容存储模式'
        : '自动保存与恢复已启用';
      try {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(graph));
      } catch {
        // IndexedDB remains the authoritative store.
      }
    } catch (error) {
      projectStoreReady = false;
      dom.projectStorageState.textContent = '项目存储不可用';
      showToast(`项目存储初始化失败：${error.message}`, true);
    }
  }

  function createBlankGraph(name) {
    return normalizeAndValidateGraph({
      version: GRAPH_SCHEMA_VERSION,
      meta: {
        title: name || '未命名关系图',
        canvasWidth: 1440,
        canvasHeight: 1030,
        updatedAt: new Date().toISOString()
      },
      settings: normalizeProjectSettings(),
      groups: [],
      nodes: [],
      edges: []
    });
  }

  function syncProjectHeader() {
    const title = graph.meta?.title || '未命名关系图';
    dom.appTitle.textContent = title;
    dom.graphTitle.textContent = title;
    dom.graphDescription.textContent = `${title}，包含 ${graph.groups.length} 个分组、${graph.nodes.length} 个节点和 ${graph.edges.length} 条关系。`;
    document.title = `关系图编辑器｜${title}`;
    dom.projectNameInput.value = title;
    if (activeProjectId) dom.projectSelect.value = activeProjectId;
  }

  async function refreshProjectOptions() {
    if (!projectStoreReady && !activeProjectId) return;
    const projects = await window.GraphProjectStore.listProjects();
    dom.projectSelect.replaceChildren();
    projects.forEach(project => {
      const option = document.createElement('option');
      option.value = project.id;
      option.textContent = project.name;
      dom.projectSelect.appendChild(option);
    });
    dom.projectSelect.value = activeProjectId;
    dom.deleteProjectButton.disabled = projects.length <= 1;
  }

  function resetEditorForGraph(nextGraph) {
    cancelScheduledNodeDrag();
    graph = normalizeAndValidateGraph(nextGraph);
    history = [];
    future = [];
    selection = { type: null, id: null };
    selectedNodeIds = new Set();
    dragState = null;
    groupDragState = null;
    groupResizeState = null;
    linkState = null;
    panState = null;
    minimapPanState = null;
    selectionBoxState = null;
    alignmentGuides = [];
    dom.graphSearchInput.value = '';
    closeSearchResults();
    viewport = {
      x: 0,
      y: 0,
      w: Number(graph.meta.canvasWidth) || 1440,
      h: Number(graph.meta.canvasHeight) || 1030
    };
    invalidateFocusedGeometry();
    applyProjectAppearance();
    buildMarkers();
    populateGroupOptions();
    populateNodeSelects();
    populateRelationTypeOptions();
    renderAll();
    syncProjectHeader();
    updateHistoryButtons();
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(graph));
    } catch {
      // The project database is still authoritative.
    }
  }

  async function handleProjectChange() {
    const projectId = dom.projectSelect.value;
    if (!projectId || projectId === activeProjectId) return;
    await projectSaveChain;
    try {
      const project = await window.GraphProjectStore.getProject(projectId);
      if (!project) throw new Error('所选项目不存在。');
      activeProjectId = project.id;
      window.GraphProjectStore.setActiveProjectId(activeProjectId);
      resetEditorForGraph(project.graph);
      updateSaveState('项目已切换');
      showToast(`已打开“${project.name}”`);
    } catch (error) {
      dom.projectSelect.value = activeProjectId;
      showToast(`无法切换项目：${error.message}`, true);
    }
  }

  async function createNewProject() {
    if (!projectStoreReady) return;
    await projectSaveChain;
    try {
      const projects = await window.GraphProjectStore.listProjects();
      let index = 1;
      const names = new Set(projects.map(project => project.name));
      while (names.has(`未命名关系图 ${index}`)) index += 1;
      const name = `未命名关系图 ${index}`;
      const project = await window.GraphProjectStore.createProject(name, createBlankGraph(name));
      activeProjectId = project.id;
      window.GraphProjectStore.setActiveProjectId(activeProjectId);
      resetEditorForGraph(project.graph);
      await window.GraphProjectStore.createSnapshot(activeProjectId, graph, '创建新项目');
      await refreshProjectOptions();
      showToast('新项目已创建');
      requestAnimationFrame(() => {
        dom.projectNameInput.focus();
        dom.projectNameInput.select();
      });
    } catch (error) {
      showToast(`新建项目失败：${error.message}`, true);
    }
  }

  async function duplicateCurrentProject() {
    if (!projectStoreReady || !activeProjectId) return;
    await projectSaveChain;
    try {
      const project = await window.GraphProjectStore.duplicateProject(activeProjectId);
      activeProjectId = project.id;
      window.GraphProjectStore.setActiveProjectId(activeProjectId);
      resetEditorForGraph(project.graph);
      await refreshProjectOptions();
      showToast('项目副本已创建，可用于跨项目编辑');
    } catch (error) {
      showToast(`复制项目失败：${error.message}`, true);
    }
  }

  async function renameCurrentProject() {
    if (!projectStoreReady || !activeProjectId) return;
    const name = dom.projectNameInput.value.trim();
    if (!name) {
      showToast('项目名称不能为空。', true);
      dom.projectNameInput.focus();
      return;
    }
    await projectSaveChain;
    try {
      const project = await window.GraphProjectStore.renameProject(activeProjectId, name);
      resetEditorForGraph(project.graph);
      await refreshProjectOptions();
      showToast('项目已重命名');
    } catch (error) {
      showToast(`重命名失败：${error.message}`, true);
    }
  }

  async function deleteCurrentProject() {
    if (!projectStoreReady || !activeProjectId) return;
    await projectSaveChain;
    try {
      const nextProject = await window.GraphProjectStore.deleteProject(activeProjectId);
      activeProjectId = nextProject.id;
      resetEditorForGraph(nextProject.graph);
      await refreshProjectOptions();
      showToast('项目已删除');
    } catch (error) {
      showToast(`删除项目失败：${error.message}`, true);
    }
  }

  async function openRecoveryDialog() {
    if (!projectStoreReady || !activeProjectId) return;
    await projectSaveChain;
    dom.recoveryList.replaceChildren();
    const loading = document.createElement('div');
    loading.className = 'recovery-empty';
    loading.textContent = '正在读取恢复记录…';
    dom.recoveryList.appendChild(loading);
    dom.recoveryDialog.showModal();
    try {
      const snapshots = await window.GraphProjectStore.listSnapshots(activeProjectId);
      dom.recoveryList.replaceChildren();
      if (!snapshots.length) {
        const empty = document.createElement('div');
        empty.className = 'recovery-empty';
        empty.textContent = window.GraphProjectStore.fallback
          ? '当前浏览器处于兼容存储模式，不支持恢复快照。'
          : '还没有恢复记录。完成一次编辑后会自动生成。';
        dom.recoveryList.appendChild(empty);
        return;
      }
      snapshots.forEach(snapshot => {
        const row = document.createElement('div');
        row.className = 'recovery-row';
        const copy = document.createElement('div');
        const title = document.createElement('strong');
        title.textContent = snapshot.reason || '自动保存';
        const time = document.createElement('span');
        time.textContent = new Date(snapshot.createdAt).toLocaleString('zh-CN');
        copy.append(title, time);
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'button';
        button.textContent = '恢复到此处';
        button.addEventListener('click', () => restoreProjectSnapshot(snapshot.id));
        row.append(copy, button);
        dom.recoveryList.appendChild(row);
      });
    } catch (error) {
      dom.recoveryList.textContent = `读取失败：${error.message}`;
    }
  }

  async function restoreProjectSnapshot(snapshotId) {
    if (!window.confirm('恢复后，当前状态仍会自动备份。是否继续？')) return;
    await projectSaveChain;
    try {
      const project = await window.GraphProjectStore.restoreSnapshot(snapshotId);
      activeProjectId = project.id;
      resetEditorForGraph(project.graph);
      await refreshProjectOptions();
      dom.recoveryDialog.close();
      showToast('项目已恢复，恢复前状态已保留');
    } catch (error) {
      showToast(`恢复失败：${error.message}`, true);
    }
  }

  function getRelationTypes() {
    return graph.settings?.relationTypes?.length
      ? graph.settings.relationTypes
      : DEFAULT_RELATION_TYPES;
  }

  function getEdgeCategoryName(categoryId) {
    return getRelationTypes().find(type => type.id === categoryId)?.label || categoryId || '未分类';
  }

  function getEdgeCategoryColor(categoryId) {
    return getRelationTypes().find(type => type.id === categoryId)?.color || EDGE_COLORS[categoryId] || '#e8963e';
  }

  function getVisibleCategories(viewId) {
    const available = new Set(getRelationTypes().map(type => type.id));
    if (viewId === 'all') return available;
    if (viewId.startsWith('type:')) {
      const relationTypeId = viewId.slice(5);
      return available.has(relationTypeId) ? new Set([relationTypeId]) : available;
    }
    const preset = CATEGORY_VIEWS[viewId] || CATEGORY_VIEWS.all;
    return new Set([...preset].filter(category => available.has(category)));
  }

  function populateCategoryFilterOptions() {
    const current = dom.categoryFilter.value;
    const available = new Set(getRelationTypes().map(type => type.id));
    const presets = [
      ['all', '全部关系'],
      ['economy', '资源经济综合']
    ];
    dom.categoryFilter.replaceChildren();
    presets.forEach(([value, label]) => {
      if (value !== 'all' && ![...(CATEGORY_VIEWS[value] || [])].some(typeId => available.has(typeId))) return;
      const option = document.createElement('option');
      option.value = value;
      option.textContent = label;
      dom.categoryFilter.appendChild(option);
    });
    const relationGroup = document.createElement('optgroup');
    relationGroup.label = '按单一关系类型';
    getRelationTypes().forEach(type => {
      const option = document.createElement('option');
      option.value = `type:${type.id}`;
      option.textContent = type.label;
      relationGroup.appendChild(option);
    });
    dom.categoryFilter.appendChild(relationGroup);
    dom.categoryFilter.value = Array.from(dom.categoryFilter.options).some(option => option.value === current)
      ? current
      : 'all';
  }

  function populateRelationTypeOptions() {
    const current = dom.edgeCategoryInput.value;
    const createCurrent = dom.createRelationCategoryInput.value;
    dom.edgeCategoryInput.replaceChildren();
    dom.createRelationCategoryInput.replaceChildren();
    getRelationTypes().forEach(type => {
      const option = document.createElement('option');
      option.value = type.id;
      option.textContent = type.label;
      dom.edgeCategoryInput.appendChild(option);
      dom.createRelationCategoryInput.appendChild(option.cloneNode(true));
    });
    if (getRelationTypes().some(type => type.id === current)) dom.edgeCategoryInput.value = current;
    if (getRelationTypes().some(type => type.id === createCurrent)) dom.createRelationCategoryInput.value = createCurrent;
    populateCategoryFilterOptions();
  }

  function getSettingsColorInputs() {
    return {
      primary: dom.colorPrimaryInput,
      bg: dom.colorBgInput,
      surface: dom.colorSurfaceInput,
      resource: dom.colorResourceInput,
      system: dom.colorSystemInput,
      output: dom.colorOutputInput,
      content: dom.colorContentInput,
      staff: dom.colorStaffInput,
      commercial: dom.colorCommercialInput
    };
  }

  function getPaletteColor(key, theme) {
    const dark = theme === 'dark' || (theme === 'system' && window.matchMedia?.('(prefers-color-scheme: dark)').matches);
    if (!dark) return PROJECT_COLOR_DEFAULTS[key];
    const darkOverrides = { primary: '#78aefa', bg: '#11151b', surface: '#191f28' };
    return darkOverrides[key] || PROJECT_COLOR_DEFAULTS[key];
  }

  function applyProjectAppearance() {
    const settings = normalizeProjectSettings(graph.settings);
    graph.settings = settings;
    if (settings.theme === 'system') document.documentElement.removeAttribute('data-theme');
    else document.documentElement.dataset.theme = settings.theme;
    Object.entries(PROJECT_COLOR_VARIABLES).forEach(([key, variable]) => {
      const color = settings.colors[key];
      if (color) document.documentElement.style.setProperty(variable, color);
      else document.documentElement.style.removeProperty(variable);
    });
  }

  function openProjectSettings() {
    const settings = normalizeProjectSettings(graph.settings);
    dom.canvasWidthInput.value = String(graph.meta.canvasWidth);
    dom.canvasHeightInput.value = String(graph.meta.canvasHeight);
    dom.projectThemeInput.value = settings.theme;
    const customColors = Object.keys(settings.colors).length > 0;
    dom.customColorsToggle.checked = customColors;
    dom.colorSettingsFieldset.disabled = !customColors;
    Object.entries(getSettingsColorInputs()).forEach(([key, input]) => {
      input.value = settings.colors[key] || getPaletteColor(key, settings.theme);
    });
    editingRelationTypes = deepClone(settings.relationTypes);
    renderRelationTypeEditor();
    dom.projectSettingsDialog.showModal();
  }

  function renderRelationTypeEditor() {
    dom.relationTypeEditor.replaceChildren();
    editingRelationTypes.forEach((type, index) => {
      const row = document.createElement('div');
      row.className = 'relation-type-row';
      row.dataset.relationTypeId = type.id;
      const labelField = document.createElement('label');
      labelField.textContent = '名称';
      const labelInput = document.createElement('input');
      labelInput.type = 'text';
      labelInput.maxLength = 30;
      labelInput.required = true;
      labelInput.value = type.label;
      labelInput.addEventListener('input', () => { editingRelationTypes[index].label = labelInput.value; });
      labelField.appendChild(labelInput);
      const colorField = document.createElement('label');
      colorField.textContent = '线条颜色';
      const colorInput = document.createElement('input');
      colorInput.type = 'color';
      colorInput.value = type.color;
      colorInput.addEventListener('input', () => { editingRelationTypes[index].color = colorInput.value; });
      colorField.appendChild(colorInput);
      const usedCount = graph.edges.filter(edge => edge.category === type.id).length;
      const action = document.createElement('div');
      const usage = document.createElement('span');
      usage.className = 'usage-count';
      usage.textContent = `${usedCount} 条关系`;
      const remove = document.createElement('button');
      remove.type = 'button';
      remove.className = 'button button-danger-quiet';
      remove.textContent = '删除';
      remove.disabled = usedCount > 0 || editingRelationTypes.length <= 1;
      remove.title = usedCount > 0 ? '请先修改正在使用此类型的关系' : '';
      remove.addEventListener('click', () => {
        editingRelationTypes.splice(index, 1);
        renderRelationTypeEditor();
      });
      action.append(usage, remove);
      row.append(labelField, colorField, action);
      dom.relationTypeEditor.appendChild(row);
    });
  }

  function addRelationTypeRow() {
    const used = new Set(editingRelationTypes.map(type => type.id));
    const id = uniqueId('custom', used);
    editingRelationTypes.push({ id, label: '新关系类型', color: '#5b8def' });
    renderRelationTypeEditor();
    requestAnimationFrame(() => {
      const input = dom.relationTypeEditor.querySelector(`[data-relation-type-id="${id}"] input[type="text"]`);
      input?.focus();
      input?.select();
    });
  }

  function applyProjectSettings(event) {
    event.preventDefault();
    const canvasWidth = Math.round(Number(dom.canvasWidthInput.value));
    const canvasHeight = Math.round(Number(dom.canvasHeightInput.value));
    if (!Number.isFinite(canvasWidth) || canvasWidth < MIN_CANVAS_WIDTH || canvasWidth > MAX_CANVAS_SIZE ||
        !Number.isFinite(canvasHeight) || canvasHeight < MIN_CANVAS_HEIGHT || canvasHeight > MAX_CANVAS_SIZE) {
      showToast(`画布尺寸需要在 ${MIN_CANVAS_WIDTH}×${MIN_CANVAS_HEIGHT} 到 ${MAX_CANVAS_SIZE}×${MAX_CANVAS_SIZE} 之间。`, true);
      return;
    }
    const relationTypes = editingRelationTypes.map(type => ({
      id: type.id,
      label: String(type.label || '').trim(),
      color: type.color
    }));
    if (relationTypes.some(type => !type.label)) {
      showToast('关系类型名称不能为空。', true);
      return;
    }
    const normalizedNames = relationTypes.map(type => type.label.toLocaleLowerCase('zh-CN'));
    if (new Set(normalizedNames).size !== normalizedNames.length) {
      showToast('关系类型名称不能重复。', true);
      return;
    }
    const colors = {};
    if (dom.customColorsToggle.checked) {
      Object.entries(getSettingsColorInputs()).forEach(([key, input]) => { colors[key] = input.value; });
    }
    commit(() => {
      graph.meta.canvasWidth = canvasWidth;
      graph.meta.canvasHeight = canvasHeight;
      graph.settings = normalizeProjectSettings({
        theme: dom.projectThemeInput.value,
        colors,
        relationTypes,
        nodeTypes: graph.settings?.nodeTypes
      });
    }, '项目设置已保存');
    applyProjectAppearance();
    buildMarkers();
    populateRelationTypeOptions();
    dom.projectSettingsDialog.close();
    fitViewport();
    renderAll();
  }

  function defaultShortcuts() {
    return Object.fromEntries(SHORTCUT_DEFINITIONS.map(definition => [definition.id, definition.defaultValue]));
  }

  function loadShortcuts() {
    const defaults = defaultShortcuts();
    try {
      const saved = JSON.parse(localStorage.getItem(SHORTCUT_STORAGE_KEY) || '{}');
      SHORTCUT_DEFINITIONS.forEach(definition => {
        if (typeof saved?.[definition.id] === 'string' && saved[definition.id]) defaults[definition.id] = saved[definition.id];
      });
    } catch {
      // Defaults remain active.
    }
    return defaults;
  }

  function shortcutFromEvent(event) {
    if (['Control', 'Shift', 'Alt', 'Meta'].includes(event.key)) return '';
    const parts = [];
    if (event.ctrlKey) parts.push('Control');
    if (event.altKey) parts.push('Alt');
    if (event.shiftKey) parts.push('Shift');
    if (event.metaKey) parts.push('Meta');
    let key = event.key;
    if (key === ' ') key = 'Space';
    else if (key.length === 1 && /[a-z]/i.test(key)) key = key.toLowerCase();
    parts.push(key);
    return parts.join('+');
  }

  function displayShortcut(value) {
    return String(value || '')
      .replace('Control', 'Ctrl')
      .replace('Meta', 'Command')
      .replace('ArrowLeft', '←')
      .replace('ArrowRight', '→')
      .replace('ArrowUp', '↑')
      .replace('ArrowDown', '↓');
  }

  function openShortcutsDialog() {
    editingShortcuts = { ...shortcuts };
    capturingShortcutAction = '';
    renderShortcutEditor();
    dom.shortcutsDialog.showModal();
  }

  function renderShortcutEditor() {
    dom.shortcutEditor.replaceChildren();
    SHORTCUT_DEFINITIONS.forEach(definition => {
      const row = document.createElement('div');
      row.className = 'shortcut-row';
      const copy = document.createElement('div');
      const title = document.createElement('strong');
      title.textContent = definition.label;
      const description = document.createElement('small');
      description.textContent = definition.description;
      copy.append(title, description);
      const capture = document.createElement('button');
      capture.type = 'button';
      capture.className = `shortcut-capture${capturingShortcutAction === definition.id ? ' is-recording' : ''}`;
      capture.textContent = capturingShortcutAction === definition.id
        ? '请按新的组合键…'
        : displayShortcut(editingShortcuts[definition.id]);
      capture.setAttribute('aria-label', `设置${definition.label}快捷键`);
      capture.addEventListener('click', () => {
        capturingShortcutAction = definition.id;
        renderShortcutEditor();
        requestAnimationFrame(() => {
          dom.shortcutEditor.querySelector('.shortcut-capture.is-recording')?.focus();
        });
      });
      capture.addEventListener('keydown', event => {
        if (capturingShortcutAction !== definition.id) return;
        event.preventDefault();
        event.stopPropagation();
        if (event.key === 'Escape') {
          capturingShortcutAction = '';
          renderShortcutEditor();
          return;
        }
        const shortcut = shortcutFromEvent(event);
        if (!shortcut) return;
        const reserved = new Set(['Control+w', 'Control+r', 'Control+l', 'Control+t', 'Control+n', 'Control+Shift+i', 'Control+Shift+j', 'Alt+F4', 'F5']);
        if (reserved.has(shortcut)) {
          showToast(`${displayShortcut(shortcut)} 是浏览器保留快捷键，请使用其他组合。`, true);
          return;
        }
        editingShortcuts[definition.id] = shortcut;
        capturingShortcutAction = '';
        renderShortcutEditor();
      });
      row.append(copy, capture);
      dom.shortcutEditor.appendChild(row);
    });
  }

  function restoreDefaultShortcuts() {
    editingShortcuts = defaultShortcuts();
    capturingShortcutAction = '';
    renderShortcutEditor();
  }

  function saveShortcuts() {
    const assigned = new Map();
    const conflicts = [];
    SHORTCUT_DEFINITIONS.forEach(definition => {
      const value = editingShortcuts[definition.id];
      if (assigned.has(value)) conflicts.push(`${definition.label} 与 ${assigned.get(value)} 都使用 ${displayShortcut(value)}`);
      else assigned.set(value, definition.label);
    });
    if (conflicts.length) {
      showToast(`快捷键冲突：${conflicts.join('；')}`, true);
      return;
    }
    shortcuts = { ...editingShortcuts };
    try {
      localStorage.setItem(SHORTCUT_STORAGE_KEY, JSON.stringify(shortcuts));
    } catch {
      showToast('快捷键本次已生效，但浏览器无法长期保存。', true);
    }
    dom.shortcutsDialog.close();
    showToast('快捷键已保存');
  }

  function openOnboarding() {
    onboardingStep = 0;
    setOnboardingStep(0);
    if (!dom.onboardingDialog.open) dom.onboardingDialog.showModal();
  }

  function setOnboardingStep(step) {
    const steps = Array.from(dom.onboardingDialog.querySelectorAll('.onboarding-step'));
    onboardingStep = Math.max(0, Math.min(steps.length - 1, step));
    steps.forEach((element, index) => { element.hidden = index !== onboardingStep; });
    dom.onboardingProgress.style.setProperty('--onboarding-progress', `${(onboardingStep + 1) / steps.length * 100}%`);
    dom.onboardingProgress.textContent = `第 ${onboardingStep + 1} 步，共 ${steps.length} 步`;
    dom.previousOnboardingButton.disabled = onboardingStep === 0;
    dom.nextOnboardingButton.hidden = onboardingStep === steps.length - 1;
    dom.finishOnboardingButton.hidden = onboardingStep !== steps.length - 1;
  }

  function finishOnboarding() {
    try {
      localStorage.setItem(ONBOARDING_STORAGE_KEY, '1');
    } catch {
      // The guide can still be opened manually.
    }
    dom.onboardingDialog.close();
  }

  function loadInitialGraph() {
    try {
      const saved = localStorage.getItem(STORAGE_KEY);
      if (saved) {
        return normalizeAndValidateGraph(JSON.parse(saved));
      }
    } catch (error) {
      console.warn('无法读取浏览器自动保存，已载入初始数据。', error);
    }
    return normalizeAndValidateGraph(deepClone(window.DEFAULT_GRAPH));
  }

  function normalizeAndValidateGraph(raw) {
    if (!raw || !Array.isArray(raw.nodes) || !Array.isArray(raw.edges) || !Array.isArray(raw.groups)) {
      throw new Error('文件缺少 groups、nodes 或 edges 数据。');
    }
    if (raw.groups.length > MAX_GRAPH_GROUPS) throw new Error(`分组数量不能超过 ${MAX_GRAPH_GROUPS} 个。`);
    if (raw.nodes.length > MAX_GRAPH_NODES) throw new Error(`节点数量不能超过 ${MAX_GRAPH_NODES} 个。`);
    if (raw.edges.length > MAX_GRAPH_EDGES) throw new Error(`关系数量不能超过 ${MAX_GRAPH_EDGES} 条。`);

    const incomingVersion = Math.max(1, Math.floor(finiteNumber(raw.version, 1)));
    if (incomingVersion > GRAPH_SCHEMA_VERSION) {
      throw new Error(`文件版本 ${incomingVersion} 高于当前支持的版本 ${GRAPH_SCHEMA_VERSION}。`);
    }

    const normalized = deepClone(raw);
    normalized.version = GRAPH_SCHEMA_VERSION;
    normalized.meta = {
      title: boundedText(normalized.meta?.title, '未命名关系图', 80),
      canvasWidth: Math.min(MAX_CANVAS_SIZE, Math.max(MIN_CANVAS_WIDTH, finiteNumber(normalized.meta?.canvasWidth, 1440))),
      canvasHeight: Math.min(MAX_CANVAS_SIZE, Math.max(MIN_CANVAS_HEIGHT, finiteNumber(normalized.meta?.canvasHeight, 1030))),
      updatedAt: normalized.meta?.updatedAt || new Date().toISOString()
    };
    normalized.settings = normalizeProjectSettings(normalized.settings);

    const groupIds = new Set();
    normalized.groups = normalized.groups.map((group, index) => {
      const id = String(group.id || `group_${index + 1}`);
      if (id.length > MAX_ENTITY_ID_LENGTH) throw new Error(`分组ID过长：${id.slice(0, 30)}…`);
      if (groupIds.has(id)) throw new Error(`分组ID重复：${id}`);
      groupIds.add(id);
      const w = Math.min(normalized.meta.canvasWidth, Math.max(120, finiteNumber(group.w, 240)));
      const h = Math.min(normalized.meta.canvasHeight, Math.max(100, finiteNumber(group.h, 300)));
      return {
        id,
        label: boundedText(group.label, `分组${index + 1}`, 40),
        x: Math.max(0, Math.min(normalized.meta.canvasWidth - w, finiteNumber(group.x, 20))),
        y: Math.max(0, Math.min(normalized.meta.canvasHeight - h, finiteNumber(group.y, 55))),
        w,
        h
      };
    });

    const nodeIds = new Set();
    normalized.nodes = normalized.nodes.map((node, index) => {
      const id = String(node.id || `node_${index + 1}`);
      if (id.length > MAX_ENTITY_ID_LENGTH) throw new Error(`节点ID过长：${id.slice(0, 30)}…`);
      if (nodeIds.has(id)) throw new Error(`节点ID重复：${id}`);
      nodeIds.add(id);
      const preferredKind = KIND_NAMES[node.kind] ? node.kind : '';
      const legacyType = preferredKind ? KIND_NAMES[preferredKind] : '';
      const type = normalizeNodeTypeLabel(node.type ?? node.typeLabel ?? legacyType);
      const typeDefinition = ensureNodeTypeInSettings(normalized.settings, type, preferredKind);
      const kind = typeDefinition.kind;
      const group = groupIds.has(String(node.group || '')) ? String(node.group) : '';
      const w = Math.min(normalized.meta.canvasWidth, Math.max(105, finiteNumber(node.w, 150)));
      const h = Math.min(normalized.meta.canvasHeight, Math.max(46, finiteNumber(node.h, 54)));
      return {
        id,
        label: boundedText(node.label, `节点${index + 1}`, 40),
        type: typeDefinition.label,
        kind,
        group,
        x: Math.max(0, Math.min(normalized.meta.canvasWidth - w, finiteNumber(node.x, 40))),
        y: Math.max(0, Math.min(normalized.meta.canvasHeight - h, finiteNumber(node.y, 80))),
        w,
        h,
        note: boundedText(node.note, '', 300)
      };
    });

    const edgeIds = new Set();
    const edgePairs = new Set();
    normalized.edges = normalized.edges.map((edge, index) => {
      const id = String(edge.id || `edge_${index + 1}`);
      if (id.length > MAX_ENTITY_ID_LENGTH) throw new Error(`关系ID过长：${id.slice(0, 30)}…`);
      if (edgeIds.has(id)) throw new Error(`关系ID重复：${id}`);
      edgeIds.add(id);
      const source = String(edge.source ?? edge.s ?? '');
      const target = String(edge.target ?? edge.t ?? '');
      const sourceType = resolveEndpointType(
        edge.sourceType ?? edge.sourceKind ?? edge.sType,
        source,
        nodeIds,
        groupIds
      );
      const targetType = resolveEndpointType(
        edge.targetType ?? edge.targetKind ?? edge.tType,
        target,
        nodeIds,
        groupIds
      );
      if (!sourceType || !targetType) {
        throw new Error(`关系 ${id} 引用了不存在的节点或分组。`);
      }
      if (source === target && sourceType === targetType) {
        throw new Error(`关系 ${id} 的起点和终点不能相同。`);
      }
      const pair = `${sourceType}:${source}\u0000${targetType}:${target}`;
      if (edgePairs.has(pair)) {
        throw new Error(`端点 ${source} 到 ${target} 存在重复关系。`);
      }
      edgePairs.add(pair);
      const requestedCategory = String(edge.category ?? edge.cat ?? '');
      const category = normalized.settings.relationTypes.some(type => type.id === requestedCategory)
        ? requestedCategory
        : normalized.settings.relationTypes[0].id;
      const sourceSide = normalizeNodePortSide(edge.sourceSide ?? edge.sourcePortSide);
      const targetSide = normalizeNodePortSide(edge.targetSide ?? edge.targetPortSide);
      return {
        id,
        source,
        target,
        sourceType,
        targetType,
        label: boundedText(edge.label, '', 40),
        category,
        lineType: normalizeEdgeLineType(edge.lineType),
        ...(sourceSide ? { sourceSide } : {}),
        ...(targetSide ? { targetSide } : {})
      };
    });

    return normalized;
  }

  function finiteNumber(value, fallback) {
    const number = Number(value);
    return Number.isFinite(number) ? number : fallback;
  }

  function normalizeNodePortSide(value) {
    const side = String(value || '').toLowerCase();
    return NODE_PORT_SIDES.includes(side) ? side : '';
  }

  function resolveEndpointType(value, id, nodeIds, groupIds) {
    const requested = String(value || '').toLowerCase();
    if (requested === 'node' && nodeIds.has(id)) return 'node';
    if (requested === 'group' && groupIds.has(id)) return 'group';
    if (nodeIds.has(id)) return 'node';
    if (groupIds.has(id)) return 'group';
    return '';
  }

  function normalizeEdgeLineType(value) {
    const lineType = String(value || '').toLowerCase();
    return EDGE_LINE_TYPES.includes(lineType) ? lineType : 'curve';
  }

  function getSelectedNewEdgeLineType() {
    const selectedInput = [
      dom.newEdgeStraightInput,
      dom.newEdgePolylineInput,
      dom.newEdgeCurveInput
    ].find(input => input.checked);
    return normalizeEdgeLineType(selectedInput?.value);
  }

  function boundedText(value, fallback, maximumLength) {
    const normalized = String(value ?? '').trim();
    return (normalized || fallback).slice(0, maximumLength);
  }

  function isHexColor(value) {
    return /^#[0-9a-f]{6}$/i.test(String(value || ''));
  }

  function normalizeNodeTypeLabel(value, fallback = '未分类') {
    return NODE_TYPE_API.normalizeLabel(value, fallback);
  }

  function ensureNodeTypeInSettings(settings, label, preferredKind = '') {
    return NODE_TYPE_API.ensureInSettings(settings, label, preferredKind);
  }

  function getNodeTypeLabel(node) {
    return NODE_TYPE_API.labelForNode(node);
  }

  function getCompactNodeTypeLabel(node) {
    const label = getNodeTypeLabel(node);
    return label.length > 16 ? `${label.slice(0, 15)}…` : label;
  }

  function normalizeProjectSettings(rawSettings = {}) {
    const theme = ['system', 'light', 'dark'].includes(rawSettings?.theme)
      ? rawSettings.theme
      : 'system';
    const colors = {};
    Object.keys(PROJECT_COLOR_VARIABLES).forEach(key => {
      if (isHexColor(rawSettings?.colors?.[key])) colors[key] = rawSettings.colors[key].toLowerCase();
    });

    const relationTypes = [];
    const usedIds = new Set();
    const sourceTypes = Array.isArray(rawSettings?.relationTypes) && rawSettings.relationTypes.length
      ? rawSettings.relationTypes
      : DEFAULT_RELATION_TYPES;
    sourceTypes.forEach((type, index) => {
      const id = String(type?.id || '').trim();
      if (!/^[a-z][a-z0-9_-]{0,31}$/i.test(id) || usedIds.has(id)) return;
      usedIds.add(id);
      relationTypes.push({
        id,
        label: String(type?.label || `关系类型 ${index + 1}`).trim().slice(0, 30) || `关系类型 ${index + 1}`,
        color: isHexColor(type?.color) ? type.color.toLowerCase() : '#e8963e'
      });
    });
    if (!relationTypes.length) relationTypes.push(...deepClone(DEFAULT_RELATION_TYPES));
    const nodeTypes = NODE_TYPE_API.normalizeCatalog(rawSettings?.nodeTypes);
    return { theme, colors, relationTypes, nodeTypes };
  }

  function findGroupForPoint(groups, xValue, yValue) {
    const x = finiteNumber(xValue, 0);
    const y = finiteNumber(yValue, 0);
    for (let index = groups.length - 1; index >= 0; index -= 1) {
      const group = groups[index];
      if (
        x >= group.x &&
        x <= group.x + group.w &&
        y >= group.y &&
        y <= group.y + group.h
      ) return group.id;
    }
    return '';
  }

  function saveGraph(message = '已自动保存') {
    graph.meta.updatedAt = new Date().toISOString();
    const graphSnapshot = deepClone(graph);
    let localMirrorError = null;
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(graphSnapshot));
    } catch (error) {
      localMirrorError = error;
    }

    syncProjectHeader();
    if (!projectStoreReady || !activeProjectId || !window.GraphProjectStore) {
      if (localMirrorError) {
        updateSaveState('自动保存失败');
        showToast('本机存储空间不足，请及时导出 JSON 备份。', true);
        console.error(localMirrorError);
      } else {
        updateSaveState(message);
      }
      return;
    }

    updateSaveState('正在保存…');

    projectSaveChain = projectSaveChain
      .catch(() => undefined)
      .then(() => window.GraphProjectStore.saveProject(activeProjectId, graphSnapshot, {
        snapshot: true,
        reason: message
      }))
      .then(project => {
        updateSaveState(message);
        const option = Array.from(dom.projectSelect.options)
          .find(item => item.value === project.id);
        if (option) option.textContent = project.name;
        dom.projectStorageState.textContent = window.GraphProjectStore.fallback
          ? '已保存到兼容存储'
          : '项目与恢复记录已保存';
      })
      .catch(error => {
        updateSaveState('项目保存失败');
        dom.projectStorageState.textContent = '项目数据库保存失败';
        showToast(`项目保存失败：${error.message}`, true);
        console.error(error);
      });
  }

  function updateSaveState(text) {
    dom.saveState.textContent = text;
    dom.saveState.dataset.state = /失败|不可用/.test(text)
      ? 'error'
      : /正在|保存中/.test(text)
        ? 'busy'
        : 'ready';
  }

  function pushHistory(snapshot = deepClone(graph)) {
    history.push(snapshot);
    if (history.length > MAX_HISTORY) history.shift();
    future = [];
    updateHistoryButtons();
  }

  function commit(mutator, message) {
    const snapshot = deepClone(graph);
    try {
      mutator();
      graph = normalizeAndValidateGraph(graph);
    } catch (error) {
      graph = snapshot;
      showToast(`操作失败：${error.message}`, true);
      return;
    }
    pushHistory(snapshot);
    invalidateFocusedGeometry();
    saveGraph();
    populateGroupOptions();
    populateNodeSelects();
    renderAll();
    if (message) showToast(message);
  }

  function undo() {
    if (!history.length) return;
    future.push(deepClone(graph));
    graph = history.pop();
    applyProjectAppearance();
    buildMarkers();
    populateRelationTypeOptions();
    invalidateFocusedGeometry();
    ensureSelectionExists();
    saveGraph('已撤销并保存');
    populateGroupOptions();
    populateNodeSelects();
    renderAll();
    updateHistoryButtons();
    showToast('已撤销');
  }

  function redo() {
    if (!future.length) return;
    history.push(deepClone(graph));
    graph = future.pop();
    applyProjectAppearance();
    buildMarkers();
    populateRelationTypeOptions();
    invalidateFocusedGeometry();
    ensureSelectionExists();
    saveGraph('已重做并保存');
    populateGroupOptions();
    populateNodeSelects();
    renderAll();
    updateHistoryButtons();
    showToast('已重做');
  }

  function updateHistoryButtons() {
    dom.undoButton.disabled = history.length === 0;
    dom.redoButton.disabled = future.length === 0;
  }

  function ensureSelectionExists() {
    selectedNodeIds = new Set([...selectedNodeIds].filter(nodeId => graph.nodes.some(node => node.id === nodeId)));
    if (selection.type === 'node' && !graph.nodes.some(node => node.id === selection.id)) clearSelection();
    if (selection.type === 'edge' && !graph.edges.some(edge => edge.id === selection.id)) clearSelection();
    if (selection.type === 'group' && !graph.groups.some(group => group.id === selection.id)) clearSelection();
  }

  function syncNodeSelection() {
    if (selection.type !== 'node') {
      selectedNodeIds.clear();
      return;
    }
    if (!selectedNodeIds.has(selection.id)) selectedNodeIds = new Set([selection.id]);
  }

  function setMode(nextMode) {
    if (dragState) {
      cancelScheduledNodeDrag();
      graph = dragState.snapshot;
      dragState = null;
      invalidateFocusedGeometry();
    }
    if (groupDragState) {
      graph = groupDragState.snapshot;
      groupDragState = null;
      invalidateFocusedGeometry();
    }
    if (groupResizeState) {
      graph = groupResizeState.snapshot;
      groupResizeState = null;
      invalidateFocusedGeometry();
    }
    selectionBoxState = null;
    panState = null;
    minimapPanState = null;
    suppressCanvasClick = false;
    mode = nextMode;
    linkState = null;
    alignmentGuides = [];
    const editing = mode === 'edit';
    if (!editing) dom.editToolsMenu.open = false;
    dom.viewModeButton.classList.toggle('is-active', !editing);
    dom.editModeButton.classList.toggle('is-active', editing);
    dom.viewModeButton.setAttribute('aria-pressed', editing ? 'false' : 'true');
    dom.editModeButton.setAttribute('aria-pressed', editing ? 'true' : 'false');
    dom.canvasPanel.classList.toggle('is-editing', editing);
    document.querySelectorAll('.edit-only').forEach(element => {
      element.hidden = !editing;
    });
    document.querySelectorAll('.view-only').forEach(element => {
      element.hidden = editing;
    });
    dom.canvasHint.textContent = editing
      ? '已选节点/分组拖动为移动；未选中时单击只选中，拖动超过 7 像素才拉线'
      : '点击节点或分组查看上下游；Tab 浏览元素；拖动画布平移，滚轮缩放';
    updateActionButtons();
    renderGraph();
    renderInspector();
  }

  function updateActionButtons() {
    const selectedNode = selection.type === 'node' && selectedNodeIds.size === 1;
    const hasSelection = Boolean(selection.type);
    dom.duplicateNodeButton.disabled = !selectedNode;
    dom.createRelationButton.disabled = graph.nodes.length + graph.groups.length < 2;
    dom.copySelectionButton.disabled = !hasSelection;
    dom.deleteSelectionButton.disabled = !hasSelection;
    dom.locateSelectionButton.disabled = !hasSelection;
    const arrangeAction = dom.arrangeAction.value;
    const arrangeMinimum = arrangeAction.startsWith('distribute-') ? 3 : 2;
    dom.applyArrangeButton.disabled = !arrangeAction || selectedNodeIds.size < arrangeMinimum;
    dom.applyLayerButton.disabled = !hasSelection;
  }

  function renderAll() {
    syncNodeSelection();
    renderGraph();
    renderInspector();
    updateActionButtons();
    updateHistoryButtons();
    if (!dom.graphSearchResults.hidden && dom.graphSearchInput.value.trim()) renderSearchResults();
  }

  function collectSearchResults(query) {
    const needle = String(query || '').trim().toLocaleLowerCase('zh-CN');
    if (!needle) return [];
    const includes = value => String(value || '').toLocaleLowerCase('zh-CN').includes(needle);
    const results = [];
    const groupNodeCounts = new Map();
    graph.nodes.forEach(node => {
      if (node.group) groupNodeCounts.set(node.group, (groupNodeCounts.get(node.group) || 0) + 1);
    });

    graph.nodes.forEach(node => {
      if (!includes(node.label) && !includes(node.note) && !includes(node.id) && !includes(getNodeTypeLabel(node))) return;
      const group = getGroup(node.group);
      results.push({
        type: 'node',
        id: node.id,
        label: node.label,
        detail: `${getNodeTypeLabel(node)} · ${group?.label || '未分组'}`
      });
    });
    graph.groups.forEach(group => {
      if (!includes(group.label) && !includes(group.id)) return;
      const count = groupNodeCounts.get(group.id) || 0;
      results.push({
        type: 'group',
        id: group.id,
        label: group.label,
        detail: `${count} 个节点`
      });
    });
    graph.edges.forEach(edge => {
      const source = getEdgeEndpoint(edge, 'source');
      const target = getEdgeEndpoint(edge, 'target');
      const sourceLabel = source ? getEndpointLabel(source.type, source.entity) : edge.source;
      const targetLabel = target ? getEndpointLabel(target.type, target.entity) : edge.target;
      const relationText = `${sourceLabel} ${edge.label} ${targetLabel}`;
      if (!includes(relationText) && !includes(edge.id) && !includes(getEdgeCategoryName(edge.category))) return;
      results.push({
        type: 'edge',
        id: edge.id,
        label: edge.label || `${sourceLabel} → ${targetLabel}`,
        detail: `${sourceLabel} → ${targetLabel}`
      });
    });

    const order = { node: 0, group: 1, edge: 2 };
    return results
      .sort((first, second) => order[first.type] - order[second.type] || first.label.localeCompare(second.label, 'zh-CN'))
      .slice(0, 60);
  }

  function renderSearchResults() {
    const query = dom.graphSearchInput.value.trim();
    if (!query) {
      closeSearchResults();
      return;
    }
    const results = collectSearchResults(query);
    dom.graphSearchResults.replaceChildren();
    activeSearchResultIndex = results.length ? 0 : -1;
    if (!results.length) {
      const empty = document.createElement('div');
      empty.className = 'graph-search-empty';
      empty.textContent = `没有找到“${query}”`;
      dom.graphSearchResults.appendChild(empty);
    } else {
      results.forEach((result, index) => {
        const button = document.createElement('button');
        button.type = 'button';
        button.id = `graphSearchOption-${index}`;
        button.className = `graph-search-result${index === activeSearchResultIndex ? ' is-active' : ''}`;
        button.setAttribute('role', 'option');
        button.setAttribute('aria-selected', index === activeSearchResultIndex ? 'true' : 'false');
        button.dataset.resultType = result.type;
        button.dataset.resultId = result.id;
        const type = document.createElement('span');
        type.className = 'result-type';
        type.textContent = { node: '节点', group: '分组', edge: '关系' }[result.type];
        const copy = document.createElement('span');
        const title = document.createElement('strong');
        title.textContent = result.label;
        const detail = document.createElement('small');
        detail.textContent = result.detail;
        copy.append(title, detail);
        button.append(type, copy);
        button.addEventListener('pointermove', () => setActiveSearchResult(index));
        button.addEventListener('click', () => activateSearchResult(result.type, result.id));
        dom.graphSearchResults.appendChild(button);
      });
    }
    dom.graphSearchResults.hidden = false;
    dom.graphSearchInput.setAttribute('aria-expanded', 'true');
    if (results.length) dom.graphSearchInput.setAttribute('aria-activedescendant', 'graphSearchOption-0');
    else dom.graphSearchInput.removeAttribute('aria-activedescendant');
  }

  function setActiveSearchResult(index) {
    const buttons = Array.from(dom.graphSearchResults.querySelectorAll('.graph-search-result'));
    if (!buttons.length) return;
    activeSearchResultIndex = (index + buttons.length) % buttons.length;
    buttons.forEach((button, buttonIndex) => {
      const active = buttonIndex === activeSearchResultIndex;
      button.classList.toggle('is-active', active);
      button.setAttribute('aria-selected', active ? 'true' : 'false');
    });
    dom.graphSearchInput.setAttribute('aria-activedescendant', buttons[activeSearchResultIndex].id);
    buttons[activeSearchResultIndex].scrollIntoView({ block: 'nearest' });
  }

  function closeSearchResults() {
    dom.graphSearchResults.hidden = true;
    dom.graphSearchInput.setAttribute('aria-expanded', 'false');
    dom.graphSearchInput.removeAttribute('aria-activedescendant');
    activeSearchResultIndex = -1;
  }

  function handleSearchKeydown(event) {
    const buttons = Array.from(dom.graphSearchResults.querySelectorAll('.graph-search-result'));
    if (event.key === 'Escape') {
      closeSearchResults();
      return;
    }
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      if (dom.graphSearchResults.hidden) renderSearchResults();
      setActiveSearchResult(activeSearchResultIndex + (event.key === 'ArrowDown' ? 1 : -1));
      return;
    }
    if (event.key === 'Enter' && buttons.length && activeSearchResultIndex >= 0) {
      event.preventDefault();
      buttons[activeSearchResultIndex].click();
    }
  }

  function activateSearchResult(type, id) {
    selectedNodeIds = type === 'node' ? new Set([id]) : new Set();
    selection = { type, id };
    closeSearchResults();
    renderAll();
    locateSelection();
    dom.svg.focus({ preventScroll: true });
  }

  function getSelectionBounds() {
    if (selection.type === 'node') {
      if (selectedNodeIds.size > 1) {
        const nodes = graph.nodes.filter(node => selectedNodeIds.has(node.id));
        if (!nodes.length) return null;
        const left = Math.min(...nodes.map(node => node.x));
        const top = Math.min(...nodes.map(node => node.y));
        const right = Math.max(...nodes.map(node => node.x + node.w));
        const bottom = Math.max(...nodes.map(node => node.y + node.h));
        return { x: left, y: top, w: right - left, h: bottom - top };
      }
      const node = getNode(selection.id);
      return node ? { x: node.x, y: node.y, w: node.w, h: node.h } : null;
    }
    if (selection.type === 'group') {
      const group = getGroup(selection.id);
      return group ? { x: group.x, y: group.y, w: group.w, h: group.h } : null;
    }
    if (selection.type === 'edge') {
      const edge = graph.edges.find(item => item.id === selection.id);
      const source = edge ? getEdgeEndpoint(edge, 'source')?.entity : null;
      const target = edge ? getEdgeEndpoint(edge, 'target')?.entity : null;
      if (!source || !target) return null;
      const left = Math.min(source.x, target.x);
      const top = Math.min(source.y, target.y);
      const right = Math.max(source.x + source.w, target.x + target.w);
      const bottom = Math.max(source.y + source.h, target.y + target.h);
      return { x: left, y: top, w: right - left, h: bottom - top };
    }
    return null;
  }

  function locateSelection() {
    const bounds = getSelectionBounds();
    if (!bounds) return;
    centerViewportOnBounds(bounds);
    renderViewportOnly();
  }

  function centerViewportOnBounds(bounds, padding = 90) {
    const rect = dom.svg.getBoundingClientRect();
    const aspect = rect.width > 0 && rect.height > 0 ? rect.height / rect.width : 0.72;
    const minimumWidth = 360;
    let width = Math.max(minimumWidth, bounds.w + padding * 2);
    let height = width * aspect;
    const requiredHeight = bounds.h + padding * 2;
    if (height < requiredHeight) {
      height = requiredHeight;
      width = height / aspect;
    }
    const canvasWidth = Number(graph.meta.canvasWidth) || 1440;
    const canvasHeight = Number(graph.meta.canvasHeight) || 1030;
    const maximumWidth = canvasWidth * 2.4;
    if (width > maximumWidth) {
      width = maximumWidth;
      height = width * aspect;
    }
    viewport = {
      x: bounds.x + bounds.w / 2 - width / 2,
      y: bounds.y + bounds.h / 2 - height / 2,
      w: width,
      h: height
    };
    constrainViewport();
  }

  function constrainViewport() {
    const canvasWidth = Number(graph.meta.canvasWidth) || 1440;
    const canvasHeight = Number(graph.meta.canvasHeight) || 1030;
    const minX = Math.min(0, canvasWidth - viewport.w);
    const maxX = Math.max(0, canvasWidth - viewport.w);
    const minY = Math.min(0, canvasHeight - viewport.h);
    const maxY = Math.max(0, canvasHeight - viewport.h);
    viewport.x = Math.max(minX, Math.min(maxX, viewport.x));
    viewport.y = Math.max(minY, Math.min(maxY, viewport.y));
  }

  function invalidateFocusedGeometry() {
    graphRevision += 1;
    cancelRoutingRequest();
    focusedGeometryCache.clear();
  }

  function readFocusedGeometryCache(key) {
    if (!focusedGeometryCache.has(key)) return null;
    const geometry = focusedGeometryCache.get(key);
    focusedGeometryCache.delete(key);
    focusedGeometryCache.set(key, geometry);
    return geometry;
  }

  function writeFocusedGeometryCache(key, geometry) {
    if (!key) return;
    focusedGeometryCache.delete(key);
    focusedGeometryCache.set(key, geometry);
    while (focusedGeometryCache.size > MAX_FOCUSED_ROUTE_CACHE) {
      focusedGeometryCache.delete(focusedGeometryCache.keys().next().value);
    }
  }

  function getFocusedGeometry(edges, nodeMap, blockingNodeIds) {
    if (dragState || groupDragState || groupResizeState) return new Map();
    const key = [
      graphRevision,
      selection.type,
      selection.id,
      dom.categoryFilter.value,
      dom.directionFilter.value,
      dom.depthFilter.value,
      [...blockingNodeIds].sort().join(','),
      edges.map(edge => edge.id).join(',')
    ].join('|');
    const cachedGeometry = readFocusedGeometryCache(key);
    if (cachedGeometry) return cachedGeometry;
    if (routingPendingKey !== key) scheduleFocusedRouting(key, edges, nodeMap, blockingNodeIds);
    const pending = buildEmergencyFocusedGeometry(edges, nodeMap, blockingNodeIds);
    pending.pending = true;
    return pending;
  }

  function updateRoutingState(text = '', error = false) {
    dom.routeState.hidden = !text;
    dom.routeState.textContent = text;
    dom.routeState.classList.toggle('is-error', error);
  }

  function destroyRoutingWorker() {
    if (routingWorker) routingWorker.terminate();
    routingWorker = null;
    if (routingWorkerUrl) URL.revokeObjectURL(routingWorkerUrl);
    routingWorkerUrl = '';
  }

  function initializeRoutingWorker() {
    destroyRoutingWorker();
    if (typeof Worker !== 'function' || !window.GraphRouting?.createWorkerSource) return false;
    try {
      const source = window.GraphRouting.createWorkerSource();
      routingWorkerUrl = URL.createObjectURL(new Blob([source], { type: 'text/javascript' }));
      routingWorker = new Worker(routingWorkerUrl, { name: 'relationship-routing' });
      routingWorker.addEventListener('message', handleRoutingWorkerMessage);
      routingWorker.addEventListener('error', event => {
        event.preventDefault();
        handleRoutingFailure(event.message || '布线线程发生错误。');
      });
      return true;
    } catch (error) {
      console.warn('无法启动后台布线线程。', error);
      destroyRoutingWorker();
      return false;
    }
  }

  function cancelRoutingRequest() {
    const hadPendingWork = Boolean(routingPendingKey);
    clearTimeout(routingDebounceTimer);
    clearTimeout(routingTimeoutTimer);
    routingDebounceTimer = 0;
    routingTimeoutTimer = 0;
    routingPendingKey = '';
    routingPendingPayload = null;
    updateRoutingState();
    if (hadPendingWork && routingWorker) initializeRoutingWorker();
  }

  function scheduleFocusedRouting(key, edges, nodeMap, blockingNodeIds) {
    clearTimeout(routingDebounceTimer);
    clearTimeout(routingTimeoutTimer);
    if (routingPendingKey && routingPendingKey !== key) initializeRoutingWorker();
    routingPendingKey = key;
    const requestId = ++routingRequestId;
    updateRoutingState(`正在优化 ${edges.length} 条连线…`);
    const nodes = [...nodeMap.values()].map(node => ({ ...node }));
    const edgeCopies = edges.map(edge => ({ ...edge }));
    const blockingIds = [...blockingNodeIds];
    const canvasMeta = {
      canvasWidth: Number(graph.meta.canvasWidth) || 1440,
      canvasHeight: Number(graph.meta.canvasHeight) || 1030
    };
    routingPendingPayload = { key, edges: edgeCopies, nodeMap, blockingNodeIds };
    routingDebounceTimer = setTimeout(() => {
      if (routingPendingKey !== key || requestId !== routingRequestId) return;
      if (!routingWorker && !initializeRoutingWorker()) {
        applyRoutingFallback(key, edgeCopies, nodeMap, blockingNodeIds, '当前环境不支持后台线程');
        return;
      }
      try {
        routingWorker.postMessage({
          type: 'route',
          requestId,
          edges: edgeCopies,
          nodes,
          blockingNodeIds: blockingIds,
          canvasMeta
        });
        routingTimeoutTimer = setTimeout(() => {
          if (routingPendingKey !== key || requestId !== routingRequestId) return;
          destroyRoutingWorker();
          applyRoutingFallback(key, edgeCopies, nodeMap, blockingNodeIds, '精细布线超时');
        }, 15000);
      } catch (error) {
        handleRoutingFailure(error.message, key, edgeCopies, nodeMap, blockingNodeIds);
      }
    }, 45);
  }

  function handleRoutingWorkerMessage(event) {
    const message = event.data || {};
    if (message.requestId !== routingRequestId || !routingPendingKey) return;
    if (message.type === 'route-error') {
      handleRoutingFailure(message.error || '后台布线失败。');
      return;
    }
    if (message.type !== 'route-result') return;
    clearTimeout(routingTimeoutTimer);
    const key = routingPendingKey;
    const payload = routingPendingPayload;
    routingPendingKey = '';
    const result = message.result || {};
    const geometryMap = new Map(result.entries || []);
    geometryMap.crossingGaps = result.crossingGaps || [];
    geometryMap.relaxedEdgeIds = result.relaxedEdgeIds || [];
    if (payload) {
      const emergency = buildEmergencyFocusedGeometry(payload.edges, payload.nodeMap, payload.blockingNodeIds);
      payload.edges.forEach(edge => {
        if (!geometryMap.has(edge.id) && emergency.has(edge.id)) geometryMap.set(edge.id, emergency.get(edge.id));
      });
      geometryMap.relaxedEdgeIds = [
        ...new Set([
          ...geometryMap.relaxedEdgeIds,
          ...payload.edges.filter(edge => !result.entries?.some(entry => entry[0] === edge.id)).map(edge => edge.id)
        ])
      ];
    }
    routingPendingPayload = null;
    writeFocusedGeometryCache(key, geometryMap);
    updateRoutingState();
    renderGraph();
  }

  function handleRoutingFailure(message, key = routingPendingKey, edges = null, nodeMap = null, blockingNodeIds = null) {
    clearTimeout(routingTimeoutTimer);
    destroyRoutingWorker();
    console.warn('后台布线已降级。', message);
    if (!edges && routingPendingPayload) {
      ({ key, edges, nodeMap, blockingNodeIds } = routingPendingPayload);
    }
    if (edges && nodeMap && blockingNodeIds) {
      applyRoutingFallback(key, edges, nodeMap, blockingNodeIds, message);
      return;
    }
    const currentKey = routingPendingKey;
    routingPendingKey = '';
    routingPendingPayload = null;
    updateRoutingState('精细布线失败，已保留简化连线', true);
    if (!routingFailureNotified) {
      routingFailureNotified = true;
      showToast('精细布线未完成，已保留简化连线，节点关系不会消失。', true);
    }
    writeFocusedGeometryCache(currentKey, new Map());
    setTimeout(() => updateRoutingState(), 2600);
    renderGraph();
  }

  function applyRoutingFallback(key, edges, nodeMap, blockingNodeIds, reason) {
    clearTimeout(routingTimeoutTimer);
    routingPendingKey = '';
    routingPendingPayload = null;
    let geometryMap;
    if (edges.length <= 18 && window.GraphRouting?.calculateFocusedEdgeGeometries) {
      try {
        geometryMap = calculateFocusedEdgeGeometries(edges, nodeMap, blockingNodeIds);
      } catch (error) {
        console.warn('同步布线兜底失败。', error);
      }
    }
    if (!geometryMap) geometryMap = buildEmergencyFocusedGeometry(edges, nodeMap, blockingNodeIds);
    writeFocusedGeometryCache(key, geometryMap);
    updateRoutingState(`${reason}，已使用简化布线`, true);
    if (!routingFailureNotified) {
      routingFailureNotified = true;
      showToast(`${reason}，已自动使用简化布线。`, true);
    }
    setTimeout(() => updateRoutingState(), 2800);
    renderGraph();
  }

  function buildEmergencyFocusedGeometry(edges, nodeMap, blockingNodeIds) {
    const geometryMap = new Map();
    const canvasWidth = Number(graph.meta.canvasWidth) || 1440;
    const canvasHeight = Number(graph.meta.canvasHeight) || 1030;
    const blockingNodes = [...blockingNodeIds].map(nodeId => nodeMap.get(nodeId)).filter(Boolean);
    const chosenRoutes = [];

    function segmentIntersectsRect(start, end, node, padding = 8) {
      const left = node.x - padding;
      const right = node.x + node.w + padding;
      const top = node.y - padding;
      const bottom = node.y + node.h + padding;
      const dx = end.x - start.x;
      const dy = end.y - start.y;
      let minimum = 0;
      let maximum = 1;
      for (const [direction, distance] of [
        [-dx, start.x - left],
        [dx, right - start.x],
        [-dy, start.y - top],
        [dy, bottom - start.y]
      ]) {
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

    function simplify(points) {
      const unique = [];
      points.forEach(point => {
        const previous = unique.at(-1);
        if (!previous || Math.hypot(previous.x - point.x, previous.y - point.y) > 0.1) unique.push(point);
      });
      if (unique.length < 3) return unique;
      return unique.filter((point, index) => {
        if (!index || index === unique.length - 1) return true;
        const previous = unique[index - 1];
        const next = unique[index + 1];
        return !(
          (Math.abs(previous.x - point.x) < 0.1 && Math.abs(point.x - next.x) < 0.1) ||
          (Math.abs(previous.y - point.y) < 0.1 && Math.abs(point.y - next.y) < 0.1)
        );
      });
    }

    function segments(points) {
      return points.slice(1).map((point, index) => ({ a: points[index], b: point }));
    }

    function intersection(first, second) {
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

    function overlapLength(first, second) {
      const firstHorizontal = Math.abs(first.a.y - first.b.y) < 0.1;
      const secondHorizontal = Math.abs(second.a.y - second.b.y) < 0.1;
      if (firstHorizontal !== secondHorizontal) return 0;
      if (firstHorizontal) {
        if (Math.abs(first.a.y - second.a.y) > 0.5) return 0;
        return Math.max(0, Math.min(Math.max(first.a.x, first.b.x), Math.max(second.a.x, second.b.x)) -
          Math.max(Math.min(first.a.x, first.b.x), Math.min(second.a.x, second.b.x)));
      }
      if (Math.abs(first.a.x - second.a.x) > 0.5) return 0;
      return Math.max(0, Math.min(Math.max(first.a.y, first.b.y), Math.max(second.a.y, second.b.y)) -
        Math.max(Math.min(first.a.y, first.b.y), Math.min(second.a.y, second.b.y)));
    }

    function candidateScore(candidate, sourceId, targetId) {
      const routeSegments = segments(candidate);
      if (candidate.some(point => point.x < 4 || point.x > canvasWidth - 4 || point.y < 4 || point.y > canvasHeight - 4)) return null;
      for (const segment of routeSegments) {
        if (blockingNodes.some(node => node.id !== sourceId && node.id !== targetId && segmentIntersectsRect(segment.a, segment.b, node))) return null;
      }
      let crossings = 0;
      for (const existing of chosenRoutes) {
        let pairCrossings = 0;
        for (const segment of routeSegments) {
          for (const other of existing) {
            if (overlapLength(segment, other) > 1) return null;
            if (intersection(segment, other)) pairCrossings += 1;
          }
        }
        if (pairCrossings > 1) return null;
        crossings += pairCrossings;
      }
      const length = routeSegments.reduce((sum, segment) =>
        sum + Math.hypot(segment.b.x - segment.a.x, segment.b.y - segment.a.y), 0);
      return { score: crossings * 1000 + length + routeSegments.length * 5, routeSegments };
    }

    function labelPoint(points) {
      const routeSegments = segments(points);
      const lengths = routeSegments.map(segment => Math.hypot(segment.b.x - segment.a.x, segment.b.y - segment.a.y));
      let remaining = lengths.reduce((sum, length) => sum + length, 0) / 2;
      for (let index = 0; index < routeSegments.length; index += 1) {
        if (remaining <= lengths[index]) {
          const segment = routeSegments[index];
          const ratio = lengths[index] ? remaining / lengths[index] : 0;
          return {
            x: segment.a.x + (segment.b.x - segment.a.x) * ratio,
            y: segment.a.y + (segment.b.y - segment.a.y) * ratio - 10
          };
        }
        remaining -= lengths[index];
      }
      return points[Math.floor(points.length / 2)];
    }

    edges.forEach((edge, index) => {
      const source = nodeMap.get(edge.source);
      const target = nodeMap.get(edge.target);
      if (!source || !target) return;
      const sourceCenter = { x: source.x + source.w / 2, y: source.y + source.h / 2 };
      const targetCenter = { x: target.x + target.w / 2, y: target.y + target.h / 2 };
      const horizontal = Math.abs(targetCenter.x - sourceCenter.x) >= Math.abs(targetCenter.y - sourceCenter.y);
      const portOffset = ((index % 7) - 3) * 4;
      const start = horizontal
        ? {
          x: targetCenter.x >= sourceCenter.x ? source.x + source.w : source.x,
          y: Math.max(source.y + 8, Math.min(source.y + source.h - 8, sourceCenter.y + portOffset))
        }
        : {
          x: Math.max(source.x + 8, Math.min(source.x + source.w - 8, sourceCenter.x + portOffset)),
          y: targetCenter.y >= sourceCenter.y ? source.y + source.h : source.y
        };
      const end = horizontal
        ? {
          x: targetCenter.x >= sourceCenter.x ? target.x : target.x + target.w,
          y: Math.max(target.y + 8, Math.min(target.y + target.h - 8, targetCenter.y - portOffset))
        }
        : {
          x: Math.max(target.x + 8, Math.min(target.x + target.w - 8, targetCenter.x - portOffset)),
          y: targetCenter.y >= sourceCenter.y ? target.y : target.y + target.h
        };
      const xLanes = [
        (start.x + end.x) / 2,
        ...blockingNodes.flatMap(node => [node.x - 14, node.x + node.w + 14]),
        8 + index * 3,
        canvasWidth - 8 - index * 3
      ].filter(value => value >= 4 && value <= canvasWidth - 4);
      const yLanes = [
        (start.y + end.y) / 2,
        ...blockingNodes.flatMap(node => [node.y - 14, node.y + node.h + 14]),
        8 + index * 3,
        canvasHeight - 8 - index * 3
      ].filter(value => value >= 4 && value <= canvasHeight - 4);
      const candidates = [
        [start, end],
        [start, { x: end.x, y: start.y }, end],
        [start, { x: start.x, y: end.y }, end],
        ...xLanes.map(x => [start, { x, y: start.y }, { x, y: end.y }, end]),
        ...yLanes.map(y => [start, { x: start.x, y }, { x: end.x, y }, end])
      ].map(simplify);
      let best = null;
      candidates.forEach(points => {
        const scored = candidateScore(points, source.id, target.id);
        if (scored && (!best || scored.score < best.score)) best = { ...scored, points };
      });
      if (!best) {
        const geometry = calculateEdgeGeometry(source, target, index);
        geometry.routeQuality = 'emergency';
        geometryMap.set(edge.id, geometry);
        return;
      }
      chosenRoutes.push(best.routeSegments);
      const label = labelPoint(best.points);
      geometryMap.set(edge.id, {
        d: best.points.map((point, pointIndex) => `${pointIndex ? 'L' : 'M'} ${point.x} ${point.y}`).join(' '),
        lx: label.x,
        ly: label.y,
        routeQuality: 'emergency'
      });
    });
    geometryMap.crossingGaps = [];
    geometryMap.relaxedEdgeIds = edges.map(edge => edge.id);
    return geometryMap;
  }

  function buildMarkers() {
    dom.defs.replaceChildren();
    getRelationTypes().forEach(type => {
      const category = type.id;
      const color = type.color;
      const marker = createSvg('marker', {
        id: `arrow-${category}`,
        viewBox: '0 0 10 10',
        refX: '9',
        refY: '5',
        markerUnits: 'userSpaceOnUse',
        markerWidth: '9',
        markerHeight: '9',
        orient: 'auto-start-reverse'
      });
      const path = createSvg('path', { d: 'M 0 0 L 10 5 L 0 10 z' });
      path.style.fill = color;
      marker.appendChild(path);
      dom.defs.appendChild(marker);
    });

    const linkMarker = createSvg('marker', {
      id: 'arrow-link',
      viewBox: '0 0 10 10',
      refX: '9',
      refY: '5',
      markerUnits: 'userSpaceOnUse',
      markerWidth: '9',
      markerHeight: '9',
      orient: 'auto-start-reverse'
    });
    const linkArrow = createSvg('path', { d: 'M 0 0 L 10 5 L 0 10 z' });
    linkArrow.style.fill = 'var(--primary)';
    linkMarker.appendChild(linkArrow);
    dom.defs.appendChild(linkMarker);
  }

  function createSvg(tag, attributes = {}) {
    const element = document.createElementNS(SVG_NS, tag);
    Object.entries(attributes).forEach(([name, value]) => element.setAttribute(name, String(value)));
    return element;
  }

  function focusRenderedGraphElement(attribute, id) {
    requestAnimationFrame(() => {
      const element = Array.from(dom.svg.querySelectorAll(`[${attribute}]`))
        .find(item => item.getAttribute(attribute) === id && item.hasAttribute('tabindex'));
      element?.focus({ preventScroll: true });
    });
  }

  function renderGraph() {
    dom.svg.setAttribute('viewBox', `${viewport.x} ${viewport.y} ${viewport.w} ${viewport.h}`);
    dom.groupLayer.replaceChildren();
    dom.edgeLayer.replaceChildren();
    dom.edgeGapLayer.replaceChildren();
    dom.edgeLabelLayer.replaceChildren();
    dom.linkPreviewLayer.replaceChildren();
    dom.nodeLayer.replaceChildren();
    dom.groupHandleLayer.replaceChildren();
    dom.alignmentGuideLayer.replaceChildren();
    dom.selectionBoxLayer.replaceChildren();

    const visibleCategories = getVisibleCategories(dom.categoryFilter.value);
    const focus = linkState || selectedNodeIds.size > 1
      ? { isFocused: false, nodeIds: new Set(), entityKeys: new Set(), edgeIds: new Set() }
      : computeFocus(visibleCategories);
    const participatingEndpointKeys = new Set();
    graph.edges.forEach(edge => {
      if (!visibleCategories.has(edge.category)) return;
      participatingEndpointKeys.add(getEdgeEndpointKey(edge, 'source'));
      participatingEndpointKeys.add(getEdgeEndpointKey(edge, 'target'));
    });
    if (!focus.isFocused && routingPendingKey) cancelRoutingRequest();

    graph.groups.forEach(group => {
      const isSelected = selection.type === 'group' && selection.id === group.id;
      const groupKey = endpointKey('group', group.id);
      const participates = participatingEndpointKeys.has(groupKey);
      const isDimmedByFilter = dom.categoryFilter.value !== 'all' && !participates;
      const isDimmedByFocus = focus.isFocused && !focus.entityKeys.has(groupKey);
      const isRelatedByFocus = focus.isFocused && focus.entityKeys.has(groupKey) && !isSelected;
      const groupClasses = ['graph-group'];
      if (isSelected) groupClasses.push('is-selected');
      if (isRelatedByFocus) groupClasses.push('is-related');
      if (isDimmedByFilter || isDimmedByFocus) groupClasses.push('is-dimmed');
      if (groupDragState?.groupId === group.id) groupClasses.push('is-dragging');
      if (linkState?.sourceType === 'group' && linkState.sourceId === group.id) groupClasses.push('is-link-source');
      if (linkState?.targetType === 'group' && linkState.targetId === group.id) groupClasses.push('is-link-target');
      const wrapper = createSvg('g', {
        class: groupClasses.join(' '),
        'data-group-id': group.id,
        tabindex: '0',
        role: 'button',
        'aria-label': `分组：${group.label}`,
        'aria-pressed': isSelected ? 'true' : 'false'
      });
      const rect = createSvg('rect', {
        class: 'graph-group-box',
        x: group.x,
        y: group.y,
        width: group.w,
        height: group.h,
        rx: 12
      });
      const label = createSvg('text', {
        class: 'graph-group-label',
        x: group.x + 12,
        y: group.y + 23
      });
      label.textContent = group.label;
      wrapper.append(rect, label);

      if (mode === 'edit') {
        wrapper.addEventListener('pointerdown', event => {
          event.stopPropagation();
          if (isSelected) startGroupDrag(event, group);
          else startLinkDrag(event, group, 'group');
        });
      }
      wrapper.addEventListener('click', event => {
        event.stopPropagation();
        if (mode === 'edit') return;
        selectGroup(group.id);
      });
      wrapper.addEventListener('keydown', event => {
        if (event.key !== 'Enter' && event.key !== ' ') return;
        event.preventDefault();
        selectGroup(group.id);
        focusRenderedGraphElement('data-group-id', group.id);
      });

      if (mode === 'edit' && isSelected) {
        const edgeZones = [
          ['n', group.x + 6, group.y - 6, group.w - 12, 12],
          ['e', group.x + group.w - 6, group.y + 6, 12, group.h - 12],
          ['s', group.x + 6, group.y + group.h - 6, group.w - 12, 12],
          ['w', group.x - 6, group.y + 6, 12, group.h - 12]
        ];
        edgeZones.forEach(([direction, x, y, width, height]) => {
          const edgeZone = createSvg('rect', {
            class: 'graph-group-resize-edge',
            x,
            y,
            width,
            height,
            'data-resize-direction': direction,
            'aria-label': `拖动${group.label}分组边界`
          });
          edgeZone.addEventListener('pointerdown', event => {
            event.stopPropagation();
            startGroupResize(event, group, direction);
          });
          edgeZone.addEventListener('click', event => event.stopPropagation());
          dom.groupHandleLayer.appendChild(edgeZone);
        });

        const size = 10;
        const half = size / 2;
        const handles = [
          ['nw', group.x - half, group.y - half],
          ['n', group.x + group.w / 2 - half, group.y - half],
          ['ne', group.x + group.w - half, group.y - half],
          ['e', group.x + group.w - half, group.y + group.h / 2 - half],
          ['se', group.x + group.w - half, group.y + group.h - half],
          ['s', group.x + group.w / 2 - half, group.y + group.h - half],
          ['sw', group.x - half, group.y + group.h - half],
          ['w', group.x - half, group.y + group.h / 2 - half]
        ];
        handles.forEach(([direction, x, y]) => {
          const handle = createSvg('rect', {
            class: 'graph-group-resize-handle',
            x,
            y,
            width: size,
            height: size,
            rx: 2,
            'data-resize-direction': direction,
            'aria-label': `调整${group.label}分组边界`
          });
          handle.addEventListener('pointerdown', event => {
            event.stopPropagation();
            startGroupResize(event, group, direction);
          });
          handle.addEventListener('click', event => event.stopPropagation());
          dom.groupHandleLayer.appendChild(handle);
        });

        NODE_PORT_SIDES.forEach(side => {
          const point = getNodePortPoint(group, side);
          const linkHandle = createSvg('circle', {
            class: `graph-link-handle graph-group-link-handle is-${side}${linkState?.sourceType === 'group' && linkState.sourceId === group.id && linkState.sourceSide === side ? ' is-active' : ''}`,
            cx: point.x,
            cy: point.y,
            r: 8,
            'data-link-source': group.id,
            'data-link-source-type': 'group',
            'data-link-side': side,
            'aria-label': `从“${group.label}”分组的${getNodePortSideLabel(side)}方向拖线创建关系`
          });
          linkHandle.addEventListener('pointerdown', event => {
            event.stopPropagation();
            startLinkDrag(event, group, 'group', side, true);
          });
          linkHandle.addEventListener('click', event => event.stopPropagation());
          dom.groupHandleLayer.appendChild(linkHandle);
        });
      }

      dom.groupLayer.appendChild(wrapper);
    });

    const nodeMap = new Map(graph.nodes.map(node => [node.id, node]));
    const focusedGeometry = selection.type === 'node' && focus.isFocused && !dragState && !groupDragState && !groupResizeState
      ? getFocusedGeometry(
        graph.edges.filter(edge =>
          focus.edgeIds.has(edge.id) &&
          visibleCategories.has(edge.category) &&
          getEdgeEndpointType(edge, 'source') === 'node' &&
          getEdgeEndpointType(edge, 'target') === 'node'
        ),
        nodeMap,
        focus.nodeIds
      )
      : new Map();
    const focusedRoutingPending = Boolean(focusedGeometry.pending);
    const originalEdgeIndex = new Map(graph.edges.map((edge, index) => [edge.id, index]));
    const renderEdges = focus.isFocused
      ? graph.edges.filter(edge => focus.edgeIds.has(edge.id))
      : graph.edges;

    renderEdges.forEach(edge => {
      const sourceEndpoint = getEdgeEndpoint(edge, 'source');
      const targetEndpoint = getEdgeEndpoint(edge, 'target');
      const source = sourceEndpoint?.entity;
      const target = targetEndpoint?.entity;
      if (!source || !target) return;

      const categoryVisible = visibleCategories.has(edge.category);
      const isSelected = selection.type === 'edge' && selection.id === edge.id;
      const isRelated = focus.edgeIds.has(edge.id);
      if (
        !dom.edgeVisibilityToggle.checked &&
        (!focus.isFocused || !isRelated)
      ) {
        return;
      }
      const focusedRoute = focusedGeometry.get(edge.id);
      const temporarilyUsingSimpleRoute = Boolean(dragState || groupDragState || groupResizeState);
      const geometry = focusedRoute ||
        calculateEdgeGeometry(source, target, originalEdgeIndex.get(edge.id) || 0, edge);
      if (!focusedRoute && selection.type === 'node' && focus.isFocused && isRelated) {
        geometry.routeQuality = focusedRoutingPending ? 'pending' : 'fallback';
      }
      const isDimmed = !categoryVisible || (focus.isFocused && !isRelated);

      const classNames = ['graph-edge', edge.category];
      if (isSelected) classNames.push('is-selected');
      else if (isRelated && focus.isFocused) classNames.push('is-related');
      if (isDimmed) classNames.push('is-dimmed');

      const path = createSvg('path', {
        class: classNames.join(' '),
        d: geometry.d,
        'marker-end': `url(#arrow-${edge.category})`,
        'data-edge-id': edge.id,
        'data-line-type': normalizeEdgeLineType(edge.lineType),
        'data-route-quality': geometry.routeQuality || 'default',
        'data-route-order': geometry.routeOrder ?? -1
      });
      path.style.stroke = getEdgeCategoryColor(edge.category);

      const hitPath = createSvg('path', {
        class: 'graph-edge-hit',
        d: geometry.d,
        'data-edge-id': edge.id,
        tabindex: '0',
        role: 'button',
        'aria-pressed': isSelected ? 'true' : 'false',
        'aria-label': edge.label
          ? `关系：${edge.label}，${getEndpointLabel(sourceEndpoint.type, source)} 到 ${getEndpointLabel(targetEndpoint.type, target)}`
          : `关系：${getEndpointLabel(sourceEndpoint.type, source)} 到 ${getEndpointLabel(targetEndpoint.type, target)}`
      });
      hitPath.addEventListener('click', event => {
        event.stopPropagation();
        selectEdge(edge.id);
      });
      hitPath.addEventListener('keydown', event => {
        if (event.key !== 'Enter' && event.key !== ' ') return;
        event.preventDefault();
        selectEdge(edge.id);
        focusRenderedGraphElement('data-edge-id', edge.id);
      });

      const label = createSvg('text', {
        class: `graph-edge-label${isSelected || (focus.isFocused && isRelated) ? ' is-visible' : ''}`,
        x: geometry.lx,
        y: geometry.ly,
        'data-edge-label-id': edge.id,
        'text-anchor': 'middle'
      });
      label.textContent = edge.label;

      dom.edgeLayer.append(path, hitPath);
      if (edge.label) dom.edgeLabelLayer.appendChild(label);
    });

    (focusedGeometry.crossingGaps || []).forEach(point => {
      dom.edgeGapLayer.appendChild(createSvg('circle', {
        class: 'graph-edge-crossing-gap',
        cx: point.x,
        cy: point.y,
        r: 4.5
      }));
    });

    graph.nodes.forEach(node => {
      const nodeKey = endpointKey('node', node.id);
      const participates = participatingEndpointKeys.has(nodeKey);
      const isSelected = selectedNodeIds.has(node.id);
      const isPrimary = selection.type === 'node' && selection.id === node.id;
      const isDimmedByFilter = dom.categoryFilter.value !== 'all' && !participates;
      const isDimmedByFocus = focus.isFocused && !focus.entityKeys.has(nodeKey);
      const isRelatedByFocus = focus.isFocused && focus.entityKeys.has(nodeKey) && !isPrimary;

      const classNames = ['graph-node', node.kind];
      if (isSelected) classNames.push('is-selected');
      if (isPrimary) classNames.push('is-primary');
      if (isRelatedByFocus) classNames.push('is-related');
      if (isDimmedByFilter || isDimmedByFocus) classNames.push('is-dimmed');
      if (dragState?.nodeIds?.includes(node.id)) classNames.push('is-dragging');
      if (linkState?.sourceType === 'node' && linkState.sourceId === node.id) classNames.push('is-link-source');
      if (linkState?.targetType === 'node' && linkState.targetId === node.id) classNames.push('is-link-target');

      const group = createSvg('g', {
        class: classNames.join(' '),
        'data-node-id': node.id,
        tabindex: '0',
        role: 'button',
        'aria-pressed': isSelected ? 'true' : 'false',
        'aria-label': `${node.label}，类型：${getNodeTypeLabel(node)}：${node.note}`
      });
      const rect = createSvg('rect', {
        class: 'graph-node-rect',
        x: node.x,
        y: node.y,
        width: node.w,
        height: node.h
      });
      const kind = createSvg('text', {
        class: 'graph-node-kind',
        x: node.x + 10,
        y: node.y + 15
      });
      kind.textContent = getCompactNodeTypeLabel(node);
      group.append(rect, kind);

      const lines = splitLabel(node.label);
      const baseY = node.y + (lines.length === 1 ? node.h / 2 + 10 : node.h / 2 + 2 - (lines.length - 2) * 7);
      lines.forEach((line, lineIndex) => {
        const text = createSvg('text', {
          class: 'graph-node-label',
          x: node.x + node.w / 2,
          y: baseY + lineIndex * 15,
          'text-anchor': 'middle'
        });
        text.textContent = line;
        group.appendChild(text);
      });

      if (mode === 'edit') {
        NODE_PORT_SIDES.forEach(side => {
          const point = getNodePortPoint(node, side);
          const linkHandle = createSvg('circle', {
            class: `graph-link-handle is-${side}${linkState?.sourceType === 'node' && linkState.sourceId === node.id && linkState.sourceSide === side ? ' is-active' : ''}`,
            cx: point.x,
            cy: point.y,
            r: 7,
            'data-link-source': node.id,
            'data-link-side': side,
            'aria-label': `从${node.label}的${getNodePortSideLabel(side)}方向拖线创建关系`
          });
          linkHandle.addEventListener('pointerdown', event => {
            event.stopPropagation();
            startLinkDrag(event, node, 'node', side, true);
          });
          linkHandle.addEventListener('click', event => {
            event.stopPropagation();
          });
          group.appendChild(linkHandle);
        });
      }

      group.addEventListener('pointerdown', event => {
        if (mode !== 'edit') return;
        event.stopPropagation();
        const additive = event.shiftKey || event.ctrlKey || event.metaKey;
        if (selectedNodeIds.has(node.id) || additive) startNodeDrag(event, node);
        else startLinkDrag(event, node, 'node');
      });
      group.addEventListener('click', event => {
        event.stopPropagation();
        if (mode === 'edit') return;
        selectNode(node.id, event);
      });
      group.addEventListener('keydown', event => {
        if (event.key !== 'Enter' && event.key !== ' ') return;
        event.preventDefault();
        event.stopPropagation();
        selectNode(node.id, event);
        focusRenderedGraphElement('data-node-id', node.id);
      });
      dom.nodeLayer.appendChild(group);
    });

    renderLinkPreview();
    renderAlignmentGuides();
    renderSelectionBox();
    dom.canvasCounts.textContent = `${graph.nodes.length}个节点 · ${graph.edges.length}条关系`;
    renderMinimap();
  }

  function updateMinimapViewport() {
    const indicator = dom.minimapCanvas.querySelector('.minimap-viewport');
    if (!indicator) return;
    indicator.setAttribute('x', String(viewport.x));
    indicator.setAttribute('y', String(viewport.y));
    indicator.setAttribute('width', String(viewport.w));
    indicator.setAttribute('height', String(viewport.h));
  }

  function renderViewportOnly() {
    dom.svg.setAttribute('viewBox', `${viewport.x} ${viewport.y} ${viewport.w} ${viewport.h}`);
    updateMinimapViewport();
  }

  function scheduleViewportRender() {
    if (viewportAnimationFrame) return;
    viewportAnimationFrame = requestAnimationFrame(() => {
      viewportAnimationFrame = 0;
      renderViewportOnly();
    });
  }

  function renderMinimap() {
    const canvasWidth = Number(graph.meta.canvasWidth) || 1440;
    const canvasHeight = Number(graph.meta.canvasHeight) || 1030;
    dom.minimapCanvas.setAttribute('viewBox', `0 0 ${canvasWidth} ${canvasHeight}`);
    dom.minimapCanvas.replaceChildren();

    graph.groups.forEach(group => {
      dom.minimapCanvas.appendChild(createSvg('rect', {
        class: 'minimap-group',
        x: group.x,
        y: group.y,
        width: group.w,
        height: group.h,
        rx: 10
      }));
    });
    graph.nodes.forEach(node => {
      const selected = selectedNodeIds.has(node.id);
      dom.minimapCanvas.appendChild(createSvg('rect', {
        class: `minimap-node${selected ? ' is-selected' : ''}`,
        'data-minimap-node-id': node.id,
        x: node.x,
        y: node.y,
        width: node.w,
        height: node.h,
        rx: 5
      }));
    });
    dom.minimapCanvas.appendChild(createSvg('rect', {
      class: 'minimap-viewport',
      x: viewport.x,
      y: viewport.y,
      width: viewport.w,
      height: viewport.h,
      rx: 7
    }));
  }

  function renderSelectionBox() {
    dom.selectionBoxLayer.replaceChildren();
    if (!selectionBoxState) return;
    const left = Math.min(selectionBoxState.startX, selectionBoxState.currentX);
    const top = Math.min(selectionBoxState.startY, selectionBoxState.currentY);
    const width = Math.abs(selectionBoxState.currentX - selectionBoxState.startX);
    const height = Math.abs(selectionBoxState.currentY - selectionBoxState.startY);
    dom.selectionBoxLayer.appendChild(createSvg('rect', {
      class: 'graph-selection-box',
      x: left,
      y: top,
      width,
      height
    }));
  }

  function renderAlignmentGuides() {
    dom.alignmentGuideLayer.replaceChildren();
    const canvasWidth = Number(graph.meta.canvasWidth) || 1440;
    const canvasHeight = Number(graph.meta.canvasHeight) || 1030;
    alignmentGuides.forEach(guide => {
      const attributes = guide.axis === 'x'
        ? { x1: guide.value, y1: 0, x2: guide.value, y2: canvasHeight }
        : { x1: 0, y1: guide.value, x2: canvasWidth, y2: guide.value };
      dom.alignmentGuideLayer.appendChild(createSvg('line', {
        class: 'graph-alignment-guide',
        ...attributes
      }));
    });
  }

  function calculateSnappedDelta(state, requestedDeltaX, requestedDeltaY) {
    if (!dom.snapToggle.checked) {
      alignmentGuides = [];
      return { x: requestedDeltaX, y: requestedDeltaY };
    }
    const rect = dom.svg.getBoundingClientRect();
    const threshold = Math.max(4, Math.min(14, rect.width ? viewport.w / rect.width * 8 : 8));
    const left = Math.min(...state.positions.map(position => position.x));
    const right = Math.max(...state.positions.map(position => position.x + position.w));
    const top = Math.min(...state.positions.map(position => position.y));
    const bottom = Math.max(...state.positions.map(position => position.y + position.h));
    const movingX = [left + requestedDeltaX, (left + right) / 2 + requestedDeltaX, right + requestedDeltaX];
    const movingY = [top + requestedDeltaY, (top + bottom) / 2 + requestedDeltaY, bottom + requestedDeltaY];
    const selectedIds = new Set(state.nodeIds);
    const targets = graph.nodes.filter(node => !selectedIds.has(node.id));
    let bestX = null;
    let bestY = null;

    targets.forEach(node => {
      const targetXs = [node.x, node.x + node.w / 2, node.x + node.w];
      const targetYs = [node.y, node.y + node.h / 2, node.y + node.h];
      movingX.forEach(moving => targetXs.forEach(target => {
        const difference = target - moving;
        if (Math.abs(difference) <= threshold && (!bestX || Math.abs(difference) < Math.abs(bestX.difference))) {
          bestX = { difference, value: target };
        }
      }));
      movingY.forEach(moving => targetYs.forEach(target => {
        const difference = target - moving;
        if (Math.abs(difference) <= threshold && (!bestY || Math.abs(difference) < Math.abs(bestY.difference))) {
          bestY = { difference, value: target };
        }
      }));
    });

    let deltaX = requestedDeltaX;
    let deltaY = requestedDeltaY;
    alignmentGuides = [];
    if (bestX) {
      deltaX += bestX.difference;
      alignmentGuides.push({ axis: 'x', value: bestX.value });
    } else {
      deltaX = Math.round((left + deltaX) / 10) * 10 - left;
    }
    if (bestY) {
      deltaY += bestY.difference;
      alignmentGuides.push({ axis: 'y', value: bestY.value });
    } else {
      deltaY = Math.round((top + deltaY) / 10) * 10 - top;
    }
    return { x: deltaX, y: deltaY };
  }

  function handleMinimapPointerDown(event) {
    if (event.button !== 0) return;
    event.preventDefault();
    event.stopPropagation();
    minimapPanState = { pointerId: event.pointerId };
    moveViewportFromMinimapEvent(event);
  }

  function moveViewportFromMinimapEvent(event) {
    const rect = dom.minimapCanvas.getBoundingClientRect();
    if (!rect.width || !rect.height) return;
    const canvasWidth = Number(graph.meta.canvasWidth) || 1440;
    const canvasHeight = Number(graph.meta.canvasHeight) || 1030;
    const x = Math.max(0, Math.min(rect.width, event.clientX - rect.left)) / rect.width * canvasWidth;
    const y = Math.max(0, Math.min(rect.height, event.clientY - rect.top)) / rect.height * canvasHeight;
    viewport.x = x - viewport.w / 2;
    viewport.y = y - viewport.h / 2;
    constrainViewport();
    scheduleViewportRender();
  }

  function handleMinimapKeydown(event) {
    const stepX = viewport.w * (event.shiftKey ? 0.25 : 0.08);
    const stepY = viewport.h * (event.shiftKey ? 0.25 : 0.08);
    const movement = {
      ArrowLeft: [-stepX, 0],
      ArrowRight: [stepX, 0],
      ArrowUp: [0, -stepY],
      ArrowDown: [0, stepY]
    }[event.key];
    if (movement) {
      event.preventDefault();
      viewport.x += movement[0];
      viewport.y += movement[1];
      constrainViewport();
      renderViewportOnly();
      return;
    }
    if (event.key === 'Home') {
      event.preventDefault();
      fitViewport();
    }
  }

  function renderLinkPreview() {
    if (!linkState || !linkState.moved) return;
    const source = getEntity(linkState.sourceType, linkState.sourceId);
    if (!source) return;
    const target = linkState.targetId ? getEntity(linkState.targetType, linkState.targetId) : null;
    const sourceSide = normalizeNodePortSide(linkState.sourceSide) || 'right';
    const start = getNodePortPoint(source, sourceSide);
    const targetSide = target ? getNodeConnectionSide(target, start) : '';
    const end = target ? getNodePortPoint(target, targetSide) : linkState.point;
    const distance = Math.hypot(end.x - start.x, end.y - start.y);
    const bend = Math.max(48, Math.min(170, distance * 0.42));
    const sourceVector = getNodePortSideVector(sourceSide);
    const targetVector = target
      ? getNodePortSideVector(targetSide)
      : { x: -sourceVector.x, y: -sourceVector.y };
    const lineType = normalizeEdgeLineType(linkState.lineType);
    let pathData = `M ${start.x} ${start.y} L ${end.x} ${end.y}`;
    if (lineType === 'polyline') {
      pathData = buildOrthogonalLinePath(start, end, sourceSide, targetSide);
    } else if (lineType === 'curve') {
      pathData = `M ${start.x} ${start.y} C ${start.x + sourceVector.x * bend} ${start.y + sourceVector.y * bend}, ` +
        `${end.x + targetVector.x * bend} ${end.y + targetVector.y * bend}, ${end.x} ${end.y}`;
    }
    const path = createSvg('path', {
      class: `graph-link-preview is-${lineType}${target ? ' is-valid' : ''}`,
      d: pathData,
      'marker-end': 'url(#arrow-link)'
    });
    dom.linkPreviewLayer.appendChild(path);
  }

  function buildOrthogonalLinePath(start, end, sourceSide, requestedTargetSide) {
    const targetSide = normalizeNodePortSide(requestedTargetSide) || ({
      top: 'bottom',
      right: 'left',
      bottom: 'top',
      left: 'right'
    }[sourceSide] || 'left');
    const distance = Math.hypot(end.x - start.x, end.y - start.y);
    const stub = Math.max(18, Math.min(34, distance * 0.18));
    const sourceVector = getNodePortSideVector(sourceSide);
    const targetVector = getNodePortSideVector(targetSide);
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
    const simplified = points.filter((point, index) => {
      const previous = points[index - 1];
      return !previous || Math.hypot(point.x - previous.x, point.y - previous.y) > 0.1;
    });
    return simplified.map((point, index) => `${index ? 'L' : 'M'} ${point.x} ${point.y}`).join(' ');
  }

  function getNodeConnectionSide(node, fromPoint) {
    const centerX = node.x + node.w / 2;
    const centerY = node.y + node.h / 2;
    const normalizedX = (fromPoint.x - centerX) / Math.max(1, node.w / 2);
    const normalizedY = (fromPoint.y - centerY) / Math.max(1, node.h / 2);
    if (Math.abs(normalizedX) >= Math.abs(normalizedY)) return normalizedX <= 0 ? 'left' : 'right';
    return normalizedY <= 0 ? 'top' : 'bottom';
  }

  function getNodePortPoint(node, side) {
    if (side === 'top') return { x: node.x + node.w / 2, y: node.y };
    if (side === 'bottom') return { x: node.x + node.w / 2, y: node.y + node.h };
    if (side === 'left') return { x: node.x, y: node.y + node.h / 2 };
    return { x: node.x + node.w, y: node.y + node.h / 2 };
  }

  function getNodePortSideVector(side) {
    if (side === 'top') return { x: 0, y: -1 };
    if (side === 'bottom') return { x: 0, y: 1 };
    if (side === 'left') return { x: -1, y: 0 };
    return { x: 1, y: 0 };
  }

  function getNodePortSideLabel(side) {
    return { top: '上方', right: '右侧', bottom: '下方', left: '左侧' }[side] || '右侧';
  }

  function getNearestNodePortSide(node, point) {
    const distances = [
      ['top', Math.abs(point.y - node.y)],
      ['right', Math.abs(point.x - (node.x + node.w))],
      ['bottom', Math.abs(point.y - (node.y + node.h))],
      ['left', Math.abs(point.x - node.x)]
    ];
    distances.sort((first, second) => first[1] - second[1]);
    return distances[0][0];
  }

  function getDragDirectionSide(origin, point, fallback) {
    const dx = point.x - origin.x;
    const dy = point.y - origin.y;
    if (Math.hypot(dx, dy) < 5) return fallback;
    if (Math.abs(dx) >= Math.abs(dy)) return dx >= 0 ? 'right' : 'left';
    return dy >= 0 ? 'bottom' : 'top';
  }

  function calculateEdgeGeometry(source, target, index, edge = null) {
    if (window.GraphRouting?.calculateEdgeGeometry) {
      return window.GraphRouting.calculateEdgeGeometry(source, target, index, edge);
    }
    const sx = source.x + source.w / 2;
    const sy = source.y + source.h / 2;
    const tx = target.x + target.w / 2;
    const ty = target.y + target.h / 2;
    return {
      d: `M ${sx} ${sy} L ${tx} ${ty}`,
      lx: (sx + tx) / 2,
      ly: (sy + ty) / 2,
      routeQuality: 'emergency'
    };
  }

  function calculateFocusedEdgeGeometries(edges, nodeMap, blockingNodeIds) {
    if (!window.GraphRouting?.calculateFocusedEdgeGeometries) return new Map();
    return window.GraphRouting.calculateFocusedEdgeGeometries(
      edges,
      nodeMap,
      blockingNodeIds,
      graph.meta
    );
  }
  function splitLabel(label) {
    const normalized = String(label || '').trim();
    if (!normalized) return ['未命名'];
    if (normalized.length <= 10) return [normalized];

    const slashIndex = normalized.lastIndexOf('/', Math.ceil(normalized.length / 2));
    const splitIndex = slashIndex > 1 ? slashIndex + 1 : Math.ceil(normalized.length / 2);
    const first = normalized.slice(0, splitIndex);
    const second = normalized.slice(splitIndex);
    if (second.length > 10) {
      const secondSplit = Math.ceil(second.length / 2);
      return [first, second.slice(0, secondSplit), second.slice(secondSplit)];
    }
    return [first, second];
  }

  function computeFocus(visibleCategories) {
    const result = {
      isFocused: selection.type === 'node' || selection.type === 'group' || selection.type === 'edge',
      nodeIds: new Set(),
      entityKeys: new Set(),
      edgeIds: new Set()
    };

    if (!selection.type) return result;

    if (selection.type === 'edge') {
      const edge = graph.edges.find(item => item.id === selection.id);
      if (!edge) return result;
      const source = getEdgeEndpoint(edge, 'source');
      const target = getEdgeEndpoint(edge, 'target');
      result.edgeIds.add(edge.id);
      if (source) {
        result.entityKeys.add(endpointKey(source.type, source.id));
        if (source.type === 'node') result.nodeIds.add(source.id);
      }
      if (target) {
        result.entityKeys.add(endpointKey(target.type, target.id));
        if (target.type === 'node') result.nodeIds.add(target.id);
      }
      return result;
    }

    const depth = Math.max(1, Math.min(3, Number(dom.depthFilter.value) || 1));
    const direction = dom.directionFilter.value;
    const initialKey = endpointKey(selection.type, selection.id);
    const queue = [{ key: initialKey, level: 0 }];
    let queueIndex = 0;
    const visited = new Map([[initialKey, 0]]);
    const adjacency = new Map();
    const addNeighbor = (from, to, edgeId) => {
      if (!adjacency.has(from)) adjacency.set(from, []);
      adjacency.get(from).push({ key: to, edgeId });
    };
    graph.edges.forEach(edge => {
      if (!visibleCategories.has(edge.category)) return;
      const sourceKey = getEdgeEndpointKey(edge, 'source');
      const targetKey = getEdgeEndpointKey(edge, 'target');
      if (direction === 'all' || direction === 'downstream') addNeighbor(sourceKey, targetKey, edge.id);
      if (direction === 'all' || direction === 'upstream') addNeighbor(targetKey, sourceKey, edge.id);
    });
    result.entityKeys.add(initialKey);
    if (selection.type === 'node') result.nodeIds.add(selection.id);

    while (queueIndex < queue.length) {
      const current = queue[queueIndex];
      queueIndex += 1;
      if (current.level >= depth) continue;

      (adjacency.get(current.key) || []).forEach(neighbor => {
        result.edgeIds.add(neighbor.edgeId);
        result.entityKeys.add(neighbor.key);
        const parsedNeighbor = parseEndpointKey(neighbor.key);
        if (parsedNeighbor.type === 'node') result.nodeIds.add(parsedNeighbor.id);
        const nextLevel = current.level + 1;
        if (!visited.has(neighbor.key) || visited.get(neighbor.key) > nextLevel) {
          visited.set(neighbor.key, nextLevel);
          queue.push({ key: neighbor.key, level: nextLevel });
        }
      });
    }

    return result;
  }

  function selectNode(nodeId, event = null) {
    const additive = Boolean(event && (event.shiftKey || event.ctrlKey || event.metaKey));
    if (additive) {
      if (selectedNodeIds.has(nodeId)) selectedNodeIds.delete(nodeId);
      else selectedNodeIds.add(nodeId);
      const nextPrimary = selectedNodeIds.has(nodeId) ? nodeId : [...selectedNodeIds].at(-1);
      selection = nextPrimary ? { type: 'node', id: nextPrimary } : { type: null, id: null };
    } else {
      selectedNodeIds = new Set([nodeId]);
      selection = { type: 'node', id: nodeId };
    }
    renderAll();
  }

  function selectEdge(edgeId) {
    selectedNodeIds.clear();
    selection = { type: 'edge', id: edgeId };
    renderAll();
  }

  function selectGroup(groupId) {
    selectedNodeIds.clear();
    selection = { type: 'group', id: groupId };
    renderAll();
  }

  function clearSelection() {
    selectedNodeIds.clear();
    selection = { type: null, id: null };
  }

  function renderInspector() {
    const multiNodeSelection = selectedNodeIds.size > 1;
    dom.emptyInspector.hidden = Boolean(selection.type) || multiNodeSelection;
    dom.batchInspector.hidden = !multiNodeSelection;
    dom.nodeInspector.hidden = selection.type !== 'node' || multiNodeSelection;
    dom.groupInspector.hidden = selection.type !== 'group';
    dom.edgeInspector.hidden = selection.type !== 'edge';

    if (multiNodeSelection) {
      dom.batchInspectorTitle.textContent = `已选择 ${selectedNodeIds.size} 个节点`;
      dom.batchSelectionCount.textContent = `${selectedNodeIds.size} 项`;
      dom.batchKindInput.value = '';
      dom.batchGroupInput.value = BATCH_GROUP_KEEP;
    }

    if (selection.type === 'group') {
      const group = graph.groups.find(item => item.id === selection.id);
      if (!group) {
        clearSelection();
        renderInspector();
        return;
      }
      dom.groupInspectorTitle.textContent = group.label;
      dom.groupLabelInput.value = group.label;
      const incoming = graph.edges.filter(edge => getEdgeEndpointKey(edge, 'target') === endpointKey('group', group.id));
      const outgoing = graph.edges.filter(edge => getEdgeEndpointKey(edge, 'source') === endpointKey('group', group.id));
      dom.groupIncomingCount.textContent = String(incoming.length);
      dom.groupOutgoingCount.textContent = String(outgoing.length);
      renderRelationList(dom.groupIncomingList, incoming, true);
      renderRelationList(dom.groupOutgoingList, outgoing, false);
    }

    if (selection.type === 'node' && !multiNodeSelection) {
      const node = graph.nodes.find(item => item.id === selection.id);
      if (!node) {
        clearSelection();
        renderInspector();
        return;
      }
      dom.nodeInspectorTitle.textContent = node.label;
      dom.nodeTypeChip.textContent = getNodeTypeLabel(node);
      dom.nodeNoteView.textContent = node.note || '暂无说明。';
      dom.nodeLabelInput.value = node.label;
      dom.nodeKindInput.value = getNodeTypeLabel(node);
      dom.nodeGroupInput.value = node.group;
      dom.nodeNoteInput.value = node.note;

      const incoming = graph.edges.filter(edge => getEdgeEndpointKey(edge, 'target') === endpointKey('node', node.id));
      const outgoing = graph.edges.filter(edge => getEdgeEndpointKey(edge, 'source') === endpointKey('node', node.id));
      dom.incomingCount.textContent = String(incoming.length);
      dom.outgoingCount.textContent = String(outgoing.length);
      renderRelationList(dom.incomingList, incoming, true);
      renderRelationList(dom.outgoingList, outgoing, false);
    }

    if (selection.type === 'edge') {
      const edge = graph.edges.find(item => item.id === selection.id);
      if (!edge) {
        clearSelection();
        renderInspector();
        return;
      }
      const source = getEdgeEndpoint(edge, 'source');
      const target = getEdgeEndpoint(edge, 'target');
      dom.edgeInspectorTitle.textContent = edge.label || '未命名关系';
      dom.edgeTypeChip.textContent = getEdgeCategoryName(edge.category);
      dom.edgeSourceView.textContent = source ? getEndpointLabel(source.type, source.entity) : edge.source;
      dom.edgeTargetView.textContent = target ? getEndpointLabel(target.type, target.entity) : edge.target;
      dom.edgeSourceInput.value = getEdgeEndpointKey(edge, 'source');
      dom.edgeTargetInput.value = getEdgeEndpointKey(edge, 'target');
      dom.edgeLabelInput.value = edge.label;
      dom.edgeCategoryInput.value = edge.category;
    }
  }

  function renderRelationList(container, edges, incoming) {
    container.replaceChildren();
    if (!edges.length) {
      const empty = document.createElement('span');
      empty.className = 'empty-list';
      empty.textContent = '暂无关系';
      container.appendChild(empty);
      return;
    }

    edges.forEach(edge => {
      const counterpart = getEdgeEndpoint(edge, incoming ? 'source' : 'target');
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'relation-item';
      const title = document.createElement('strong');
      title.textContent = edge.label || '未命名关系';
      const detail = document.createElement('span');
      detail.textContent = `${incoming ? '来自' : '流向'}：${counterpart ? getEndpointLabel(counterpart.type, counterpart.entity) : '未知端点'} · ${getEdgeCategoryName(edge.category)}`;
      button.append(title, detail);
      button.addEventListener('click', () => selectEdge(edge.id));
      container.appendChild(button);
    });
  }

  function refreshEntityIndexes() {
    if (
      indexedGraph === graph &&
      indexedNodes === graph.nodes &&
      indexedGroups === graph.groups &&
      indexedNodeCount === graph.nodes.length &&
      indexedGroupCount === graph.groups.length
    ) return;
    nodeIndex = new Map(graph.nodes.map(node => [node.id, node]));
    groupIndex = new Map(graph.groups.map(group => [group.id, group]));
    indexedGraph = graph;
    indexedNodes = graph.nodes;
    indexedGroups = graph.groups;
    indexedNodeCount = graph.nodes.length;
    indexedGroupCount = graph.groups.length;
  }

  function getNode(nodeId) {
    refreshEntityIndexes();
    return nodeIndex.get(nodeId);
  }

  function getGroup(groupId) {
    refreshEntityIndexes();
    return groupIndex.get(groupId);
  }

  function getEntity(type, id) {
    return type === 'group' ? getGroup(id) : getNode(id);
  }

  function getEdgeEndpointType(edge, role) {
    const id = String(edge?.[role] || '');
    const requested = String(edge?.[`${role}Type`] || '').toLowerCase();
    if (requested === 'node' || requested === 'group') return requested;
    if (getNode(id)) return 'node';
    if (getGroup(id)) return 'group';
    return '';
  }

  function getEdgeEndpoint(edge, role) {
    const type = getEdgeEndpointType(edge, role);
    const id = String(edge?.[role] || '');
    const entity = getEntity(type, id);
    return entity ? { type, id, entity } : null;
  }

  function endpointKey(type, id) {
    return `${type}:${id}`;
  }

  function getEdgeEndpointKey(edge, role) {
    return endpointKey(getEdgeEndpointType(edge, role), edge?.[role] || '');
  }

  function parseEndpointKey(value) {
    const text = String(value || '');
    const separator = text.indexOf(':');
    if (separator < 1) return { type: '', id: '' };
    return { type: text.slice(0, separator), id: text.slice(separator + 1) };
  }

  function getEndpointLabel(type, entity) {
    if (!entity) return '未知端点';
    return type === 'group' ? `分组｜${entity.label}` : `节点｜${entity.label}`;
  }

  function edgeMatchesEndpoints(edge, source, target) {
    return getEdgeEndpointKey(edge, 'source') === endpointKey(source.type, source.id) &&
      getEdgeEndpointKey(edge, 'target') === endpointKey(target.type, target.id);
  }

  function edgeReferencesEndpoint(edge, type, id) {
    const key = endpointKey(type, id);
    return getEdgeEndpointKey(edge, 'source') === key || getEdgeEndpointKey(edge, 'target') === key;
  }

  function populateGroupOptions() {
    const current = dom.nodeGroupInput.value;
    const batchCurrent = dom.batchGroupInput.value;
    dom.nodeGroupInput.replaceChildren();
    dom.batchGroupInput.replaceChildren();
    const ungroupedOption = document.createElement('option');
    ungroupedOption.value = '';
    ungroupedOption.textContent = '未分组';
    dom.nodeGroupInput.appendChild(ungroupedOption);
    const keepOption = document.createElement('option');
    keepOption.value = BATCH_GROUP_KEEP;
    keepOption.textContent = '保持各自分组';
    dom.batchGroupInput.appendChild(keepOption);
    const batchUngroupedOption = ungroupedOption.cloneNode(true);
    batchUngroupedOption.textContent = '设为未分组';
    dom.batchGroupInput.appendChild(batchUngroupedOption);
    graph.groups.forEach(group => {
      const option = document.createElement('option');
      option.value = group.id;
      option.textContent = group.label;
      dom.nodeGroupInput.appendChild(option);
      dom.batchGroupInput.appendChild(option.cloneNode(true));
    });
    if (!current || graph.groups.some(group => group.id === current)) dom.nodeGroupInput.value = current;
    if (batchCurrent === BATCH_GROUP_KEEP || !batchCurrent || graph.groups.some(group => group.id === batchCurrent)) {
      dom.batchGroupInput.value = batchCurrent;
    }
  }

  function populateNodeSelects() {
    const selects = [
      dom.edgeSourceInput,
      dom.edgeTargetInput,
      dom.createRelationSourceInput,
      dom.createRelationTargetInput
    ];
    const values = selects.map(select => select.value);
    selects.forEach(select => select.replaceChildren());

    const endpoints = [
      ...[...graph.nodes]
        .sort((a, b) => a.label.localeCompare(b.label, 'zh-CN'))
        .map(node => ({ type: 'node', id: node.id, label: `${node.label}（${getNodeTypeLabel(node)}）` })),
      ...[...graph.groups]
        .sort((a, b) => a.label.localeCompare(b.label, 'zh-CN'))
        .map(group => ({ type: 'group', id: group.id, label: group.label }))
    ];
    endpoints.forEach(endpoint => {
      selects.forEach(select => {
        const option = document.createElement('option');
        option.value = endpointKey(endpoint.type, endpoint.id);
        option.textContent = `${endpoint.type === 'group' ? '分组' : '节点'}｜${endpoint.label}`;
        select.appendChild(option);
      });
    });
    selects.forEach((select, index) => {
      if (endpoints.some(endpoint => endpointKey(endpoint.type, endpoint.id) === values[index])) {
        select.value = values[index];
      }
    });
    populateNodeTypeSuggestions();
  }

  function keepRelationEndpointsDistinct() {
    if (dom.createRelationSourceInput.value !== dom.createRelationTargetInput.value) return;
    const alternative = Array.from(dom.createRelationTargetInput.options)
      .find(option => option.value !== dom.createRelationSourceInput.value);
    if (alternative) dom.createRelationTargetInput.value = alternative.value;
  }

  function openCreateRelationDialog() {
    const endpoints = [
      ...graph.nodes.map(node => ({ type: 'node', id: node.id })),
      ...graph.groups.map(group => ({ type: 'group', id: group.id }))
    ];
    if (endpoints.length < 2) {
      showToast('至少需要两个节点或分组才能创建关系。', true);
      return;
    }
    if (mode !== 'edit') setMode('edit');
    populateNodeSelects();
    populateRelationTypeOptions();
    const selectedSource = selection.type === 'node' || selection.type === 'group'
      ? { type: selection.type, id: selection.id }
      : endpoints[0];
    dom.createRelationSourceInput.value = endpointKey(selectedSource.type, selectedSource.id);
    keepRelationEndpointsDistinct();
    dom.createRelationLabelInput.value = '';
    dom.createRelationCategoryInput.value = getRelationTypes()[0].id;
    dom.createRelationDialog.showModal();
    requestAnimationFrame(() => {
      dom.createRelationLabelInput.focus();
      dom.createRelationLabelInput.select();
    });
  }

  function createRelationFromDialog(event) {
    event.preventDefault();
    const source = parseEndpointKey(dom.createRelationSourceInput.value);
    const target = parseEndpointKey(dom.createRelationTargetInput.value);
    const label = dom.createRelationLabelInput.value.trim();
    if (!source.id || !target.id || endpointKey(source.type, source.id) === endpointKey(target.type, target.id)) {
      showToast('关系起点和终点必须是两个不同的节点或分组。', true);
      return;
    }
    const existing = graph.edges.find(edge => edgeMatchesEndpoints(edge, source, target));
    if (existing) {
      dom.createRelationDialog.close();
      selectEdge(existing.id);
      showToast('这两个端点已有同方向关系，已为你选中。');
      return;
    }
    const id = uniqueId('edge', new Set(graph.edges.map(edge => edge.id)));
    dom.createRelationDialog.close();
    commit(() => {
      graph.edges.push({
        id,
        source: source.id,
        target: target.id,
        sourceType: source.type,
        targetType: target.type,
        label,
        category: dom.createRelationCategoryInput.value,
        lineType: getSelectedNewEdgeLineType()
      });
      selection = { type: 'edge', id };
      selectedNodeIds.clear();
    }, '关系已创建');
  }

  function populateNodeTypeSuggestions() {
    dom.nodeTypeSuggestions.replaceChildren();
    const seen = new Set();
    const labels = [
      ...(graph.settings?.nodeTypes || []).map(type => type.label),
      ...graph.nodes.map(node => getNodeTypeLabel(node))
    ];
    labels.forEach(labelValue => {
      const label = normalizeNodeTypeLabel(labelValue, '');
      const key = label.toLocaleLowerCase('zh-CN');
      if (!label || seen.has(key)) return;
      seen.add(key);
      const option = document.createElement('option');
      option.value = label;
      dom.nodeTypeSuggestions.appendChild(option);
    });
  }

  function applyNodeForm(event) {
    event.preventDefault();
    if (mode !== 'edit' || selection.type !== 'node') return;
    const nodeId = selection.id;
    const label = dom.nodeLabelInput.value.trim();
    if (!label) {
      showToast('节点名称不能为空。', true);
      return;
    }
    const typeLabel = normalizeNodeTypeLabel(dom.nodeKindInput.value, '');
    if (!typeLabel) {
      showToast('节点类型不能为空。你可以输入任意名称。', true);
      dom.nodeKindInput.focus();
      return;
    }
    commit(() => {
      const node = getNode(nodeId);
      if (!node) return;
      const previousGroup = node.group;
      const nextGroup = dom.nodeGroupInput.value;
      node.label = label;
      const typeDefinition = ensureNodeTypeInSettings(graph.settings, typeLabel);
      node.type = typeDefinition.label;
      node.kind = typeDefinition.kind;
      node.group = nextGroup;
      node.note = dom.nodeNoteInput.value.trim();
      if (previousGroup !== nextGroup) {
        placeNodeInGroup(node, nextGroup);
      }
    }, '节点已更新');
  }

  function applyBatchForm(event) {
    event.preventDefault();
    if (mode !== 'edit' || selectedNodeIds.size < 2) return;
    const typeLabel = normalizeNodeTypeLabel(dom.batchKindInput.value, '');
    const groupId = dom.batchGroupInput.value;
    const updateGroup = groupId !== BATCH_GROUP_KEEP;
    if (!typeLabel && !updateGroup) {
      showToast('请选择需要统一修改的类型或分组。', true);
      return;
    }
    const selectedIds = new Set(selectedNodeIds);
    commit(() => {
      const selectedNodes = graph.nodes.filter(node => selectedIds.has(node.id));
      if (typeLabel) {
        const typeDefinition = ensureNodeTypeInSettings(graph.settings, typeLabel);
        selectedNodes.forEach(node => {
          node.type = typeDefinition.label;
          node.kind = typeDefinition.kind;
        });
      }
      if (updateGroup && !groupId) {
        selectedNodes.forEach(node => { node.group = ''; });
      } else if (updateGroup) {
        const group = getGroup(groupId);
        const existingCount = graph.nodes.filter(node => node.group === groupId && !selectedIds.has(node.id)).length;
        const horizontalGap = 14;
        const verticalGap = 14;
        const nodeWidth = Math.max(...selectedNodes.map(node => node.w), 150);
        const usableWidth = Math.max(nodeWidth, group.w - 32);
        const columns = Math.max(1, Math.floor((usableWidth + horizontalGap) / (nodeWidth + horizontalGap)));
        selectedNodes.forEach((node, index) => {
          const slot = existingCount + index;
          const column = slot % columns;
          const row = Math.floor(slot / columns);
          node.group = groupId;
          node.x = roundToFive(group.x + 16 + column * (nodeWidth + horizontalGap) + (nodeWidth - node.w) / 2);
          node.y = roundToFive(group.y + 42 + row * (node.h + verticalGap));
        });
        const neededBottom = Math.max(...selectedNodes.map(node => node.y + node.h + 16));
        const canvasHeight = Number(graph.meta.canvasHeight) || 1030;
        group.h = Math.min(canvasHeight - group.y, Math.max(group.h, neededBottom - group.y));
      }
    }, `已批量更新 ${selectedNodeIds.size} 个节点`);
  }

  function applyGroupForm(event) {
    event.preventDefault();
    if (mode !== 'edit' || selection.type !== 'group') return;
    const groupId = selection.id;
    const label = dom.groupLabelInput.value.trim();
    if (!label) {
      showToast('分组名称不能为空。', true);
      return;
    }
    commit(() => {
      const group = getGroup(groupId);
      if (group) group.label = label;
    }, '分组已更新');
  }

  function placeNodeInGroup(node, groupId) {
    const group = graph.groups.find(item => item.id === groupId);
    if (!group) return;
    const peers = graph.nodes.filter(item => item.id !== node.id && item.group === groupId);
    const horizontalGap = 14;
    const verticalGap = 14;
    const usableWidth = Math.max(node.w, group.w - 32);
    const columns = Math.max(1, Math.floor((usableWidth + horizontalGap) / (node.w + horizontalGap)));
    const slot = peers.length;
    const column = slot % columns;
    const row = Math.floor(slot / columns);
    node.x = group.x + 16 + column * (node.w + horizontalGap);
    node.y = group.y + 42 + row * (node.h + verticalGap);
  }

  function autoLayout() {
    if (mode !== 'edit') setMode('edit');
    commit(() => {
      const orderedGroups = [...graph.groups].sort((a, b) => {
        if (a.x !== b.x) return a.x - b.x;
        return a.y - b.y;
      });
      const sweeps = [
        orderedGroups,
        [...orderedGroups].reverse(),
        orderedGroups
      ];
      sweeps.forEach(groups => {
        groups.forEach(layoutGroupNodes);
      });
      layoutUngroupedNodes();
    }, '自动排版完成，可使用撤销恢复');
    fitViewport();
  }

  function layoutGroupNodes(group) {
    const groupNodes = graph.nodes.filter(node => node.group === group.id);
    if (!groupNodes.length) return;

    const originalOrder = new Map(
      groupNodes.map((node, index) => [node.id, { y: node.y, index }])
    );
    groupNodes.sort((a, b) => {
      const scoreDifference = connectedVerticalScore(a) - connectedVerticalScore(b);
      if (Math.abs(scoreDifference) > 0.5) return scoreDifference;
      const yDifference = originalOrder.get(a.id).y - originalOrder.get(b.id).y;
      if (Math.abs(yDifference) > 0.5) return yDifference;
      return originalOrder.get(a.id).index - originalOrder.get(b.id).index;
    });

    const horizontalPadding = 16;
    const topPadding = 42;
    const bottomPadding = 16;
    const horizontalGap = 14;
    const maxNodeWidth = Math.max(...groupNodes.map(node => node.w));
    const usableWidth = Math.max(maxNodeWidth, group.w - horizontalPadding * 2);
    const columns = Math.max(
      1,
      Math.floor((usableWidth + horizontalGap) / (maxNodeWidth + horizontalGap))
    );
    const rows = Math.ceil(groupNodes.length / columns);
    const rowHeights = Array.from({ length: rows }, () => 0);
    groupNodes.forEach((node, index) => {
      const row = Math.floor(index / columns);
      rowHeights[row] = Math.max(rowHeights[row], node.h);
    });

    const availableHeight = Math.max(
      rowHeights.reduce((sum, height) => sum + height, 0),
      group.h - topPadding - bottomPadding
    );
    const nodeHeightTotal = rowHeights.reduce((sum, height) => sum + height, 0);
    const verticalGap = rows > 1
      ? Math.max(8, Math.min(28, (availableHeight - nodeHeightTotal) / (rows - 1)))
      : 0;
    const contentHeight = nodeHeightTotal + verticalGap * Math.max(0, rows - 1);
    const gridWidth = columns * maxNodeWidth + horizontalGap * Math.max(0, columns - 1);
    const startX = group.x + Math.max(horizontalPadding, (group.w - gridWidth) / 2);
    let rowY = group.y + topPadding + Math.max(0, (availableHeight - contentHeight) / 2);

    for (let row = 0; row < rows; row += 1) {
      const rowHeight = rowHeights[row];
      for (let column = 0; column < columns; column += 1) {
        const index = row * columns + column;
        const node = groupNodes[index];
        if (!node) continue;
        node.x = roundToFive(
          startX + column * (maxNodeWidth + horizontalGap) + (maxNodeWidth - node.w) / 2
        );
        node.y = roundToFive(rowY + (rowHeight - node.h) / 2);
      }
      rowY += rowHeight + verticalGap;
    }
  }

  function layoutUngroupedNodes() {
    const nodes = graph.nodes
      .filter(node => !getGroup(node.group))
      .sort((first, second) => connectedVerticalScore(first) - connectedVerticalScore(second));
    if (!nodes.length) return;
    const canvasWidth = Number(graph.meta.canvasWidth) || 1440;
    const canvasHeight = Number(graph.meta.canvasHeight) || 1030;
    const paddingX = 30;
    const paddingY = 55;
    const gapX = 28;
    const gapY = 24;
    const maxNodeWidth = Math.max(...nodes.map(node => node.w));
    const maxNodeHeight = Math.max(...nodes.map(node => node.h));
    const columns = Math.max(1, Math.floor(
      (canvasWidth - paddingX * 2 + gapX) / (maxNodeWidth + gapX)
    ));
    nodes.forEach((node, index) => {
      const column = index % columns;
      const row = Math.floor(index / columns);
      node.group = '';
      node.x = roundToFive(Math.min(
        canvasWidth - node.w,
        paddingX + column * (maxNodeWidth + gapX) + (maxNodeWidth - node.w) / 2
      ));
      node.y = roundToFive(Math.min(
        canvasHeight - node.h,
        paddingY + row * (maxNodeHeight + gapY) + (maxNodeHeight - node.h) / 2
      ));
    });
  }

  function connectedVerticalScore(node) {
    const neighborYs = [];
    graph.edges.forEach(edge => {
      let neighborId = null;
      if (getEdgeEndpointKey(edge, 'source') === endpointKey('node', node.id) && getEdgeEndpointType(edge, 'target') === 'node') {
        neighborId = edge.target;
      } else if (getEdgeEndpointKey(edge, 'target') === endpointKey('node', node.id) && getEdgeEndpointType(edge, 'source') === 'node') {
        neighborId = edge.source;
      }
      if (!neighborId) return;
      const neighbor = getNode(neighborId);
      if (neighbor) neighborYs.push(neighbor.y + neighbor.h / 2);
    });
    if (!neighborYs.length) return node.y + node.h / 2;
    return neighborYs.reduce((sum, value) => sum + value, 0) / neighborYs.length;
  }

  function roundToFive(value) {
    return Math.round(value / 5) * 5;
  }

  function applyEdgeForm(event) {
    event.preventDefault();
    if (mode !== 'edit' || selection.type !== 'edge') return;
    const edgeId = selection.id;
    const label = dom.edgeLabelInput.value.trim();
    const source = parseEndpointKey(dom.edgeSourceInput.value);
    const target = parseEndpointKey(dom.edgeTargetInput.value);
    if (endpointKey(source.type, source.id) === endpointKey(target.type, target.id)) {
      showToast('起点和终点不能相同。', true);
      return;
    }
    const duplicate = graph.edges.some(edge =>
      edge.id !== edgeId &&
      edgeMatchesEndpoints(edge, source, target)
    );
    if (duplicate) {
      showToast('这两个端点已经存在同方向关系。', true);
      return;
    }
    commit(() => {
      const edge = graph.edges.find(item => item.id === edgeId);
      if (!edge) return;
      const endpointsChanged = !edgeMatchesEndpoints(edge, source, target);
      edge.source = source.id;
      edge.target = target.id;
      edge.sourceType = source.type;
      edge.targetType = target.type;
      edge.label = label;
      edge.category = dom.edgeCategoryInput.value;
      if (endpointsChanged) {
        delete edge.sourceSide;
        delete edge.targetSide;
      }
    }, '关系已更新');
  }

  function addNode() {
    if (mode !== 'edit') setMode('edit');
    const id = uniqueId('node', new Set(graph.nodes.map(node => node.id)));
    const centerX = viewport.x + viewport.w / 2;
    const centerY = viewport.y + viewport.h / 2;
    const canvasWidth = Number(graph.meta.canvasWidth) || 1440;
    const canvasHeight = Number(graph.meta.canvasHeight) || 1030;
    const x = Math.max(0, Math.min(
      canvasWidth - 150,
      Math.round((centerX - 75) / 10) * 10
    ));
    const y = Math.max(0, Math.min(
      canvasHeight - 54,
      Math.round((centerY - 27) / 10) * 10
    ));
    const group = findGroupForPoint(graph.groups, x + 75, y + 27);
    commit(() => {
      graph.nodes.push({
        id,
        label: '新节点',
        type: '未分类',
        kind: 'system',
        group,
        x,
        y,
        w: 150,
        h: 54,
        note: ''
      });
      selection = { type: 'node', id };
    }, '已新增节点');
    requestAnimationFrame(() => dom.nodeLabelInput.select());
  }

  function addGroup() {
    if (mode !== 'edit') setMode('edit');
    const id = uniqueId('group', new Set(graph.groups.map(group => group.id)));
    const canvasWidth = Number(graph.meta.canvasWidth) || 1440;
    const canvasHeight = Number(graph.meta.canvasHeight) || 1030;
    const width = Math.min(260, canvasWidth);
    const height = Math.min(300, canvasHeight);
    const centerX = viewport.x + viewport.w / 2;
    const centerY = viewport.y + viewport.h / 2;
    const x = roundToFive(Math.max(0, Math.min(canvasWidth - width, centerX - width / 2)));
    const y = roundToFive(Math.max(0, Math.min(canvasHeight - height, centerY - height / 2)));
    commit(() => {
      graph.groups.push({
        id,
        label: '新分组',
        x,
        y,
        w: width,
        h: height
      });
      selection = { type: 'group', id };
    }, '已新增分组，可拖动边界调整范围');
    requestAnimationFrame(() => {
      dom.groupLabelInput.focus();
      dom.groupLabelInput.select();
    });
  }

  function duplicateSelectedNode() {
    if (mode !== 'edit' || selection.type !== 'node') return;
    const original = getNode(selection.id);
    if (!original) return;
    const id = uniqueId(`${original.id}_copy`, new Set(graph.nodes.map(node => node.id)));
    const canvasWidth = Number(graph.meta.canvasWidth) || 1440;
    const canvasHeight = Number(graph.meta.canvasHeight) || 1030;
    commit(() => {
      graph.nodes.push({
        ...deepClone(original),
        id,
        label: `${original.label} 副本`,
        x: Math.max(0, Math.min(canvasWidth - original.w, original.x + 30)),
        y: Math.max(0, Math.min(canvasHeight - original.h, original.y + 30))
      });
      selection = { type: 'node', id };
    }, '已复制节点');
  }

  function buildClipboardPayload() {
    const nodeIds = new Set();
    const groupIds = new Set();
    let explicitEdges = null;
    if (selection.type === 'node') {
      selectedNodeIds.forEach(id => nodeIds.add(id));
    } else if (selection.type === 'group') {
      groupIds.add(selection.id);
      graph.nodes.filter(node => node.group === selection.id).forEach(node => nodeIds.add(node.id));
    } else if (selection.type === 'edge') {
      const edge = graph.edges.find(item => item.id === selection.id);
      if (edge) {
        const source = getEdgeEndpoint(edge, 'source');
        const target = getEdgeEndpoint(edge, 'target');
        if (source?.type === 'node') nodeIds.add(source.id);
        if (source?.type === 'group') groupIds.add(source.id);
        if (target?.type === 'node') nodeIds.add(target.id);
        if (target?.type === 'group') groupIds.add(target.id);
        explicitEdges = [edge];
      }
    }
    const nodes = graph.nodes.filter(node => nodeIds.has(node.id));
    nodes.forEach(node => { if (node.group) groupIds.add(node.group); });
    const groups = graph.groups.filter(group => groupIds.has(group.id));
    const copiedEndpointKeys = new Set([
      ...nodes.map(node => endpointKey('node', node.id)),
      ...groups.map(group => endpointKey('group', group.id))
    ]);
    const edges = explicitEdges || graph.edges.filter(edge =>
      copiedEndpointKeys.has(getEdgeEndpointKey(edge, 'source')) &&
      copiedEndpointKeys.has(getEdgeEndpointKey(edge, 'target'))
    );
    if (!nodes.length && !groups.length) return null;
    return {
      kind: 'relationship-graph-editor/selection',
      version: 1,
      sourceProjectId: activeProjectId,
      sourceProjectName: graph.meta?.title || '',
      selectionType: selection.type,
      copiedAt: new Date().toISOString(),
      groups: deepClone(groups),
      nodes: deepClone(nodes),
      edges: deepClone(edges)
    };
  }

  async function copySelection() {
    const payload = buildClipboardPayload();
    if (!payload) {
      showToast('当前没有可复制的内容。', true);
      return;
    }
    const text = JSON.stringify(payload);
    let systemClipboardSaved = false;
    try {
      await navigator.clipboard?.writeText(text);
      systemClipboardSaved = Boolean(navigator.clipboard);
    } catch {
      systemClipboardSaved = false;
    }
    try {
      localStorage.setItem(CLIPBOARD_KEY, text);
    } catch {
      if (!systemClipboardSaved) {
        showToast('浏览器不允许写入剪贴板。', true);
        return;
      }
    }
    showToast(`已复制 ${payload.nodes.length} 个节点和 ${payload.edges.length} 条关系，可切换项目后粘贴`);
  }

  async function readClipboardPayload() {
    const candidates = [];
    try {
      if (navigator.clipboard?.readText) candidates.push(await navigator.clipboard.readText());
    } catch {
      // Use the internal cross-project clipboard below.
    }
    try {
      candidates.push(localStorage.getItem(CLIPBOARD_KEY) || '');
    } catch {
      // No compatible clipboard is available.
    }
    for (const candidate of candidates) {
      if (!candidate) continue;
      try {
        const parsed = JSON.parse(candidate);
        if (
          parsed?.kind === 'relationship-graph-editor/selection' &&
          Array.isArray(parsed.nodes) &&
          Array.isArray(parsed.edges) &&
          Array.isArray(parsed.groups)
        ) return parsed;
      } catch {
        // Ignore unrelated clipboard text.
      }
    }
    return null;
  }

  async function pasteSelection() {
    const payload = await readClipboardPayload();
    if (!payload) {
      showToast('剪贴板中没有可粘贴的关系图内容。', true);
      return;
    }
    if (mode !== 'edit') setMode('edit');
    const canvasWidth = Number(graph.meta.canvasWidth) || 1440;
    const canvasHeight = Number(graph.meta.canvasHeight) || 1030;
    const usedGroupIds = new Set(graph.groups.map(group => group.id));
    const usedNodeIds = new Set(graph.nodes.map(node => node.id));
    const usedEdgeIds = new Set(graph.edges.map(edge => edge.id));
    const groupMap = new Map();
    const groupOffsets = new Map();
    const groupsToAdd = [];
    const forceGroupCopy = payload.selectionType === 'group';

    payload.groups.forEach(sourceGroup => {
      const existing = !forceGroupCopy && (
        getGroup(sourceGroup.id) || graph.groups.find(group => group.label === sourceGroup.label)
      );
      if (existing) {
        groupMap.set(sourceGroup.id, existing.id);
        groupOffsets.set(sourceGroup.id, { x: 30, y: 30 });
        return;
      }
      const id = uniqueId(sourceGroup.id || 'group_copy', usedGroupIds);
      usedGroupIds.add(id);
      const x = Math.max(0, Math.min(canvasWidth - sourceGroup.w, finiteNumber(sourceGroup.x, 20) + 30));
      const y = Math.max(0, Math.min(canvasHeight - sourceGroup.h, finiteNumber(sourceGroup.y, 55) + 30));
      groupsToAdd.push({
        ...deepClone(sourceGroup),
        id,
        label: forceGroupCopy ? `${sourceGroup.label} 副本` : sourceGroup.label,
        x,
        y
      });
      groupMap.set(sourceGroup.id, id);
      groupOffsets.set(sourceGroup.id, {
        x: x - finiteNumber(sourceGroup.x, 0),
        y: y - finiteNumber(sourceGroup.y, 0)
      });
    });

    const nodeMap = new Map();
    const nodesToAdd = payload.nodes.map(sourceNode => {
      const id = uniqueId(sourceNode.id || 'node_copy', usedNodeIds);
      usedNodeIds.add(id);
      nodeMap.set(sourceNode.id, id);
      const targetGroupId = groupMap.get(sourceNode.group) || '';
      const offset = groupOffsets.get(sourceNode.group) || { x: 30, y: 30 };
      const width = Math.max(105, finiteNumber(sourceNode.w, 150));
      const height = Math.max(46, finiteNumber(sourceNode.h, 54));
      return {
        ...deepClone(sourceNode),
        id,
        group: targetGroupId,
        x: Math.max(0, Math.min(canvasWidth - width, finiteNumber(sourceNode.x, 0) + offset.x)),
        y: Math.max(0, Math.min(canvasHeight - height, finiteNumber(sourceNode.y, 0) + offset.y)),
        w: width,
        h: height
      };
    });

    const edgesToAdd = payload.edges.flatMap(sourceEdge => {
      const sourceType = String(sourceEdge.sourceType || 'node');
      const targetType = String(sourceEdge.targetType || 'node');
      const source = sourceType === 'group'
        ? groupMap.get(sourceEdge.source)
        : nodeMap.get(sourceEdge.source);
      const target = targetType === 'group'
        ? groupMap.get(sourceEdge.target)
        : nodeMap.get(sourceEdge.target);
      if (!source || !target || (sourceType === targetType && source === target)) return [];
      const id = uniqueId(sourceEdge.id || 'edge_copy', usedEdgeIds);
      usedEdgeIds.add(id);
      return [{ ...deepClone(sourceEdge), id, source, target, sourceType, targetType }];
    });
    if (!groupsToAdd.length && !nodesToAdd.length) {
      showToast('复制内容为空，无法粘贴。', true);
      return;
    }

    commit(() => {
      graph.groups.push(...groupsToAdd);
      graph.nodes.push(...nodesToAdd);
      graph.edges.push(...edgesToAdd);
      selectedNodeIds = new Set(nodesToAdd.map(node => node.id));
      const primary = nodesToAdd.at(-1)?.id;
      selection = primary
        ? { type: 'node', id: primary }
        : { type: 'group', id: groupsToAdd.at(-1).id };
    }, `已粘贴 ${nodesToAdd.length} 个节点和 ${edgesToAdd.length} 条关系`);
    locateSelection();
  }

  function applyArrangeAction() {
    const action = dom.arrangeAction.value;
    const nodes = graph.nodes.filter(node => selectedNodeIds.has(node.id));
    const minimum = action.startsWith('distribute-') ? 3 : 2;
    if (!action || nodes.length < minimum) {
      showToast(action.startsWith('distribute-') ? '等间距至少需要选择 3 个节点。' : '对齐至少需要选择 2 个节点。', true);
      return;
    }
    const anchor = getNode(selection.id) || nodes[0];
    const canvasWidth = Number(graph.meta.canvasWidth) || 1440;
    const canvasHeight = Number(graph.meta.canvasHeight) || 1030;
    commit(() => {
      if (action === 'align-left') nodes.forEach(node => { node.x = anchor.x; });
      if (action === 'align-center-x') {
        const center = anchor.x + anchor.w / 2;
        nodes.forEach(node => { node.x = center - node.w / 2; });
      }
      if (action === 'align-right') {
        const right = anchor.x + anchor.w;
        nodes.forEach(node => { node.x = right - node.w; });
      }
      if (action === 'align-top') nodes.forEach(node => { node.y = anchor.y; });
      if (action === 'align-center-y') {
        const center = anchor.y + anchor.h / 2;
        nodes.forEach(node => { node.y = center - node.h / 2; });
      }
      if (action === 'align-bottom') {
        const bottom = anchor.y + anchor.h;
        nodes.forEach(node => { node.y = bottom - node.h; });
      }
      if (action === 'distribute-x') {
        const sorted = [...nodes].sort((first, second) => first.x - second.x);
        const left = sorted[0].x;
        const right = sorted.at(-1).x + sorted.at(-1).w;
        const contentWidth = sorted.reduce((sum, node) => sum + node.w, 0);
        const gap = (right - left - contentWidth) / (sorted.length - 1);
        let cursor = left;
        sorted.forEach(node => {
          node.x = cursor;
          cursor += node.w + gap;
        });
      }
      if (action === 'distribute-y') {
        const sorted = [...nodes].sort((first, second) => first.y - second.y);
        const top = sorted[0].y;
        const bottom = sorted.at(-1).y + sorted.at(-1).h;
        const contentHeight = sorted.reduce((sum, node) => sum + node.h, 0);
        const gap = (bottom - top - contentHeight) / (sorted.length - 1);
        let cursor = top;
        sorted.forEach(node => {
          node.y = cursor;
          cursor += node.h + gap;
        });
      }
      nodes.forEach(node => {
        node.x = roundToFive(Math.max(0, Math.min(canvasWidth - node.w, node.x)));
        node.y = roundToFive(Math.max(0, Math.min(canvasHeight - node.h, node.y)));
        node.group = findGroupForPoint(graph.groups, node.x + node.w / 2, node.y + node.h / 2);
      });
    }, `已对 ${nodes.length} 个节点执行${dom.arrangeAction.selectedOptions[0].textContent}`);
  }

  function reorderItems(items, selectedIds, action) {
    if (action === 'front') {
      return [...items.filter(item => !selectedIds.has(item.id)), ...items.filter(item => selectedIds.has(item.id))];
    }
    if (action === 'back') {
      return [...items.filter(item => selectedIds.has(item.id)), ...items.filter(item => !selectedIds.has(item.id))];
    }
    const reordered = [...items];
    if (action === 'forward') {
      for (let index = reordered.length - 2; index >= 0; index -= 1) {
        if (selectedIds.has(reordered[index].id) && !selectedIds.has(reordered[index + 1].id)) {
          [reordered[index], reordered[index + 1]] = [reordered[index + 1], reordered[index]];
        }
      }
    } else if (action === 'backward') {
      for (let index = 1; index < reordered.length; index += 1) {
        if (selectedIds.has(reordered[index].id) && !selectedIds.has(reordered[index - 1].id)) {
          [reordered[index], reordered[index - 1]] = [reordered[index - 1], reordered[index]];
        }
      }
    }
    return reordered;
  }

  function applyLayerAction() {
    if (!selection.type) return;
    const action = dom.layerAction.value;
    const selectedIds = selection.type === 'node'
      ? new Set(selectedNodeIds)
      : new Set([selection.id]);
    commit(() => {
      if (selection.type === 'node') graph.nodes = reorderItems(graph.nodes, selectedIds, action);
      if (selection.type === 'group') graph.groups = reorderItems(graph.groups, selectedIds, action);
      if (selection.type === 'edge') graph.edges = reorderItems(graph.edges, selectedIds, action);
    }, `已${dom.layerAction.selectedOptions[0].textContent}`);
  }

  function deleteSelection() {
    if (mode !== 'edit' || !selection.type) return;

    if (selection.type === 'node') {
      if (selectedNodeIds.size > 1) {
        const selectedIds = new Set(selectedNodeIds);
        commit(() => {
          graph.nodes = graph.nodes.filter(item => !selectedIds.has(item.id));
          graph.edges = graph.edges.filter(edge =>
            !(getEdgeEndpointType(edge, 'source') === 'node' && selectedIds.has(edge.source)) &&
            !(getEdgeEndpointType(edge, 'target') === 'node' && selectedIds.has(edge.target))
          );
          clearSelection();
        }, `已删除 ${selectedIds.size} 个节点及相关关系`);
        return;
      }
      const node = getNode(selection.id);
      if (!node) return;
      commit(() => {
        graph.nodes = graph.nodes.filter(item => item.id !== node.id);
        graph.edges = graph.edges.filter(edge => !edgeReferencesEndpoint(edge, 'node', node.id));
        clearSelection();
      }, '节点及其关系已删除');
      return;
    }

    if (selection.type === 'group') {
      const group = getGroup(selection.id);
      if (!group) return;
      const nodeCount = graph.nodes.filter(node => node.group === group.id).length;
      commit(() => {
        graph.groups = graph.groups.filter(item => item.id !== group.id);
        graph.edges = graph.edges.filter(edge => !edgeReferencesEndpoint(edge, 'group', group.id));
        graph.nodes
          .filter(node => node.group === group.id)
          .forEach(node => {
            node.group = '';
          });
        clearSelection();
      }, nodeCount ? `分组已删除，${nodeCount} 个节点已设为未分组` : '分组已删除');
      return;
    }

    const edge = graph.edges.find(item => item.id === selection.id);
    if (!edge) return;
    commit(() => {
      graph.edges = graph.edges.filter(item => item.id !== edge.id);
      clearSelection();
    }, '关系已删除');
  }

  function uniqueId(prefix, usedIds) {
    const safePrefix = String(prefix || 'item').replace(/[^a-zA-Z0-9_-]/g, '_');
    let index = 1;
    let id = safePrefix;
    while (usedIds.has(id)) {
      id = `${safePrefix}_${index}`;
      index += 1;
    }
    return id;
  }

  function startLinkDrag(event, entity, entityType = 'node', requestedSide = '', lockSide = false) {
    if (mode !== 'edit' || event.button !== 0) return;
    event.preventDefault();
    const origin = eventToGraphPoint(event);
    const sourceSide = normalizeNodePortSide(requestedSide) || getNearestNodePortSide(entity, origin);
    selectedNodeIds = entityType === 'node' ? new Set([entity.id]) : new Set();
    linkState = {
      pointerId: event.pointerId,
      sourceId: entity.id,
      sourceType: entityType,
      sourceSide,
      sourceSideLocked: lockSide,
      lineType: getSelectedNewEdgeLineType(),
      origin,
      startClientX: event.clientX,
      startClientY: event.clientY,
      moved: false,
      targetId: null,
      targetType: null,
      point: origin
    };
    selection = { type: entityType, id: entity.id };
    suppressCanvasClick = true;
    renderAll();
  }

  function startNodeDrag(event, node) {
    if (mode !== 'edit' || event.button !== 0) return;
    event.preventDefault();
    cancelScheduledNodeDrag();
    const additive = event.shiftKey || event.ctrlKey || event.metaKey;
    const collapseToSingleOnClick = !additive && selectedNodeIds.size > 1 && selectedNodeIds.has(node.id);
    if (additive && selectedNodeIds.has(node.id)) {
      selectedNodeIds.delete(node.id);
      const nextPrimary = [...selectedNodeIds].at(-1);
      selection = nextPrimary ? { type: 'node', id: nextPrimary } : { type: null, id: null };
      renderAll();
      return;
    }
    if (additive) selectedNodeIds.add(node.id);
    else if (!selectedNodeIds.has(node.id)) selectedNodeIds = new Set([node.id]);
    selection = { type: 'node', id: node.id };

    const point = eventToGraphPoint(event);
    const positions = [...selectedNodeIds]
      .map(nodeId => getNode(nodeId))
      .filter(Boolean)
      .map(item => ({ id: item.id, x: item.x, y: item.y, w: item.w, h: item.h }));
    const movingNodeIds = positions.map(position => position.id);
    const movingNodeIdSet = new Set(movingNodeIds);
    dragState = {
      pointerId: event.pointerId,
      nodeId: node.id,
      nodeIds: movingNodeIds,
      positions,
      affectedEdgeIds: graph.edges
        .filter(edge =>
          (getEdgeEndpointType(edge, 'source') === 'node' && movingNodeIdSet.has(edge.source)) ||
          (getEdgeEndpointType(edge, 'target') === 'node' && movingNodeIdSet.has(edge.target))
        )
        .map(edge => edge.id),
      edgeIndexes: new Map(graph.edges.map((edge, index) => [edge.id, index])),
      offsetX: point.x - node.x,
      offsetY: point.y - node.y,
      startX: node.x,
      startY: node.y,
      snapshot: deepClone(graph),
      collapseToSingleOnClick,
      moved: false
    };
    selection = { type: 'node', id: node.id };
    try {
      event.currentTarget?.setPointerCapture?.(event.pointerId);
    } catch {
      // Window-level listeners still keep the drag active if capture is unavailable.
    }
    renderGraph();
    captureNodeDragElements();
    renderInspector();
    updateActionButtons();
  }

  function startGroupDrag(event, group) {
    if (mode !== 'edit' || event.button !== 0) return;
    event.preventDefault();
    const point = eventToGraphPoint(event);
    const members = graph.nodes
      .filter(node => node.group === group.id)
      .map(node => ({ id: node.id, x: node.x, y: node.y, w: node.w, h: node.h }));
    groupDragState = {
      pointerId: event.pointerId,
      groupId: group.id,
      startPointerX: point.x,
      startPointerY: point.y,
      startX: group.x,
      startY: group.y,
      startW: group.w,
      startH: group.h,
      members,
      snapshot: deepClone(graph),
      moved: false
    };
    selectedNodeIds.clear();
    selection = { type: 'group', id: group.id };
    suppressCanvasClick = true;
    try {
      event.currentTarget?.setPointerCapture?.(event.pointerId);
    } catch {
      // Window-level listeners still keep the drag active if capture is unavailable.
    }
    renderGraph();
    renderInspector();
    updateActionButtons();
  }

  function startGroupResize(event, group, direction) {
    if (mode !== 'edit' || event.button !== 0) return;
    event.preventDefault();
    const point = eventToGraphPoint(event);
    groupResizeState = {
      pointerId: event.pointerId,
      groupId: group.id,
      direction,
      startPointerX: point.x,
      startPointerY: point.y,
      startX: group.x,
      startY: group.y,
      startW: group.w,
      startH: group.h,
      snapshot: deepClone(graph),
      moved: false
    };
    selectedNodeIds.clear();
    selection = { type: 'group', id: group.id };
    suppressCanvasClick = true;
    renderInspector();
    updateActionButtons();
  }

  function handleCanvasPointerDown(event) {
    if (event.button !== 0) return;
    if (
      event.target.closest?.('[data-node-id]') ||
      event.target.closest?.('[data-edge-id]') ||
      event.target.closest?.('[data-group-id]') ||
      event.target.closest?.('[data-resize-direction]')
    ) return;
    const point = eventToGraphPoint(event);
    if (event.shiftKey) {
      selectionBoxState = {
        pointerId: event.pointerId,
        startX: point.x,
        startY: point.y,
        currentX: point.x,
        currentY: point.y,
        additive: event.ctrlKey || event.metaKey,
        previousNodeIds: new Set(selectedNodeIds),
        moved: false
      };
      suppressCanvasClick = true;
      renderSelectionBox();
      return;
    }
    panState = {
      pointerId: event.pointerId,
      startClientX: event.clientX,
      startClientY: event.clientY,
      startX: viewport.x,
      startY: viewport.y,
      moved: false,
      graphPoint: point
    };
  }

  function cancelScheduledNodeDrag() {
    if (dragAnimationFrame) cancelAnimationFrame(dragAnimationFrame);
    dragAnimationFrame = 0;
    pendingDragPointer = null;
  }

  function scheduleNodeDrag(event) {
    const coalescedEvents = event.getCoalescedEvents?.() || [];
    const latestEvent = coalescedEvents.at(-1) || event;
    pendingDragPointer = {
      pointerId: latestEvent.pointerId,
      clientX: latestEvent.clientX,
      clientY: latestEvent.clientY
    };
    if (dragAnimationFrame) return;
    dragAnimationFrame = requestAnimationFrame(() => {
      dragAnimationFrame = 0;
      const pointer = pendingDragPointer;
      pendingDragPointer = null;
      if (pointer) applyNodeDragPointer(pointer);
    });
  }

  function getSvgElementsByData(attribute, id, root = dom.svg) {
    return Array.from(root.querySelectorAll(`[${attribute}]`))
      .filter(element => element.getAttribute(attribute) === id);
  }

  function captureNodeDragElements() {
    if (!dragState) return;
    dragState.renderedNodes = new Map(dragState.nodeIds.map(nodeId => [
      nodeId,
      getSvgElementsByData('data-node-id', nodeId)[0] || null
    ]));
    dragState.minimapNodes = new Map(dragState.nodeIds.map(nodeId => [
      nodeId,
      getSvgElementsByData('data-minimap-node-id', nodeId, dom.minimapCanvas)[0] || null
    ]));
    dragState.renderedEdges = new Map(dragState.affectedEdgeIds.map(edgeId => [
      edgeId,
      getSvgElementsByData('data-edge-id', edgeId)
    ]));
    dragState.renderedEdgeLabels = new Map(dragState.affectedEdgeIds.map(edgeId => [
      edgeId,
      getSvgElementsByData('data-edge-label-id', edgeId)[0] || null
    ]));
  }

  function updateNodeDragFrame() {
    if (!dragState) return;
    dragState.positions.forEach(position => {
      const movingNode = getNode(position.id);
      if (!movingNode) return;
      const renderedNode = dragState.renderedNodes?.get(position.id);
      renderedNode?.setAttribute(
        'transform',
        `translate(${movingNode.x - position.x} ${movingNode.y - position.y})`
      );
      const minimapNode = dragState.minimapNodes?.get(position.id);
      if (minimapNode) {
        minimapNode.setAttribute('x', String(movingNode.x));
        minimapNode.setAttribute('y', String(movingNode.y));
      }
    });

    dragState.affectedEdgeIds.forEach(edgeId => {
      const edge = graph.edges.find(item => item.id === edgeId);
      const source = edge ? getEdgeEndpoint(edge, 'source')?.entity : null;
      const target = edge ? getEdgeEndpoint(edge, 'target')?.entity : null;
      if (!edge || !source || !target) return;
      const geometry = calculateEdgeGeometry(source, target, dragState.edgeIndexes.get(edge.id) || 0, edge);
      (dragState.renderedEdges?.get(edge.id) || []).forEach(path => {
        path.setAttribute('d', geometry.d);
        path.setAttribute('data-route-quality', 'drag');
      });
      const edgeLabel = dragState.renderedEdgeLabels?.get(edge.id);
      if (edgeLabel) {
        edgeLabel.setAttribute('x', String(geometry.lx));
        edgeLabel.setAttribute('y', String(geometry.ly));
      }
    });
    renderAlignmentGuides();
  }

  function applyNodeDragPointer(pointer) {
    if (!dragState || pointer.pointerId !== dragState.pointerId) return;
    const node = getNode(dragState.nodeId);
    if (!node) return;
    const point = eventToGraphPoint(pointer);
    const nextX = point.x - dragState.offsetX;
    const nextY = point.y - dragState.offsetY;
    const snappedDelta = calculateSnappedDelta(
      dragState,
      nextX - dragState.startX,
      nextY - dragState.startY
    );
    let deltaX = snappedDelta.x;
    let deltaY = snappedDelta.y;
    if (Math.abs(deltaX) > 1 || Math.abs(deltaY) > 1) {
      dragState.moved = true;
      suppressCanvasClick = true;
    }
    const canvasWidth = Number(graph.meta.canvasWidth) || 1440;
    const canvasHeight = Number(graph.meta.canvasHeight) || 1030;
    const left = Math.min(...dragState.positions.map(position => position.x));
    const right = Math.max(...dragState.positions.map(position => position.x + position.w));
    const top = Math.min(...dragState.positions.map(position => position.y));
    const bottom = Math.max(...dragState.positions.map(position => position.y + position.h));
    deltaX = Math.max(-left, Math.min(canvasWidth - right, deltaX));
    deltaY = Math.max(-top, Math.min(canvasHeight - bottom, deltaY));
    dragState.positions.forEach(position => {
      const movingNode = getNode(position.id);
      if (!movingNode) return;
      movingNode.x = position.x + deltaX;
      movingNode.y = position.y + deltaY;
      movingNode.group = findGroupForPoint(
        graph.groups,
        movingNode.x + movingNode.w / 2,
        movingNode.y + movingNode.h / 2
      );
    });
    updateNodeDragFrame();
  }

  function applyGroupDragPointer(pointer) {
    if (!groupDragState || pointer.pointerId !== groupDragState.pointerId) return;
    const group = getGroup(groupDragState.groupId);
    if (!group) return;
    const point = eventToGraphPoint(pointer);
    let deltaX = roundToFive(point.x - groupDragState.startPointerX);
    let deltaY = roundToFive(point.y - groupDragState.startPointerY);
    const canvasWidth = Number(graph.meta.canvasWidth) || 1440;
    const canvasHeight = Number(graph.meta.canvasHeight) || 1030;
    const left = Math.min(groupDragState.startX, ...groupDragState.members.map(node => node.x));
    const top = Math.min(groupDragState.startY, ...groupDragState.members.map(node => node.y));
    const right = Math.max(
      groupDragState.startX + groupDragState.startW,
      ...groupDragState.members.map(node => node.x + node.w)
    );
    const bottom = Math.max(
      groupDragState.startY + groupDragState.startH,
      ...groupDragState.members.map(node => node.y + node.h)
    );
    deltaX = Math.max(-left, Math.min(canvasWidth - right, deltaX));
    deltaY = Math.max(-top, Math.min(canvasHeight - bottom, deltaY));
    if (Math.abs(deltaX) > 1 || Math.abs(deltaY) > 1) {
      groupDragState.moved = true;
      suppressCanvasClick = true;
    }
    group.x = groupDragState.startX + deltaX;
    group.y = groupDragState.startY + deltaY;
    groupDragState.members.forEach(position => {
      const member = getNode(position.id);
      if (!member) return;
      member.x = position.x + deltaX;
      member.y = position.y + deltaY;
      member.group = group.id;
    });
    renderGraph();
  }

  function handleGlobalPointerMove(event) {
    if (minimapPanState && event.pointerId === minimapPanState.pointerId) {
      moveViewportFromMinimapEvent(event);
      return;
    }

    if (selectionBoxState && event.pointerId === selectionBoxState.pointerId) {
      const point = eventToGraphPoint(event);
      selectionBoxState.currentX = point.x;
      selectionBoxState.currentY = point.y;
      selectionBoxState.moved = Math.abs(point.x - selectionBoxState.startX) > 3 ||
        Math.abs(point.y - selectionBoxState.startY) > 3;
      renderSelectionBox();
      return;
    }

    if (linkState && event.pointerId === linkState.pointerId) {
      const point = eventToGraphPoint(event);
      const dragDistance = Math.hypot(
        event.clientX - linkState.startClientX,
        event.clientY - linkState.startClientY
      );
      const target = findEndpointAtPoint(point, linkState.sourceType, linkState.sourceId);
      linkState.point = point;
      if (!linkState.moved && dragDistance >= LINK_DRAG_THRESHOLD_PX) linkState.moved = true;
      if (!linkState.moved) {
        linkState.targetId = null;
        linkState.targetType = null;
        return;
      }
      if (!linkState.sourceSideLocked) {
        linkState.sourceSide = getDragDirectionSide(linkState.origin, point, linkState.sourceSide);
      }
      linkState.targetId = target?.id || null;
      linkState.targetType = target?.type || null;
      renderGraph();
      return;
    }

    if (groupDragState && event.pointerId === groupDragState.pointerId) {
      applyGroupDragPointer(event);
      return;
    }

    if (groupResizeState && event.pointerId === groupResizeState.pointerId) {
      const group = getGroup(groupResizeState.groupId);
      if (!group) return;
      const point = eventToGraphPoint(event);
      const deltaX = roundToFive(point.x - groupResizeState.startPointerX);
      const deltaY = roundToFive(point.y - groupResizeState.startPointerY);
      const canvasWidth = Number(graph.meta.canvasWidth) || 1440;
      const canvasHeight = Number(graph.meta.canvasHeight) || 1030;
      const minimumWidth = 120;
      const minimumHeight = 100;
      const startRight = groupResizeState.startX + groupResizeState.startW;
      const startBottom = groupResizeState.startY + groupResizeState.startH;
      let x = groupResizeState.startX;
      let y = groupResizeState.startY;
      let right = startRight;
      let bottom = startBottom;

      if (groupResizeState.direction.includes('w')) {
        x = Math.max(0, Math.min(startRight - minimumWidth, groupResizeState.startX + deltaX));
      }
      if (groupResizeState.direction.includes('e')) {
        right = Math.max(groupResizeState.startX + minimumWidth, Math.min(canvasWidth, startRight + deltaX));
      }
      if (groupResizeState.direction.includes('n')) {
        y = Math.max(0, Math.min(startBottom - minimumHeight, groupResizeState.startY + deltaY));
      }
      if (groupResizeState.direction.includes('s')) {
        bottom = Math.max(groupResizeState.startY + minimumHeight, Math.min(canvasHeight, startBottom + deltaY));
      }

      const memberNodes = graph.nodes.filter(node => node.group === group.id);
      if (memberNodes.length) {
        const memberLeft = Math.min(...memberNodes.map(node => node.x)) - 12;
        const memberRight = Math.max(...memberNodes.map(node => node.x + node.w)) + 12;
        const memberTop = Math.min(...memberNodes.map(node => node.y)) - 38;
        const memberBottom = Math.max(...memberNodes.map(node => node.y + node.h)) + 12;
        if (groupResizeState.direction.includes('w')) {
          x = Math.max(0, Math.min(x, memberLeft));
        }
        if (groupResizeState.direction.includes('e')) {
          right = Math.min(canvasWidth, Math.max(right, memberRight));
        }
        if (groupResizeState.direction.includes('n')) {
          y = Math.max(0, Math.min(y, memberTop));
        }
        if (groupResizeState.direction.includes('s')) {
          bottom = Math.min(canvasHeight, Math.max(bottom, memberBottom));
        }
      }

      const nextW = right - x;
      const nextH = bottom - y;
      if (
        Math.abs(x - groupResizeState.startX) > 1 ||
        Math.abs(y - groupResizeState.startY) > 1 ||
        Math.abs(nextW - groupResizeState.startW) > 1 ||
        Math.abs(nextH - groupResizeState.startH) > 1
      ) {
        groupResizeState.moved = true;
        suppressCanvasClick = true;
      }
      group.x = roundToFive(x);
      group.y = roundToFive(y);
      group.w = roundToFive(nextW);
      group.h = roundToFive(nextH);
      renderGraph();
      return;
    }

    if (dragState && event.pointerId === dragState.pointerId) {
      scheduleNodeDrag(event);
      return;
    }

    if (panState && event.pointerId === panState.pointerId) {
      const rect = dom.svg.getBoundingClientRect();
      const dx = (event.clientX - panState.startClientX) / rect.width * viewport.w;
      const dy = (event.clientY - panState.startClientY) / rect.height * viewport.h;
      viewport.x = panState.startX - dx;
      viewport.y = panState.startY - dy;
      constrainViewport();
      if (Math.abs(dx) > 1 || Math.abs(dy) > 1) {
        panState.moved = true;
        suppressCanvasClick = true;
      }
      scheduleViewportRender();
    }
  }

  function handleGlobalPointerUp(event) {
    if (minimapPanState && event.pointerId === minimapPanState.pointerId) {
      minimapPanState = null;
    }

    if (selectionBoxState && event.pointerId === selectionBoxState.pointerId) {
      const completedBox = selectionBoxState;
      selectionBoxState = null;
      const cancelled = event.type === 'pointercancel';
      if (!cancelled && completedBox.moved) {
        const left = Math.min(completedBox.startX, completedBox.currentX);
        const right = Math.max(completedBox.startX, completedBox.currentX);
        const top = Math.min(completedBox.startY, completedBox.currentY);
        const bottom = Math.max(completedBox.startY, completedBox.currentY);
        const matches = graph.nodes
          .filter(node => node.x < right && node.x + node.w > left && node.y < bottom && node.y + node.h > top)
          .map(node => node.id);
        selectedNodeIds = completedBox.additive
          ? new Set([...completedBox.previousNodeIds, ...matches])
          : new Set(matches);
        const primary = matches.at(-1) || [...selectedNodeIds].at(-1);
        selection = primary ? { type: 'node', id: primary } : { type: null, id: null };
      }
      renderAll();
      setTimeout(() => { suppressCanvasClick = false; }, 0);
    }

    if (linkState && event.pointerId === linkState.pointerId) {
      finishLinkDrag(event);
    }

    if (groupDragState && event.pointerId === groupDragState.pointerId) {
      const cancelled = event.type === 'pointercancel';
      if (!cancelled) applyGroupDragPointer(event);
      const completedGroupDrag = groupDragState;
      groupDragState = null;
      if (cancelled) {
        graph = completedGroupDrag.snapshot;
        invalidateFocusedGeometry();
        renderAll();
      } else if (completedGroupDrag.moved) {
        pushHistory(completedGroupDrag.snapshot);
        graph = normalizeAndValidateGraph(graph);
        invalidateFocusedGeometry();
        saveGraph();
        renderAll();
        showToast('分组及组内节点位置已保存');
      } else {
        renderAll();
      }
      setTimeout(() => {
        suppressCanvasClick = false;
      }, 0);
    }

    if (groupResizeState && event.pointerId === groupResizeState.pointerId) {
      const completedResize = groupResizeState;
      groupResizeState = null;
      const cancelled = event.type === 'pointercancel';
      if (cancelled) {
        graph = completedResize.snapshot;
        invalidateFocusedGeometry();
        renderAll();
      } else if (completedResize.moved) {
        pushHistory(completedResize.snapshot);
        graph = normalizeAndValidateGraph(graph);
        invalidateFocusedGeometry();
        saveGraph();
        renderAll();
        showToast('分组范围已保存');
      } else {
        renderAll();
      }
      setTimeout(() => {
        suppressCanvasClick = false;
      }, 0);
    }

    if (dragState && event.pointerId === dragState.pointerId) {
      cancelScheduledNodeDrag();
      const cancelled = event.type === 'pointercancel';
      if (!cancelled) applyNodeDragPointer(event);
      const completedDrag = dragState;
      dragState = null;
      alignmentGuides = [];
      if (cancelled) {
        graph = completedDrag.snapshot;
        invalidateFocusedGeometry();
        renderAll();
      } else if (completedDrag.moved) {
        pushHistory(completedDrag.snapshot);
        graph = normalizeAndValidateGraph(graph);
        invalidateFocusedGeometry();
        saveGraph();
        renderAll();
        showToast('节点位置已保存');
      } else {
        if (completedDrag.collapseToSingleOnClick) {
          selectedNodeIds = new Set([completedDrag.nodeId]);
          selection = { type: 'node', id: completedDrag.nodeId };
        }
        renderAll();
      }
      setTimeout(() => {
        suppressCanvasClick = false;
      }, 0);
    }

    if (panState && event.pointerId === panState.pointerId) {
      panState = null;
      setTimeout(() => {
        suppressCanvasClick = false;
      }, 0);
    }
  }

  function finishLinkDrag(event) {
    const completedLink = linkState;
    const cancelled = event.type === 'pointercancel';
    const finalPoint = cancelled ? completedLink.point : eventToGraphPoint(event);
    const finalDistance = cancelled ? 0 : Math.hypot(
      event.clientX - completedLink.startClientX,
      event.clientY - completedLink.startClientY
    );
    const didDrag = !cancelled && (completedLink.moved || finalDistance >= LINK_DRAG_THRESHOLD_PX);
    if (didDrag && !completedLink.sourceSideLocked) {
      completedLink.sourceSide = getDragDirectionSide(
        completedLink.origin,
        finalPoint,
        completedLink.sourceSide
      );
    }
    const targetDescriptor = !didDrag
      ? null
      : findEndpointAtPoint(finalPoint, completedLink.sourceType, completedLink.sourceId);
    const source = getEntity(completedLink.sourceType, completedLink.sourceId);
    const target = targetDescriptor?.entity || null;
    const sourceSide = normalizeNodePortSide(completedLink.sourceSide) || 'right';
    const targetSide = source && target
      ? getNodeConnectionSide(target, getNodePortPoint(source, sourceSide))
      : '';
    linkState = null;

    setTimeout(() => {
      suppressCanvasClick = false;
    }, 0);

    if (!didDrag) {
      renderAll();
      return;
    }

    if (!source || !target) {
      renderAll();
      return;
    }

    const existing = graph.edges.find(edge =>
      edgeMatchesEndpoints(
        edge,
        { type: completedLink.sourceType, id: source.id },
        { type: targetDescriptor.type, id: target.id }
      )
    );
    if (existing) {
      selection = { type: 'edge', id: existing.id };
      renderAll();
      showToast('这两个端点已有同方向关系，已为你选中');
      return;
    }

    const id = uniqueId('edge', new Set(graph.edges.map(edge => edge.id)));
    commit(() => {
      graph.edges.push({
        id,
        source: source.id,
        target: target.id,
        sourceType: completedLink.sourceType,
        targetType: targetDescriptor.type,
        label: '',
        category: inferEdgeCategory(source, target),
        lineType: normalizeEdgeLineType(completedLink.lineType),
        sourceSide,
        targetSide
      });
      selection = { type: 'edge', id };
    }, '拖线关系已创建，请在右侧修改名称');
    requestAnimationFrame(() => {
      dom.edgeLabelInput.focus();
      dom.edgeLabelInput.select();
    });
  }

  function findEndpointAtPoint(point, excludedType = '', excludedId = null) {
    for (let index = graph.nodes.length - 1; index >= 0; index -= 1) {
      const node = graph.nodes[index];
      if (excludedType === 'node' && node.id === excludedId) continue;
      if (
        point.x >= node.x - 8 &&
        point.x <= node.x + node.w + 8 &&
        point.y >= node.y - 8 &&
        point.y <= node.y + node.h + 8
      ) {
        return { type: 'node', id: node.id, entity: node };
      }
    }
    for (let index = graph.groups.length - 1; index >= 0; index -= 1) {
      const group = graph.groups[index];
      if (excludedType === 'group' && group.id === excludedId) continue;
      if (
        point.x >= group.x - 8 &&
        point.x <= group.x + group.w + 8 &&
        point.y >= group.y - 8 &&
        point.y <= group.y + group.h + 8
      ) {
        return { type: 'group', id: group.id, entity: group };
      }
    }
    return null;
  }

  function handleCanvasContextMenu(event) {
    event.preventDefault();
    const hadSelection = Boolean(selection.type) || selectedNodeIds.size > 0;
    linkState = null;
    clearSelection();
    renderAll();
    if (hadSelection) showToast('已取消全部选择');
  }

  function inferEdgeCategory(source, target) {
    return getRelationTypes()[0].id;
  }

  function handleCanvasClick(event) {
    if (suppressCanvasClick) return;
    if (event.target.closest?.('[data-node-id]') || event.target.closest?.('[data-edge-id]')) return;
    clearSelection();
    renderAll();
  }

  function eventToGraphPoint(event) {
    const rect = dom.svg.getBoundingClientRect();
    return {
      x: viewport.x + (event.clientX - rect.left) / rect.width * viewport.w,
      y: viewport.y + (event.clientY - rect.top) / rect.height * viewport.h
    };
  }

  function handleWheel(event) {
    event.preventDefault();
    const factor = event.deltaY < 0 ? 0.88 : 1.14;
    const point = eventToGraphPoint(event);
    zoomViewport(factor, point);
  }

  function zoomViewport(factor, center = null) {
    const minWidth = 360;
    const maxWidth = (graph.meta.canvasWidth || 1440) * 2.4;
    const newWidth = Math.max(minWidth, Math.min(maxWidth, viewport.w * factor));
    const aspect = viewport.h / viewport.w;
    const newHeight = newWidth * aspect;
    const target = center || {
      x: viewport.x + viewport.w / 2,
      y: viewport.y + viewport.h / 2
    };
    const ratioX = (target.x - viewport.x) / viewport.w;
    const ratioY = (target.y - viewport.y) / viewport.h;
    viewport.x = target.x - newWidth * ratioX;
    viewport.y = target.y - newHeight * ratioY;
    viewport.w = newWidth;
    viewport.h = newHeight;
    constrainViewport();
    scheduleViewportRender();
  }

  function fitViewport() {
    viewport = {
      x: 0,
      y: 0,
      w: Number(graph.meta.canvasWidth) || 1440,
      h: Number(graph.meta.canvasHeight) || 1030
    };
    renderViewportOnly();
  }

  function exportJson() {
    const exportGraph = deepClone(graph);
    exportGraph.meta.updatedAt = new Date().toISOString();
    downloadBlob(
      JSON.stringify(exportGraph, null, 2),
      safeExportName('json'),
      'application/json;charset=utf-8'
    );
    showToast('JSON数据已导出');
  }

  function safeExportName(extension) {
    const title = String(graph.meta?.title || '关系图')
      .replace(/[\\/:*?"<>|\u0000-\u001f]/g, '_')
      .trim()
      .slice(0, 80) || '关系图';
    return `${title}.${extension}`;
  }

  function collectExportCss() {
    const rules = [];
    Array.from(document.styleSheets).forEach(sheet => {
      try {
        rules.push(...Array.from(sheet.cssRules || []).map(rule => rule.cssText));
      } catch {
        // Cross-origin styles are not needed by this local application.
      }
    });
    const computed = getComputedStyle(document.documentElement);
    const variables = [
      '--bg', '--surface', '--surface-soft', '--surface-strong', '--text', '--text-soft',
      '--border', '--border-strong', '--primary', '--primary-soft', '--danger', '--danger-soft',
      '--resource', '--system', '--output', '--content', '--staff', '--commercial'
    ];
    const variableCss = variables
      .map(variable => `${variable}:${computed.getPropertyValue(variable).trim()};`)
      .join('');
    return `:root{${variableCss}font-family:"Microsoft YaHei UI","PingFang SC",sans-serif;}${rules.join('\n')}`;
  }

  function buildExportSvg() {
    const previousSelection = { ...selection };
    const previousNodeIds = new Set(selectedNodeIds);
    const previousCategory = dom.categoryFilter.value;
    const previousEdgeVisibility = dom.edgeVisibilityToggle.checked;
    let clone;
    try {
      selection = { type: null, id: null };
      selectedNodeIds = new Set();
      dom.categoryFilter.value = 'all';
      dom.edgeVisibilityToggle.checked = true;
      renderGraph();
      clone = dom.svg.cloneNode(true);
    } finally {
      selection = previousSelection;
      selectedNodeIds = previousNodeIds;
      dom.categoryFilter.value = previousCategory;
      dom.edgeVisibilityToggle.checked = previousEdgeVisibility;
      renderGraph();
    }

    const width = Number(graph.meta.canvasWidth) || 1440;
    const height = Number(graph.meta.canvasHeight) || 1030;
    clone.setAttribute('xmlns', SVG_NS);
    clone.setAttribute('width', String(width));
    clone.setAttribute('height', String(height));
    clone.setAttribute('viewBox', `0 0 ${width} ${height}`);
    clone.removeAttribute('tabindex');
    clone.querySelectorAll('[tabindex]').forEach(element => element.removeAttribute('tabindex'));
    clone.querySelectorAll('[role="button"]').forEach(element => {
      element.removeAttribute('role');
      element.removeAttribute('aria-pressed');
      element.removeAttribute('aria-label');
    });
    clone.querySelector('#groupHandleLayer')?.replaceChildren();
    clone.querySelector('#linkPreviewLayer')?.replaceChildren();
    clone.querySelector('#alignmentGuideLayer')?.replaceChildren();
    clone.querySelector('#selectionBoxLayer')?.replaceChildren();
    clone.querySelectorAll('.graph-edge-hit,.graph-link-handle').forEach(element => element.remove());
    clone.querySelectorAll('.is-selected,.is-primary,.is-link-source,.is-link-target,.is-dragging')
      .forEach(element => element.classList.remove('is-selected', 'is-primary', 'is-link-source', 'is-link-target', 'is-dragging'));
    const background = createSvg('rect', { x: 0, y: 0, width, height });
    background.style.fill = getComputedStyle(document.documentElement).getPropertyValue('--bg').trim();
    const viewportLayer = clone.querySelector('#viewportLayer');
    viewportLayer?.insertBefore(background, viewportLayer.firstChild);
    const style = createSvg('style');
    style.textContent = collectExportCss();
    clone.insertBefore(style, clone.firstChild);
    return `<?xml version="1.0" encoding="UTF-8"?>\n${new XMLSerializer().serializeToString(clone)}`;
  }

  function exportSvg() {
    try {
      downloadBlob(buildExportSvg(), safeExportName('svg'), 'image/svg+xml;charset=utf-8');
      showToast('SVG 已导出');
    } catch (error) {
      showToast(`SVG 导出失败：${error.message}`, true);
    }
  }

  async function rasterizeExport(type = 'image/png', quality = 0.94) {
    const svg = buildExportSvg();
    const svgBlob = new Blob([svg], { type: 'image/svg+xml;charset=utf-8' });
    const url = URL.createObjectURL(svgBlob);
    try {
      const image = new Image();
      image.decoding = 'async';
      image.src = url;
      await image.decode();
      const sourceWidth = Number(graph.meta.canvasWidth) || 1440;
      const sourceHeight = Number(graph.meta.canvasHeight) || 1030;
      const maximumDimension = 8192;
      const maximumPixels = 32000000;
      const scale = Math.min(
        2,
        maximumDimension / Math.max(sourceWidth, sourceHeight),
        Math.sqrt(maximumPixels / Math.max(1, sourceWidth * sourceHeight))
      );
      const width = Math.max(1, Math.round(sourceWidth * scale));
      const height = Math.max(1, Math.round(sourceHeight * scale));
      const canvas = document.createElement('canvas');
      canvas.width = width;
      canvas.height = height;
      const context = canvas.getContext('2d');
      if (!context) throw new Error('浏览器无法创建图像画布。');
      context.drawImage(image, 0, 0, width, height);
      const blob = await new Promise((resolve, reject) => {
        canvas.toBlob(result => result ? resolve(result) : reject(new Error('图像编码失败。')), type, quality);
      });
      return { blob, width, height };
    } finally {
      URL.revokeObjectURL(url);
    }
  }

  async function exportPng() {
    dom.exportPngButton.disabled = true;
    updateSaveState('正在生成 PNG…');
    try {
      const { blob } = await rasterizeExport('image/png');
      downloadBlob(blob, safeExportName('png'), 'image/png');
      showToast('PNG 已导出');
      updateSaveState('PNG 已生成');
    } catch (error) {
      showToast(`PNG 导出失败：${error.message}`, true);
      updateSaveState('PNG 导出失败');
    } finally {
      dom.exportPngButton.disabled = false;
    }
  }

  function concatenateBytes(chunks) {
    const length = chunks.reduce((sum, chunk) => sum + chunk.length, 0);
    const result = new Uint8Array(length);
    let offset = 0;
    chunks.forEach(chunk => {
      result.set(chunk, offset);
      offset += chunk.length;
    });
    return result;
  }

  function buildPdfWithJpeg(jpegBytes, imageWidth, imageHeight) {
    const encoder = new TextEncoder();
    const landscape = imageWidth >= imageHeight;
    const pageWidth = landscape ? 842 : 595;
    const pageHeight = landscape ? 595 : 842;
    const margin = 24;
    const scale = Math.min((pageWidth - margin * 2) / imageWidth, (pageHeight - margin * 2) / imageHeight);
    const drawWidth = imageWidth * scale;
    const drawHeight = imageHeight * scale;
    const drawX = (pageWidth - drawWidth) / 2;
    const drawY = (pageHeight - drawHeight) / 2;
    const content = `q\n${drawWidth.toFixed(3)} 0 0 ${drawHeight.toFixed(3)} ${drawX.toFixed(3)} ${drawY.toFixed(3)} cm\n/Im0 Do\nQ\n`;
    const objects = [
      encoder.encode('1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n'),
      encoder.encode('2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n'),
      encoder.encode(`3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 ${pageWidth} ${pageHeight}] /Resources << /XObject << /Im0 5 0 R >> >> /Contents 4 0 R >>\nendobj\n`),
      encoder.encode(`4 0 obj\n<< /Length ${encoder.encode(content).length} >>\nstream\n${content}endstream\nendobj\n`),
      concatenateBytes([
        encoder.encode(`5 0 obj\n<< /Type /XObject /Subtype /Image /Width ${imageWidth} /Height ${imageHeight} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length ${jpegBytes.length} >>\nstream\n`),
        jpegBytes,
        encoder.encode('\nendstream\nendobj\n')
      ])
    ];
    const header = encoder.encode('%PDF-1.4\n');
    const offsets = [0];
    let offset = header.length;
    objects.forEach(object => {
      offsets.push(offset);
      offset += object.length;
    });
    const xrefOffset = offset;
    const xref = [
      'xref',
      `0 ${objects.length + 1}`,
      '0000000000 65535 f ',
      ...offsets.slice(1).map(value => `${String(value).padStart(10, '0')} 00000 n `),
      `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>`,
      `startxref\n${xrefOffset}`,
      '%%EOF\n'
    ].join('\n');
    return concatenateBytes([header, ...objects, encoder.encode(xref)]);
  }

  async function exportPdf() {
    dom.exportPdfButton.disabled = true;
    updateSaveState('正在生成 PDF…');
    try {
      const { blob, width, height } = await rasterizeExport('image/jpeg', 0.92);
      const jpegBytes = new Uint8Array(await blob.arrayBuffer());
      const pdfBytes = buildPdfWithJpeg(jpegBytes, width, height);
      downloadBlob(pdfBytes, safeExportName('pdf'), 'application/pdf');
      showToast('PDF 已导出');
      updateSaveState('PDF 已生成');
    } catch (error) {
      showToast(`PDF 导出失败：${error.message}`, true);
      updateSaveState('PDF 导出失败');
    } finally {
      dom.exportPdfButton.disabled = false;
    }
  }

  function validateGraphForImport(raw) {
    const errors = [];
    const warnings = [];
    const stats = {
      groups: Array.isArray(raw?.groups) ? raw.groups.length : 0,
      nodes: Array.isArray(raw?.nodes) ? raw.nodes.length : 0,
      edges: Array.isArray(raw?.edges) ? raw.edges.length : 0
    };
    if (!raw || typeof raw !== 'object' || Array.isArray(raw)) {
      errors.push('根数据必须是一个 JSON 对象。');
      return { errors, warnings, stats, graph: null };
    }
    ['groups', 'nodes', 'edges'].forEach(key => {
      if (!Array.isArray(raw[key])) errors.push(`字段 ${key} 必须是数组。`);
    });
    if (errors.length) return { errors, warnings, stats, graph: null };
    if (raw.groups.length > MAX_GRAPH_GROUPS) errors.push(`分组数量 ${raw.groups.length} 超过上限 ${MAX_GRAPH_GROUPS}。`);
    if (raw.nodes.length > MAX_GRAPH_NODES) errors.push(`节点数量 ${raw.nodes.length} 超过上限 ${MAX_GRAPH_NODES}。`);
    if (raw.edges.length > MAX_GRAPH_EDGES) errors.push(`关系数量 ${raw.edges.length} 超过上限 ${MAX_GRAPH_EDGES}。`);
    const version = Math.max(1, Math.floor(finiteNumber(raw.version, 1)));
    if (version > GRAPH_SCHEMA_VERSION) errors.push(`文件版本 ${version} 高于当前支持的版本 ${GRAPH_SCHEMA_VERSION}。`);
    if (!raw.meta?.title) warnings.push('未提供项目标题，将使用默认标题。');
    else if (String(raw.meta.title).trim().length > 80) warnings.push('项目标题超过 80 个字符，将自动截短。');
    const width = Number(raw.meta?.canvasWidth);
    const height = Number(raw.meta?.canvasHeight);
    if (!Number.isFinite(width) || !Number.isFinite(height)) warnings.push('画布尺寸缺失或无效，将使用默认尺寸。');
    else if (width < MIN_CANVAS_WIDTH || height < MIN_CANVAS_HEIGHT ||
             width > MAX_CANVAS_SIZE || height > MAX_CANVAS_SIZE) {
      warnings.push(`画布尺寸超出 ${MIN_CANVAS_WIDTH}×${MIN_CANVAS_HEIGHT} 到 ${MAX_CANVAS_SIZE}×${MAX_CANVAS_SIZE}，导入时会自动限制。`);
    }

    const groupIds = new Set();
    raw.groups.forEach((group, index) => {
      const id = String(group?.id || `group_${index + 1}`);
      if (!group?.id) warnings.push(`分组 #${index + 1} 缺少 ID，将自动生成 ${id}。`);
      if (id.length > MAX_ENTITY_ID_LENGTH) errors.push(`分组 #${index + 1} 的 ID 超过 ${MAX_ENTITY_ID_LENGTH} 个字符。`);
      if (groupIds.has(id)) errors.push(`分组 #${index + 1} 的 ID“${id}”重复。`);
      groupIds.add(id);
      if (!group?.label) warnings.push(`分组“${id}”缺少名称，将自动补全。`);
      else if (String(group.label).trim().length > 40) warnings.push(`分组“${id}”的名称超过 40 个字符，将自动截短。`);
    });

    const nodeIds = new Set();
    raw.nodes.forEach((node, index) => {
      const id = String(node?.id || `node_${index + 1}`);
      if (!node?.id) warnings.push(`节点 #${index + 1} 缺少 ID，将自动生成 ${id}。`);
      if (id.length > MAX_ENTITY_ID_LENGTH) errors.push(`节点 #${index + 1} 的 ID 超过 ${MAX_ENTITY_ID_LENGTH} 个字符。`);
      if (nodeIds.has(id)) errors.push(`节点 #${index + 1} 的 ID“${id}”重复。`);
      nodeIds.add(id);
      if (!node?.label) warnings.push(`节点“${id}”缺少名称，将自动补全。`);
      else if (String(node.label).trim().length > 40) warnings.push(`节点“${id}”的名称超过 40 个字符，将自动截短。`);
      if (String(node?.note || '').length > 300) warnings.push(`节点“${id}”的说明超过 300 个字符，将自动截短。`);
      if (node?.group && !groupIds.has(String(node.group))) warnings.push(`节点“${id}”引用了不存在的分组“${node.group}”，将设为未分组。`);
      const importedType = String(node?.type ?? node?.typeLabel ?? '').trim();
      if (!importedType && !KIND_NAMES[node?.kind]) warnings.push(`节点“${id}”缺少类型，将使用“未分类”。`);
      if (importedType.length > 30) warnings.push(`节点“${id}”的类型超过 30 个字符，将自动截短。`);
      if (node?.kind && !KIND_NAMES[node.kind]) warnings.push(`节点“${id}”的旧版配色标识“${node.kind}”无效，将根据类型自动配色。`);
    });

    const edgeIds = new Set();
    const edgePairs = new Set();
    if (raw.settings?.relationTypes !== undefined && !Array.isArray(raw.settings.relationTypes)) {
      errors.push('settings.relationTypes 必须是数组。');
    } else if (Array.isArray(raw.settings?.relationTypes)) {
      const importedTypeIds = new Set();
      raw.settings.relationTypes.forEach((type, index) => {
        const id = String(type?.id || '').trim();
        if (!/^[a-z][a-z0-9_-]{0,31}$/i.test(id)) errors.push(`关系类型 #${index + 1} 的 ID“${id || '空'}”无效。`);
        else if (importedTypeIds.has(id)) errors.push(`关系类型 #${index + 1} 的 ID“${id}”重复。`);
        importedTypeIds.add(id);
        if (!String(type?.label || '').trim()) warnings.push(`关系类型“${id || index + 1}”缺少名称，将自动补全。`);
        if (type?.color && !isHexColor(type.color)) warnings.push(`关系类型“${id || index + 1}”的颜色无效，将使用默认颜色。`);
      });
    }
    if (raw.settings?.nodeTypes !== undefined && !Array.isArray(raw.settings.nodeTypes)) {
      errors.push('settings.nodeTypes 必须是数组。');
    } else if (Array.isArray(raw.settings?.nodeTypes)) {
      const importedNodeTypeNames = new Set();
      raw.settings.nodeTypes.forEach((type, index) => {
        const label = String(type?.label || '').trim();
        const key = label.toLocaleLowerCase('zh-CN');
        if (!label) warnings.push(`节点类型标签 #${index + 1} 为空，将忽略。`);
        else if (importedNodeTypeNames.has(key)) warnings.push(`节点类型标签“${label}”重复，将合并。`);
        importedNodeTypeNames.add(key);
        if (label.length > 30) warnings.push(`节点类型标签“${label}”超过 30 个字符，将自动截短。`);
        if (type?.kind && !KIND_NAMES[type.kind]) warnings.push(`节点类型标签“${label || index + 1}”的配色标识无效，将自动配色。`);
      });
    }
    const relationTypeIds = new Set(normalizeProjectSettings(raw.settings).relationTypes.map(type => type.id));
    raw.edges.forEach((edge, index) => {
      const id = String(edge?.id || `edge_${index + 1}`);
      const source = String(edge?.source ?? edge?.s ?? '');
      const target = String(edge?.target ?? edge?.t ?? '');
      const sourceType = resolveEndpointType(
        edge?.sourceType ?? edge?.sourceKind ?? edge?.sType,
        source,
        nodeIds,
        groupIds
      );
      const targetType = resolveEndpointType(
        edge?.targetType ?? edge?.targetKind ?? edge?.tType,
        target,
        nodeIds,
        groupIds
      );
      if (!edge?.id) warnings.push(`关系 #${index + 1} 缺少 ID，将自动生成 ${id}。`);
      if (id.length > MAX_ENTITY_ID_LENGTH) errors.push(`关系 #${index + 1} 的 ID 超过 ${MAX_ENTITY_ID_LENGTH} 个字符。`);
      if (edgeIds.has(id)) errors.push(`关系 #${index + 1} 的 ID“${id}”重复。`);
      edgeIds.add(id);
      if (!sourceType) errors.push(`关系“${id}”的起点“${source || '空'}”不是现有节点或分组。`);
      if (!targetType) errors.push(`关系“${id}”的终点“${target || '空'}”不是现有节点或分组。`);
      if (source && source === target && sourceType === targetType) errors.push(`关系“${id}”的起点和终点相同。`);
      const pair = `${sourceType}:${source}\u0000${targetType}:${target}`;
      if (edgePairs.has(pair)) errors.push(`关系“${id}”与其他关系重复连接 ${source} → ${target}。`);
      edgePairs.add(pair);
      const category = String(edge?.category ?? edge?.cat ?? '');
      if (category && !relationTypeIds.has(category)) warnings.push(`关系“${id}”的类型“${category}”不存在，将改为默认关系类型。`);
      if (edge?.lineType && !EDGE_LINE_TYPES.includes(String(edge.lineType).toLowerCase())) {
        warnings.push(`关系“${id}”的线型“${edge.lineType}”不存在，将使用曲线。`);
      }
      if (String(edge?.label || '').trim().length > 40) warnings.push(`关系“${id}”的名称超过 40 个字符，将自动截短。`);
    });

    let normalized = null;
    if (!errors.length) {
      try {
        normalized = normalizeAndValidateGraph(raw);
      } catch (error) {
        errors.push(error.message);
      }
    }
    return { errors, warnings, stats, graph: normalized };
  }

  function renderImportIssues(container, issues) {
    container.replaceChildren();
    issues.forEach(issue => {
      const item = document.createElement('li');
      item.textContent = issue;
      container.appendChild(item);
    });
  }

  function showImportPreview(fileName, validation, format = '') {
    const formatLabel = format === 'readonly-html'
      ? '只读交互版 HTML'
      : format === 'json' ? 'JSON' : '导入文件';
    pendingImport = validation.graph ? { fileName, graph: validation.graph, format } : null;
    dom.importPreviewSummary.textContent = validation.errors.length
      ? `“${fileName}”（${formatLabel}）存在错误，修正后才能导入。`
      : `“${fileName}”（${formatLabel}）检查通过，确认后将替换当前项目内容，并保留撤销与恢复记录。`;
    dom.importPreviewStats.replaceChildren();
    [
      ['分组', validation.stats.groups, graph.groups.length],
      ['节点', validation.stats.nodes, graph.nodes.length],
      ['关系', validation.stats.edges, graph.edges.length]
    ].forEach(([label, next, current]) => {
      const item = document.createElement('div');
      item.className = 'import-stat';
      const name = document.createElement('span');
      name.textContent = `${label}（当前 ${current}）`;
      const value = document.createElement('strong');
      const difference = next - current;
      value.textContent = `${next} ${difference === 0 ? '不变' : difference > 0 ? `+${difference}` : difference}`;
      item.append(name, value);
      dom.importPreviewStats.appendChild(item);
    });
    dom.importErrorCount.textContent = String(validation.errors.length);
    dom.importWarningCount.textContent = String(validation.warnings.length);
    renderImportIssues(dom.importErrorList, validation.errors);
    renderImportIssues(dom.importWarningList, validation.warnings);
    dom.confirmImportButton.disabled = Boolean(validation.errors.length || !validation.graph);
    dom.importPreviewDialog.showModal();
  }

  async function importGraphFile() {
    const file = dom.importFileInput.files?.[0];
    dom.importFileInput.value = '';
    if (!file) return;
    if (file.size > MAX_IMPORT_FILE_BYTES) {
      showImportPreview(file.name, {
        errors: [`文件大小超过 ${Math.round(MAX_IMPORT_FILE_BYTES / 1024 / 1024)} MB 上限。`],
        warnings: [],
        stats: { groups: 0, nodes: 0, edges: 0 },
        graph: null
      });
      return;
    }
    try {
      const text = await file.text();
      let parsed;
      try {
        parsed = IMPORT_API.parseGraphFileText(text, file.name, file.type);
      } catch (error) {
        showImportPreview(file.name, {
          errors: [error.message],
          warnings: [],
          stats: { groups: 0, nodes: 0, edges: 0 },
          graph: null
        });
        return;
      }
      showImportPreview(file.name, validateGraphForImport(parsed.graph), parsed.format);
    } catch (error) {
      showToast(`无法读取导入文件：${error.message}`, true);
    }
  }

  function closeImportPreview() {
    pendingImport = null;
    dom.importPreviewDialog.close();
  }

  function confirmImport() {
    if (!pendingImport?.graph) return;
    const importedFormat = pendingImport.format;
    pushHistory();
    graph = pendingImport.graph;
    clearSelection();
    invalidateFocusedGeometry();
    viewport = {
      x: 0,
      y: 0,
      w: Number(graph.meta.canvasWidth) || 1440,
      h: Number(graph.meta.canvasHeight) || 1030
    };
    applyProjectAppearance();
    buildMarkers();
    populateRelationTypeOptions();
    saveGraph('导入成功并已保存');
    populateGroupOptions();
    populateNodeSelects();
    renderAll();
    const counts = `${graph.nodes.length} 个节点和 ${graph.edges.length} 条关系`;
    pendingImport = null;
    dom.importPreviewDialog.close();
    const sourceLabel = importedFormat === 'readonly-html' ? '只读交互版' : 'JSON';
    showToast(`已从${sourceLabel}导入 ${counts}`);
  }

  function restoreDefault() {
    if (!window.confirm('恢复初始数据会替换当前编辑内容。建议先导出JSON备份，是否继续？')) return;
    pushHistory();
    graph = normalizeAndValidateGraph(deepClone(window.DEFAULT_GRAPH));
    invalidateFocusedGeometry();
    applyProjectAppearance();
    buildMarkers();
    populateRelationTypeOptions();
    clearSelection();
    viewport = {
      x: 0,
      y: 0,
      w: Number(graph.meta.canvasWidth) || 1440,
      h: Number(graph.meta.canvasHeight) || 1030
    };
    saveGraph('已恢复初始数据');
    populateGroupOptions();
    populateNodeSelects();
    renderAll();
    showToast('已恢复初始关系图');
  }

  function exportReadonly() {
    if (typeof window.buildReadonlyGraphHtml !== 'function') {
      showToast('只读导出模块未载入。', true);
      return;
    }
    const html = window.buildReadonlyGraphHtml(graph);
    downloadBlob(html, safeExportName('html'), 'text/html;charset=utf-8');
    showToast('只读交互版已导出');
  }

  function downloadBlob(content, filename, type) {
    const blob = content instanceof Blob ? content : new Blob([content], { type });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = filename;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }

  function focusSelectionLabel() {
    if (!selection.type || selectedNodeIds.size > 1) {
      showToast('请先选择一个节点、分组或关系。', true);
      return;
    }
    if (mode !== 'edit') setMode('edit');
    const target = selection.type === 'node'
      ? dom.nodeLabelInput
      : selection.type === 'group'
        ? dom.groupLabelInput
        : dom.edgeLabelInput;
    requestAnimationFrame(() => {
      target.focus();
      target.select();
    });
  }

  function nudgeSelectedNodes(deltaX, deltaY) {
    if (mode !== 'edit' || !selectedNodeIds.size) return;
    const selectedIds = new Set(selectedNodeIds);
    const nodes = graph.nodes.filter(node => selectedIds.has(node.id));
    const canvasWidth = Number(graph.meta.canvasWidth) || 1440;
    const canvasHeight = Number(graph.meta.canvasHeight) || 1030;
    const left = Math.min(...nodes.map(node => node.x));
    const right = Math.max(...nodes.map(node => node.x + node.w));
    const top = Math.min(...nodes.map(node => node.y));
    const bottom = Math.max(...nodes.map(node => node.y + node.h));
    const safeDeltaX = Math.max(-left, Math.min(canvasWidth - right, deltaX));
    const safeDeltaY = Math.max(-top, Math.min(canvasHeight - bottom, deltaY));
    if (!safeDeltaX && !safeDeltaY) return;
    commit(() => {
      nodes.forEach(node => {
        node.x += safeDeltaX;
        node.y += safeDeltaY;
        node.group = findGroupForPoint(graph.groups, node.x + node.w / 2, node.y + node.h / 2);
      });
    });
  }

  function panViewportByKeyboard(deltaX, deltaY) {
    viewport.x += deltaX;
    viewport.y += deltaY;
    constrainViewport();
    renderViewportOnly();
  }

  function nudgeSelectedGroup(deltaX, deltaY, resize = false) {
    if (mode !== 'edit' || selection.type !== 'group') return;
    const group = getGroup(selection.id);
    if (!group) return;
    const canvasWidth = Number(graph.meta.canvasWidth) || 1440;
    const canvasHeight = Number(graph.meta.canvasHeight) || 1030;
    const members = graph.nodes.filter(node => node.group === group.id);
    commit(() => {
      if (resize) {
        const memberRight = members.length ? Math.max(...members.map(node => node.x + node.w + 12)) : group.x + 120;
        const memberBottom = members.length ? Math.max(...members.map(node => node.y + node.h + 12)) : group.y + 100;
        group.w = Math.max(120, memberRight - group.x, Math.min(canvasWidth - group.x, group.w + deltaX));
        group.h = Math.max(100, memberBottom - group.y, Math.min(canvasHeight - group.y, group.h + deltaY));
        return;
      }
      const memberLeft = members.length ? Math.min(...members.map(node => node.x)) : group.x;
      const memberTop = members.length ? Math.min(...members.map(node => node.y)) : group.y;
      const memberRight = members.length ? Math.max(...members.map(node => node.x + node.w)) : group.x + group.w;
      const memberBottom = members.length ? Math.max(...members.map(node => node.y + node.h)) : group.y + group.h;
      const left = Math.min(group.x, memberLeft);
      const top = Math.min(group.y, memberTop);
      const right = Math.max(group.x + group.w, memberRight);
      const bottom = Math.max(group.y + group.h, memberBottom);
      const safeX = Math.max(-left, Math.min(canvasWidth - right, deltaX));
      const safeY = Math.max(-top, Math.min(canvasHeight - bottom, deltaY));
      group.x += safeX;
      group.y += safeY;
      members.forEach(node => {
        node.x += safeX;
        node.y += safeY;
      });
    });
  }

  function executeShortcutAction(action, event) {
    if (event.repeat && ['addNode', 'addGroup', 'delete', 'paste'].includes(action)) return;
    if (action === 'search') {
      dom.graphSearchInput.focus();
      dom.graphSearchInput.select();
    } else if (action === 'locate') locateSelection();
    else if (action === 'fit') fitViewport();
    else if (action === 'toggleMode') setMode(mode === 'edit' ? 'view' : 'edit');
    else if (action === 'addNode') addNode();
    else if (action === 'addGroup') addGroup();
    else if (action === 'addRelation') openCreateRelationDialog();
    else if (action === 'editLabel') focusSelectionLabel();
    else if (action === 'copy') copySelection();
    else if (action === 'paste') pasteSelection();
    else if (action === 'selectAll') {
      selectedNodeIds = new Set(graph.nodes.map(node => node.id));
      const primary = graph.nodes.at(-1)?.id;
      selection = primary ? { type: 'node', id: primary } : { type: null, id: null };
      renderAll();
    } else if (action === 'delete') {
      if (mode === 'edit') deleteSelection();
      else showToast('请先进入编辑模式再删除。', true);
    } else if (action === 'undo') undo();
    else if (action === 'redo') redo();
    else if (action === 'toggleEdges') {
      dom.edgeVisibilityToggle.checked = !dom.edgeVisibilityToggle.checked;
      renderGraph();
    } else if (action === 'zoomIn') zoomViewport(0.82);
    else if (action === 'zoomOut') zoomViewport(1.22);
  }

  function handleKeyboardShortcuts(event) {
    if (capturingShortcutAction) return;
    if (document.querySelector('dialog[open]')) return;
    if (event.key === 'Escape' && dom.projectMenu.open) {
      event.preventDefault();
      dom.projectMenu.open = false;
      dom.projectMenu.querySelector('summary')?.focus();
      return;
    }
    if (event.key === 'Escape' && dom.exportMenu.open) {
      event.preventDefault();
      dom.exportMenu.open = false;
      dom.exportMenu.querySelector('summary')?.focus();
      return;
    }
    if (event.key === 'Escape' && dom.editToolsMenu.open) {
      event.preventDefault();
      dom.editToolsMenu.open = false;
      dom.editToolsMenu.querySelector('summary')?.focus();
      return;
    }
    const editingText = event.target instanceof HTMLInputElement ||
      event.target instanceof HTMLTextAreaElement ||
      event.target instanceof HTMLSelectElement;
    if (editingText) return;

    const shortcut = shortcutFromEvent(event);
    let action = SHORTCUT_DEFINITIONS.find(definition => shortcuts[definition.id] === shortcut)?.id;
    if (!action && event.metaKey && shortcut.startsWith('Meta')) {
      const controlAlias = shortcut.replace(/^Meta/, 'Control');
      action = SHORTCUT_DEFINITIONS.find(definition => shortcuts[definition.id] === controlAlias)?.id;
    }
    if (action) {
      event.preventDefault();
      executeShortcutAction(action, event);
      return;
    }

    if (event.key.startsWith('Arrow')) {
      event.preventDefault();
      const step = event.shiftKey ? 20 : 5;
      const direction = {
        ArrowLeft: [-step, 0],
        ArrowRight: [step, 0],
        ArrowUp: [0, -step],
        ArrowDown: [0, step]
      }[event.key];
      if (mode === 'edit' && selectedNodeIds.size) nudgeSelectedNodes(direction[0], direction[1]);
      else if (mode === 'edit' && selection.type === 'group') {
        nudgeSelectedGroup(direction[0], direction[1], event.ctrlKey || event.metaKey);
      }
      else panViewportByKeyboard(direction[0] * 5, direction[1] * 5);
      return;
    }

    if (event.key === 'Backspace' && mode === 'edit') {
      event.preventDefault();
      deleteSelection();
      return;
    }
    if (event.key === 'Escape') {
      if (linkState) {
        linkState = null;
        suppressCanvasClick = false;
      }
      if (dragState) {
        cancelScheduledNodeDrag();
        graph = dragState.snapshot;
        dragState = null;
        suppressCanvasClick = false;
        invalidateFocusedGeometry();
      }
      if (groupDragState) {
        graph = groupDragState.snapshot;
        groupDragState = null;
        suppressCanvasClick = false;
        invalidateFocusedGeometry();
      }
      if (groupResizeState) {
        graph = groupResizeState.snapshot;
        groupResizeState = null;
        suppressCanvasClick = false;
        invalidateFocusedGeometry();
      }
      selectionBoxState = null;
      alignmentGuides = [];
      clearSelection();
      renderAll();
    }
  }

  function showToast(message, error = false) {
    const toast = document.createElement('div');
    toast.className = `toast${error ? ' is-error' : ''}`;
    toast.textContent = message;
    dom.toastRegion.appendChild(toast);
    setTimeout(() => toast.remove(), 2600);
  }
})();
