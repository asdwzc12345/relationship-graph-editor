using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("关系图编辑器")]
[assembly: System.Reflection.AssemblyDescription("纯原生 Windows 关系图编辑器")]
[assembly: System.Reflection.AssemblyCompany("Relationship Studio")]
[assembly: System.Reflection.AssemblyProduct("关系图编辑器")]
[assembly: System.Reflection.AssemblyCopyright("Copyright © 2026")]
[assembly: System.Reflection.AssemblyVersion("5.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("5.0.0.0")]

namespace RelationshipGraphNative
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0].Equals("--self-test", StringComparison.OrdinalIgnoreCase))
                return RunSelfTest(args[1]);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    MessageBox.Show("程序遇到问题：\n" + e.Exception.Message, "关系图编辑器", MessageBoxButtons.OK, MessageBoxIcon.Error);
                };
                Application.Run(new MainForm());
                return 0;
            }
            catch (Exception error)
            {
                MessageBox.Show("程序无法启动：\n" + error.Message, "关系图编辑器", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static int RunSelfTest(string reportPath)
        {
            string tempFolder = "";
            try
            {
                GraphDocument graph = GraphSerialization.LoadDefault();
                Require(graph.meta.title == "测试用图", "默认图名称错误");
                Require(graph.version == 3 && graph.nodes.All(delegate(GraphNode node) { return node.groups != null; }) && graph.groups.All(delegate(GraphGroup group) { return group.groups != null; }), "多层分组数据结构未升级");
                Require(graph.groups.Count == 4, "默认分组数量错误");
                Require(graph.nodes.Count == 12, "默认节点数量错误");
                Require(graph.edges.Count == 12, "默认关系数量错误");
                Require(graph.edges.TrueForAll(delegate(GraphEdge edge) { return edge.sourceType == "node" && edge.targetType == "node"; }), "旧版端点兼容失败");

                GraphDocument roundTrip = GraphSerialization.Deserialize(GraphSerialization.Serialize(graph, false));
                Require(roundTrip.nodes.Count == graph.nodes.Count && roundTrip.edges.Count == graph.edges.Count, "JSON 往返失败");
                GraphHistory history = new GraphHistory();
                for (int historyIndex = 0; historyIndex < 70; historyIndex++)
                {
                    GraphDocument snapshot = GraphSerialization.Clone(graph); snapshot.meta.title = "历史 " + historyIndex;
                    history.Push(snapshot);
                }
                Require(history.Count <= 60 && history.StoredCharactersForTesting <= 16 * 1024 * 1024, "撤销历史没有按数量和内存预算收敛");
                Require(history.Pop().meta.title == "历史 69", "撤销历史顺序错误");
                GraphDocument blank = GraphSerialization.CreateBlank("空白图");
                Require(blank.groups.Count == 0 && blank.nodes.Count == 0, "无分组空白图失败");

                GraphDocument replacementGraph = GraphSerialization.CreateBlank("替换测试");
                replacementGraph.nodes.Add(new GraphNode { id = "replace_1", label = "Alpha Alpha", type = "alpha 类型", kind = "system", group = "", x = 20, y = 20, w = 150, h = 54, note = "备注 ALPHA" });
                replacementGraph.nodes.Add(new GraphNode { id = "replace_2", label = "Alpha", type = "步骤", kind = "system", group = "", x = 220, y = 20, w = 150, h = 54, note = "" });
                replacementGraph.groups.Add(new GraphGroup { id = "replace_group", label = "Alpha 分组", x = 0, y = 0, w = 420, h = 160 });
                replacementGraph.edges.Add(new GraphEdge { id = "replace_edge", source = "replace_1", target = "replace_2", sourceType = "node", targetType = "node", label = "alpha 关系", category = "core", lineType = "curve" });
                replacementGraph = GraphSerialization.Normalize(replacementGraph);
                GraphTextReplacementResult selectedReplacement = GraphTextReplacement.ReplaceSelection(replacementGraph, "alpha", "Beta", new string[] { "replace_1" }, new string[0], "");
                Require(selectedReplacement.AppliedOccurrences == 4 && selectedReplacement.ChangedFields == 3, "替换当前所选没有覆盖名称、类型和备注中的全部匹配");
                Require(replacementGraph.nodes[0].label == "Beta Beta" && replacementGraph.nodes[1].label == "Alpha" && replacementGraph.groups[0].label == "Alpha 分组", "替换当前所选误改了未选对象");
                GraphTextReplacementResult emptyNameReplacement = GraphTextReplacement.ReplaceSelection(replacementGraph, "Alpha", "", new string[] { "replace_2" }, new string[0], "");
                Require(emptyNameReplacement.AppliedOccurrences == 0 && emptyNameReplacement.SkippedOccurrences == 1 && replacementGraph.nodes[1].label == "Alpha", "替换导致空名称时没有安全跳过");
                GraphTextReplacementResult allReplacement = GraphTextReplacement.ReplaceAll(replacementGraph, "alpha", "Gamma");
                Require(allReplacement.AppliedOccurrences == 3 && replacementGraph.nodes[1].label == "Gamma" && replacementGraph.groups[0].label == "Gamma 分组" && replacementGraph.edges[0].label == "Gamma 关系", "全部替换没有覆盖整张图的可编辑文字");
                int literalOccurrences;
                Require(GraphTextReplacement.ReplaceText("a.a.A", "a.", "$1", out literalOccurrences) == "$1$1A" && literalOccurrences == 2, "替换内容被错误当作正则表达式处理");

                GraphDocument repaired = GraphSerialization.CreateBlank("兼容测试");
                repaired.groups.Add(new GraphGroup { id = "分组 一", label = "分组", x = 10, y = 10, w = 220, h = 180 });
                repaired.nodes.Add(new GraphNode { id = "节点 一", label = "节点一", type = "步骤", kind = "system", group = "分组 一", x = 30, y = 50, w = 120, h = 50 });
                repaired.nodes.Add(new GraphNode { id = "节点 二", label = "节点二", type = "步骤", kind = "system", group = "", x = 280, y = 50, w = 120, h = 50 });
                repaired.edges.Add(new GraphEdge { id = "关系 一", source = "节点 一", target = "节点 二", label = "", category = "core", lineType = "polyline" });
                repaired = GraphSerialization.Normalize(repaired);
                Require(repaired.edges.Count == 1 && repaired.nodes[0].group == repaired.groups[0].id, "旧标识符修复导致关系或分组归属丢失");

                tempFolder = Path.Combine(Path.GetTempPath(), "relationship-graph-native-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempFolder);
                string atomicPath = Path.Combine(tempFolder, "atomic-save.json");
                NativePersistence.WriteAllTextAtomic(atomicPath, "first", new UTF8Encoding(false), true);
                NativePersistence.WriteAllTextAtomic(atomicPath, "second", new UTF8Encoding(false), true);
                Require(File.ReadAllText(atomicPath, Encoding.UTF8) == "second" && File.ReadAllText(atomicPath + ".bak", Encoding.UTF8) == "first", "原子保存或备份失败");
                GraphDocument drawioTextJson = GraphSerialization.CreateBlank("JSON 格式识别测试");
                drawioTextJson.nodes.Add(new GraphNode { id = "json_text", label = "包含 <mxfile 文本", type = "节点类型", kind = "system", shape = "process", group = "", x = 10, y = 10, w = 150, h = 54, note = "<mxGraphModel 也只是普通备注" });
                drawioTextJson = GraphSerialization.Normalize(drawioTextJson);
                string drawioTextJsonPath = Path.Combine(tempFolder, "drawio-text.txt");
                NativeExport.SaveJson(drawioTextJson, drawioTextJsonPath);
                GraphDocument importedDrawioTextJson = GraphSerialization.LoadFile(drawioTextJsonPath);
                Require(importedDrawioTextJson.nodes.Count == 1 && importedDrawioTextJson.nodes[0].label == "包含 <mxfile 文本", "JSON 文本被误判为 Draw.io XML");
                bool interruptedWriteFailed = false;
                try
                {
                    NativePersistence.WriteStreamAtomic(atomicPath, true, delegate(Stream stream)
                    {
                        byte[] partial = Encoding.UTF8.GetBytes("partial"); stream.Write(partial, 0, partial.Length);
                        throw new IOException("simulated interruption");
                    });
                }
                catch (IOException) { interruptedWriteFailed = true; }
                Require(interruptedWriteFailed && File.ReadAllText(atomicPath, Encoding.UTF8) == "second", "写入中断破坏了原文件");
                Require(Directory.GetFiles(tempFolder, ".atomic-save.json.*.tmp").Length == 0, "原子保存残留临时文件");
                bool publishCancelled = false;
                try
                {
                    NativePersistence.WriteStreamAtomic(atomicPath, false, delegate(Stream stream)
                    {
                        byte[] replacement = Encoding.UTF8.GetBytes("cancelled replacement"); stream.Write(replacement, 0, replacement.Length);
                    }, delegate { throw new OperationCanceledException("simulated pre-publish cancellation"); });
                }
                catch (OperationCanceledException) { publishCancelled = true; }
                Require(publishCancelled && File.ReadAllText(atomicPath, Encoding.UTF8) == "second", "正式发布前取消仍覆盖了原文件");

                Exception concurrentSerializationError = null;
                List<Thread> serializationThreads = new List<Thread>();
                for (int threadIndex = 0; threadIndex < 4; threadIndex++)
                {
                    Thread thread = new Thread(delegate()
                    {
                        try
                        {
                            for (int iteration = 0; iteration < 60; iteration++)
                            {
                                string serialized = GraphSerialization.Serialize(graph, false);
                                GraphDocument parsed = GraphSerialization.Deserialize(serialized);
                                if (parsed.nodes.Count != graph.nodes.Count || parsed.edges.Count != graph.edges.Count) throw new InvalidDataException("并发序列化往返数量错误。");
                            }
                        }
                        catch (Exception error) { concurrentSerializationError = error; }
                    });
                    thread.IsBackground = true; serializationThreads.Add(thread); thread.Start();
                }
                foreach (Thread thread in serializationThreads) Require(thread.Join(TimeSpan.FromSeconds(5)), "并发序列化测试超时");
                Require(concurrentSerializationError == null, "并发序列化失败：" + (concurrentSerializationError == null ? "" : concurrentSerializationError.Message));

                AutosaveStore autosaveStore = new AutosaveStore(Path.Combine(tempFolder, "recovery", "autosave.json"));
                Require(autosaveStore.Save(GraphSerialization.Serialize(graph, false)).Length == 0, "自动恢复主文件保存失败");
                Require(autosaveStore.GetRecoveryCandidates().Count >= 2, "自动恢复没有生成主文件和历史版本");
                autosaveStore.ClearAll();
                Require(autosaveStore.GetRecoveryCandidates().Count == 0 && !Directory.Exists(autosaveStore.HistoryDirectory), "放弃修改后自动恢复数据未清理干净");

                string recentInputFolder = Path.Combine(tempFolder, "recent-input");
                Directory.CreateDirectory(recentInputFolder);
                List<string> recentInputFiles = new List<string>();
                for (int recentIndex = 0; recentIndex < 12; recentIndex++)
                {
                    string recentInputPath = Path.Combine(recentInputFolder, "recent-" + recentIndex + ".json");
                    File.WriteAllText(recentInputPath, "{}", new UTF8Encoding(false));
                    recentInputFiles.Add(Path.GetFullPath(recentInputPath));
                }
                RecentFileStore recentFileStore = new RecentFileStore(Path.Combine(tempFolder, "recent-files.txt"));
                foreach (string recentInputPath in recentInputFiles) Require(recentFileStore.Add(recentInputPath), "最近文件写入失败");
                IList<string> recentFiles = recentFileStore.GetFiles();
                Require(recentFiles.Count == RecentFileStore.MaximumFiles && recentFiles[0] == recentInputFiles[11] && recentFiles[9] == recentInputFiles[2], "最近文件数量上限或顺序错误");
                Require(recentFileStore.Add(recentInputFiles[5]), "最近文件重复项更新失败");
                recentFiles = recentFileStore.GetFiles();
                Require(recentFiles.Count == RecentFileStore.MaximumFiles && recentFiles[0] == recentInputFiles[5] && recentFiles.Distinct(StringComparer.OrdinalIgnoreCase).Count() == recentFiles.Count, "最近文件没有去重或置顶");
                File.Delete(recentInputFiles[5]);
                recentFiles = recentFileStore.GetFiles();
                Require(recentFiles.Count == RecentFileStore.MaximumFiles - 1 && !recentFiles.Contains(recentInputFiles[5], StringComparer.OrdinalIgnoreCase), "最近文件没有过滤已不存在的文件");
                Require(!recentFileStore.Add(Path.Combine(recentInputFolder, "missing.json")), "最近文件错误接受了不存在的文件");

                GraphDocument saveAsGraph = GraphSerialization.CreateBlank("另存为原始版本");
                string firstSaveAsPath = Path.Combine(tempFolder, "save-as-first.json"), secondSaveAsPath = Path.Combine(tempFolder, "save-as-second.json");
                using (MainForm saveAsWindow = new MainForm(saveAsGraph, false, Path.Combine(tempFolder, "save-as-recent.txt")))
                {
                    Require(saveAsWindow.SaveJsonToFileForTesting(firstSaveAsPath), "首次保存 JSON 失败");
                    saveAsWindow.CanvasForTesting.Document.meta.title = "另存为新版本";
                    Require(saveAsWindow.SaveJsonToFileForTesting(secondSaveAsPath), "另存为新路径失败");
                    Require(String.Equals(saveAsWindow.CurrentFileForTesting, secondSaveAsPath, StringComparison.OrdinalIgnoreCase), "另存为后当前文件路径未切换");
                }
                Require(GraphSerialization.LoadFile(firstSaveAsPath).meta.title == "另存为原始版本" && GraphSerialization.LoadFile(secondSaveAsPath).meta.title == "另存为新版本", "另存为错误覆盖了原文件或未写入新文件");

                using (BackgroundWorkQueue backgroundQueue = new BackgroundWorkQueue("RelationshipGraph.SelfTest"))
                using (ManualResetEvent progressSeen = new ManualResetEvent(false))
                using (ManualResetEvent firstCompleted = new ManualResetEvent(false))
                using (ManualResetEvent cancelStarted = new ManualResetEvent(false))
                using (ManualResetEvent cancelCompleted = new ManualResetEvent(false))
                {
                    BackgroundWorkProgressEventArgs reportedProgress = null;
                    BackgroundWorkCompletedEventArgs firstResult = null;
                    BackgroundWorkCompletedEventArgs cancelResult = null;
                    backgroundQueue.ProgressChanged += delegate(object sender, BackgroundWorkProgressEventArgs e)
                    {
                        if (e.Name != "progress-test") return;
                        reportedProgress = e;
                        progressSeen.Set();
                    };
                    backgroundQueue.WorkCompleted += delegate(object sender, BackgroundWorkCompletedEventArgs e)
                    {
                        if (e.Name == "progress-test") { firstResult = e; firstCompleted.Set(); }
                        else if (e.Name == "cancel-test") { cancelResult = e; cancelCompleted.Set(); }
                    };

                    long progressWorkId = backgroundQueue.Enqueue("progress-test", delegate(BackgroundWorkContext context)
                    {
                        context.ReportProgress(42, "self-test progress");
                    });
                    Require(progressSeen.WaitOne(TimeSpan.FromSeconds(2)), "后台任务没有报告进度");
                    Require(firstCompleted.WaitOne(TimeSpan.FromSeconds(2)), "后台任务没有报告完成");
                    Require(reportedProgress != null && reportedProgress.WorkId == progressWorkId && reportedProgress.Percentage == 42 && reportedProgress.Message == "self-test progress", "后台任务进度内容错误");
                    Require(firstResult != null && firstResult.WorkId == progressWorkId && firstResult.Succeeded, "后台任务完成状态错误");

                    long cancelWorkId = backgroundQueue.Enqueue("cancel-test", delegate(BackgroundWorkContext context)
                    {
                        cancelStarted.Set();
                        while (true)
                        {
                            context.ThrowIfCancellationRequested();
                            Thread.Sleep(5);
                        }
                    });
                    Require(cancelStarted.WaitOne(TimeSpan.FromSeconds(2)), "待取消后台任务没有启动");
                    Require(backgroundQueue.Cancel(cancelWorkId), "后台任务取消请求失败");
                    Require(cancelCompleted.WaitOne(TimeSpan.FromSeconds(2)), "后台任务取消没有完成");
                    Require(cancelResult != null && cancelResult.WorkId == cancelWorkId && cancelResult.Cancelled && cancelResult.Error == null, "后台任务取消状态错误");
                    Require(backgroundQueue.WaitForIdle(TimeSpan.FromSeconds(2)), "后台任务队列未及时空闲");
                }

                List<string> savedSnapshots = new List<string>();
                object savedSnapshotsSync = new object();
                using (DebouncedSaveQueue<string> saveQueue = new DebouncedSaveQueue<string>(TimeSpan.FromMilliseconds(100), delegate(string snapshot, BackgroundWorkContext context)
                {
                    context.ThrowIfCancellationRequested();
                    lock (savedSnapshotsSync) savedSnapshots.Add(snapshot);
                }))
                {
                    saveQueue.QueueSave("snapshot-1");
                    saveQueue.QueueSave("snapshot-2");
                    saveQueue.QueueSave("snapshot-3");
                    Require(saveQueue.Flush(TimeSpan.FromSeconds(2)), "防抖保存 Flush 超时");
                }
                lock (savedSnapshotsSync)
                    Require(savedSnapshots.Count == 1 && savedSnapshots[0] == "snapshot-3", "防抖保存没有只保留最新快照");
                Require(MainForm.AutosaveNeedsSynchronousFallbackForTesting(false, null) && MainForm.AutosaveNeedsSynchronousFallbackForTesting(true, new IOException("save failed")) && !MainForm.AutosaveNeedsSynchronousFallbackForTesting(true, null), "关闭前自动保存失败兜底判定错误");
                using (ManualResetEvent failedSaveCompleted = new ManualResetEvent(false))
                using (DebouncedSaveQueue<string> failedSaveQueue = new DebouncedSaveQueue<string>(TimeSpan.Zero, delegate(string snapshot, BackgroundWorkContext context) { throw new IOException("simulated autosave failure"); }))
                {
                    BackgroundWorkCompletedEventArgs failedSaveResult = null;
                    failedSaveQueue.SaveCompleted += delegate(object sender, BackgroundWorkCompletedEventArgs e) { failedSaveResult = e; failedSaveCompleted.Set(); };
                    failedSaveQueue.QueueSave("broken");
                    Require(failedSaveQueue.Flush(TimeSpan.FromSeconds(2)) && failedSaveCompleted.WaitOne(TimeSpan.FromSeconds(2)), "失败的后台保存没有完成通知");
                    Require(failedSaveResult != null && failedSaveResult.Error is IOException, "后台保存错误未传播给关闭兜底逻辑");
                }

                string backgroundPngPath = Path.Combine(tempFolder, "background-export.png"), backgroundPdfPath = Path.Combine(tempFolder, "background-export.pdf");
                using (BackgroundWorkQueue exportQueue = new BackgroundWorkQueue("RelationshipGraph.ExportSelfTest"))
                using (ManualResetEvent exportCompleted = new ManualResetEvent(false))
                {
                    BackgroundWorkCompletedEventArgs exportResult = null;
                    exportQueue.WorkCompleted += delegate(object sender, BackgroundWorkCompletedEventArgs e) { exportResult = e; exportCompleted.Set(); };
                    exportQueue.Enqueue("background-export", delegate(BackgroundWorkContext context)
                    {
                        context.ReportProgress(10, "rendering"); context.ThrowIfCancellationRequested();
                        NativeExport.SavePng(GraphSerialization.CreateImmutableSnapshot(graph), false, backgroundPngPath);
                        context.ThrowIfCancellationRequested();
                        NativeExport.SavePdf(GraphSerialization.CreateImmutableSnapshot(graph), backgroundPdfPath);
                    });
                    Require(exportCompleted.WaitOne(TimeSpan.FromSeconds(5)), "后台 PNG/PDF 导出超时");
                    Require(exportResult != null && exportResult.Succeeded && File.Exists(backgroundPngPath) && File.Exists(backgroundPdfPath), "后台线程无法安全生成 PNG/PDF");
                }

                string htmlPath = Path.Combine(tempFolder, "readonly.html");
                NativeExport.SaveReadonlyHtml(graph, htmlPath);
                GraphDocument imported = GraphSerialization.LoadFile(htmlPath);
                Require(imported.meta.title == graph.meta.title && imported.edges.Count == graph.edges.Count, "只读可视图导入失败");
                string html = File.ReadAllText(htmlPath, Encoding.UTF8);
                Require(html.Contains("id=\"graphData\"") && html.Contains("<svg"), "只读可视图内容不完整");
                Require(html.Contains("id='toggleLines'") && html.Contains("lines-hidden") && html.Contains("applyFocus()"), "只读可视图缺少连线开关或节点关系响应");
                Require(html.Contains("id='theme'") && html.Contains("prefers-color-scheme: dark") && html.Contains("theme-dark") && html.Contains("applyTheme()"), "只读可视图缺少跟随系统、浅色或深色主题");
                Require(html.Contains("pointermove") && html.Contains("event.button!==2") && html.Contains("if(!moved){selected='';clearClasses();}") && html.Contains("view.x-=") && html.Contains("addEventListener('wheel'") && html.Contains("id='zoomIn'"), "只读可视图缺少右键清除、右键拖动平移或缩放能力");
                Require(html.Contains("contextmenu") && html.Contains("preventDefault"), "只读可视图未禁用浏览器右键菜单");
                Require(NativeExport.BuildSvg(graph).StartsWith("<svg", StringComparison.Ordinal), "SVG 导出失败");
                Require(NativeExport.BuildSvg(graph).Contains("data-kind="), "SVG 节点缺少只读深色主题所需的颜色分类标记");

                GraphDocument endpointGraph = GraphSerialization.Clone(graph);
                endpointGraph.nodes[0].label = "Draw.io & <节点>";
                endpointGraph.nodes[0].type = "类型 & <测试>";
                endpointGraph.nodes[0].shape = "decision";
                endpointGraph.nodes[0].kind = "content";
                endpointGraph.nodes[0].note = "Draw.io 往返备注\n第二行";
                endpointGraph.edges.Add(new GraphEdge { id = "group_link", sourceType = "group", source = endpointGraph.groups[0].id, targetType = "group", target = endpointGraph.groups[1].id, label = "", category = "core", lineType = "straight" });
                endpointGraph = GraphSerialization.Normalize(endpointGraph);
                Require(endpointGraph.edges.Exists(delegate(GraphEdge edge) { return edge.id == "group_link" && edge.sourceType == "group" && edge.targetType == "group" && edge.label == ""; }), "分组关系或空名称兼容失败");
                string drawioPath = Path.Combine(tempFolder, "feishu-board.drawio");
                NativeExport.SaveDrawio(endpointGraph, drawioPath);
                string drawio = File.ReadAllText(drawioPath, Encoding.UTF8);
                System.Xml.XmlDocument drawioXml = new System.Xml.XmlDocument(); drawioXml.LoadXml(drawio);
                Require(drawio.StartsWith("<?xml", StringComparison.Ordinal) && drawio.Contains("<mxfile") && drawio.Contains("<mxGraphModel"), "飞书画板文件结构不完整");
                Require(drawio.Contains("vertex=\"1\"") && drawio.Contains("locked=0") && drawio.Contains("id=\"n_"), "飞书画板节点不是独立可移动图形");
                Require(drawio.Contains("id=\"g_") && drawio.Contains("container=1") && drawio.Contains("id=\"e_group_link\""), "飞书画板分组或连线导出失败");
                Require(drawio.Contains("source=\"g_") && drawio.Contains("target=\"g_"), "飞书画板分组关系未保持连接");
                Require(drawio.Contains("rgFormat=\"relationship-graph-v1\"") && drawio.Contains("rgShape=\"decision\"") && drawio.Contains("rhombus;"), "Draw.io 导出缺少可逆元数据或流程图形状");

                string drawioNotice;
                GraphDocument importedDrawio = GraphSerialization.LoadFile(drawioPath, out drawioNotice);
                GraphNode originalDrawioNode = endpointGraph.nodes[0];
                GraphNode importedDrawioNode = importedDrawio.nodes.First(delegate(GraphNode node) { return node.id == originalDrawioNode.id; });
                Require(drawioNotice.Length == 0 && importedDrawio.meta.title == endpointGraph.meta.title && importedDrawio.meta.diagramType == endpointGraph.meta.diagramType, "本工具 Draw.io 标题或图类型往返失败");
                Require(importedDrawio.groups.Count == endpointGraph.groups.Count && importedDrawio.nodes.Count == endpointGraph.nodes.Count && importedDrawio.edges.Count == endpointGraph.edges.Count, "本工具 Draw.io 对象数量往返失败");
                Require(importedDrawioNode.type == originalDrawioNode.type && importedDrawioNode.label == originalDrawioNode.label && importedDrawioNode.kind == "content" && importedDrawioNode.shape == "decision" && importedDrawioNode.note == "Draw.io 往返备注\n第二行", "Draw.io 节点属性往返失败");
                Require(Math.Abs(importedDrawioNode.x - originalDrawioNode.x) < .01f && Math.Abs(importedDrawioNode.y - originalDrawioNode.y) < .01f, "Draw.io 往返未恢复原始画布位置");
                Require(importedDrawio.edges.Exists(delegate(GraphEdge edge) { return edge.id == "group_link" && edge.sourceType == "group" && edge.targetType == "group" && edge.lineType == "straight"; }), "Draw.io 分组关系往返失败");

                string legacyDrawioPath = Path.Combine(tempFolder, "legacy-export.drawio");
                string legacyDrawio = System.Text.RegularExpressions.Regex.Replace(drawio, "\\s+rg[A-Za-z]+=[\"'][^\"']*[\"']", "");
                File.WriteAllText(legacyDrawioPath, legacyDrawio, new UTF8Encoding(false));
                GraphDocument legacyImportedDrawio = GraphSerialization.LoadFile(legacyDrawioPath, out drawioNotice);
                GraphNode legacyImportedNode = legacyImportedDrawio.nodes.First(delegate(GraphNode node) { return node.label == "Draw.io & <节点>"; });
                Require(legacyImportedDrawio.nodes.Count == endpointGraph.nodes.Count && legacyImportedDrawio.groups.Count == endpointGraph.groups.Count && legacyImportedDrawio.edges.Count == endpointGraph.edges.Count, "旧版 Draw.io 导出文件对象导入失败");
                Require(legacyImportedNode.type == "类型 & <测试>" && legacyImportedNode.kind == "content" && legacyImportedDrawio.edges.Exists(delegate(GraphEdge edge) { return edge.sourceType == "group" && edge.targetType == "group"; }), "旧版 Draw.io 文字、颜色或分组端点兼容失败");

                string genericDrawioPath = Path.Combine(tempFolder, "generic-multipage.drawio");
                string genericDrawio = @"<?xml version=""1.0"" encoding=""UTF-8""?><mxfile><diagram name=""通用第一页""><mxGraphModel><root><mxCell id=""0""/><mxCell id=""1"" parent=""0""/><mxCell id=""lane"" value=""业务域"" style=""swimlane;container=1;"" vertex=""1"" parent=""1""><mxGeometry x=""100"" y=""80"" width=""500"" height=""300"" as=""geometry""/></mxCell><object id=""wrapped"" label=""包装 &lt;API&gt; &amp; 节点""><mxCell style=""rounded=1;fillColor=#dcecff;"" vertex=""1"" parent=""lane""><mxGeometry x=""40"" y=""60"" width=""140"" height=""60"" as=""geometry""/></mxCell></object><mxCell id=""wrapped-port"" style=""ellipse;"" vertex=""1"" parent=""wrapped""><mxGeometry x=""1"" y=""0.5"" width=""8"" height=""8"" relative=""1"" as=""geometry""/></mxCell><UserObject id=""user-object"" label=""用户对象""><mxCell style=""rounded=1;"" vertex=""1"" parent=""lane""><mxGeometry x=""40"" y=""180"" width=""140"" height=""60"" as=""geometry""/></mxCell></UserObject><mxCell id=""decision"" value=""&lt;b&gt;审批&lt;/b&gt;"" style=""rhombus;whiteSpace=wrap;html=1;"" vertex=""1"" parent=""lane""><mxGeometry x=""260"" y=""70"" width=""120"" height=""80"" as=""geometry""/></mxCell><mxCell id=""link"" value="""" style=""edgeStyle=orthogonalEdgeStyle;entryX=0;entryY=0.5;"" edge=""1"" parent=""1"" source=""wrapped-port"" target=""decision""><mxGeometry relative=""1"" as=""geometry""/></mxCell><mxCell id=""link-label"" value=""调用"" style=""edgeLabel;html=1;"" vertex=""1"" parent=""link""><mxGeometry relative=""1"" as=""geometry""/></mxCell><mxCell id=""dangling"" edge=""1"" parent=""1"" source=""wrapped"" target=""missing""><mxGeometry relative=""1"" as=""geometry""/></mxCell></root></mxGraphModel></diagram><diagram name=""第二页""><mxGraphModel><root><mxCell id=""0""/><mxCell id=""1"" parent=""0""/><mxCell id=""ignored"" value=""不应导入"" vertex=""1"" parent=""1""><mxGeometry x=""0"" y=""0"" width=""120"" height=""50"" as=""geometry""/></mxCell></root></mxGraphModel></diagram></mxfile>";
                File.WriteAllText(genericDrawioPath, genericDrawio, new UTF8Encoding(false));
                GraphDocument genericImported = GraphSerialization.LoadFile(genericDrawioPath, out drawioNotice);
                GraphNode wrappedNode = genericImported.nodes.First(delegate(GraphNode node) { return node.label == "包装 <API> & 节点"; });
                GraphNode decisionNode = genericImported.nodes.First(delegate(GraphNode node) { return node.label == "审批"; });
                Require(genericImported.meta.title == "通用第一页" && drawioNotice.Contains("2") && drawioNotice.Contains("1 条") && genericImported.groups.Count == 1 && genericImported.nodes.Count == 3 && genericImported.edges.Count == 1, "通用多页 Draw.io 导入范围或忽略提示错误");
                Require(Math.Abs(wrappedNode.x - 140f) < .01f && Math.Abs(wrappedNode.y - 140f) < .01f && wrappedNode.group == genericImported.groups[0].id && wrappedNode.kind == "resource", "Draw.io 包装节点、父坐标或分组归属导入失败");
                Require(genericImported.nodes.Exists(delegate(GraphNode node) { return node.label == "用户对象"; }) && decisionNode.shape == "decision" && genericImported.edges[0].label == "调用" && genericImported.edges[0].lineType == "polyline" && genericImported.edges[0].sourceSide == "right" && genericImported.edges[0].targetSide == "left", "Draw.io UserObject、形状、关系名称、线型或相对端口导入失败");

                IList<DrawioPageInfo> genericPageInfos = NativeDrawioImport.GetPageInfos(genericDrawio, "后备标题");
                Require(genericPageInfos.Count == 2 && genericPageInfos[0].Index == 0 && genericPageInfos[0].Name == "通用第一页" && genericPageInfos[0].DisplayName == "通用第一页" && !genericPageInfos[0].IsBareModel &&
                    genericPageInfos[1].Index == 1 && genericPageInfos[1].Name == "第二页" && genericPageInfos[1].DisplayName == "第二页" && !genericPageInfos[1].IsBareModel,
                    "Draw.io 多页面信息枚举的数量、索引或名称错误");
                string contentDetectedDrawioPath = Path.Combine(tempFolder, "content-detected.txt");
                File.WriteAllText(contentDetectedDrawioPath, genericDrawio, new UTF8Encoding(false));
                IList<DrawioPageInfo> contentDetectedPages;
                Require(GraphSerialization.TryGetDrawioPageInfos(contentDetectedDrawioPath, out contentDetectedPages) && contentDetectedPages.Count == 2, "非标准扩展名的 Draw.io 未进入多页面识别流程");
                string jsonNamedDrawioPath = Path.Combine(tempFolder, "content-detected.json");
                File.WriteAllText(jsonNamedDrawioPath, genericDrawio, new UTF8Encoding(false));
                Require(GraphSerialization.TryGetDrawioPageInfos(jsonNamedDrawioPath, out contentDetectedPages) && contentDetectedPages.Count == 2, "以 .json 命名的 Draw.io 未进入多页面识别流程");
                string selectedPageNotice;
                GraphDocument selectedSecondPage = NativeDrawioImport.ImportPage(genericDrawio, "后备标题", 1, out selectedPageNotice);
                Require(selectedSecondPage.meta.title == "第二页" && selectedSecondPage.groups.Count == 0 && selectedSecondPage.nodes.Count == 1 && selectedSecondPage.nodes[0].label == "不应导入" && selectedSecondPage.edges.Count == 0,
                    "Draw.io 未按索引导入第二页内容");
                Require(selectedPageNotice.Contains("第 2 页") && selectedPageNotice.Contains("第二页") && selectedPageNotice.Contains("索引 1") && selectedPageNotice.Contains("其余 1 个页面未导入"),
                    "Draw.io 按页导入提示没有明确说明所选页面或未导入页数");
                bool drawioPageIndexRejected = false;
                try { NativeDrawioImport.ImportPage(genericDrawio, "后备标题", 2, out selectedPageNotice); }
                catch (ArgumentOutOfRangeException error)
                {
                    drawioPageIndexRejected = error.ParamName == "pageIndex" && error.Message.Contains("共有 2 个页面") && error.Message.Contains("0 到 1");
                }
                Require(drawioPageIndexRejected, "Draw.io 越界页面索引未被明确拒绝");

                string compressedDrawioPath = Path.Combine(tempFolder, "compressed.drawio");
                string compressedPayload = "vZPBboMwDIafxvc02bT0CC30tFOfIBCLoAWCTCjt20+QtF1FW3XStAuK/9924i8ExKY57kh15tNptCAyEBtyzodVc9ygtcBZrUFsgXMGnAPPH7ir2WWdImz9KwUqFByUHTAokEmQCUgO2cf0TdfAWRLSen+yMY3c0GqcuqxApKOpPe47VU7uSKoDkRrf2GjHTZA8Hh8edJbiKXfoGvR0As7OBWEOdgphHIuNtfYmZpw1g3VlYtP3qKk+xNWl8RUK8Dxyuc+oeI1RumRkXFMM/b/wuUz6BBC7A0j+ASC8CyhlIMUEaP0GiVzAQV3hPoaOvHGVa5XNrmqKrU6I3AhiW1hXfl1ATaXPMXHWu4FKvPnJvaIK/Y87XcIktMrXh9vuv0ADPL++3dm7edrf";
                File.WriteAllText(compressedDrawioPath, "<mxfile><diagram name=\"压缩页\">" + compressedPayload + "</diagram></mxfile>", new UTF8Encoding(false));
                GraphDocument compressedImported = GraphSerialization.LoadFile(compressedDrawioPath, out drawioNotice);
                Require(compressedImported.meta.title == "压缩页" && compressedImported.nodes.Count == 2 && compressedImported.edges.Count == 1, "压缩 Draw.io 页面导入失败");
                Require(compressedImported.nodes.Exists(delegate(GraphNode node) { return node.label == "节点 A"; }) && compressedImported.nodes.Exists(delegate(GraphNode node) { return node.label == "节点 B" && node.shape == "decision"; }), "压缩 Draw.io 中文或形状解码失败");
                Require(compressedImported.edges[0].label == "调用" && compressedImported.edges[0].lineType == "polyline", "压缩 Draw.io 关系解码失败");

                string bareDrawioPath = Path.Combine(tempFolder, "bare-model.xml");
                File.WriteAllText(bareDrawioPath, "<mxGraphModel><root><mxCell id=\"0\"/><mxCell id=\"1\" parent=\"0\"/><mxCell id=\"bare\" value=\"独立模型\" vertex=\"1\" parent=\"1\"><mxGeometry x=\"20\" y=\"30\" width=\"130\" height=\"50\" as=\"geometry\"/></mxCell></root></mxGraphModel>", new UTF8Encoding(false));
                GraphDocument bareImported = GraphSerialization.LoadFile(bareDrawioPath, out drawioNotice);
                Require(bareImported.nodes.Count == 1 && bareImported.nodes[0].label == "独立模型", "独立 mxGraphModel 导入失败");

                string escapedDrawioPath = Path.Combine(tempFolder, "escaped-page.drawio");
                string escapedModel = "<mxGraphModel><root><mxCell id=\"0\"/><mxCell id=\"1\" parent=\"0\"/><mxCell id=\"escaped\" value=\"A &lt; B &gt; C\" vertex=\"1\" parent=\"1\"><mxGeometry x=\"20\" y=\"30\" width=\"130\" height=\"50\" as=\"geometry\"/></mxCell></root></mxGraphModel>";
                File.WriteAllText(escapedDrawioPath, "<mxfile><diagram name=\"转义页\">" + System.Security.SecurityElement.Escape(escapedModel) + "</diagram></mxfile>", new UTF8Encoding(false));
                GraphDocument escapedImported = GraphSerialization.LoadFile(escapedDrawioPath, out drawioNotice);
                Require(escapedImported.meta.title == "转义页" && escapedImported.nodes.Count == 1 && escapedImported.nodes[0].label == "A < B > C", "转义 Draw.io 页面或纯文本尖括号导入失败");

                bool invalidDrawioRejected = false;
                string invalidDrawioPath = Path.Combine(tempFolder, "invalid.drawio");
                File.WriteAllText(invalidDrawioPath, "<mxfile><diagram>不是有效的压缩内容</diagram></mxfile>", new UTF8Encoding(false));
                try { GraphSerialization.LoadFile(invalidDrawioPath); }
                catch (InvalidDataException) { invalidDrawioRejected = true; }
                Require(invalidDrawioRejected, "损坏的 Draw.io 压缩内容未被安全拒绝");

                bool drawioDtdRejected = false;
                string dtdDrawioPath = Path.Combine(tempFolder, "dtd.drawio");
                File.WriteAllText(dtdDrawioPath, "<!DOCTYPE mxfile [<!ENTITY unsafe SYSTEM \"file:///C:/Windows/win.ini\">]><mxfile><diagram name=\"DTD\"><mxGraphModel><root><mxCell id=\"0\"/><mxCell id=\"1\" parent=\"0\"/><mxCell id=\"n\" value=\"&unsafe;\" vertex=\"1\" parent=\"1\"><mxGeometry x=\"0\" y=\"0\" width=\"120\" height=\"50\" as=\"geometry\"/></mxCell></root></mxGraphModel></diagram></mxfile>", new UTF8Encoding(false));
                try { GraphSerialization.LoadFile(dtdDrawioPath); }
                catch (InvalidDataException) { drawioDtdRejected = true; }
                Require(drawioDtdRejected, "Draw.io XML 外部实体未被安全拒绝");

                string pngPath = Path.Combine(tempFolder, "graph.png"), pdfPath = Path.Combine(tempFolder, "graph.pdf");
                using (GraphCanvas canvas = new GraphCanvas())
                {
                    canvas.Size = new System.Drawing.Size(1200, 760);
                    canvas.Document = GraphSerialization.Clone(graph);
                    NativeExport.SavePng(canvas, pngPath);
                    NativeExport.SavePdf(canvas, pdfPath);
                    canvas.DarkTheme = true;
                    using (System.Drawing.Bitmap darkPreview = canvas.ExportBitmap(1024))
                        Require(darkPreview.GetPixel(2, 2).GetBrightness() < .3f, "深色画布未生效");
                }
                byte[] png = File.ReadAllBytes(pngPath), pdf = File.ReadAllBytes(pdfPath);
                Require(png.Length > 10000 && png[0] == 0x89 && png[1] == 0x50, "PNG 导出失败");
                Require(pdf.Length > 1000 && pdf[0] == 0x25 && pdf[1] == 0x50 && pdf[2] == 0x44 && pdf[3] == 0x46, "PDF 导出失败");
                string pdfStructure = Encoding.GetEncoding(1252).GetString(pdf);
                Require(!pdfStructure.Contains("/Subtype /Image") && pdfStructure.Contains("/Contents"), "PDF 仍然是位图封装，而不是矢量图形");
                Require(pdfStructure.Contains("/Filter /FlateDecode") && pdfStructure.Contains("/Length 5 0 R"), "PDF 矢量内容流没有使用完整的流式压缩结构");

                using (GraphCanvas blankCanvas = new GraphCanvas())
                {
                    blankCanvas.Size = new System.Drawing.Size(800, 600); blankCanvas.Document = GraphSerialization.CreateBlank("双击测试"); blankCanvas.EditMode = true;
                    bool doubleClickRaised = false;
                    blankCanvas.BlankDoubleClicked += delegate(object sender, CanvasPointEventArgs e) { doubleClickRaised = e.WorldPoint.X >= 0 && e.WorldPoint.Y >= 0; };
                    System.Reflection.MethodInfo mouseDoubleClick = typeof(GraphCanvas).GetMethod("OnMouseDoubleClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    mouseDoubleClick.Invoke(blankCanvas, new object[] { new MouseEventArgs(MouseButtons.Left, 2, 400, 300, 0) });
                    Require(doubleClickRaised, "双击画布空白位置事件未触发");
                    System.Reflection.MethodInfo mouseDown = typeof(GraphCanvas).GetMethod("OnMouseDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    System.Reflection.MethodInfo mouseMove = typeof(GraphCanvas).GetMethod("OnMouseMove", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    System.Reflection.MethodInfo mouseUp = typeof(GraphCanvas).GetMethod("OnMouseUp", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    System.Drawing.PointF offsetBefore = blankCanvas.ViewOffset;
                    mouseDown.Invoke(blankCanvas, new object[] { new MouseEventArgs(MouseButtons.Right, 1, 200, 180, 0) });
                    Require(blankCanvas.Cursor == Cursors.Cross, "右键按下时未显示十字光标");
                    mouseMove.Invoke(blankCanvas, new object[] { new MouseEventArgs(MouseButtons.Right, 0, 265, 225, 0) });
                    mouseUp.Invoke(blankCanvas, new object[] { new MouseEventArgs(MouseButtons.Right, 1, 265, 225, 0) });
                    Require(Math.Abs(blankCanvas.ViewOffset.X - offsetBefore.X - 65) < .1f && Math.Abs(blankCanvas.ViewOffset.Y - offsetBefore.Y - 45) < .1f, "右键拖动画布失败");
                    Require(blankCanvas.Cursor == Cursors.Default, "右键拖动结束后光标未恢复");
                    blankCanvas.Document.nodes.Add(new GraphNode { id = "right_click_node", label = "右键测试", type = "步骤", kind = "system", group = "", x = 50, y = 50, w = 150, h = 55 });
                    blankCanvas.RefreshDocument(); blankCanvas.SelectEntity("node", "right_click_node");
                    mouseDown.Invoke(blankCanvas, new object[] { new MouseEventArgs(MouseButtons.Right, 1, 300, 260, 0) });
                    mouseUp.Invoke(blankCanvas, new object[] { new MouseEventArgs(MouseButtons.Right, 1, 300, 260, 0) });
                    Require(blankCanvas.SelectedType.Length == 0, "右键单击未清除选择");
                }

                GraphDocument editGraph = GraphSerialization.Clone(graph);
                using (GraphCanvas editCanvas = new GraphCanvas())
                {
                    editCanvas.Size = new System.Drawing.Size(1200, 760); editCanvas.Document = editGraph; editCanvas.EditMode = true;
                    int falseBlankEvents = 0; editCanvas.BlankDoubleClicked += delegate { falseBlankEvents++; };
                    System.Reflection.MethodInfo doubleClick = typeof(GraphCanvas).GetMethod("OnMouseDoubleClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    GraphNode editableNode = editGraph.nodes[0]; GraphGroup editableGroup = editGraph.groups[0];
                    Func<System.Drawing.PointF, MouseEventArgs> doubleClickAt = delegate(System.Drawing.PointF world)
                    {
                        int x = (int)Math.Round(world.X * editCanvas.Zoom + editCanvas.ViewOffset.X), y = (int)Math.Round(world.Y * editCanvas.Zoom + editCanvas.ViewOffset.Y);
                        return new MouseEventArgs(MouseButtons.Left, 2, x, y, 0);
                    };
                    doubleClick.Invoke(editCanvas, new object[] { doubleClickAt(new System.Drawing.PointF(editableNode.x + 12, editableNode.y + 10)) });
                    Require(editCanvas.InlineEditorActiveForTesting && editCanvas.InlineEditFieldForTesting == "type", "双击节点角标未进入类型编辑");
                    editCanvas.SetInlineEditorTextForTesting("新类型"); editCanvas.CommitInlineEditForTesting(); Require(editableNode.type == "新类型", "节点类型直接编辑未保存");
                    doubleClick.Invoke(editCanvas, new object[] { doubleClickAt(new System.Drawing.PointF(editableNode.x + editableNode.w / 2, editableNode.y + 43)) });
                    Require(editCanvas.InlineEditorActiveForTesting && editCanvas.InlineEditFieldForTesting == "label", "双击节点名称未进入名称编辑");
                    editCanvas.SetInlineEditorTextForTesting("新节点名称"); editCanvas.CommitInlineEditForTesting(); Require(editableNode.label == "新节点名称", "节点名称直接编辑未保存");
                    doubleClick.Invoke(editCanvas, new object[] { doubleClickAt(new System.Drawing.PointF(editableGroup.x + 16, editableGroup.y + 15)) });
                    Require(editCanvas.InlineEditorActiveForTesting && editCanvas.InlineEditFieldForTesting == "label", "双击分组角标未进入名称编辑");
                    editCanvas.SetInlineEditorTextForTesting("新分组名称"); editCanvas.CommitInlineEditForTesting(); Require(editableGroup.label == "新分组名称", "分组名称直接编辑未保存");
                    Require(falseBlankEvents == 0, "节点或分组文字区域被错误识别为空白区域");
                }

                GraphDocument windowGraph = GraphSerialization.Clone(graph);
                using (MainForm window = new MainForm(windowGraph, false))
                {
                    window.WindowState = FormWindowState.Normal;
                    window.Size = new System.Drawing.Size(1280, 800);
                    window.CreateControl();
                    window.PerformLayout();
                    Require(window.Text.Contains("关系图编辑器"), "主窗口构造失败");
                    Require(window.Icon != null && window.Icon.Width > 0 && window.Icon.Height > 0, "主窗口未加载统一程序图标");
                    using (System.Drawing.Icon executableIcon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                        Require(IconsMatch(window.Icon, executableIcon), "窗口图标与 EXE/任务栏图标不一致");
                    window.SetThemeForTesting("dark"); Require(window.DarkThemeForTesting, "深色界面主题未生效");
                    window.SetThemeForTesting("light"); Require(!window.DarkThemeForTesting, "浅色界面主题未生效");
                    window.CanvasForTesting.SelectEntity("node", windowGraph.nodes[0].id);
                    Require(window.ReplacementInitialQueryForTesting() == windowGraph.nodes[0].label, "打开替换窗口时没有优先带入当前单选节点名称");
                    window.CanvasForTesting.ReplaceModeActive = true;
                    Require(window.CanvasForTesting.ReplaceModeHighlightActiveForTesting, "替换模式没有为当前所选对象启用特殊高亮");
                    window.CanvasForTesting.ReplaceModeActive = false;
                    Require(!window.CanvasForTesting.ReplaceModeHighlightActiveForTesting, "退出替换模式后特殊高亮没有停止");
                    Require(window.EditModeForTesting, "主程序启动后未默认进入编辑状态");
                    Require(window.InspectorEditableForTesting, "主程序右侧属性未直接开放编辑");
                    Require(window.InspectorTextVisibleForTesting, "高 DPI 下右侧属性文字仍有遮挡");
                    Require(!window.InspectorHasApplyButtonForTesting, "右侧属性中仍存在应用按钮");
                    Require(window.ColorCategoryExamplesForTesting, "颜色分类未同时提供色块和颜色文字");
                    string savedNodeName = windowGraph.nodes[0].label;
                    Require(window.SetSelectedNodeNameThroughInspectorForTesting("自动应用名称"), "右侧输入内容未自动应用到节点");
                    Require(window.DirtyForTesting, "修改关系图后未显示未保存状态");
                    window.UndoForTesting();
                    Require(!window.DirtyForTesting && window.CanvasForTesting.Document.nodes[0].label == savedNodeName, "撤销回保存点后未清除未保存状态");
                    window.RedoForTesting();
                    Require(window.DirtyForTesting && window.CanvasForTesting.Document.nodes[0].label == "自动应用名称", "重做后未恢复未保存状态");
                    window.CanvasForTesting.SelectEntity("node", window.CanvasForTesting.Document.nodes[0].id);
                    GraphTextReplacementResult windowReplacement = window.ReplaceSelectedTextForTesting("自动", "批量");
                    Require(windowReplacement.AppliedOccurrences == 1 && window.CanvasForTesting.Document.nodes[0].label == "批量应用名称", "主窗口替换当前所选未提交");
                    window.UndoForTesting();
                    Require(window.CanvasForTesting.Document.nodes[0].label == "自动应用名称", "替换当前所选不能通过一次撤销完整恢复");
                    windowGraph = window.CanvasForTesting.Document;
                    Require(!window.CanvasForTesting.LinkHandlesVisibleForSelectionForTesting, "选中节点后仍显示四个连线圆圈");
                    window.CanvasForTesting.SelectEntity("group", windowGraph.groups[0].id);
                    Require(!window.CanvasForTesting.LinkHandlesVisibleForSelectionForTesting, "选中分组后仍显示四个连线圆圈");
                    GraphGroup resizeGroup = windowGraph.groups[0];
                    GraphCanvas resizeCanvas = window.CanvasForTesting;
                    Require(resizeCanvas.ResizeHandleAtForTesting(new System.Drawing.PointF(resizeGroup.x, resizeGroup.y + resizeGroup.h / 2f)) == "w", "分组左边缘未识别为缩放区域");
                    Require(resizeCanvas.ResizeHandleAtForTesting(new System.Drawing.PointF(resizeGroup.x + resizeGroup.w, resizeGroup.y + resizeGroup.h / 2f)) == "e", "分组右边缘未识别为缩放区域");
                    Require(resizeCanvas.ResizeHandleAtForTesting(new System.Drawing.PointF(resizeGroup.x + resizeGroup.w / 2f, resizeGroup.y)) == "n", "分组上边缘未识别为缩放区域");
                    Require(resizeCanvas.ResizeHandleAtForTesting(new System.Drawing.PointF(resizeGroup.x + resizeGroup.w / 2f, resizeGroup.y + resizeGroup.h)) == "s", "分组下边缘未识别为缩放区域");
                    Require(resizeCanvas.ResizeCursorForTesting("w") == Cursors.SizeWE && resizeCanvas.ResizeCursorForTesting("n") == Cursors.SizeNS, "分组边缘缩放光标方向错误");
                    Require(resizeCanvas.ResizeCursorForTesting("nw") == Cursors.SizeNWSE && resizeCanvas.ResizeCursorForTesting("ne") == Cursors.SizeNESW, "分组四角缩放光标方向错误");
                    int nodeCount = windowGraph.nodes.Count; window.AddNodeAtForTesting(new System.Drawing.PointF(180, 250));
                    Require(windowGraph.nodes.Count == nodeCount + 1 && windowGraph.nodes[windowGraph.nodes.Count - 1].group == windowGraph.groups[0].id, "双击位置创建节点或自动归组失败");
                    Require(windowGraph.nodes[windowGraph.nodes.Count - 1].type == "节点类型", "新建节点没有使用默认的节点类型文字");
                    SplitContainer splitter = null;
                    foreach (Control control in window.Controls) if (control is SplitContainer) { splitter = (SplitContainer)control; break; }
                    Require(splitter != null && splitter.Panel1MinSize == 600 && splitter.Panel2MinSize == 360, "主窗口分栏未完成宽松布局");
                    Require(splitter.SplitterDistance >= splitter.Panel1MinSize && splitter.SplitterDistance <= splitter.ClientSize.Width - splitter.Panel2MinSize - splitter.SplitterWidth, "主窗口分隔位置超出安全范围");
                }

                GraphDocument centerCreateGraph = GraphSerialization.CreateBlank("屏幕中心创建测试");
                using (MainForm centerCreateWindow = new MainForm(centerCreateGraph, false))
                {
                    centerCreateWindow.WindowState = FormWindowState.Normal; centerCreateWindow.Size = new System.Drawing.Size(1200, 760); centerCreateWindow.CreateControl(); centerCreateWindow.PerformLayout();
                    GraphCanvas centerCreateCanvas = centerCreateWindow.CanvasForTesting; centerCreateCanvas.Size = new System.Drawing.Size(900, 650); centerCreateCanvas.FitToView();
                    System.Drawing.PointF initialCenter = centerCreateCanvas.ViewCenterWorld;
                    centerCreateWindow.AddGroupForTesting();
                    GraphGroup centeredGroup = centerCreateGraph.groups[0];
                    Require(Math.Abs(centeredGroup.x + centeredGroup.w / 2f - initialCenter.X) < .1f && Math.Abs(centeredGroup.y + centeredGroup.h / 2f - initialCenter.Y) < .1f, "新增分组未创建在当前屏幕中心");
                    centerCreateWindow.AddNodeForTesting();
                    GraphNode centeredNode = centerCreateGraph.nodes[0];
                    Require(Math.Abs(centeredNode.x + centeredNode.w / 2f - initialCenter.X) < .1f && Math.Abs(centeredNode.y + centeredNode.h / 2f - initialCenter.Y) < .1f, "新增节点未创建在当前屏幕中心");
                    Require(centeredNode.group == centeredGroup.id, "屏幕中心位于分组内时新增节点未自动归组");
                    System.Reflection.MethodInfo centerDown = typeof(GraphCanvas).GetMethod("OnMouseDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    System.Reflection.MethodInfo centerMove = typeof(GraphCanvas).GetMethod("OnMouseMove", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    System.Reflection.MethodInfo centerUp = typeof(GraphCanvas).GetMethod("OnMouseUp", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    centerDown.Invoke(centerCreateCanvas, new object[] { new MouseEventArgs(MouseButtons.Right, 1, 300, 250, 0) });
                    centerMove.Invoke(centerCreateCanvas, new object[] { new MouseEventArgs(MouseButtons.Right, 0, 390, 310, 0) });
                    centerUp.Invoke(centerCreateCanvas, new object[] { new MouseEventArgs(MouseButtons.Right, 1, 390, 310, 0) });
                    System.Drawing.PointF movedCenter = centerCreateCanvas.ViewCenterWorld;
                    centerCreateCanvas.ClearSelection();
                    centerCreateWindow.AddGroupForTesting(); GraphGroup movedCenterGroup = centerCreateGraph.groups[1];
                    Require(Math.Abs(movedCenterGroup.x + movedCenterGroup.w / 2f - movedCenter.X) < .1f && Math.Abs(movedCenterGroup.y + movedCenterGroup.h / 2f - movedCenter.Y) < .1f, "平移画布后新增分组未跟随新的屏幕中心");
                }

                GraphDocument linkGraph = GraphSerialization.CreateBlank("真实拉线回归测试");
                linkGraph.nodes.Add(new GraphNode { id = "link_1", label = "节点 1", type = "步骤", kind = "system", group = "", x = 80, y = 120, w = 150, h = 54, note = "" });
                linkGraph.nodes.Add(new GraphNode { id = "link_2", label = "节点 2", type = "步骤", kind = "resource", group = "", x = 310, y = 120, w = 150, h = 54, note = "" });
                linkGraph.nodes.Add(new GraphNode { id = "link_3", label = "节点 3", type = "步骤", kind = "content", group = "", x = 540, y = 120, w = 150, h = 54, note = "" });
                linkGraph.nodes.Add(new GraphNode { id = "link_4", label = "节点 4", type = "步骤", kind = "output", group = "", x = 770, y = 120, w = 150, h = 54, note = "" });
                linkGraph.nodes.Add(new GraphNode { id = "vertical_source", label = "上方节点", type = "步骤", kind = "commercial", group = "", x = 310, y = 300, w = 150, h = 54, note = "" });
                linkGraph.groups.Add(new GraphGroup { id = "vertical_target", label = "下方分组", x = 250, y = 500, w = 270, h = 180 });
                linkGraph = GraphSerialization.Normalize(linkGraph);
                using (MainForm linkWindow = new MainForm(linkGraph, false))
                {
                    linkWindow.WindowState = FormWindowState.Normal; linkWindow.Size = new System.Drawing.Size(1200, 760); linkWindow.CreateControl(); linkWindow.PerformLayout();
                    GraphCanvas linkCanvas = linkWindow.CanvasForTesting; linkCanvas.Size = new System.Drawing.Size(900, 650); linkCanvas.FitToView(); linkCanvas.ClearSelection();
                    System.Reflection.MethodInfo linkDown = typeof(GraphCanvas).GetMethod("OnMouseDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    System.Reflection.MethodInfo linkMove = typeof(GraphCanvas).GetMethod("OnMouseMove", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    System.Reflection.MethodInfo linkUp = typeof(GraphCanvas).GetMethod("OnMouseUp", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    Func<System.Drawing.PointF, System.Drawing.Point> linkScreenAt = delegate(System.Drawing.PointF world)
                    {
                        return new System.Drawing.Point((int)Math.Round(world.X * linkCanvas.Zoom + linkCanvas.ViewOffset.X), (int)Math.Round(world.Y * linkCanvas.Zoom + linkCanvas.ViewOffset.Y));
                    };
                    System.Drawing.Point linkStart = linkScreenAt(new System.Drawing.PointF(155, 147)), linkEnd = linkScreenAt(new System.Drawing.PointF(845, 147));
                    linkDown.Invoke(linkCanvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, linkStart.X, linkStart.Y, 0) });
                    linkMove.Invoke(linkCanvas, new object[] { new MouseEventArgs(MouseButtons.Left, 0, linkEnd.X, linkEnd.Y, 0) });
                    linkUp.Invoke(linkCanvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, linkEnd.X, linkEnd.Y, 0) });
                    Require(linkGraph.edges.Count == 1 && linkCanvas.SelectedType == "edge", "真实拉线未成功创建并选中新关系");
                    Require(linkGraph.edges[0].targetSide == "left", "从左向右拉线未连接目标节点左侧");
                    Require(linkWindow.EndpointInspectorSelectionsForTesting, "拉线到第 4 个端点后右侧起点或终点下拉框索引无效");
                    linkCanvas.ClearSelection();
                    System.Drawing.Point reverseStart = linkScreenAt(new System.Drawing.PointF(845, 147)), reverseEnd = linkScreenAt(new System.Drawing.PointF(155, 147));
                    linkDown.Invoke(linkCanvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, reverseStart.X, reverseStart.Y, 0) });
                    linkMove.Invoke(linkCanvas, new object[] { new MouseEventArgs(MouseButtons.Left, 0, reverseEnd.X, reverseEnd.Y, 0) });
                    linkUp.Invoke(linkCanvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, reverseEnd.X, reverseEnd.Y, 0) });
                    Require(linkGraph.edges.Count == 2 && linkGraph.edges[1].targetSide == "right", "从右向左拉线未连接目标节点右侧");
                    Require(GraphCanvas.NearestSideForTesting(new System.Drawing.RectangleF(80, 120, 150, 54), new System.Drawing.PointF(770, 147)) == "right", "真实连接点距离计算仍误选目标节点上方");
                    linkGraph.edges[1].targetSide = "top"; linkCanvas.RefreshDocument();
                    Require(linkGraph.edges[1].targetSide == "right", "已保存的错误目标入口未自动修复");
                    linkCanvas.ClearSelection();
                    System.Drawing.Point verticalStart = linkScreenAt(new System.Drawing.PointF(385, 327));
                    System.Drawing.Point misleadingMove = linkScreenAt(new System.Drawing.PointF(455, 327));
                    System.Drawing.Point verticalEnd = linkScreenAt(new System.Drawing.PointF(385, 530));
                    linkDown.Invoke(linkCanvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, verticalStart.X, verticalStart.Y, 0) });
                    linkMove.Invoke(linkCanvas, new object[] { new MouseEventArgs(MouseButtons.Left, 0, misleadingMove.X, misleadingMove.Y, 0) });
                    linkUp.Invoke(linkCanvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, verticalEnd.X, verticalEnd.Y, 0) });
                    Require(linkGraph.edges.Count == 3 && linkGraph.edges[2].sourceSide == "bottom" && linkGraph.edges[2].targetSide == "top", "目标位于下方时仍错误地从节点右侧出线");
                    linkGraph.edges[2].sourceSide = "right"; linkGraph.edges[2].targetSide = "left"; linkCanvas.RefreshDocument();
                    Require(linkGraph.edges[2].sourceSide == "bottom" && linkGraph.edges[2].targetSide == "top", "已存在的上下关系未自动修复为下方到上方连接");
                }

                GraphDocument labelGraph = GraphSerialization.CreateBlank("曲线名称位置测试");
                labelGraph.nodes.Add(new GraphNode { id = "label_source", label = "源节点", type = "步骤", kind = "system", group = "", x = 600, y = 600, w = 150, h = 54, note = "" });
                labelGraph.nodes.Add(new GraphNode { id = "label_target", label = "目标节点", type = "步骤", kind = "output", group = "", x = 950, y = 300, w = 150, h = 54, note = "" });
                labelGraph.edges.Add(new GraphEdge { id = "label_curve", sourceType = "node", source = "label_source", targetType = "node", target = "label_target", label = "控制", category = "core", lineType = "curve", sourceSide = "top", targetSide = "left" });
                labelGraph = GraphSerialization.Normalize(labelGraph);
                using (GraphCanvas labelCanvas = new GraphCanvas())
                {
                    labelCanvas.Size = new System.Drawing.Size(900, 650); labelCanvas.Document = labelGraph; labelCanvas.EditMode = true;
                    System.Drawing.PointF labelPoint = labelCanvas.EdgeLabelPointForTesting(labelGraph.edges[0]);
                    Require(labelGraph.edges[0].sourceSide == "right" && labelGraph.edges[0].targetSide == "left", "斜向关系未按最终对象位置修复连接方向");
                    System.Drawing.PointF exportPoint = NativeExport.EdgePathMidpointForTesting("curve", "right", "left", new System.Drawing.PointF(750, 627), new System.Drawing.PointF(950, 327));
                    float exportDifference = (float)Math.Sqrt(Math.Pow(labelPoint.X - exportPoint.X, 2) + Math.Pow(labelPoint.Y - exportPoint.Y, 2));
                    Require(exportDifference < 3, "主画布与导出文件的曲线名称位置不一致");
                    string labelSvg = NativeExport.BuildSvg(labelGraph);
                    Require(labelSvg.Contains("控制") && labelSvg.Contains("label_curve"), "带名称曲线的 SVG 导出不完整");
                    System.Reflection.MethodInfo edgeDoubleClick = typeof(GraphCanvas).GetMethod("OnMouseDoubleClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    System.Drawing.Point edgeLabelScreen = new System.Drawing.Point((int)Math.Round(labelPoint.X * labelCanvas.Zoom + labelCanvas.ViewOffset.X), (int)Math.Round(labelPoint.Y * labelCanvas.Zoom + labelCanvas.ViewOffset.Y));
                    edgeDoubleClick.Invoke(labelCanvas, new object[] { new MouseEventArgs(MouseButtons.Left, 2, edgeLabelScreen.X, edgeLabelScreen.Y, 0) });
                    Require(labelCanvas.InlineEditorActiveForTesting && labelCanvas.InlineEditFieldForTesting == "label", "双击曲线未进入关系名称编辑");
                    labelCanvas.SetInlineEditorTextForTesting("新的控制"); labelCanvas.CommitInlineEditForTesting();
                    Require(labelGraph.edges[0].label == "新的控制", "双击曲线修改关系名称未保存");
                    edgeDoubleClick.Invoke(labelCanvas, new object[] { new MouseEventArgs(MouseButtons.Left, 2, edgeLabelScreen.X, edgeLabelScreen.Y, 0) });
                    labelCanvas.SetInlineEditorTextForTesting(""); labelCanvas.CommitInlineEditForTesting();
                    Require(labelGraph.edges[0].label == "", "双击曲线后无法清空可选关系名称");
                }

                GraphDocument sourceColorGraph = GraphSerialization.CreateBlank("线色跟随起点测试");
                sourceColorGraph.nodes.Add(new GraphNode { id = "color_source", label = "蓝色起点", type = "步骤", kind = "resource", group = "", x = 100, y = 100, w = 150, h = 54, note = "" });
                sourceColorGraph.nodes.Add(new GraphNode { id = "color_target", label = "绿色终点", type = "步骤", kind = "output", group = "", x = 400, y = 100, w = 150, h = 54, note = "" });
                sourceColorGraph.edges.Add(new GraphEdge { id = "source_color_edge", sourceType = "node", source = "color_source", targetType = "node", target = "color_target", label = "跟随", category = "content", lineType = "straight", sourceSide = "right", targetSide = "left" });
                sourceColorGraph = GraphSerialization.Normalize(sourceColorGraph);
                using (MainForm colorWindow = new MainForm(sourceColorGraph, false))
                {
                    colorWindow.WindowState = FormWindowState.Normal; colorWindow.Size = new System.Drawing.Size(1200, 760); colorWindow.CreateControl(); colorWindow.PerformLayout();
                    GraphCanvas colorCanvas = colorWindow.CanvasForTesting; colorCanvas.SelectEntity("node", "color_source");
                    Require(colorCanvas.EdgeSourceColorForTesting(sourceColorGraph.edges[0]).ToArgb() == System.Drawing.Color.FromArgb(75, 158, 234).ToArgb(), "线条未采用资源类起点的蓝色");
                    string blueSvg = NativeExport.BuildSvg(sourceColorGraph);
                    Require(blueSvg.Contains("stroke=\"#4b9eea\"") && blueSvg.Contains("fill=\"#4b9eea\""), "SVG 线条或箭头未采用起点蓝色");
                    Require(colorWindow.SetSelectedNodeColorThroughInspectorForTesting("commercial"), "右侧颜色分类未成功修改起点节点");
                    Require(colorCanvas.EdgeSourceColorForTesting(sourceColorGraph.edges[0]).ToArgb() == System.Drawing.Color.FromArgb(232, 150, 62).ToArgb(), "起点改为橙色后已有线条未同步更新");
                    string orangeSvg = NativeExport.BuildSvg(sourceColorGraph);
                    Require(orangeSvg.Contains("stroke=\"#e8963e\"") && orangeSvg.Contains("fill=\"#e8963e\""), "导出文件未同步起点的新颜色");
                    sourceColorGraph.nodes[1].kind = "content"; colorCanvas.RefreshDocument();
                    Require(colorCanvas.EdgeSourceColorForTesting(sourceColorGraph.edges[0]).ToArgb() == System.Drawing.Color.FromArgb(232, 150, 62).ToArgb(), "终点改色错误影响了由起点决定的线色");
                }

                GraphDocument sameNameGraph = GraphSerialization.CreateBlank("同名节点联合高亮测试");
                sameNameGraph.nodes.Add(new GraphNode { id = "same_a", label = "完全同名", type = "功能", kind = "system", x = 100, y = 100, w = 150, h = 54, group = "", note = "" });
                sameNameGraph.nodes.Add(new GraphNode { id = "same_b", label = "完全同名", type = "功能", kind = "resource", x = 100, y = 300, w = 150, h = 54, group = "", note = "" });
                sameNameGraph.nodes.Add(new GraphNode { id = "same_c", label = "完全同名", type = "功能", kind = "staff", x = 100, y = 500, w = 150, h = 54, group = "", note = "" });
                sameNameGraph.nodes.Add(new GraphNode { id = "similar", label = "完全同名副本", type = "功能", kind = "content", x = 100, y = 700, w = 150, h = 54, group = "", note = "" });
                sameNameGraph.nodes.Add(new GraphNode { id = "down_a", label = "下游甲", type = "功能", kind = "output", x = 400, y = 100, w = 150, h = 54, group = "", note = "" });
                sameNameGraph.nodes.Add(new GraphNode { id = "down_b", label = "下游乙", type = "功能", kind = "output", x = 400, y = 300, w = 150, h = 54, group = "", note = "" });
                sameNameGraph.nodes.Add(new GraphNode { id = "up_b", label = "上游乙", type = "功能", kind = "commercial", x = -180, y = 300, w = 150, h = 54, group = "", note = "" });
                sameNameGraph.nodes.Add(new GraphNode { id = "deep_a", label = "二层下游", type = "功能", kind = "staff", x = 700, y = 100, w = 150, h = 54, group = "", note = "" });
                sameNameGraph.edges.Add(new GraphEdge { id = "same_down_a", sourceType = "node", source = "same_a", targetType = "node", target = "down_a", label = "", category = "core", lineType = "curve" });
                sameNameGraph.edges.Add(new GraphEdge { id = "same_down_b", sourceType = "node", source = "same_b", targetType = "node", target = "down_b", label = "", category = "core", lineType = "curve" });
                sameNameGraph.edges.Add(new GraphEdge { id = "same_up_b", sourceType = "node", source = "up_b", targetType = "node", target = "same_b", label = "", category = "core", lineType = "curve" });
                sameNameGraph.edges.Add(new GraphEdge { id = "same_deep_a", sourceType = "node", source = "down_a", targetType = "node", target = "deep_a", label = "", category = "core", lineType = "curve" });
                sameNameGraph = GraphSerialization.Normalize(sameNameGraph);
                using (GraphCanvas sameNameCanvas = new GraphCanvas())
                {
                    sameNameCanvas.Size = new System.Drawing.Size(1000, 700); sameNameCanvas.Document = sameNameGraph; sameNameCanvas.EditMode = true;
                    sameNameCanvas.FocusDirection = "downstream"; sameNameCanvas.FocusDepth = 1; sameNameCanvas.SelectEntity("node", "same_a");
                    Require(sameNameCanvas.SelectedNodeIds.Count == 1 && sameNameCanvas.SelectedNodeIds.Contains("same_a"), "点击同名节点错误地产生了逻辑多选");
                    Require(sameNameCanvas.SameNameHighlightedForTesting("same_a") && sameNameCanvas.SameNameHighlightedForTesting("same_b") && !sameNameCanvas.SameNameHighlightedForTesting("same_c") && sameNameCanvas.InactiveSameNameNodeForTesting("same_c") && !sameNameCanvas.SameNameHighlightedForTesting("similar"), "下游模式未过滤无出线的同名节点或相似名称被误判");
                    HashSet<string> downstreamEntities = sameNameCanvas.FocusedEntitiesForTesting(); HashSet<string> downstreamEdges = sameNameCanvas.FocusedEdgesForTesting();
                    Require(downstreamEntities.Contains("node:same_a") && downstreamEntities.Contains("node:same_b") && downstreamEntities.Contains("node:same_c") && downstreamEntities.Contains("node:down_a") && downstreamEntities.Contains("node:down_b") && !downstreamEntities.Contains("node:up_b") && !downstreamEntities.Contains("node:deep_a"), "同名节点的一层下游邻居合并错误或视觉过滤错误影响了逻辑根节点");
                    Require(downstreamEdges.SetEquals(new[] { "same_down_a", "same_down_b" }), "同名节点的一层下游关系未遵循方向规则");
                    sameNameCanvas.FocusDirection = "upstream"; sameNameCanvas.FocusDepth = 1;
                    HashSet<string> upstreamEntities = sameNameCanvas.FocusedEntitiesForTesting();
                    Require(upstreamEntities.Contains("node:up_b") && !upstreamEntities.Contains("node:down_a") && !upstreamEntities.Contains("node:down_b"), "同名节点的上游邻居未遵循方向规则");
                    Require(!sameNameCanvas.SameNameHighlightedForTesting("same_a") && sameNameCanvas.InactiveSameNameNodeForTesting("same_a") && sameNameCanvas.SelectedNodeIds.Contains("same_a") && sameNameCanvas.SameNameHighlightedForTesting("same_b") && !sameNameCanvas.SameNameHighlightedForTesting("same_c"), "上游模式未将无入线的实际选中节点保持逻辑选中并显示为暗色");
                    sameNameCanvas.FocusDirection = "all"; sameNameCanvas.FocusDepth = 2;
                    Require(sameNameCanvas.FocusedEntitiesForTesting().Contains("node:deep_a") && sameNameCanvas.SameNameHighlightedForTesting("same_c"), "同名节点的二层邻居未遵循深度规则或双向模式错误过滤同名节点");
                    float sameAX = sameNameGraph.nodes.First(delegate(GraphNode item) { return item.id == "same_a"; }).x;
                    float sameBX = sameNameGraph.nodes.First(delegate(GraphNode item) { return item.id == "same_b"; }).x;
                    sameNameCanvas.MoveSelectedNodesForTesting(35f, 0f, true);
                    Require(Math.Abs(sameNameGraph.nodes.First(delegate(GraphNode item) { return item.id == "same_a"; }).x - sameAX - 35f) < .1f && Math.Abs(sameNameGraph.nodes.First(delegate(GraphNode item) { return item.id == "same_b"; }).x - sameBX) < .1f, "视觉同名高亮错误影响了逻辑移动选择");
                }

                GraphDocument selectionGraph = GraphSerialization.CreateBlank("框选与复制粘贴测试");
                selectionGraph.nodes.Add(new GraphNode { id = "select_a", label = "节点 A", type = "步骤", kind = "system", group = "", x = 100, y = 90, w = 150, h = 54, note = "" });
                selectionGraph.nodes.Add(new GraphNode { id = "select_b", label = "节点 B", type = "步骤", kind = "resource", group = "", x = 330, y = 90, w = 150, h = 54, note = "" });
                selectionGraph.nodes.Add(new GraphNode { id = "outside", label = "外部节点", type = "步骤", kind = "output", group = "", x = 760, y = 360, w = 150, h = 54, note = "" });
                selectionGraph.groups.Add(new GraphGroup { id = "select_group", label = "框选分组", x = 270, y = 55, w = 190, h = 120 });
                selectionGraph.edges.Add(new GraphEdge { id = "internal_edge", sourceType = "node", source = "select_a", targetType = "node", target = "select_b", label = "内部", category = "core", lineType = "curve", sourceSide = "", targetSide = "" });
                selectionGraph.edges.Add(new GraphEdge { id = "external_edge", sourceType = "node", source = "select_a", targetType = "node", target = "outside", label = "外部", category = "core", lineType = "curve", sourceSide = "", targetSide = "" });
                selectionGraph = GraphSerialization.Normalize(selectionGraph);
                using (MainForm selectionWindow = new MainForm(selectionGraph, false))
                {
                    selectionWindow.WindowState = FormWindowState.Normal; selectionWindow.Size = new System.Drawing.Size(1200, 760); selectionWindow.CreateControl(); selectionWindow.PerformLayout();
                    GraphCanvas selectionCanvas = selectionWindow.CanvasForTesting; selectionCanvas.Size = new System.Drawing.Size(900, 650); selectionCanvas.FitToView();
                    System.Reflection.MethodInfo selectDown = typeof(GraphCanvas).GetMethod("OnMouseDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    System.Reflection.MethodInfo selectMove = typeof(GraphCanvas).GetMethod("OnMouseMove", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    System.Reflection.MethodInfo selectUp = typeof(GraphCanvas).GetMethod("OnMouseUp", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    Func<System.Drawing.PointF, System.Drawing.Point> selectionScreenAt = delegate(System.Drawing.PointF world)
                    {
                        return new System.Drawing.Point((int)Math.Round(world.X * selectionCanvas.Zoom + selectionCanvas.ViewOffset.X), (int)Math.Round(world.Y * selectionCanvas.Zoom + selectionCanvas.ViewOffset.Y));
                    };
                    System.Drawing.Point selectionStart = selectionScreenAt(new System.Drawing.PointF(40, 30)), selectionEnd = selectionScreenAt(new System.Drawing.PointF(550, 210));
                    selectDown.Invoke(selectionCanvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, selectionStart.X, selectionStart.Y, 0) });
                    selectMove.Invoke(selectionCanvas, new object[] { new MouseEventArgs(MouseButtons.Left, 0, selectionEnd.X, selectionEnd.Y, 0) });
                    selectUp.Invoke(selectionCanvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, selectionEnd.X, selectionEnd.Y, 0) });
                    Require(selectionCanvas.SelectedNodeIds.Count == 2 && selectionCanvas.SelectedNodeIds.Contains("select_a") && selectionCanvas.SelectedNodeIds.Contains("select_b"), "编辑模式空白左拖未直接框选多个节点");
                    Require(selectionCanvas.SelectedGroupIds.Count == 1 && selectionCanvas.SelectedGroupIds.Contains("select_group") && selectionCanvas.SelectedType == "mixed", "同一框选区域未同时选择节点和分组");
                    Require(!selectionCanvas.NeighborHighlightActiveForTesting, "框选多个节点后仍自动高亮邻居");
                    Require(selectionWindow.CopySelectionForTesting(), "Ctrl+C 对节点与分组混合选择复制失败");
                    int beforeNodeCount = selectionGraph.nodes.Count, beforeGroupCount = selectionGraph.groups.Count, beforeEdgeCount = selectionGraph.edges.Count;
                    Require(selectionWindow.PasteSelectionForTesting(), "Ctrl+V 对节点与分组混合选择粘贴失败");
                    Require(selectionGraph.nodes.Count == beforeNodeCount + 2, "粘贴未生成两个新节点");
                    Require(selectionGraph.groups.Count == beforeGroupCount + 1, "粘贴未生成选中的分组");
                    Require(selectionGraph.edges.Count == beforeEdgeCount + 1, "粘贴未仅复制所选节点之间的关系");
                    Require(selectionCanvas.SelectedNodeIds.Count == 2 && !selectionCanvas.SelectedNodeIds.Contains("select_a") && !selectionCanvas.SelectedNodeIds.Contains("select_b"), "粘贴后的新节点未保持多选");
                    Require(selectionCanvas.SelectedGroupIds.Count == 1 && !selectionCanvas.SelectedGroupIds.Contains("select_group"), "粘贴后的新分组未保持选中");
                    List<GraphNode> pastedNodes = selectionGraph.nodes.Where(delegate(GraphNode item) { return selectionCanvas.SelectedNodeIds.Contains(item.id); }).ToList();
                    List<GraphGroup> pastedGroups = selectionGraph.groups.Where(delegate(GraphGroup item) { return selectionCanvas.SelectedGroupIds.Contains(item.id); }).ToList();
                    float pastedCenterX = (Math.Min(pastedNodes.Min(delegate(GraphNode item) { return item.x; }), pastedGroups.Min(delegate(GraphGroup item) { return item.x; })) + Math.Max(pastedNodes.Max(delegate(GraphNode item) { return item.x + item.w; }), pastedGroups.Max(delegate(GraphGroup item) { return item.x + item.w; }))) / 2f;
                    float pastedCenterY = (Math.Min(pastedNodes.Min(delegate(GraphNode item) { return item.y; }), pastedGroups.Min(delegate(GraphGroup item) { return item.y; })) + Math.Max(pastedNodes.Max(delegate(GraphNode item) { return item.y + item.h; }), pastedGroups.Max(delegate(GraphGroup item) { return item.y + item.h; }))) / 2f;
                    Require(Math.Abs(pastedCenterX - selectionCanvas.ViewCenterWorld.X) < .1f && Math.Abs(pastedCenterY - selectionCanvas.ViewCenterWorld.Y) < .1f, "粘贴内容未放到当前屏幕中心");
                    Require(!selectionCanvas.NeighborHighlightActiveForTesting, "粘贴后的多选节点仍自动高亮邻居");
                    float pastedNodeX = pastedNodes[0].x, pastedGroupX = pastedGroups[0].x;
                    selectionCanvas.MoveSelectedObjectsForTesting(37f, 29f, true);
                    Require(Math.Abs(pastedNodes[0].x - pastedNodeX - 37f) < .1f && Math.Abs(pastedGroups[0].x - pastedGroupX - 37f) < .1f, "节点与分组混合选择后未整体移动");
                    selectionCanvas.ZoomBy(1.35f); float zoomBeforeUndo = selectionCanvas.Zoom; System.Drawing.PointF offsetBeforeUndo = selectionCanvas.ViewOffset;
                    selectionWindow.UndoForTesting();
                    Require(Math.Abs(selectionCanvas.Zoom - zoomBeforeUndo) < .0001f && Math.Abs(selectionCanvas.ViewOffset.X - offsetBeforeUndo.X) < .1f && Math.Abs(selectionCanvas.ViewOffset.Y - offsetBeforeUndo.Y) < .1f, "撤销操作错误改变了当前缩放比例或画布位置");
                }

                GraphDocument nestedGraph = GraphSerialization.CreateBlank("自动多层分组测试");
                nestedGraph.groups.Add(new GraphGroup { id = "menu_theme", label = "菜单主题", x = 360, y = 220, w = 420, h = 300 });
                nestedGraph.groups.Add(new GraphGroup { id = "menu", label = "菜单", x = 100, y = 100, w = 900, h = 600 });
                nestedGraph.nodes.Add(new GraphNode { id = "theme_node", label = "主题", type = "功能", kind = "commercial", group = "", x = 500, y = 320, w = 150, h = 54, note = "" });
                nestedGraph = GraphSerialization.Normalize(nestedGraph);
                GraphGroup nestedInner = nestedGraph.groups.First(delegate(GraphGroup item) { return item.id == "menu_theme"; });
                Require(nestedInner.groups.SequenceEqual(new[] { "menu" }), "内层分组未自动归入外层分组");
                Require(nestedGraph.nodes[0].groups.SequenceEqual(new[] { "menu", "menu_theme" }) && nestedGraph.nodes[0].group == "menu_theme", "节点未自动匹配多层分组");
                string nestedDrawio = NativeExport.BuildDrawio(nestedGraph); int nestedGroupStart = nestedDrawio.IndexOf("id=\"g_menu_theme\"", StringComparison.Ordinal);
                Require(nestedGroupStart >= 0 && nestedDrawio.Substring(nestedGroupStart, Math.Min(700, nestedDrawio.Length - nestedGroupStart)).Contains("parent=\"g_menu\""), "飞书画板导出未保留分组嵌套");
                string nestedDrawioPath = Path.Combine(tempFolder, "nested.drawio");
                File.WriteAllText(nestedDrawioPath, nestedDrawio, new UTF8Encoding(false));
                string nestedDrawioNotice;
                GraphDocument importedNestedGraph = GraphSerialization.LoadFile(nestedDrawioPath, out nestedDrawioNotice);
                GraphGroup importedNestedInner = importedNestedGraph.groups.First(delegate(GraphGroup item) { return item.id == "menu_theme"; });
                GraphNode importedNestedNode = importedNestedGraph.nodes.First(delegate(GraphNode item) { return item.id == "theme_node"; });
                Require(nestedDrawioNotice.Length == 0 && importedNestedInner.groups.SequenceEqual(new[] { "menu" }) && importedNestedNode.groups.SequenceEqual(new[] { "menu", "menu_theme" }), "Draw.io 导入未恢复多层分组归属");
                Require(Math.Abs(importedNestedInner.x - nestedInner.x) < .01f && Math.Abs(importedNestedInner.y - nestedInner.y) < .01f && Math.Abs(importedNestedNode.x - nestedGraph.nodes[0].x) < .01f && Math.Abs(importedNestedNode.y - nestedGraph.nodes[0].y) < .01f, "Draw.io 导入未恢复嵌套对象的绝对位置");
                using (MainForm nestedWindow = new MainForm(nestedGraph, false))
                {
                    nestedWindow.WindowState = FormWindowState.Normal; nestedWindow.Size = new System.Drawing.Size(1200, 760); nestedWindow.CreateControl(); nestedWindow.PerformLayout();
                    GraphCanvas nestedCanvas = nestedWindow.CanvasForTesting;
                    Require(nestedCanvas.GroupDrawOrderForTesting().SequenceEqual(new[] { "menu", "menu_theme" }), "分组未按从大到小的底层到上层顺序绘制");
                    Require(nestedCanvas.HitGroupIdForTesting(new System.Drawing.PointF(570, 347)) == "menu_theme", "重叠位置未优先命中较小的内层分组");
                    Require(nestedCanvas.HitEndpointKeyForTesting(new System.Drawing.PointF(570, 347)) == "node:theme_node", "节点未位于所有分组的点击层级之上");
                    nestedCanvas.SelectEntity("node", "theme_node");
                    Require(nestedWindow.MembershipTextForTesting(nestedGraph.nodes[0].groups) == "菜单 > 菜单主题", "节点右侧未显示多层分组路径");
                    float childX = nestedInner.x, nodeX = nestedGraph.nodes[0].x;
                    nestedCanvas.SelectEntity("group", "menu"); nestedCanvas.MoveSelectedGroupForTesting(45f, 30f, true);
                    Require(Math.Abs(nestedInner.x - childX - 45f) < .1f && Math.Abs(nestedGraph.nodes[0].x - nodeX - 45f) < .1f, "移动外层分组时未同步移动子分组和内部节点");
                    nestedCanvas.SelectEntity("node", "theme_node"); nestedCanvas.MoveSelectedNodesForTesting(-300f, 0f, true);
                    Require(nestedGraph.nodes[0].groups.Count == 1 && nestedGraph.nodes[0].groups[0] == "menu", "节点移动后未自动重新匹配所属分组");
                }
                GraphDocument overlapGraph = GraphSerialization.CreateBlank("独立重叠分组测试");
                overlapGraph.groups.Add(new GraphGroup { id = "overlap_a", label = "分组甲", x = 100, y = 100, w = 360, h = 300 });
                overlapGraph.groups.Add(new GraphGroup { id = "overlap_b", label = "分组乙", x = 300, y = 100, w = 360, h = 300 });
                overlapGraph.nodes.Add(new GraphNode { id = "overlap_node", label = "重叠节点", type = "功能", kind = "system", x = 330, y = 200, w = 120, h = 54, group = "", note = "" });
                overlapGraph = GraphSerialization.Normalize(overlapGraph);
                using (MainForm overlapWindow = new MainForm(overlapGraph, false))
                {
                    string overlapMembership = overlapWindow.MembershipTextForTesting(overlapGraph.nodes[0].groups);
                    Require(overlapGraph.nodes[0].groups.Count == 2 && overlapMembership.Contains("分组甲") && overlapMembership.Contains("分组乙") && !overlapMembership.Contains(" > "), "独立重叠分组被错误显示为父子层级");
                }

                GraphDocument wrapGraph = GraphSerialization.CreateBlank("框选内容创建分组测试");
                wrapGraph.groups.Add(new GraphGroup { id = "wrapped_child", label = "已有小分组", x = 350, y = 170, w = 250, h = 200 });
                wrapGraph.nodes.Add(new GraphNode { id = "wrap_a", label = "选中甲", type = "功能", kind = "system", x = 100, y = 100, w = 150, h = 54, group = "", note = "" });
                wrapGraph.nodes.Add(new GraphNode { id = "wrap_b", label = "选中乙", type = "功能", kind = "resource", x = 400, y = 230, w = 150, h = 54, group = "", note = "" });
                wrapGraph.nodes.Add(new GraphNode { id = "wrap_outside", label = "未选中", type = "功能", kind = "output", x = 900, y = 500, w = 150, h = 54, group = "", note = "" });
                wrapGraph = GraphSerialization.Normalize(wrapGraph);
                using (MainForm wrapWindow = new MainForm(wrapGraph, false))
                {
                    wrapWindow.WindowState = FormWindowState.Normal; wrapWindow.Size = new System.Drawing.Size(1200, 760); wrapWindow.CreateControl(); wrapWindow.PerformLayout();
                    GraphCanvas wrapCanvas = wrapWindow.CanvasForTesting;
                    wrapCanvas.SelectObjects(new[] { "wrap_a", "wrap_b" }, new[] { "wrapped_child" });
                    wrapWindow.AddGroupForTesting();
                    GraphGroup wrapper = wrapGraph.groups.Last();
                    Require(wrapper.id != "wrapped_child" && wrapper.x <= 72.1f && wrapper.y <= 50.1f && wrapper.x + wrapper.w >= 627.9f && wrapper.y + wrapper.h >= 397.9f, "新增分组未按合适边距包住框选内容");
                    Require(wrapGraph.groups.First(delegate(GraphGroup item) { return item.id == "wrapped_child"; }).groups.Contains(wrapper.id), "框选小分组后新增分组未成为其外层分组");
                    Require(wrapGraph.nodes.First(delegate(GraphNode item) { return item.id == "wrap_a"; }).groups.Contains(wrapper.id) && wrapGraph.nodes.First(delegate(GraphNode item) { return item.id == "wrap_b"; }).groups.Contains(wrapper.id), "框选节点未自动归入新增分组");
                    Require(!wrapGraph.nodes.First(delegate(GraphNode item) { return item.id == "wrap_outside"; }).groups.Contains(wrapper.id), "未框选的远处节点被错误归入新增分组");
                    Require(wrapCanvas.SelectedType == "group" && wrapCanvas.SelectedId == wrapper.id, "创建包裹分组后未选中新分组");
                }

                GraphDocument alignmentGraph = GraphSerialization.CreateBlank("节点自动对齐测试");
                alignmentGraph.nodes.Add(new GraphNode { id = "moving", label = "移动节点", type = "步骤", kind = "system", group = "", x = 100, y = 100, w = 150, h = 54, note = "" });
                alignmentGraph.nodes.Add(new GraphNode { id = "stationary", label = "参照节点", type = "步骤", kind = "resource", group = "", x = 330, y = 100, w = 150, h = 54, note = "" });
                alignmentGraph = GraphSerialization.Normalize(alignmentGraph);
                using (GraphCanvas alignmentCanvas = new GraphCanvas())
                {
                    alignmentCanvas.Size = new System.Drawing.Size(900, 650); alignmentCanvas.Document = alignmentGraph; alignmentCanvas.EditMode = true; alignmentCanvas.FitToView(); alignmentCanvas.SelectEntity("node", "moving");
                    System.Reflection.MethodInfo alignDown = typeof(GraphCanvas).GetMethod("OnMouseDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    System.Reflection.MethodInfo alignMove = typeof(GraphCanvas).GetMethod("OnMouseMove", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    System.Reflection.MethodInfo alignUp = typeof(GraphCanvas).GetMethod("OnMouseUp", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    Func<System.Drawing.PointF, System.Drawing.Point> alignScreenAt = delegate(System.Drawing.PointF world)
                    {
                        return new System.Drawing.Point((int)Math.Round(world.X * alignmentCanvas.Zoom + alignmentCanvas.ViewOffset.X), (int)Math.Round(world.Y * alignmentCanvas.Zoom + alignmentCanvas.ViewOffset.Y));
                    };
                    System.Drawing.Point alignStart = alignScreenAt(new System.Drawing.PointF(175, 127)), alignEnd = alignScreenAt(new System.Drawing.PointF(402, 127));
                    alignDown.Invoke(alignmentCanvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, alignStart.X, alignStart.Y, 0) });
                    alignMove.Invoke(alignmentCanvas, new object[] { new MouseEventArgs(MouseButtons.Left, 0, alignEnd.X, alignEnd.Y, 0) });
                    Require(Math.Abs(alignmentCanvas.AlignmentGuideXForTesting - 330) < .1f, "节点接近对齐位置时未显示垂直参考线");
                    Require(Math.Abs(alignmentCanvas.AlignmentGuideYForTesting - 100) < .1f, "节点同高时未显示水平参考线");
                    alignUp.Invoke(alignmentCanvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, alignEnd.X, alignEnd.Y, 0) });
                    Require(Math.Abs(alignmentGraph.nodes[0].x - 330) < .1f && Math.Abs(alignmentGraph.nodes[0].y - 100) < .1f, "拖动节点未自动吸附到参照节点");
                    Require(GraphCanvas.IsFreePlacementForTesting(Keys.Control) && !GraphCanvas.IsFreePlacementForTesting(Keys.None), "Ctrl 未正确切换自由摆放模式");
                    alignmentCanvas.MoveSelectedNodesForTesting(12.3f, 7.7f, true);
                    Require(Math.Abs(alignmentGraph.nodes[0].x - 342.3f) < .1f && Math.Abs(alignmentGraph.nodes[0].y - 107.7f) < .1f, "Ctrl 拖动仍发生网格吸附或自动对齐");
                    Require(Single.IsNaN(alignmentCanvas.AlignmentGuideXForTesting) && Single.IsNaN(alignmentCanvas.AlignmentGuideYForTesting), "自由摆放时仍显示对齐参考线");
                }

                GraphDocument spacingGraph = GraphSerialization.CreateBlank("等间距吸附测试");
                spacingGraph.nodes.Add(new GraphNode { id = "spacing_a", label = "节点 A", type = "步骤", kind = "system", group = "", x = 100, y = 100, w = 150, h = 54, note = "" });
                spacingGraph.nodes.Add(new GraphNode { id = "spacing_b", label = "节点 B", type = "步骤", kind = "resource", group = "", x = 300, y = 100, w = 150, h = 54, note = "" });
                spacingGraph.nodes.Add(new GraphNode { id = "spacing_moving", label = "移动节点", type = "步骤", kind = "output", group = "", x = 700, y = 100, w = 150, h = 54, note = "" });
                spacingGraph = GraphSerialization.Normalize(spacingGraph);
                using (GraphCanvas spacingCanvas = new GraphCanvas())
                {
                    spacingCanvas.Size = new System.Drawing.Size(900, 650); spacingCanvas.Document = spacingGraph; spacingCanvas.EditMode = true; spacingCanvas.FitToView(); spacingCanvas.SelectEntity("node", "spacing_moving");
                    spacingCanvas.MoveSelectedNodesForTesting(-198f, 0, false);
                    Require(Math.Abs(spacingGraph.nodes[2].x - 500) < .1f, "水平等间距位置未自动吸附");
                    Require(spacingCanvas.HorizontalSpacingHintActiveForTesting && Math.Abs(spacingCanvas.SpacingHintGapForTesting - 50) < .1f, "水平等间距吸附未显示间距提示");
                    spacingCanvas.MoveSelectedNodesForTesting(12.3f, 0, true);
                    Require(Math.Abs(spacingGraph.nodes[2].x - 512.3f) < .1f && !spacingCanvas.HorizontalSpacingHintActiveForTesting, "Ctrl 自由摆放未关闭等间距吸附提示");
                }

                GraphDocument verticalSpacingGraph = GraphSerialization.CreateBlank("垂直等间距吸附测试");
                verticalSpacingGraph.nodes.Add(new GraphNode { id = "vertical_a", label = "节点 A", type = "步骤", kind = "system", group = "", x = 100, y = 100, w = 150, h = 54, note = "" });
                verticalSpacingGraph.nodes.Add(new GraphNode { id = "vertical_b", label = "节点 B", type = "步骤", kind = "resource", group = "", x = 100, y = 204, w = 150, h = 54, note = "" });
                verticalSpacingGraph.nodes.Add(new GraphNode { id = "vertical_moving", label = "移动节点", type = "步骤", kind = "output", group = "", x = 100, y = 500, w = 150, h = 54, note = "" });
                verticalSpacingGraph = GraphSerialization.Normalize(verticalSpacingGraph);
                using (GraphCanvas verticalSpacingCanvas = new GraphCanvas())
                {
                    verticalSpacingCanvas.Size = new System.Drawing.Size(900, 650); verticalSpacingCanvas.Document = verticalSpacingGraph; verticalSpacingCanvas.EditMode = true; verticalSpacingCanvas.FitToView(); verticalSpacingCanvas.SelectEntity("node", "vertical_moving");
                    verticalSpacingCanvas.MoveSelectedNodesForTesting(0, -192f, false);
                    Require(Math.Abs(verticalSpacingGraph.nodes[2].y - 308) < .1f, "垂直等间距位置未自动吸附");
                    Require(verticalSpacingCanvas.VerticalSpacingHintActiveForTesting && Math.Abs(verticalSpacingCanvas.SpacingHintGapForTesting - 50) < .1f, "垂直等间距吸附未显示间距提示");
                }

                GraphDocument mixedAlignmentGraph = GraphSerialization.CreateBlank("节点与分组混合对齐测试");
                mixedAlignmentGraph.groups.Add(new GraphGroup { id = "alignment_group", label = "参照分组", x = 330, y = 100, w = 150, h = 100 });
                mixedAlignmentGraph.nodes.Add(new GraphNode { id = "alignment_node", label = "移动节点", type = "步骤", kind = "system", group = "", x = 100, y = 100, w = 150, h = 54, note = "" });
                mixedAlignmentGraph = GraphSerialization.Normalize(mixedAlignmentGraph);
                using (GraphCanvas mixedAlignmentCanvas = new GraphCanvas())
                {
                    mixedAlignmentCanvas.Size = new System.Drawing.Size(900, 650); mixedAlignmentCanvas.Document = mixedAlignmentGraph; mixedAlignmentCanvas.EditMode = true; mixedAlignmentCanvas.SelectEntity("node", "alignment_node");
                    mixedAlignmentCanvas.MoveSelectedNodesForTesting(227, 0, false);
                    Require(Math.Abs(mixedAlignmentGraph.nodes[0].x - 330) < .1f && Math.Abs(mixedAlignmentCanvas.AlignmentGuideXForTesting - 330) < .1f, "节点未能与分组边缘对齐");
                }

                GraphDocument mixedSpacingGraph = GraphSerialization.CreateBlank("节点与分组混合等间距测试");
                mixedSpacingGraph.groups.Add(new GraphGroup { id = "spacing_group_a", label = "分组 A", x = 100, y = 100, w = 150, h = 100 });
                mixedSpacingGraph.groups.Add(new GraphGroup { id = "spacing_group_moving", label = "移动分组", x = 700, y = 100, w = 150, h = 100 });
                mixedSpacingGraph.nodes.Add(new GraphNode { id = "spacing_node_b", label = "节点 B", type = "步骤", kind = "resource", group = "", x = 300, y = 100, w = 150, h = 100, note = "" });
                mixedSpacingGraph = GraphSerialization.Normalize(mixedSpacingGraph);
                using (GraphCanvas mixedSpacingCanvas = new GraphCanvas())
                {
                    mixedSpacingCanvas.Size = new System.Drawing.Size(900, 650); mixedSpacingCanvas.Document = mixedSpacingGraph; mixedSpacingCanvas.EditMode = true; mixedSpacingCanvas.SelectEntity("group", "spacing_group_moving");
                    mixedSpacingCanvas.MoveSelectedGroupForTesting(-198, 0, false);
                    Require(Math.Abs(mixedSpacingGraph.groups[1].x - 500) < .1f, "分组未能与节点、分组形成等间距");
                    Require(mixedSpacingCanvas.HorizontalSpacingHintActiveForTesting && Math.Abs(mixedSpacingCanvas.SpacingHintGapForTesting - 50) < .1f, "分组混合等间距未显示提示");
                    Require(Math.Abs(mixedSpacingCanvas.AlignmentGuideYForTesting - 100) < .1f, "分组未能与节点或其他分组对齐");
                    mixedSpacingCanvas.MoveSelectedGroupForTesting(12.3f, 7.7f, true);
                    Require(Math.Abs(mixedSpacingGraph.groups[1].x - 512.3f) < .1f && Math.Abs(mixedSpacingGraph.groups[1].y - 107.7f) < .1f, "Ctrl 拖动分组仍发生吸附");
                    Require(!mixedSpacingCanvas.HorizontalSpacingHintActiveForTesting && Single.IsNaN(mixedSpacingCanvas.AlignmentGuideYForTesting), "Ctrl 自由拖动分组仍显示吸附提示");
                }

                GraphDocument newNodeEditGraph = GraphSerialization.CreateBlank("新节点编辑回归测试");
                using (MainForm interactionWindow = new MainForm(newNodeEditGraph, false))
                {
                    interactionWindow.WindowState = FormWindowState.Normal; interactionWindow.Size = new System.Drawing.Size(1200, 760); interactionWindow.CreateControl(); interactionWindow.PerformLayout();
                    GraphCanvas interactionCanvas = interactionWindow.CanvasForTesting; interactionCanvas.Size = new System.Drawing.Size(900, 650); interactionCanvas.EditMode = true; interactionCanvas.FitToView();
                    System.Reflection.MethodInfo down = typeof(GraphCanvas).GetMethod("OnMouseDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    System.Reflection.MethodInfo up = typeof(GraphCanvas).GetMethod("OnMouseUp", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    System.Reflection.MethodInfo twice = typeof(GraphCanvas).GetMethod("OnMouseDoubleClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    Func<System.Drawing.PointF, System.Drawing.Point> screenAt = delegate(System.Drawing.PointF world)
                    {
                        return new System.Drawing.Point((int)Math.Round(world.X * interactionCanvas.Zoom + interactionCanvas.ViewOffset.X), (int)Math.Round(world.Y * interactionCanvas.Zoom + interactionCanvas.ViewOffset.Y));
                    };
                    System.Drawing.Point createPoint = screenAt(new System.Drawing.PointF(450, 300));
                    down.Invoke(interactionCanvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, createPoint.X, createPoint.Y, 0) }); up.Invoke(interactionCanvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, createPoint.X, createPoint.Y, 0) });
                    down.Invoke(interactionCanvas, new object[] { new MouseEventArgs(MouseButtons.Left, 2, createPoint.X, createPoint.Y, 0) }); twice.Invoke(interactionCanvas, new object[] { new MouseEventArgs(MouseButtons.Left, 2, createPoint.X, createPoint.Y, 0) }); up.Invoke(interactionCanvas, new object[] { new MouseEventArgs(MouseButtons.Left, 2, createPoint.X, createPoint.Y, 0) });
                    Require(newNodeEditGraph.nodes.Count == 1, "双击空白位置未创建单个节点");
                    GraphNode createdNode = newNodeEditGraph.nodes[0]; System.Drawing.Point namePoint = screenAt(new System.Drawing.PointF(createdNode.x + createdNode.w / 2, createdNode.y + 43));
                    down.Invoke(interactionCanvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, namePoint.X, namePoint.Y, 0) }); up.Invoke(interactionCanvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, namePoint.X, namePoint.Y, 0) });
                    down.Invoke(interactionCanvas, new object[] { new MouseEventArgs(MouseButtons.Left, 2, namePoint.X, namePoint.Y, 0) }); twice.Invoke(interactionCanvas, new object[] { new MouseEventArgs(MouseButtons.Left, 2, namePoint.X, namePoint.Y, 0) }); up.Invoke(interactionCanvas, new object[] { new MouseEventArgs(MouseButtons.Left, 2, namePoint.X, namePoint.Y, 0) });
                    Require(interactionCanvas.InlineEditorActiveForTesting, "新节点双击名称未进入编辑");
                    interactionCanvas.SetInlineEditorTextForTesting("保留的新节点"); interactionCanvas.CommitInlineEditForTesting();
                    Require(newNodeEditGraph.nodes.Count == 1 && newNodeEditGraph.nodes[0].label == "保留的新节点", "新节点修改名称后丢失");
                }

                GraphDocument infiniteGraph = GraphSerialization.CreateBlank("infinite canvas test");
                infiniteGraph.groups.Add(new GraphGroup { id = "negative_group", label = "negative", x = -900, y = -650, w = 260, h = 220 });
                infiniteGraph.nodes.Add(new GraphNode { id = "far_node", label = "far", type = "test", kind = "system", group = "", x = 2600, y = 1900, w = 150, h = 55 });
                infiniteGraph = GraphSerialization.Normalize(infiniteGraph);
                Require(infiniteGraph.groups[0].x == -900 && infiniteGraph.groups[0].y == -650, "negative coordinates were clipped");
                using (GraphCanvas infiniteCanvas = new GraphCanvas())
                {
                    infiniteCanvas.Size = new System.Drawing.Size(900, 650); infiniteCanvas.Document = infiniteGraph; infiniteCanvas.EditMode = true;
                    infiniteCanvas.SelectEntity("node", "far_node"); infiniteCanvas.MoveSelectedNodesForTesting(5000, 4000, true); infiniteCanvas.RefreshDocument();
                    Require(infiniteGraph.nodes[0].x == 7600 && infiniteGraph.nodes[0].y == 5900, "content outside the initial viewport was clipped");
                    System.Drawing.RectangleF content = infiniteCanvas.ContentBoundsForTesting(0);
                    Require(content.Left <= -900 && content.Right >= 7750, "infinite canvas content bounds are incomplete");
                    infiniteCanvas.FitToView();
                    System.Drawing.PointF screen = new System.Drawing.PointF(infiniteGraph.nodes[0].x * infiniteCanvas.Zoom + infiniteCanvas.ViewOffset.X, infiniteGraph.nodes[0].y * infiniteCanvas.Zoom + infiniteCanvas.ViewOffset.Y);
                    Require(screen.X >= 0 && screen.X <= infiniteCanvas.ClientSize.Width && screen.Y >= 0 && screen.Y <= infiniteCanvas.ClientSize.Height, "fit to view omitted distant content");
                    using (System.Drawing.Bitmap exported = infiniteCanvas.ExportBitmap(1024)) Require(exported.Width > 0 && exported.Height > 0, "infinite canvas bitmap export failed");
                }
                string infiniteSvg = NativeExport.BuildSvg(infiniteGraph);
                Require(infiniteSvg.Contains("viewBox=\"-") && !infiniteSvg.Contains("viewBox=\"0 0 1380 760\""), "SVG still uses a fixed canvas boundary");

                GraphDocument extremeGraph = GraphSerialization.CreateBlank("extreme fit test");
                extremeGraph.nodes.Add(new GraphNode { id = "extreme_left", label = "left", type = "test", kind = "system", group = "", x = -50000000f, y = 0, w = 150, h = 55 });
                extremeGraph.nodes.Add(new GraphNode { id = "extreme_right", label = "right", type = "test", kind = "resource", group = "", x = 50000000f, y = 0, w = 150, h = 55 });
                extremeGraph.edges.Add(new GraphEdge { id = "extreme_edge", source = "extreme_left", target = "extreme_right", sourceType = "node", targetType = "node", sourceSide = "right", targetSide = "left", lineType = "straight", category = "core", label = "" });
                extremeGraph = GraphSerialization.Normalize(extremeGraph);
                using (GraphCanvas extremeCanvas = new GraphCanvas())
                {
                    extremeCanvas.Size = new System.Drawing.Size(900, 650); extremeCanvas.Document = extremeGraph; extremeCanvas.FitToView();
                    Require(extremeCanvas.Zoom < .00001f && extremeCanvas.Zoom >= .00000001f, "亿级坐标跨度未使用安全的极小缩放");
                    Require(extremeCanvas.HitEdgeIdForTesting(new System.Drawing.PointF(75f, 27.5f)) == "extreme_edge", "极小缩放下关系线命中失败");
                }
                GraphDocument unsafeCoordinateGraph = GraphSerialization.CreateBlank("unsafe coordinate test");
                unsafeCoordinateGraph.nodes.Add(new GraphNode { id = "unsafe", label = "unsafe", type = "test", kind = "system", group = "", x = Single.MaxValue, y = Single.MinValue, w = 150, h = 55 });
                unsafeCoordinateGraph = GraphSerialization.Normalize(unsafeCoordinateGraph);
                Require(unsafeCoordinateGraph.nodes[0].x == GraphSerialization.MaxCoordinate && unsafeCoordinateGraph.nodes[0].y == -GraphSerialization.MaxCoordinate, "超出安全绘制范围的有限坐标未收敛");

                GraphDocument automaticLayoutGraph = GraphSerialization.CreateBlank("自动排版回归测试");
                automaticLayoutGraph.nodes.Add(new GraphNode { id = "layout_a", label = "起点", type = "步骤", kind = "system", group = "", x = 100, y = 100, w = 150, h = 54, note = "" });
                automaticLayoutGraph.nodes.Add(new GraphNode { id = "layout_b", label = "分支甲", type = "步骤", kind = "resource", group = "", x = 100, y = 100, w = 150, h = 54, note = "" });
                automaticLayoutGraph.nodes.Add(new GraphNode { id = "layout_c", label = "分支乙", type = "步骤", kind = "content", group = "", x = 100, y = 100, w = 150, h = 54, note = "" });
                automaticLayoutGraph.nodes.Add(new GraphNode { id = "layout_d", label = "分支丙", type = "步骤", kind = "staff", group = "", x = 100, y = 100, w = 150, h = 54, note = "" });
                automaticLayoutGraph.nodes.Add(new GraphNode { id = "layout_e", label = "终点", type = "步骤", kind = "output", group = "", x = 100, y = 100, w = 150, h = 54, note = "" });
                automaticLayoutGraph.edges.Add(new GraphEdge { id = "layout_ab", source = "layout_a", target = "layout_b", sourceType = "node", targetType = "node", label = "路径甲", category = "core", lineType = "polyline" });
                automaticLayoutGraph.edges.Add(new GraphEdge { id = "layout_ac", source = "layout_a", target = "layout_c", sourceType = "node", targetType = "node", label = "路径乙", category = "core", lineType = "polyline" });
                automaticLayoutGraph.edges.Add(new GraphEdge { id = "layout_ad", source = "layout_a", target = "layout_d", sourceType = "node", targetType = "node", label = "路径丙", category = "core", lineType = "polyline" });
                automaticLayoutGraph.edges.Add(new GraphEdge { id = "layout_be", source = "layout_b", target = "layout_e", sourceType = "node", targetType = "node", label = "汇总甲", category = "core", lineType = "polyline" });
                automaticLayoutGraph.edges.Add(new GraphEdge { id = "layout_ce", source = "layout_c", target = "layout_e", sourceType = "node", targetType = "node", label = "汇总乙", category = "core", lineType = "polyline" });
                automaticLayoutGraph.edges.Add(new GraphEdge { id = "layout_de", source = "layout_d", target = "layout_e", sourceType = "node", targetType = "node", label = "汇总丙", category = "core", lineType = "polyline" });
                automaticLayoutGraph.edges.Add(new GraphEdge { id = "layout_ae", source = "layout_a", target = "layout_e", sourceType = "node", targetType = "node", label = "直达", category = "core", lineType = "polyline" });
                automaticLayoutGraph = GraphSerialization.Normalize(automaticLayoutGraph);
                GraphLayoutResult automaticLayout = GraphLayout.Calculate(automaticLayoutGraph);
                GraphLayoutResult repeatedAutomaticLayout = GraphLayout.Calculate(automaticLayoutGraph);
                Require(LayoutResultsMatch(automaticLayout, repeatedAutomaticLayout), "同一关系图的自动排版结果不确定");
                int layoutCancellationChecks = 0; bool layoutCancellationObserved = false;
                GraphLayoutOptions cancellingLayoutOptions = GraphLayoutOptions.ForDocument(automaticLayoutGraph);
                cancellingLayoutOptions.CancellationRequested = delegate { layoutCancellationChecks++; return layoutCancellationChecks >= 3; };
                try { GraphLayout.Calculate(automaticLayoutGraph, cancellingLayoutOptions); }
                catch (OperationCanceledException) { layoutCancellationObserved = true; }
                Require(layoutCancellationObserved, "自动排版没有在安全检查点响应取消请求");
                bool automaticPositionChanged = false;
                List<RectangleF> automaticNodeBounds = new List<RectangleF>();
                foreach (GraphNode node in automaticLayoutGraph.nodes)
                {
                    RectangleF bounds;
                    Require(automaticLayout.TryGetNodeBounds(node.id, out bounds), "自动排版遗漏节点：" + node.id);
                    if (Math.Abs(bounds.X - node.x) > .01f || Math.Abs(bounds.Y - node.y) > .01f) automaticPositionChanged = true;
                    automaticNodeBounds.Add(bounds);
                }
                Require(automaticPositionChanged, "自动排版未改变重叠节点位置");
                for (int firstIndex = 0; firstIndex < automaticNodeBounds.Count; firstIndex++)
                    for (int secondIndex = firstIndex + 1; secondIndex < automaticNodeBounds.Count; secondIndex++)
                        Require(!automaticNodeBounds[firstIndex].IntersectsWith(automaticNodeBounds[secondIndex]), "自动排版后节点仍相互重叠");

                List<PointF> fanOutPorts = new List<PointF>();
                foreach (string edgeId in new[] { "layout_ab", "layout_ac", "layout_ad", "layout_ae" })
                {
                    GraphEdgeLayout route;
                    Require(automaticLayout.TryGetEdge(edgeId, out route), "自动路由遗漏同源关系：" + edgeId);
                    Require(route.SourceSide == "right", "从左到右排版未从起点右侧出线：" + edgeId);
                    for (int portIndex = 0; portIndex < fanOutPorts.Count; portIndex++)
                        Require(!PointsClose(fanOutPorts[portIndex], route.SourcePoint), "同端口多条关系仍使用同一个端口点");
                    fanOutPorts.Add(route.SourcePoint);
                }

                List<RectangleF> labelBounds = new List<RectangleF>();
                foreach (GraphEdge edge in automaticLayoutGraph.edges)
                {
                    GraphEdgeLayout route;
                    Require(automaticLayout.TryGetEdge(edge.id, out route), "自动排版遗漏关系路线：" + edge.id);
                    Require(!route.HasObstacleConflict, "自动路线仍存在障碍冲突：" + edge.id);
                    Require(RouteIsOrthogonal(route.Points), "自动路线包含非正交线段：" + edge.id);
                    foreach (KeyValuePair<string, RectangleF> nodeBounds in automaticLayout.NodeBounds)
                        Require(!RouteCrossesRectangleInterior(route.Points, nodeBounds.Value), "自动路线穿过节点：" + edge.id + " / " + nodeBounds.Key);
                    Require(!String.IsNullOrWhiteSpace(edge.label) && !route.LabelBounds.IsEmpty, "非空关系名称没有标签边界：" + edge.id);
                    for (int labelIndex = 0; labelIndex < labelBounds.Count; labelIndex++)
                        Require(!labelBounds[labelIndex].IntersectsWith(route.LabelBounds), "自动关系标签仍相互重叠");
                    labelBounds.Add(route.LabelBounds);
                }

                GraphDocument flowLayoutGraph = GraphSerialization.CreateBlank("流程图自动排版方向测试");
                flowLayoutGraph.meta.diagramType = "flowchart";
                flowLayoutGraph.nodes.Add(new GraphNode { id = "flow_top", label = "开始", type = "步骤", kind = "system", shape = "terminator", group = "", x = 200, y = 200, w = 150, h = 54, note = "" });
                flowLayoutGraph.nodes.Add(new GraphNode { id = "flow_middle", label = "处理", type = "步骤", kind = "resource", shape = "decision", group = "", x = 200, y = 200, w = 150, h = 54, note = "" });
                flowLayoutGraph.nodes.Add(new GraphNode { id = "flow_bottom", label = "结束", type = "步骤", kind = "output", shape = "document", group = "", x = 200, y = 200, w = 150, h = 54, note = "" });
                flowLayoutGraph.edges.Add(new GraphEdge { id = "flow_first", source = "flow_top", target = "flow_middle", sourceType = "node", targetType = "node", label = "执行", category = "core", lineType = "polyline" });
                flowLayoutGraph.edges.Add(new GraphEdge { id = "flow_second", source = "flow_middle", target = "flow_bottom", sourceType = "node", targetType = "node", label = "完成", category = "core", lineType = "polyline" });
                flowLayoutGraph = GraphSerialization.Normalize(flowLayoutGraph);
                GraphLayoutResult flowLayout = GraphLayout.Calculate(flowLayoutGraph);
                RectangleF flowTop = RectangleF.Empty, flowMiddle = RectangleF.Empty, flowBottom = RectangleF.Empty;
                Require(flowLayout.TryGetNodeBounds("flow_top", out flowTop) && flowLayout.TryGetNodeBounds("flow_middle", out flowMiddle) && flowLayout.TryGetNodeBounds("flow_bottom", out flowBottom), "流程图自动排版遗漏节点");
                Require(flowTop.Top < flowMiddle.Top && flowMiddle.Top < flowBottom.Top, "流程图没有按从上到下方向排版");
                GraphEdgeLayout flowFirstRoute = null, flowSecondRoute = null;
                Require(flowLayout.TryGetEdge("flow_first", out flowFirstRoute) && flowLayout.TryGetEdge("flow_second", out flowSecondRoute), "流程图自动路由遗漏关系");
                Require(flowFirstRoute.SourceSide == "bottom" && flowFirstRoute.TargetSide == "top" && flowSecondRoute.SourceSide == "bottom" && flowSecondRoute.TargetSide == "top", "流程图关系没有使用上下端口");
                string flowExportSvg = NativeExport.BuildSvg(flowLayoutGraph);
                Require(flowExportSvg.Contains("data-shape=\"terminator\"") && flowExportSvg.Contains("data-shape=\"decision\"") && flowExportSvg.Contains("data-shape=\"document\"") && flowExportSvg.Contains("<polygon") && flowExportSvg.Contains("<path"), "SVG/只读 HTML 未保留流程图形状");
                Require(!flowExportSvg.Contains(">步骤</text>"), "流程图 SVG 仍错误导出节点类型");
                byte[] flowExportPdf = NativePdfExport.Build(flowLayoutGraph);
                Require(flowExportPdf.Length > 500 && Encoding.ASCII.GetString(flowExportPdf, 0, Math.Min(flowExportPdf.Length, 16)).StartsWith("%PDF-1.4", StringComparison.Ordinal), "流程图 PDF 形状导出失败");

                GraphDocument lineStyleGraph = GraphSerialization.CreateBlank("自动避障与传统线型测试");
                lineStyleGraph.nodes.Add(new GraphNode { id = "style_auto_a", label = "自动 A", type = "步骤", kind = "system", group = "", x = 40, y = 40, w = 150, h = 54, note = "" });
                lineStyleGraph.nodes.Add(new GraphNode { id = "style_auto_b", label = "自动 B", type = "步骤", kind = "resource", group = "", x = 420, y = 180, w = 150, h = 54, note = "" });
                lineStyleGraph.nodes.Add(new GraphNode { id = "style_curve_a", label = "曲线 A", type = "步骤", kind = "content", group = "", x = 40, y = 360, w = 150, h = 54, note = "" });
                lineStyleGraph.nodes.Add(new GraphNode { id = "style_curve_b", label = "曲线 B", type = "步骤", kind = "output", group = "", x = 420, y = 460, w = 150, h = 54, note = "" });
                lineStyleGraph.edges.Add(new GraphEdge { id = "style_auto_edge", source = "style_auto_a", target = "style_auto_b", sourceType = "node", targetType = "node", label = "自动", category = "core", lineType = "auto" });
                lineStyleGraph.edges.Add(new GraphEdge { id = "style_curve_edge", source = "style_curve_a", target = "style_curve_b", sourceType = "node", targetType = "node", label = "曲线", category = "core", lineType = "curve" });
                lineStyleGraph = GraphSerialization.Normalize(lineStyleGraph);
                string lineStyleSvg = NativeExport.BuildSvg(lineStyleGraph);
                int autoStyleStart = lineStyleSvg.IndexOf("data-id=\"style_auto_edge\"", StringComparison.Ordinal), curveStyleStart = lineStyleSvg.IndexOf("data-id=\"style_curve_edge\"", StringComparison.Ordinal);
                Require(autoStyleStart >= 0 && curveStyleStart >= 0, "线型 SVG 缺少测试关系");
                int autoStyleEnd = lineStyleSvg.IndexOf("</g>", autoStyleStart, StringComparison.Ordinal), curveStyleEnd = lineStyleSvg.IndexOf("</g>", curveStyleStart, StringComparison.Ordinal);
                string autoStyleFragment = lineStyleSvg.Substring(autoStyleStart, autoStyleEnd - autoStyleStart);
                string curveStyleFragment = lineStyleSvg.Substring(curveStyleStart, curveStyleEnd - curveStyleStart);
                Require(autoStyleFragment.Contains(" L ") && !autoStyleFragment.Contains(" C "), "自动避障关系未导出为正交路线");
                Require(curveStyleFragment.Contains(" C "), "曲线关系被后台自动路由覆盖");
                string autoLineDrawioPath = Path.Combine(tempFolder, "auto-line.drawio");
                NativeExport.SaveDrawio(lineStyleGraph, autoLineDrawioPath);
                string autoLineNotice;
                GraphDocument autoLineRoundTrip = GraphSerialization.LoadFile(autoLineDrawioPath, out autoLineNotice);
                Require(autoLineRoundTrip.edges.Any(delegate(GraphEdge edge) { return edge.id == "style_auto_edge" && edge.lineType == "auto"; }), "Draw.io 往返未保留自动避障线型");

                GraphDocument routeOnlyGraph = GraphSerialization.CreateBlank("仅路由保留边界测试");
                routeOnlyGraph.groups.Add(new GraphGroup { id = "route_group", label = "保留分组", x = -180.25f, y = 65.5f, w = 520.75f, h = 310.25f });
                routeOnlyGraph.nodes.Add(new GraphNode { id = "route_left", label = "左侧", type = "步骤", kind = "system", group = "", x = -120.5f, y = 130.25f, w = 137.5f, h = 51.25f, note = "" });
                routeOnlyGraph.nodes.Add(new GraphNode { id = "route_right", label = "右侧", type = "步骤", kind = "output", group = "", x = 430.75f, y = 245.5f, w = 166.25f, h = 62.75f, note = "" });
                routeOnlyGraph.edges.Add(new GraphEdge { id = "route_only_edge", source = "route_left", target = "route_right", sourceType = "node", targetType = "node", label = "只调整路线", category = "core", lineType = "polyline" });
                routeOnlyGraph = GraphSerialization.Normalize(routeOnlyGraph);
                string routeOnlyBefore = GraphSerialization.Serialize(routeOnlyGraph, false);
                GraphLayoutResult routeOnlyLayout = GraphLayout.CalculateRoutes(routeOnlyGraph);
                Require(routeOnlyBefore == GraphSerialization.Serialize(routeOnlyGraph, false), "CalculateRoutes 修改了原关系图");
                foreach (GraphNode node in routeOnlyGraph.nodes)
                {
                    RectangleF bounds;
                    Require(routeOnlyLayout.TryGetNodeBounds(node.id, out bounds) && RectangleMatchesNode(bounds, node), "CalculateRoutes 改变了节点边界：" + node.id);
                }
                foreach (GraphGroup group in routeOnlyGraph.groups)
                {
                    RectangleF bounds;
                    Require(routeOnlyLayout.TryGetGroupBounds(group.id, out bounds) && RectangleMatchesGroup(bounds, group), "CalculateRoutes 改变了分组边界：" + group.id);
                }

                GraphDocument groupObstacleGraph = GraphSerialization.CreateBlank("分组避障测试");
                groupObstacleGraph.groups.Add(new GraphGroup { id = "blocking_group", label = "不可穿越标题区", x = 250, y = 60, w = 220, h = 190 });
                groupObstacleGraph.nodes.Add(new GraphNode { id = "outside_left", label = "左", type = "步骤", kind = "system", group = "", x = 20, y = 120, w = 150, h = 54, note = "" });
                groupObstacleGraph.nodes.Add(new GraphNode { id = "outside_right", label = "右", type = "步骤", kind = "output", group = "", x = 560, y = 120, w = 150, h = 54, note = "" });
                groupObstacleGraph.edges.Add(new GraphEdge { id = "around_group", source = "outside_left", target = "outside_right", sourceType = "node", targetType = "node", label = "绕过分组", category = "core", lineType = "auto" });
                groupObstacleGraph = GraphSerialization.Normalize(groupObstacleGraph);
                GraphLayoutResult groupObstacleLayout = GraphLayout.CalculateRoutes(groupObstacleGraph);
                GraphEdgeLayout aroundGroupRoute;
                Require(groupObstacleLayout.TryGetEdge("around_group", out aroundGroupRoute) && !aroundGroupRoute.HasObstacleConflict, "自动路线未能绕开无关分组");
                Require(!RouteCrossesRectangleInterior(aroundGroupRoute.Points, new RectangleF(250, 60, 220, 190)), "自动路线穿过无关分组或标题区域");

                GraphDocument automaticCanvasGraph = GraphSerialization.Clone(automaticLayoutGraph);
                GraphLayoutResult automaticCanvasLayout = GraphLayout.Calculate(automaticCanvasGraph);
                string automaticCanvasBefore = GraphSerialization.Serialize(automaticCanvasGraph, false);
                using (GraphCanvas automaticCanvas = new GraphCanvas())
                using (Bitmap automaticFrame = new Bitmap(900, 650))
                {
                    automaticCanvas.Size = new Size(900, 650); automaticCanvas.Document = automaticCanvasGraph; automaticCanvas.EditMode = true;
                    Require(automaticCanvas.ApplyAutomaticLayout(automaticCanvasLayout, automaticCanvasBefore), "GraphCanvas 未应用自动排版");
                    Require(automaticCanvasGraph.edges.All(delegate(GraphEdge edge) { return edge.lineType == "auto"; }), "自动排版未将关系切换为自动避障线型");
                    Require(automaticCanvas.HasAutomaticRoutingForTesting, "GraphCanvas 应用自动排版后未使用自动路线");
                    RectangleF canvasContent = automaticCanvas.ContentBoundsForTesting(0);
                    Require(FinitePositiveRectangle(canvasContent), "自动排版后的画布内容边界无效");
                    Rectangle miniMap = automaticCanvas.MiniMapBoundsForTesting;
                    Require(miniMap.Width > 0 && miniMap.Height > 0 && miniMap.Left >= 0 && miniMap.Top >= 0 && miniMap.Right <= automaticCanvas.ClientSize.Width && miniMap.Bottom <= automaticCanvas.ClientSize.Height, "自动排版后小地图边界无效");
                    automaticCanvas.DrawToBitmap(automaticFrame, new Rectangle(0, 0, automaticFrame.Width, automaticFrame.Height));
                    Require(automaticCanvas.HasAutomaticRoutingForTesting, "绘制小地图或画布后自动路线丢失");
                }

                GraphDocument keyboardGraph = GraphSerialization.CreateBlank("键盘与无障碍测试");
                keyboardGraph.nodes.Add(new GraphNode { id = "keyboard_left", label = "左侧节点", type = "步骤", kind = "system", group = "", x = 40, y = 100, w = 150, h = 54, note = "键盘说明" });
                keyboardGraph.nodes.Add(new GraphNode { id = "keyboard_right", label = "右侧节点", type = "步骤", kind = "resource", group = "", x = 420, y = 100, w = 150, h = 54, note = "" });
                keyboardGraph.nodes.Add(new GraphNode { id = "keyboard_down", label = "下方节点", type = "步骤", kind = "output", group = "", x = 40, y = 360, w = 150, h = 54, note = "" });
                keyboardGraph = GraphSerialization.Normalize(keyboardGraph);
                using (GraphCanvas keyboardCanvas = new GraphCanvas())
                {
                    keyboardCanvas.Size = new System.Drawing.Size(900, 650); keyboardCanvas.Document = keyboardGraph; keyboardCanvas.EditMode = true; keyboardCanvas.CreateControl();
                    keyboardCanvas.SelectEntity("node", "keyboard_left"); float originalX = keyboardGraph.nodes[0].x;
                    keyboardCanvas.NudgeSelectionForTesting(5, 0);
                    Require(Math.Abs(keyboardGraph.nodes[0].x - originalX - 5f) < .01f, "方向键移动逻辑未移动选中节点");
                    keyboardCanvas.SelectNearestInDirectionForTesting(1, 0);
                    Require(keyboardCanvas.SelectedType == "node" && keyboardCanvas.SelectedId == "keyboard_right", "Alt+方向键未选择对应方向的相邻对象");
                    Require(keyboardCanvas.BeginSelectedLabelEdit() && keyboardCanvas.InlineEditorActiveForTesting, "Enter/F2 名称编辑入口不可用");
                    keyboardCanvas.SetInlineEditorTextForTesting("键盘改名"); keyboardCanvas.CommitInlineEditForTesting();
                    Require(keyboardGraph.nodes[1].label == "键盘改名", "键盘名称编辑未提交");
                    AccessibleObject canvasAccessibility = keyboardCanvas.AccessibilityObject;
                    Require(canvasAccessibility != null && canvasAccessibility.GetChildCount() == keyboardGraph.nodes.Count, "画布无障碍对象树未暴露全部节点");
                    AccessibleObject firstAccessibleNode = canvasAccessibility.GetChild(0);
                    Require(firstAccessibleNode != null && firstAccessibleNode.Name.Contains("节点") && firstAccessibleNode.Description.Contains("类型"), "画布节点缺少可读的无障碍名称或说明");
                    System.Drawing.Rectangle map = keyboardCanvas.MiniMapBoundsForTesting; System.Drawing.Point mapTarget = new System.Drawing.Point(map.Right - 12, map.Top + map.Height / 2);
                    System.Drawing.PointF viewBeforeMap = keyboardCanvas.ViewOffset;
                    Require(keyboardCanvas.NavigateMiniMapForTesting(mapTarget), "小地图未响应导航位置");
                    Require(Math.Abs(keyboardCanvas.ViewOffset.X - viewBeforeMap.X) > .01f || Math.Abs(keyboardCanvas.ViewOffset.Y - viewBeforeMap.Y) > .01f, "小地图导航未改变视野");
                }

                GraphDocument accessibleFlowGraph = GraphSerialization.CreateBlank("流程图组件无障碍测试"); accessibleFlowGraph.meta.diagramType = "flowchart";
                using (MainForm accessibleFlowWindow = new MainForm(accessibleFlowGraph, false))
                {
                    accessibleFlowWindow.WindowState = FormWindowState.Normal; accessibleFlowWindow.Size = new System.Drawing.Size(1200, 760); accessibleFlowWindow.CreateControl(); accessibleFlowWindow.PerformLayout();
                    Require(accessibleFlowWindow.ToolbarAccessibleForTesting, "工具栏组合框缺少无障碍名称");
                    Require(accessibleFlowWindow.FlowchartPaletteAccessibleForTesting, "流程图组件按钮缺少可读名称或键盘焦点");
                    Require(accessibleFlowWindow.AddFlowchartShapeUsingKeyboardForTesting("decision") && accessibleFlowGraph.nodes.Count == 1 && accessibleFlowGraph.nodes[0].shape == "decision", "流程图组件按钮无法通过键盘点击添加图形");
                }

                GraphDocument swapDirectionGraph = GraphSerialization.CreateBlank("关系方向交换测试");
                swapDirectionGraph.nodes.Add(new GraphNode { id = "swap_a", label = "A", type = "步骤", kind = "system", group = "", x = 40, y = 40, w = 150, h = 54, note = "" });
                swapDirectionGraph.nodes.Add(new GraphNode { id = "swap_b", label = "B", type = "步骤", kind = "resource", group = "", x = 320, y = 40, w = 150, h = 54, note = "" });
                swapDirectionGraph.edges.Add(new GraphEdge { id = "swap_edge", source = "swap_a", target = "swap_b", sourceType = "node", targetType = "node", label = "交换", category = "core", lineType = "polyline" });
                swapDirectionGraph = GraphSerialization.Normalize(swapDirectionGraph);
                using (MainForm swapWindow = new MainForm(swapDirectionGraph, false))
                {
                    swapWindow.WindowState = FormWindowState.Normal; swapWindow.Size = new System.Drawing.Size(1200, 760); swapWindow.CreateControl(); swapWindow.PerformLayout();
                    swapWindow.CanvasForTesting.SelectEntity("edge", "swap_edge");
                    Require(swapWindow.SwapSelectedEdgeDirectionForTesting(), "关系方向一键交换操作失败");
                    Require(swapDirectionGraph.edges[0].source == "swap_b" && swapDirectionGraph.edges[0].target == "swap_a", "关系方向交换未更新起点和终点");
                    Require(swapWindow.CanvasForTesting.EdgeSourceColorForTesting(swapDirectionGraph.edges[0]).ToArgb() == System.Drawing.Color.FromArgb(75, 158, 234).ToArgb(), "关系方向交换后线色未跟随新起点");
                    swapWindow.UndoForTesting();
                    Require(swapWindow.CanvasForTesting.Document.edges[0].source == "swap_a" && swapWindow.CanvasForTesting.Document.edges[0].target == "swap_b", "关系方向交换无法撤销");
                    swapWindow.RedoForTesting();
                    Require(swapWindow.CanvasForTesting.Document.edges[0].source == "swap_b" && swapWindow.CanvasForTesting.Document.edges[0].target == "swap_a", "关系方向交换无法重做");
                }

                GraphDocument duplicateReverseGraph = GraphSerialization.Clone(swapDirectionGraph);
                duplicateReverseGraph.edges.Add(new GraphEdge { id = "opposite_edge", source = "swap_a", target = "swap_b", sourceType = "node", targetType = "node", label = "反向已存在", category = "core", lineType = "auto" });
                duplicateReverseGraph = GraphSerialization.Normalize(duplicateReverseGraph);
                using (MainForm duplicateReverseWindow = new MainForm(duplicateReverseGraph, false))
                {
                    duplicateReverseWindow.CanvasForTesting.SelectEntity("edge", "swap_edge");
                    Require(!duplicateReverseWindow.SwapSelectedEdgeDirectionForTesting(), "存在反向关系时仍错误交换方向");
                }

                GraphDocument maximumGraph = GraphSerialization.CreateBlank("maximum graph smoke test");
                for (int nodeIndex = 0; nodeIndex < GraphSerialization.MaxNodes; nodeIndex++)
                    maximumGraph.nodes.Add(new GraphNode { id = "max_node_" + nodeIndex, label = "N" + nodeIndex, type = "test", kind = "system", group = "", x = (nodeIndex % 50) * 220f, y = (nodeIndex / 50) * 110f, w = 150, h = 55 });
                for (int edgeIndex = 0; edgeIndex < GraphSerialization.MaxEdges; edgeIndex++)
                {
                    int sourceIndex = edgeIndex / 5, targetIndex = (sourceIndex + edgeIndex % 5 + 1) % GraphSerialization.MaxNodes;
                    maximumGraph.edges.Add(new GraphEdge { id = "max_edge_" + edgeIndex, source = "max_node_" + sourceIndex, target = "max_node_" + targetIndex, sourceType = "node", targetType = "node", lineType = edgeIndex % 3 == 0 ? "curve" : edgeIndex % 3 == 1 ? "straight" : "polyline", category = "core", label = "" });
                }
                maximumGraph = GraphSerialization.Normalize(maximumGraph);
                System.Diagnostics.Stopwatch maximumGraphTimer = System.Diagnostics.Stopwatch.StartNew();
                using (GraphCanvas maximumCanvas = new GraphCanvas())
                using (System.Drawing.Bitmap maximumFrame = new System.Drawing.Bitmap(900, 650))
                {
                    maximumCanvas.Size = new System.Drawing.Size(900, 650); maximumCanvas.Document = maximumGraph;
                    maximumCanvas.DrawToBitmap(maximumFrame, new System.Drawing.Rectangle(0, 0, maximumFrame.Width, maximumFrame.Height));
                }
                maximumGraphTimer.Stop();
                Require(maximumGraphTimer.Elapsed < TimeSpan.FromSeconds(15), "最大规模关系图首次绘制超过 15 秒");

                string folder = Path.GetDirectoryName(Path.GetFullPath(reportPath));
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                Require(Array.IndexOf(System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceNames(), "RelationshipGraphNative.Assets.app-icon.ico") >= 0, "程序图标未嵌入 EXE");
                Require(!MainForm.ShouldDeleteSelectionForTesting(Keys.Back, false), "Backspace 仍会删除节点");
                Require(!MainForm.ShouldDeleteSelectionForTesting(Keys.Delete, true), "文字输入时 Delete 仍会删除节点");
                Require(MainForm.ShouldDeleteSelectionForTesting(Keys.Delete, false), "画布 Delete 删除功能失效");
                File.WriteAllText(reportPath, "{\"ok\":true,\"version\":\"5.0.0\",\"groups\":4,\"nodes\":12,\"edges\":12,\"runtime\":\"native-winforms\"}", new UTF8Encoding(false));
                return 0;
            }
            catch (Exception error)
            {
                try
                {
                    string folder = Path.GetDirectoryName(Path.GetFullPath(reportPath));
                    if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                    string message = (error.Message ?? "unknown").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
                    File.WriteAllText(reportPath, "{\"ok\":false,\"error\":\"" + message + "\"}", new UTF8Encoding(false));
                }
                catch { }
                return 1;
            }
            finally
            {
                try { if (tempFolder.Length > 0 && Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true); }
                catch { }
            }
        }

        private static bool LayoutResultsMatch(GraphLayoutResult first, GraphLayoutResult second)
        {
            if (first == null || second == null || first.NodeBounds.Count != second.NodeBounds.Count || first.GroupBounds.Count != second.GroupBounds.Count || first.Edges.Count != second.Edges.Count) return false;
            foreach (KeyValuePair<string, RectangleF> pair in first.NodeBounds)
            {
                RectangleF other;
                if (!second.NodeBounds.TryGetValue(pair.Key, out other) || !RectanglesClose(pair.Value, other)) return false;
            }
            foreach (KeyValuePair<string, RectangleF> pair in first.GroupBounds)
            {
                RectangleF other;
                if (!second.GroupBounds.TryGetValue(pair.Key, out other) || !RectanglesClose(pair.Value, other)) return false;
            }
            foreach (KeyValuePair<string, GraphEdgeLayout> pair in first.Edges)
            {
                GraphEdgeLayout other;
                GraphEdgeLayout route = pair.Value;
                if (!second.Edges.TryGetValue(pair.Key, out other) || route.SourceSide != other.SourceSide || route.TargetSide != other.TargetSide ||
                    !PointsClose(route.SourcePoint, other.SourcePoint) || !PointsClose(route.TargetPoint, other.TargetPoint) ||
                    !RectanglesClose(route.LabelBounds, other.LabelBounds) || route.Points.Count != other.Points.Count) return false;
                for (int index = 0; index < route.Points.Count; index++) if (!PointsClose(route.Points[index], other.Points[index])) return false;
            }
            return true;
        }

        private static bool RectanglesClose(RectangleF first, RectangleF second)
        {
            return Math.Abs(first.X - second.X) < .01f && Math.Abs(first.Y - second.Y) < .01f && Math.Abs(first.Width - second.Width) < .01f && Math.Abs(first.Height - second.Height) < .01f;
        }

        private static bool PointsClose(PointF first, PointF second)
        {
            return Math.Abs(first.X - second.X) < .01f && Math.Abs(first.Y - second.Y) < .01f;
        }

        private static bool RectangleMatchesNode(RectangleF bounds, GraphNode node)
        {
            return bounds.X == node.x && bounds.Y == node.y && bounds.Width == node.w && bounds.Height == node.h;
        }

        private static bool RectangleMatchesGroup(RectangleF bounds, GraphGroup group)
        {
            return bounds.X == group.x && bounds.Y == group.y && bounds.Width == group.w && bounds.Height == group.h;
        }

        private static bool RouteIsOrthogonal(IList<PointF> points)
        {
            if (points == null || points.Count < 2) return false;
            for (int index = 1; index < points.Count; index++)
                if (Math.Abs(points[index - 1].X - points[index].X) >= .01f && Math.Abs(points[index - 1].Y - points[index].Y) >= .01f) return false;
            return true;
        }

        private static bool RouteCrossesRectangleInterior(IList<PointF> points, RectangleF bounds)
        {
            if (points == null) return false;
            for (int index = 1; index < points.Count; index++)
            {
                PointF first = points[index - 1], second = points[index];
                if (Math.Abs(first.Y - second.Y) < .01f)
                {
                    float y = first.Y, left = Math.Min(first.X, second.X), right = Math.Max(first.X, second.X);
                    if (y > bounds.Top + .01f && y < bounds.Bottom - .01f && right > bounds.Left + .01f && left < bounds.Right - .01f) return true;
                }
                else if (Math.Abs(first.X - second.X) < .01f)
                {
                    float x = first.X, top = Math.Min(first.Y, second.Y), bottom = Math.Max(first.Y, second.Y);
                    if (x > bounds.Left + .01f && x < bounds.Right - .01f && bottom > bounds.Top + .01f && top < bounds.Bottom - .01f) return true;
                }
                else return true;
            }
            return false;
        }

        private static bool FinitePositiveRectangle(RectangleF bounds)
        {
            return !Single.IsNaN(bounds.X) && !Single.IsInfinity(bounds.X) && !Single.IsNaN(bounds.Y) && !Single.IsInfinity(bounds.Y) &&
                !Single.IsNaN(bounds.Width) && !Single.IsInfinity(bounds.Width) && !Single.IsNaN(bounds.Height) && !Single.IsInfinity(bounds.Height) && bounds.Width > 0 && bounds.Height > 0;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static bool IconsMatch(System.Drawing.Icon first, System.Drawing.Icon second)
        {
            if (first == null || second == null) return false;
            using (System.Drawing.Bitmap a = first.ToBitmap())
            using (System.Drawing.Bitmap b = second.ToBitmap())
            {
                if (a.Width != b.Width || a.Height != b.Height) return false;
                for (int y = 0; y < a.Height; y++) for (int x = 0; x < a.Width; x++)
                    if (a.GetPixel(x, y).ToArgb() != b.GetPixel(x, y).ToArgb()) return false;
                return true;
            }
        }
    }
}
