import { spawn } from 'node:child_process';
import fs from 'node:fs';
import http from 'node:http';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const toolDirectory = path.dirname(fileURLToPath(import.meta.url));
const projectDirectory = path.dirname(toolDirectory);
const read = file => fs.readFileSync(path.join(projectDirectory, file), 'utf8');
const issues = [];
const html = read('index.html');
const css = read('app.css');
const app = read('app.js');
const launcherSource = read('desktop/RelationshipGraphLauncher.cs');
const packageJson = JSON.parse(read('package.json'));
const buildScript = read('desktop/build-desktop.ps1');
const executablePath = path.join(projectDirectory, '关系图编辑器.exe');
const iconPath = path.join(projectDirectory, 'assets', 'app-icon.ico');
const embeddedResourceFiles = [
  'index.html',
  'app.css',
  'app.js',
  'system-function-default.js',
  'node-type-core.js',
  'routing-core.js',
  'project-store.js',
  'readonly-export.js',
  'import-core.js',
  'assets/app-icon.svg'
];

const expectText = (source, expected, message) => {
  if (!source.includes(expected)) issues.push(message);
};

expectText(html, 'class="brand-mark"', '桌面界面缺少品牌标志');
expectText(html, 'RELATIONSHIP STUDIO', '桌面界面缺少产品品牌文字');
expectText(html, '<header class="app-header">', '桌面界面缺少主顶栏');
expectText(html, 'class="app-header"', '桌面界面缺少顶栏结构');
expectText(html, 'class="graph-search" id="graphSearch"', '全局搜索没有进入桌面顶栏');
expectText(html, 'id="projectMenu"', '低频项目操作没有收进项目菜单');
expectText(html, 'assets/app-icon.svg', '入口页缺少品牌图标');
expectText(css, '/* Desktop product surface — v1.2 */', '缺少桌面视觉系统');
expectText(css, '.canvas-panel,\n  .inspector', '画布与详情面板没有统一产品表面');
expectText(css, 'backdrop-filter: blur', '桌面弹层缺少视觉层次');
expectText(app, 'initializeDesktopHeartbeat()', '桌面窗口没有维持本地服务的心跳');
expectText(launcherSource, 'IPAddress.Loopback', '桌面服务没有限制到本机回环地址');
expectText(launcherSource, '--app=', '启动器没有使用独立应用窗口');
expectText(launcherSource, '--disable-pinch', '桌面窗口没有禁用浏览器缩放手势');
expectText(launcherSource, '--overscroll-history-navigation=0', '桌面窗口没有禁用历史导航手势');
expectText(launcherSource, 'msEdgeMouseGestureDefaultEnabled', '桌面窗口没有禁用 Edge 鼠标手势');
expectText(launcherSource, '--user-data-dir=', '桌面窗口没有使用独立浏览器配置，启动参数可能被已有浏览器进程忽略');
expectText(launcherSource, '--disable-extensions', '桌面窗口没有禁用可能接管右键的浏览器扩展');
const chromeCandidateIndex = launcherSource.indexOf('Path.Combine(programFiles, "Google", "Chrome"');
const edgeCandidateIndex = launcherSource.indexOf('Path.Combine(programFilesX86, "Microsoft", "Edge"');
if (chromeCandidateIndex < 0 || edgeCandidateIndex < 0 || chromeCandidateIndex > edgeCandidateIndex) {
  issues.push('桌面启动器没有优先选择无内置右键手势的 Chrome');
}
expectText(launcherSource, 'http://127.0.0.1:{0}/index.html?desktop=1', '启动器没有使用稳定的本地桌面地址');
expectText(launcherSource, 'X-Content-Type-Options: nosniff', '桌面本地服务缺少内容类型保护');
expectText(launcherSource, 'Content-Security-Policy:', '桌面本地服务缺少内容安全策略');
expectText(launcherSource, 'IsAllowedLocalOrigin(headers)', '桌面内部控制端点缺少来源校验');
expectText(launcherSource, 'GetManifestResourceStream', '桌面程序没有从 EXE 读取内嵌界面资源');
expectText(buildScript, '/resource:', '桌面构建没有把界面资源嵌入 EXE');
if (launcherSource.includes('File.Exists(entryFile)')) issues.push('桌面程序仍依赖同目录 index.html');

