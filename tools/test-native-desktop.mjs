import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import process from "node:process";
import { spawnSync } from "node:child_process";

const root = path.resolve(import.meta.dirname, "..");
const nativeRoot = path.join(root, "native");
const packageJson = JSON.parse(fs.readFileSync(path.join(root, "package.json"), "utf8"));
const buildScript = fs.readFileSync(path.join(root, "desktop", "build-desktop.ps1"), "utf8");
const manifestPath = path.join(root, "desktop", "app.manifest");
const manifest = fs.readFileSync(manifestPath, "utf8");
const sources = fs.readdirSync(nativeRoot).filter((name) => name.endsWith(".cs"));
const sourceText = sources.map((name) => fs.readFileSync(path.join(nativeRoot, name), "utf8")).join("\n");
const mainFormText = fs.readFileSync(path.join(nativeRoot, "MainForm.cs"), "utf8");
const graphCanvasText = fs.readFileSync(path.join(nativeRoot, "GraphCanvas.cs"), "utf8");
const pdfExportText = fs.readFileSync(path.join(nativeRoot, "NativePdfExport.cs"), "utf8");
const exe = process.env.RELATIONSHIP_GRAPH_EXE ? path.resolve(process.env.RELATIONSHIP_GRAPH_EXE) : path.join(root, "关系图编辑器.exe");
const staticOnly = process.argv.includes("--static");
const versionPattern = packageJson.version.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");

