# 关系图编辑器

纯原生 Windows 桌面关系图编辑器，使用 C#、WinForms 和 GDI+。程序不依赖浏览器、不启动本地服务，也不会联网。

当前版本：`4.4.0`

## 快速开始

双击根目录的 `关系图编辑器.exe` 即可运行。它是独立 EXE，可直接复制给其他 Windows 10/11 用户；系统需启用 .NET Framework 4.x。

## 核心功能

- 创建和编辑节点、分组及关系，支持多层嵌套与自动归组。
- 查看上游、下游或双向关系，可聚焦 1～3 层相邻对象。
- 拖动对齐、等间距吸附；按住 `Ctrl` 可自由摆放。
- 框选、多选、复制粘贴、撤销重做、查找和无限方向画布。
- 双击画布或对象可快速创建、重命名和编辑类型。
- 右侧属性即时生效，无需额外点击“应用”。
- 支持跟随系统、浅色和深色主题。
- 自动保存、历史恢复和原子写入，避免写入中断破坏文件。

## 常用操作

| 操作 | 方法 |
| --- | --- |
| 新增节点 | 双击画布空白处，或点击“＋节点” |
| 移动对象 | 先选中，再拖动；按住 `Ctrl` 关闭吸附 |
| 创建关系 | 将未选中的节点或分组拖到目标对象 |
| 编辑名称或类型 | 双击对象上的对应文字 |
| 调整分组大小 | 选中分组后拖动边缘或四角 |
| 多选 / 框选 | `Ctrl` 或 `Shift` + 单击；空白处按住左键拖动 |
| 复制 / 粘贴 | `Ctrl+C` / `Ctrl+V` |
| 删除 | `Delete`，误删可撤销 |
| 撤销 / 重做 | `Ctrl+Z` / `Ctrl+Y` |
| 平移 / 缩放 | 按住右键拖动；滚轮缩放 |
| 显示全部内容 | “适合窗口”或 `Ctrl+0` |
| 查找 | `Ctrl+F` |
| 导入 / 保存 | `Ctrl+O` / `Ctrl+S` |

## 文件格式

可导入本工具的 JSON，以及旧版导出的只读 HTML。

可导出：

- JSON：保留完整编辑数据；
- `.drawio`：可导入飞书画板继续编辑；
- HTML：便于分享和交互查看；
- SVG、PNG、PDF：PDF 为矢量图形与文字轮廓。

JSON 再次保存时会保留上一版 `.bak`。自动恢复数据位于：

```text
%LocalAppData%\Relationship Studio\autosave-native.json
```

## 构建与检查

```powershell
npm run build:desktop   # 生成单个 EXE
npm test                # 构建并运行完整原生自检
npm run audit           # 仅做源码静态检查
```

默认构建不签名。发布时可传入 Windows 证书存储中的代码签名证书：

```powershell
powershell -ExecutionPolicy Bypass -File desktop/build-desktop.ps1 `
  -CertificateThumbprint "你的证书指纹"
```

构建会嵌入应用图标、默认关系图和高 DPI 清单。正式发布前建议在不同缩放比例的显示器上检查界面。

## 目录说明

- `native/`：当前 WinForms 程序源码；
- `desktop/`：构建脚本与应用清单；
- `tools/`：原生检查工具；
- `system-function-graph.json`：默认关系图唯一数据源；
- `legacy-web/`：旧网页版本及其专属测试，不参与原生 EXE 构建。

旧网页默认数据变更后，可运行 `npm run legacy:sync-default` 更新镜像。