if (packageJson.version !== '1.3.9') issues.push('桌面程序版本号不是 1.3.9');
if (!String(packageJson.scripts?.['build:desktop']).includes('build-desktop.ps1')) {
  issues.push('缺少桌面启动器构建命令');
}

if (!fs.existsSync(executablePath)) issues.push('缺少可双击的关系图编辑器.exe');
else {
  const executable = fs.readFileSync(executablePath);
  if (executable.length < 400000) issues.push('桌面程序文件异常过小，可能没有包含全部界面资源');
  if (executable[0] !== 0x4d || executable[1] !== 0x5a) issues.push('桌面启动器不是有效的 Windows PE 文件');
  const buildInputs = [
    'desktop/RelationshipGraphLauncher.cs',
    'desktop/build-desktop.ps1',
    ...embeddedResourceFiles
  ];
  const newestInputTime = Math.max(...buildInputs.map(file => fs.statSync(path.join(projectDirectory, file)).mtimeMs));
  if (fs.statSync(executablePath).mtimeMs < newestInputTime) issues.push('桌面程序早于源码或内嵌资源，需要重新构建');
}

if (!fs.existsSync(iconPath)) issues.push('缺少 Windows 桌面图标');
else {
  const icon = fs.readFileSync(iconPath);
  if (icon.length < 10000 || icon[0] !== 0 || icon[1] !== 0 || icon[2] !== 1 || icon[3] !== 0) {
    issues.push('Windows 桌面图标无效');
  }
}

const request = (url, options = {}) => new Promise((resolve, reject) => {
  const requestObject = http.request(url, {
    method: options.method || 'GET',
    timeout: options.timeout ?? 1800,
    headers: options.headers || {}
  }, response => {
    const chunks = [];
    response.on('data', chunk => chunks.push(chunk));
    response.on('end', () => {
      const body = Buffer.concat(chunks);
      resolve({
        status: response.statusCode || 0,
        ok: (response.statusCode || 0) >= 200 && (response.statusCode || 0) < 300,
        headers: {
          get(name) {
            const value = response.headers[String(name).toLowerCase()];
            return Array.isArray(value) ? value.join(', ') : value ?? null;
          }
        },
        async text() { return body.toString('utf8'); }
      });
    });
  });
  requestObject.on('timeout', () => requestObject.destroy(new Error('request timeout')));
  requestObject.on('error', reject);
  requestObject.end();
});

const healthUrl = 'http://127.0.0.1:17653/__health';
let preexistingServer = false;
try {
  const health = await request(healthUrl, { timeout: 500 });
  preexistingServer = health.ok && await health.text() === 'relationship-studio-desktop';
} catch {
  // A free port is the expected state before the launcher test.
}