const requiredSources = ["GraphCanvas.cs", "GraphHistory.cs", "GraphModel.cs", "MainForm.cs", "NativeAppIcon.cs", "NativeExport.cs", "NativePdfExport.cs", "NativePersistence.cs", "NativeTheme.cs", "Program.cs"];
assert.deepEqual(requiredSources.filter((name) => !sources.includes(name)), [], "原生应用缺少必要源码");
assert.ok(sources.every((name) => /^[A-Za-z0-9_.-]+\.cs$/.test(name)), "native 目录包含异常 C# 文件名");
assert.match(buildScript, /native/i);
assert.match(buildScript, /System\.Drawing\.dll/);
assert.match(buildScript, /System\.Windows\.Forms\.dll/);
assert.match(buildScript, /RelationshipGraphNative\.Data\.default\.json/);
assert.match(buildScript, /win32icon:\$iconPath/);
assert.match(buildScript, /win32manifest:\$manifestPath/);
assert.match(buildScript, /RelationshipGraphNative\.Assets\.app-icon\.ico/);
assert.match(buildScript, /CertificateThumbprint/);
assert.match(buildScript, /TimestampUrl/);
assert.match(buildScript, /signtool\.exe/i);
assert.match(buildScript, /temporaryOutputPath/);
assert.match(buildScript, /\[IO\.File\]::Replace\(\$temporaryOutputPath, \$outputPath/);
assert.match(buildScript, /Authenticode signature verification failed/);
assert.doesNotMatch(buildScript, /RelationshipGraphLauncher\.cs/);
assert.doesNotMatch(buildScript, /index\.html|app\.js|app\.css|RelationshipGraphEditor\.Web/);
assert.match(manifest, /PerMonitorV2/);
assert.match(manifest, /true\/pm/);
assert.match(manifest, /requestedExecutionLevel level="asInvoker"/);
assert.match(manifest, /supportedOS Id="\{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a\}"/i);
assert.match(manifest, new RegExp(`version="${versionPattern}\\.0"`));

assert.equal(packageJson.scripts?.test, "npm run test:native");
assert.equal(packageJson.scripts?.audit, "npm run audit:native");
assert.match(packageJson.scripts?.["test:native"] || "", /build:desktop.*test-native-desktop/s);
assert.match(packageJson.scripts?.["audit:native"] || "", /test-native-desktop\.mjs --static/);
assert.ok(Object.entries(packageJson.scripts || {})
  .filter(([name]) => !name.startsWith("legacy:"))
  .every(([, command]) => !command.includes("legacy-web")), "默认命令不应调用旧网页检查");
for (const file of ["index.html", "app.js", "app.css", "system-function-default.js"]) {
  assert.ok(!fs.existsSync(path.join(root, file)), `旧网页文件仍留在根目录：${file}`);
  assert.ok(fs.existsSync(path.join(root, "legacy-web", file)), `legacy-web 缺少文件：${file}`);
}
assert.ok(fs.existsSync(path.join(root, "system-function-graph.json")), "根目录缺少原生默认图数据源");
assert.ok(!fs.existsSync(path.join(root, "legacy-web", "system-function-graph.json")), "旧网页目录不应复制默认图 JSON 数据源");

assert.match(sourceText, /namespace RelationshipGraphNative/);
assert.match(sourceText, new RegExp(`AssemblyFileVersion\\("${versionPattern}\\.0"\\)`));
assert.match(sourceText, /Application\.Run\(new MainForm\(\)\)/);
assert.match(sourceText, /class GraphCanvas : Control/);
assert.match(sourceText, /class GraphHistory/);
assert.match(sourceText, /MaximumCharacters = 16 \* 1024 \* 1024/);
assert.match(sourceText, /WriteStreamAtomic/);
assert.match(sourceText, /File\.Replace\(temporaryPath, targetPath/);
assert.match(sourceText, /MaximumHistoryFiles = 10/);
assert.match(sourceText, /HistoryRetryInterval = TimeSpan\.FromSeconds\(30\)/);
assert.match(sourceText, /System\.Drawing\.Drawing2D/);
assert.match(sourceText, /sourceType|targetType/);
assert.match(sourceText, /public List<string> groups \{ get; set; \}/);
assert.match(sourceText, /UpdateAutomaticMemberships/);
assert.match(sourceText, /所属分组（自动匹配，只读）/);
assert.doesNotMatch(sourceText, /AddGroupField|所属分组（可选）/);
assert.match(graphCanvasText, /DirectionSide\(_worldDown, _worldCurrent, _sourceSide\)/);
assert.match(graphCanvasText, /dx >= 0 \? "right" : "left"/);
assert.match(graphCanvasText, /dy >= 0 \? "bottom" : "top"/);
assert.doesNotMatch(graphCanvasText, /float radius = 7f \* unit/);
assert.match(graphCanvasText, /if \(_selectedType != "group" \|\| _selectedGroups\.Count != 1 \|\| !_groups\.ContainsKey\(_selectedId\)\) return false/);
assert.match(graphCanvasText, /RestoreDocumentPreservingView/);
assert.match(graphCanvasText, /BeforeJson/);
assert.match(graphCanvasText, /EnsureGestureSnapshot/);
assert.match(graphCanvasText, /GestureSnapshotAvailableForTesting/);
assert.match(graphCanvasText, /CommitPendingEdit/);
assert.match(graphCanvasText, /MaxCoordinate/);
assert.match(graphCanvasText, /SelectedGroupIds/);
assert.match(graphCanvasText, /world\.Contains\(RectOf\(group\)\)/);
assert.match(graphCanvasText, /CanvasGesture\.MoveSelection/);
assert.match(graphCanvasText, /MoveSelectedObjectsForTesting/);
assert.match(graphCanvasText, /GroupsBackToFront/);
assert.match(graphCanvasText, /OrderByDescending\(delegate\(GraphGroup group\) \{ return group\.w \* group\.h; \}\)/);
assert.match(graphCanvasText, /IsSameNameHighlightedNode/);
assert.match(graphCanvasText, /IsInactiveSameNameNode/);
assert.match(graphCanvasText, /direction == "downstream" && sourceKey == nodeKey/);
assert.match(graphCanvasText, /direction == "upstream" && targetKey == nodeKey/);
assert.match(graphCanvasText, /String\.Equals\(node\.label \?\? "", selectedLabel, StringComparison\.Ordinal\)/);
assert.match(graphCanvasText, /foreach \(string root in roots\)/);
assert.match(sourceText, /AppsUseLightTheme/);
assert.match(sourceText, /DwmSetWindowAttribute/);
assert.match(sourceText, /"system".*"light".*"dark"/s);
assert.match(sourceText, /OnMouseDoubleClick/);
assert.match(sourceText, /BlankDoubleClicked/);
assert.match(sourceText, /MouseButtons\.Right/);
assert.match(sourceText, /Cursors\.Cross/);
assert.match(sourceText, /BeginInlineEdit/);
assert.match(sourceText, /NodeTypeEditArea/);
assert.match(sourceText, /GroupLabelEditArea/);
assert.match(sourceText, /_gestureMoved && _gestureBefore/);
assert.match(sourceText, /TextInputHasFocus/);
assert.doesNotMatch(sourceText, /e\.KeyCode\s*==\s*Keys\.Back/);
assert.match(sourceText, /颜色分类/);
assert.match(sourceText, /仅影响节点配色，不影响节点类型、关系或功能/);
assert.match(mainFormText, /_canvas\.Document = graph; _canvas\.EditMode = true/);
assert.doesNotMatch(mainFormText, /_viewButton|_editButton|查看模式/);
assert.match(sourceText, /ApplyInspectorAccessMode/);
assert.match(sourceText, /ApplyInspectorChange/);
assert.match(sourceText, /ColorCategoryChoice/);
assert.match(sourceText, /GraphClipboardPayload/);
assert.match(sourceText, /Keys\.C.*CopySelected/s);
assert.match(sourceText, /Keys\.V.*PasteSelected/s);
assert.match(sourceText, /ViewCenterWorld/);
assert.match(sourceText, /target\.X - \(left \+ right\) \/ 2f/);
assert.doesNotMatch(sourceText, /_pasteOffsetStep/);
assert.match(mainFormText, /center\.X - width \/ 2f/);
assert.match(mainFormText, /AddNodeAt\(_canvas\.ViewCenterWorld/);
assert.match(mainFormText, /TrySelectedContentBounds/);
assert.match(mainFormText, /selectedBounds\.Height \+ titlePadding \+ bottomPadding/);
assert.match(mainFormText, /已为选中内容创建分组/);
assert.match(sourceText, /EditMode \|\| \(ModifierKeys & Keys\.Shift\)/);
assert.match(sourceText, /ShouldHighlightNeighbors/);
assert.match(sourceText, /ApplyNodeAlignment/);
assert.match(sourceText, /DrawAlignmentGuides/);
assert.match(sourceText, /IsFreePlacement\(ModifierKeys\)/);
assert.match(sourceText, /FindEqualSpacing/);
assert.match(sourceText, /DrawEqualSpacingHints/);
assert.match(sourceText, /等距 /);
assert.match(graphCanvasText, /AlignmentCandidates/);
assert.match(graphCanvasText, /MoveSelectedGroupForTesting/);
assert.match(graphCanvasText, /CanvasGesture\.MoveNodes \|\| _gesture == CanvasGesture\.MoveGroup/);
assert.doesNotMatch(graphCanvasText, /HitLinkHandle/);
assert.match(graphCanvasText, /UpdateHoverCursor/);
assert.match(graphCanvasText, /Cursors\.SizeNS/);
assert.match(graphCanvasText, /Cursors\.SizeWE/);
assert.match(graphCanvasText, /Cursors\.SizeNESW/);
assert.match(graphCanvasText, /Cursors\.SizeNWSE/);
assert.match(graphCanvasText, /Math\.Abs\(world\.X - rect\.Left\)/);
assert.match(graphCanvasText, /ContentBounds/);
assert.match(graphCanvasText, /VisibleWorldRectangle/);
assert.doesNotMatch(graphCanvasText, /world\.X < 0|world\.Y < 0/);
assert.doesNotMatch(graphCanvasText, /meta\.canvasWidth - right|meta\.canvasHeight - bottom/);
assert.match(sourceText, /Append\(Number\(bounds\.X\)\)/);
assert.match(sourceText, /\/Filter \/FlateDecode/);
assert.match(sourceText, /Adler32WriteStream/);
assert.match(sourceText, /WriteUInt32BigEndian/);
assert.match(sourceText, /ExpandScientificNotation/);
assert.doesNotMatch(pdfExportText, /\/Subtype \/Image/);
assert.match(sourceText, /box\.Items\.AddRange\(endpoints\.Cast<object>\(\)\.ToArray\(\)\)/);
assert.match(sourceText, /RepairAutomaticSides/);
assert.match(sourceText, /Distance\(point, GetPortPoint\(rect, "right"\)\)/);
assert.match(sourceText, /PathPointAtFraction\((?:path|geometry\.Path), \.5f\)/);
assert.match(sourceText, /EdgePathMidpoint/);
assert.match(sourceText, /EdgeSourceColor/);
assert.match(sourceText, /SourceAccentColor/);
assert.match(sourceText, /BuildDrawio/);
assert.match(sourceText, /SaveDrawio/);
assert.match(sourceText, /BuildReadonlyHtml/);
assert.match(mainFormText, /DocumentFingerprint/);
assert.match(mainFormText, /FinishPendingCanvasWork/);
assert.match(mainFormText, /ClearAutosaveSafely/);
assert.match(mainFormText, /preferredInspector = Math\.Max\(440, Math\.Min\(560/);
assert.match(mainFormText, /_themeBox\.Width = 128/);
assert.match(mainFormText, /MeasureInspectorTextHeight/);
assert.match(sourceText, /toggleLines/);
assert.match(sourceText, /id='theme'/);
assert.match(sourceText, /prefers-color-scheme: dark/);
assert.match(sourceText, /theme-dark/);
assert.match(sourceText, /addEventListener\('pointermove'/);
assert.match(sourceText, /addEventListener\('wheel'/);
assert.match(sourceText, /event\.button!==2/);
assert.match(sourceText, /if\(!moved\)\{selected=''\;clearClasses\(\)\;\}/);
assert.match(sourceText, /locked=0/);
assert.match(mainFormText, /飞书画板（draw\.io，可编辑）/);
assert.doesNotMatch(graphCanvasText, /RelationColor/);
assert.match(graphCanvasText, /BeginInlineEdit\("edge", edge\.id, "label"/);
assert.match(sourceText, /new ColorCategoryChoice\("resource", "蓝色"/);
assert.doesNotMatch(sourceText, /灰蓝色（|蓝色（|绿色（|粉色（|紫色（|橙色（/);
assert.doesNotMatch(sourceText, /"应用修改"|"批量应用"/);
assert.doesNotMatch(sourceText, /new ToolStripButton\("＋关系"\)|new ToolStripButton\("删除"\)/);
assert.doesNotMatch(sourceText, /TcpListener|HttpListener|WebBrowser|Process\.Start\([^\n]*http|localhost|127\.0\.0\.1/);

if (staticOnly) {
  console.log(`Native desktop static checks passed (${sources.length} source files).`);
} else {
  assert.ok(fs.existsSync(exe), "关系图编辑器.exe 不存在");
  const exeBytes = fs.readFileSync(exe);
  assert.equal(exeBytes[0], 0x4d);
  assert.equal(exeBytes[1], 0x5a);
  assert.ok(exeBytes.length > 100_000, "EXE 体积异常");
  const latestInput = Math.max(
    ...sources.map((name) => fs.statSync(path.join(nativeRoot, name)).mtimeMs),
    fs.statSync(path.join(root, "desktop", "build-desktop.ps1")).mtimeMs,
    fs.statSync(manifestPath).mtimeMs,
    fs.statSync(path.join(root, "system-function-graph.json")).mtimeMs,
    fs.statSync(path.join(root, "assets", "app-icon.ico")).mtimeMs,
  );
  assert.ok(fs.statSync(exe).mtimeMs >= latestInput, "EXE 不是由最新原生源码构建的");

  const temp = fs.mkdtempSync(path.join(os.tmpdir(), "relationship-native-test-"));
  try {
    const isolatedExe = path.join(temp, "关系图编辑器.exe");
    const report = path.join(temp, "self-test.json");
    fs.copyFileSync(exe, isolatedExe);
    const run = spawnSync(isolatedExe, ["--self-test", report], { cwd: temp, windowsHide: true, timeout: 30_000 });
    assert.equal(run.error, undefined, run.error?.message);
    assert.ok(fs.existsSync(report), "独立 EXE 未生成自检报告");
    const result = JSON.parse(fs.readFileSync(report, "utf8"));
    assert.equal(result.ok, true, result.error);
    assert.equal(result.version, packageJson.version);
    assert.equal(result.runtime, "native-winforms");
    assert.deepEqual([result.groups, result.nodes, result.edges], [4, 12, 12]);
    const companions = fs.readdirSync(temp).filter((name) => name !== "关系图编辑器.exe" && name !== "self-test.json");
    assert.deepEqual(companions, [], "EXE 在独立文件夹中依赖了额外程序文件");
  } finally {
    fs.rmSync(temp, { recursive: true, force: true });
  }

  console.log(`Native desktop checks passed (${sources.length} source files, ${exeBytes.length} byte EXE).`);
}
