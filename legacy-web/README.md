# 旧网页版本

这里保留关系图编辑器重写为原生 WinForms 应用之前的网页实现、浏览器启动器及其专属测试，供历史回归和参考。它不参与当前原生 EXE 的构建或发布。

直接打开 `index.html` 可使用静态网页版本。默认图以仓库根目录的 `system-function-graph.json` 为唯一数据源；数据变更后在仓库根目录运行 `npm run legacy:sync-default` 更新本目录的 `system-function-default.js`。

旧版检查统一使用 `legacy:*` 命令，例如 `npm run legacy:audit`、`npm run legacy:audit:routing` 和 `npm run legacy:test:store`。如需检查历史单文件浏览器启动器，可通过 `RELATIONSHIP_GRAPH_LEGACY_EXE` 指定已有的旧版 EXE 后运行 `npm run legacy:test:desktop`；未指定时只检查保留的源码与资源。