let launcherProcess = null;
let serverRuntimeVerified = false;
let singleFilePortable = false;
let portableTestDirectory = null;
try {
  if (!preexistingServer && fs.existsSync(executablePath)) {
    portableTestDirectory = fs.mkdtempSync(path.join(os.tmpdir(), 'relationship-graph-single-exe-'));
    const portableExecutablePath = path.join(portableTestDirectory, '关系图编辑器.exe');
    fs.copyFileSync(executablePath, portableExecutablePath);
    launcherProcess = spawn(portableExecutablePath, ['--server-only'], {
      cwd: portableTestDirectory,
      detached: false,
      stdio: 'ignore',
      windowsHide: true
    });

    let ready = false;
    for (let attempt = 0; attempt < 30 && !ready; attempt += 1) {
      await new Promise(resolve => setTimeout(resolve, 100));
      try {
        const health = await request(healthUrl, { timeout: 400 });
        ready = health.ok && await health.text() === 'relationship-studio-desktop';
      } catch {
        // The native listener may still be starting.
      }
    }
    if (!ready) issues.push('桌面启动器的本地服务未能启动');
  }

  if ((preexistingServer || launcherProcess) && !issues.some(issue => issue.includes('未能启动'))) {
    const page = await request('http://127.0.0.1:17653/index.html?desktop=1');
    const pageText = await page.text();
    const style = await request('http://127.0.0.1:17653/app.css?v=20260807-3');
    const applicationScript = await request('http://127.0.0.1:17653/app.js?v=portable');
    const defaultGraph = await request('http://127.0.0.1:17653/system-function-default.js?v=portable');
    const importCore = await request('http://127.0.0.1:17653/import-core.js?v=portable');
    const browserIcon = await request('http://127.0.0.1:17653/assets/app-icon.svg');
    const heartbeat = await request('http://127.0.0.1:17653/__heartbeat', { method: 'POST' });
    const heartbeatGet = await request('http://127.0.0.1:17653/__heartbeat');
    const foreignHeartbeat = await request('http://127.0.0.1:17653/__heartbeat', {
      method: 'POST',
      headers: { Origin: 'https://example.invalid' }
    });
    const foreignShutdown = await request('http://127.0.0.1:17653/__shutdown', {
      method: 'POST',
      headers: { Origin: 'https://example.invalid' }
    });
    const traversal = await request('http://127.0.0.1:17653/%2e%2e/%2e%2e/Windows/win.ini');

    if (!page.ok || !pageText.includes('RELATIONSHIP STUDIO')) issues.push('桌面服务没有正确提供入口页');
    if (!String(page.headers.get('content-type')).startsWith('text/html')) issues.push('桌面入口页内容类型错误');
    if (page.headers.get('x-content-type-options') !== 'nosniff') issues.push('桌面入口页缺少 nosniff 响应头');
    if (!String(page.headers.get('content-security-policy')).includes("default-src 'self'")) issues.push('桌面入口页缺少有效内容安全策略');
    if (!style.ok || !String(style.headers.get('content-type')).startsWith('text/css')) issues.push('桌面服务没有正确提供样式表');
    if (!applicationScript.ok || !(await applicationScript.text()).includes('EDGE_LINE_TYPES')) issues.push('单文件 EXE 没有正确提供主程序脚本');
    if (!defaultGraph.ok || !(await defaultGraph.text()).includes('测试用图')) issues.push('单文件 EXE 没有包含默认关系图');
    if (!importCore.ok || !(await importCore.text()).includes('parseGraphFileText')) issues.push('单文件 EXE 没有包含只读版导入核心');
    if (!browserIcon.ok || !String(browserIcon.headers.get('content-type')).startsWith('image/svg+xml')) issues.push('单文件 EXE 没有包含界面图标');
    if (heartbeat.status !== 204) issues.push('桌面心跳端点不可用');
    if (heartbeatGet.status !== 405) issues.push('桌面心跳端点允许了错误的请求方法');
    if (foreignHeartbeat.status !== 403 || foreignShutdown.status !== 403) issues.push('桌面内部控制端点没有阻止外部网页来源');
    if (traversal.status !== 404) issues.push('桌面服务没有阻止目录越界访问');
    serverRuntimeVerified = !issues.some(issue => issue.startsWith('桌面服务') || issue.startsWith('桌面入口') || issue.startsWith('桌面心跳'));
    singleFilePortable = Boolean(portableTestDirectory) && serverRuntimeVerified &&
      !issues.some(issue => issue.startsWith('单文件 EXE'));
  }
} catch (error) {
  issues.push(`桌面运行链路验证失败：${error.message}`);
} finally {
  if (launcherProcess && !preexistingServer) {
    try {
      await request('http://127.0.0.1:17653/__shutdown', { method: 'POST', timeout: 800 });
    } catch {
      // The inactivity timer is a safe fallback.
    }
    await Promise.race([
      new Promise(resolve => launcherProcess.once('exit', resolve)),
      new Promise(resolve => setTimeout(resolve, 1600))
    ]);
  }
  if (portableTestDirectory) {
    try { fs.rmSync(portableTestDirectory, { recursive: true, force: true }); }
    catch { /* Antivirus scanners may briefly retain the copied executable. */ }
  }
}

const summary = {
  brandedDesktopUi: !issues.some(issue => issue.includes('品牌') || issue.includes('视觉系统') || issue.includes('产品表面')),
  nativeLauncher: fs.existsSync(executablePath) && !issues.some(issue => issue.includes('启动器')),
  singleFilePortable,
  loopbackOnly: launcherSource.includes('IPAddress.Loopback'),
  serverRuntimeVerified,
  browserChromeRestricted: launcherSource.includes('--disable-pinch') &&
    launcherSource.includes('--overscroll-history-navigation=0') &&
    launcherSource.includes('msEdgeMouseGestureDefaultEnabled'),
  issues
};

console.log(JSON.stringify(summary, null, 2));
if (issues.length) process.exitCode = 1;
