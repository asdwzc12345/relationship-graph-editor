using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace RelationshipGraphNative
{
    public static class NativeExport
    {
        public static void SaveJson(GraphDocument graph, string fileName)
        {
            NativePersistence.WriteAllTextAtomic(fileName, GraphSerialization.Serialize(graph, true), new UTF8Encoding(false), true);
        }

        public static void SavePng(GraphCanvas canvas, string fileName)
        {
            using (Bitmap bitmap = canvas.ExportBitmap(8192))
                NativePersistence.WriteStreamAtomic(fileName, false, delegate(Stream stream) { bitmap.Save(stream, ImageFormat.Png); });
        }

        public static void SavePng(GraphDocument graph, bool darkTheme, string fileName)
        {
            using (GraphCanvas canvas = new GraphCanvas())
            {
                canvas.Size = new Size(1200, 800);
                canvas.Document = GraphSerialization.CreateImmutableSnapshot(graph);
                canvas.DarkTheme = darkTheme;
                canvas.SetAutomaticRouting(GraphLayout.CalculateRoutes(canvas.Document, GraphLayoutOptions.ForDocument(canvas.Document)));
                SavePng(canvas, fileName);
            }
        }

        public static void SaveSvg(GraphDocument graph, string fileName)
        {
            NativePersistence.WriteAllTextAtomic(fileName, BuildSvg(graph), new UTF8Encoding(false), false);
        }

        public static void SaveDrawio(GraphDocument graph, string fileName)
        {
            NativePersistence.WriteAllTextAtomic(fileName, BuildDrawio(graph), new UTF8Encoding(false), false);
        }

        public static void SaveReadonlyHtml(GraphDocument graph, string fileName)
        {
            NativePersistence.WriteAllTextAtomic(fileName, BuildReadonlyHtml(graph), new UTF8Encoding(false), false);
        }

        public static string BuildReadonlyHtml(GraphDocument graph)
        {
            string title = Xml(graph.meta.title);
            string graphJson = GraphSerialization.Serialize(graph, false).Replace("<", "\\u003c");
            return @"<!doctype html>
<html lang='zh-CN'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width,initial-scale=1'>
<title>" + title + @"（只读）</title>
<style>
html,body{margin:0;width:100%;height:100%;overflow:hidden;background:#eef2f6;color:#17202b;font-family:'Microsoft YaHei UI','Segoe UI',sans-serif}
*{box-sizing:border-box}.head{height:62px;display:flex;align-items:center;gap:18px;padding:10px 18px;background:#fff;border-bottom:1px solid #d9e0e7;box-shadow:0 2px 12px #24344b12;position:relative;z-index:2}
.head h1{font-size:17px;margin:0;max-width:32vw;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.badge{font-size:12px;color:#637083;white-space:nowrap}.toolbar{margin-left:auto;display:flex;align-items:center;gap:8px}.control,button,select{height:34px;border:1px solid #cfd8e2;border-radius:7px;background:#fff;color:#263548;font:13px inherit}button,select{padding:0 11px}button{cursor:pointer}button:hover{background:#f2f6fb;border-color:#9fb1c5}.control{display:flex;align-items:center;gap:6px;padding:0 10px;cursor:pointer}.control input{margin:0}.zoom-value{width:56px;text-align:center;color:#526277;font-size:12px}
.stage{position:relative;width:100%;height:calc(100vh - 62px);overflow:hidden;cursor:default;touch-action:none;background:linear-gradient(90deg,#e7edf3 1px,transparent 1px),linear-gradient(#e7edf3 1px,transparent 1px),#f7f9fb;background-size:24px 24px}.stage.panning{cursor:crosshair}.sheet{position:absolute;inset:0}.sheet svg{display:block;width:100%;height:100%;max-width:none;user-select:none}.sheet [data-type]{transition:opacity .12s ease}.sheet [data-type='node'],.sheet [data-type='group'],.sheet [data-type='edge']{cursor:pointer}.sheet .is-dim{opacity:.12}.sheet .is-selected rect{stroke:#4c8bf5!important;stroke-width:4!important}.sheet [data-type='edge'].is-selected path{stroke-width:4!important}.sheet .is-same rect{stroke:#4c8bf5!important;stroke-width:3.4!important}.sheet .is-related rect{stroke:#4abe7e!important;stroke-width:3!important}.sheet [data-type='edge'].is-related path{stroke-width:3.2!important}.lines-hidden .sheet [data-type='edge']{display:none}.hint{position:absolute;left:16px;bottom:14px;padding:7px 10px;border-radius:7px;background:#fffde6dd;color:#58616d;font-size:12px;box-shadow:0 2px 10px #24344b18;pointer-events:none}
.theme-dark{background:#191f26;color:#e5ebf1;color-scheme:dark}.theme-dark .head{background:#20272f;border-color:#34414d;box-shadow:0 2px 12px #0005}.theme-dark .badge,.theme-dark .zoom-value{color:#aebbc8}.theme-dark .control,.theme-dark button,.theme-dark select{background:#29323b;border-color:#465563;color:#e5ebf1}.theme-dark button:hover{background:#34404b;border-color:#617487}.theme-dark .stage{background:linear-gradient(90deg,#28323c 1px,transparent 1px),linear-gradient(#28323c 1px,transparent 1px),#191f26;background-size:24px 24px}.theme-dark .sheet svg>rect:first-child{fill:#191f26!important}.theme-dark .sheet [data-type='group'] rect{fill:#303943!important;stroke:#657482!important}.theme-dark .sheet [data-type='group'] text{fill:#d7e0e9!important}.theme-dark .sheet [data-type='node'] rect{fill:#354251!important;stroke:#7d8c9a!important}.theme-dark .sheet [data-type='node'][data-kind='resource'] rect{fill:#2d4c64!important}.theme-dark .sheet [data-type='node'][data-kind='output'] rect{fill:#2b523b!important}.theme-dark .sheet [data-type='node'][data-kind='content'] rect{fill:#58354f!important}.theme-dark .sheet [data-type='node'][data-kind='staff'] rect{fill:#463a5e!important}.theme-dark .sheet [data-type='node'][data-kind='commercial'] rect{fill:#5e482e!important}.theme-dark .sheet [data-type='node'] text{fill:#f2f5f8!important}.theme-dark .sheet [data-type='node'] text:first-of-type{fill:#bec9d3!important}.theme-dark .sheet [data-type='edge'] rect{fill:#191f26!important;stroke:#495a6b!important}.theme-dark .sheet [data-type='edge'] text{fill:#dee6ee!important}.theme-dark .hint{background:#27313bdd;color:#c5d0db;box-shadow:0 2px 10px #0005}
</style>
</head>
<body>
<header class='head'><h1>" + title + @"</h1><span class='badge'>只读可视图 · 可重新导入关系图编辑器</span><div class='toolbar'>
<label class='control'><input id='toggleLines' type='checkbox' checked>显示连线</label>
<select id='theme' title='界面主题'><option value='system'>跟随系统</option><option value='light'>浅色</option><option value='dark'>深色</option></select>
<select id='direction' title='关系方向'><option value='all'>全部</option><option value='upstream'>产出</option><option value='downstream'>消耗</option></select>
<select id='depth' title='关系层数'><option value='1'>1 层</option><option value='2'>2 层</option><option value='3'>3 层</option></select>
<button id='zoomOut' title='缩小'>－</button><span id='zoomValue' class='zoom-value'>100%</span><button id='zoomIn' title='放大'>＋</button><button id='fit' title='适合窗口'>适合窗口</button><button id='clear' title='清除高亮'>清除高亮</button>
</div></header>
<main id='stage' class='stage'><div class='sheet'>" + BuildSvg(graph) + @"</div><div class='hint'>左键点击查看关系 · 右键单击清除 · 右键拖动画布 · 滚轮缩放</div></main>
<script id=""graphData"" type=""application/json"">" + graphJson + @"</script>
<script>
(()=>{
'use strict';
const data=JSON.parse(document.getElementById('graphData').textContent);
const stage=document.getElementById('stage');
const svg=stage.querySelector('svg');
const theme=document.getElementById('theme');
const direction=document.getElementById('direction');
const depth=document.getElementById('depth');
const zoomValue=document.getElementById('zoomValue');
svg.removeAttribute('width');svg.removeAttribute('height');
const initial={x:svg.viewBox.baseVal.x,y:svg.viewBox.baseVal.y,w:svg.viewBox.baseVal.width,h:svg.viewBox.baseVal.height};
const view={x:initial.x,y:initial.y,w:initial.w,h:initial.h};
const key=(type,id)=>(type||'node')+':'+id;
const elements=[...svg.querySelectorAll('[data-type][data-id]')];
const elementByKey=new Map(elements.map(el=>[key(el.dataset.type,el.dataset.id),el]));
const nodes=new Map((data.nodes||[]).map(node=>[key('node',node.id),node]));
const edges=data.edges||[];
let selected='';let suppressClick=false;
function edgeEnds(edge){return [key(edge.sourceType,edge.source),key(edge.targetType,edge.target)];}
function setView(){svg.setAttribute('viewBox',[view.x,view.y,view.w,view.h].join(' '));zoomValue.textContent=Math.round(initial.w/view.w*100)+'%';}
function fit(){Object.assign(view,initial);setView();}
function zoomAt(factor,clientX,clientY){
 const rect=stage.getBoundingClientRect();const rx=(clientX-rect.left)/Math.max(1,rect.width),ry=(clientY-rect.top)/Math.max(1,rect.height);
 const nextW=Math.max(initial.w/20,Math.min(initial.w*20,view.w*factor));const actual=nextW/view.w;const nextH=view.h*actual;
 view.x+=rx*(view.w-nextW);view.y+=ry*(view.h-nextH);view.w=nextW;view.h=nextH;setView();
}
function clearClasses(){elements.forEach(el=>el.classList.remove('is-selected','is-same','is-related','is-dim'));}
const systemDark=window.matchMedia('(prefers-color-scheme: dark)');
function applyTheme(){const dark=theme.value==='dark'||(theme.value==='system'&&systemDark.matches);document.body.classList.toggle('theme-dark',dark);document.documentElement.style.colorScheme=dark?'dark':'light';}
function hasDirectionalLink(root,mode){
 if(mode==='all')return true;
 return edges.some(edge=>{const ends=edgeEnds(edge);if(ends[0]===ends[1])return false;return mode==='downstream'?ends[0]===root:ends[1]===root;});
}
function applyFocus(){
 clearClasses();if(!selected)return;
 const selectedEl=elementByKey.get(selected);if(!selectedEl)return;
 const selectedType=selectedEl.dataset.type;
 if(selectedType==='edge'){
  selectedEl.classList.add('is-selected');const edge=edges.find(item=>item.id===selectedEl.dataset.id);
  const related=edge?edgeEnds(edge):[];elements.forEach(el=>{const itemKey=key(el.dataset.type,el.dataset.id);if(el!==selectedEl){if(related.includes(itemKey))el.classList.add('is-related');else el.classList.add('is-dim');}});return;
 }
 let roots=[selected];
 if(selectedType==='node'){
  const selectedNode=nodes.get(selected);if(selectedNode)roots=[...nodes.entries()].filter(pair=>pair[1].label===selectedNode.label).map(pair=>pair[0]);
 }
 const mode=direction.value;const maxDepth=Number(depth.value)||1;const adjacency=new Map();
 const add=(from,to,edgeId)=>{if(!adjacency.has(from))adjacency.set(from,[]);adjacency.get(from).push({to,edgeId});};
 edges.forEach(edge=>{const ends=edgeEnds(edge);if(mode==='all'||mode==='downstream')add(ends[0],ends[1],edge.id);if(mode==='all'||mode==='upstream')add(ends[1],ends[0],edge.id);});
 const entities=new Set(roots),relatedEdges=new Set(),queue=roots.map(root=>({key:root,level:0})),seen=new Map(roots.map(root=>[root,0]));
 while(queue.length){const current=queue.shift();if(current.level>=maxDepth)continue;(adjacency.get(current.key)||[]).forEach(next=>{entities.add(next.to);relatedEdges.add(next.edgeId);const level=current.level+1;if(!seen.has(next.to)||seen.get(next.to)>level){seen.set(next.to,level);queue.push({key:next.to,level});}});}
 const inactiveRoots=new Set(roots.filter(root=>!hasDirectionalLink(root,mode)));
 elements.forEach(el=>{
  const itemKey=key(el.dataset.type,el.dataset.id);
  if(el.dataset.type==='edge'){
   if(relatedEdges.has(el.dataset.id))el.classList.add('is-related');else el.classList.add('is-dim');return;
  }
  if(itemKey===selected)el.classList.add('is-selected');
  if(roots.includes(itemKey)){
   if(inactiveRoots.has(itemKey))el.classList.add('is-dim');else if(itemKey!==selected)el.classList.add('is-same');
  }else if(entities.has(itemKey))el.classList.add('is-related');else el.classList.add('is-dim');
 });
}
document.getElementById('toggleLines').addEventListener('change',event=>document.body.classList.toggle('lines-hidden',!event.target.checked));
theme.addEventListener('change',applyTheme);if(systemDark.addEventListener)systemDark.addEventListener('change',()=>{if(theme.value==='system')applyTheme();});
direction.addEventListener('change',applyFocus);depth.addEventListener('change',applyFocus);
document.getElementById('clear').addEventListener('click',()=>{selected='';clearClasses();});
document.getElementById('fit').addEventListener('click',fit);
document.getElementById('zoomIn').addEventListener('click',()=>{const r=stage.getBoundingClientRect();zoomAt(.8,r.left+r.width/2,r.top+r.height/2);});
document.getElementById('zoomOut').addEventListener('click',()=>{const r=stage.getBoundingClientRect();zoomAt(1.25,r.left+r.width/2,r.top+r.height/2);});
stage.addEventListener('wheel',event=>{event.preventDefault();zoomAt(event.deltaY < 0 ? .88 : 1.14,event.clientX,event.clientY);},{passive:false});
let drag=null;
stage.addEventListener('pointerdown',event=>{if(event.button!==2)return;event.preventDefault();drag={x:event.clientX,y:event.clientY,lastX:event.clientX,lastY:event.clientY,moved:false};stage.setPointerCapture(event.pointerId);stage.classList.add('panning');});
stage.addEventListener('pointermove',event=>{if(!drag)return;const total=Math.hypot(event.clientX-drag.x,event.clientY-drag.y);if(total>3)drag.moved=true;if(drag.moved){const rect=stage.getBoundingClientRect();view.x-=(event.clientX-drag.lastX)*view.w/Math.max(1,rect.width);view.y-=(event.clientY-drag.lastY)*view.h/Math.max(1,rect.height);setView();}drag.lastX=event.clientX;drag.lastY=event.clientY;});
function finishDrag(event){if(!drag)return;const moved=drag.moved;suppressClick=moved;drag=null;stage.classList.remove('panning');if(stage.hasPointerCapture(event.pointerId))stage.releasePointerCapture(event.pointerId);if(!moved){selected='';clearClasses();}setTimeout(()=>suppressClick=false,0);}
stage.addEventListener('pointerup',finishDrag);stage.addEventListener('pointercancel',finishDrag);
stage.addEventListener('click',event=>{if(suppressClick)return;const entity=event.target.closest('[data-type][data-id]');if(!entity){selected='';clearClasses();return;}selected=key(entity.dataset.type,entity.dataset.id);applyFocus();});
document.addEventListener('keydown',event=>{if(event.target.matches('select,input,button'))return;const r=stage.getBoundingClientRect();if(event.key==='Escape'){selected='';clearClasses();}else if(event.key==='0')fit();else if(event.key==='+'||event.key==='=')zoomAt(.8,r.left+r.width/2,r.top+r.height/2);else if(event.key==='-')zoomAt(1.25,r.left+r.width/2,r.top+r.height/2);});
document.addEventListener('contextmenu',event=>event.preventDefault());
applyTheme();setView();
})();
</script>
</body>
</html>";
        }

        public static void SavePdf(GraphCanvas canvas, string fileName)
        {
            if (canvas == null || canvas.Document == null) throw new InvalidOperationException("没有可导出的关系图。");
            NativePersistence.WriteStreamAtomic(fileName, false, delegate(Stream stream) { NativePdfExport.Write(canvas.Document, stream); });
        }

        public static void SavePdf(GraphDocument graph, string fileName)
        {
            if (graph == null) throw new InvalidOperationException("没有可导出的关系图。");
            NativePersistence.WriteStreamAtomic(fileName, false, delegate(Stream stream) { NativePdfExport.Write(graph, stream); });
        }

        public static string BuildDrawio(GraphDocument graph)
        {
            graph = GraphSerialization.Normalize(GraphSerialization.Clone(graph));
            Dictionary<string, GraphNode> nodes = graph.nodes.ToDictionary(delegate(GraphNode item) { return item.id; }, StringComparer.Ordinal);
            Dictionary<string, GraphGroup> groups = graph.groups.ToDictionary(delegate(GraphGroup item) { return item.id; }, StringComparer.Ordinal);
            RectangleF bounds = ExportBounds(graph, nodes, groups);
            float shiftX = 60f - bounds.Left, shiftY = 60f - bounds.Top;
            StringBuilder xml = new StringBuilder(32768);
            xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<mxfile host=\"app.diagrams.net\" modified=\"").Append(Xml(DateTime.UtcNow.ToString("o")))
                .Append("\" agent=\"Relationship Graph Editor\" version=\"24.7.17\" type=\"device\"><diagram id=\"relationship-graph\" name=\"").Append(Xml(graph.meta.title)).Append("\">");
            xml.Append("<mxGraphModel dx=\"1200\" dy=\"800\" grid=\"1\" gridSize=\"10\" guides=\"1\" tooltips=\"1\" connect=\"1\" arrows=\"1\" fold=\"1\" page=\"0\" pageScale=\"1\" pageWidth=\"1169\" pageHeight=\"827\" math=\"0\" shadow=\"0\" rgFormat=\"relationship-graph-v1\" rgShiftX=\"").Append(Number(shiftX))
                .Append("\" rgShiftY=\"").Append(Number(shiftY)).Append("\" rgCanvasWidth=\"").Append(Number(graph.meta.canvasWidth))
                .Append("\" rgCanvasHeight=\"").Append(Number(graph.meta.canvasHeight)).Append("\" rgDiagramType=\"").Append(Xml(graph.meta.diagramType)).Append("\"><root><mxCell id=\"0\"/><mxCell id=\"1\" parent=\"0\"/>");

            foreach (GraphGroup group in graph.groups.OrderByDescending(delegate(GraphGroup item) { return item.w * item.h; }))
            {
                GraphGroup parentGroup = null;
                string parentGroupId = group.groups == null ? "" : group.groups.LastOrDefault() ?? "";
                bool nested = !String.IsNullOrEmpty(parentGroupId) && groups.TryGetValue(parentGroupId, out parentGroup);
                string parent = nested ? MxId("group", parentGroup.id) : "1";
                float x = nested ? group.x - parentGroup.x : group.x + shiftX;
                float y = nested ? group.y - parentGroup.y : group.y + shiftY;
                xml.Append("<mxCell id=\"").Append(Xml(MxId("group", group.id))).Append("\" rgEntity=\"group\" rgId=\"").Append(Xml(group.id)).Append("\" value=\"").Append(Xml(group.label))
                    .Append("\" style=\"swimlane;horizontal=1;startSize=32;rounded=1;html=1;whiteSpace=wrap;container=1;collapsible=0;recursiveResize=0;fillColor=#f4f7fa;swimlaneFillColor=#ffffff;strokeColor=#9ba6b2;dashed=1;dashPattern=7 5;fontColor=#2d3946;fontStyle=1;locked=0;\" vertex=\"1\" connectable=\"1\" parent=\"").Append(Xml(parent)).Append("\"><mxGeometry x=\"")
                    .Append(Number(x)).Append("\" y=\"").Append(Number(y)).Append("\" width=\"").Append(Number(group.w)).Append("\" height=\"").Append(Number(group.h)).Append("\" as=\"geometry\"/></mxCell>");
            }

            foreach (GraphNode node in graph.nodes)
            {
                GraphGroup parentGroup = null;
                bool grouped = !String.IsNullOrEmpty(node.group) && groups.TryGetValue(node.group, out parentGroup);
                string parent = grouped ? MxId("group", parentGroup.id) : "1";
                float x = grouped ? node.x - parentGroup.x : node.x + shiftX;
                float y = grouped ? node.y - parentGroup.y : node.y + shiftY;
                string value = "<font style=\"font-size:10px;color:#425064\">" + Xml(node.type) + "</font><br><b>" + Xml(node.label) + "</b>";
                xml.Append("<mxCell id=\"").Append(Xml(MxId("node", node.id))).Append("\" rgEntity=\"node\" rgId=\"").Append(Xml(node.id))
                    .Append("\" rgType=\"").Append(Xml(node.type)).Append("\" rgKind=\"").Append(Xml(node.kind)).Append("\" rgShape=\"").Append(Xml(node.shape))
                    .Append("\" rgNote=\"").Append(Xml(node.note)).Append("\" value=\"").Append(Xml(value))
                    .Append("\" style=\"").Append(DrawioNodeShapeStyle(node.shape)).Append("whiteSpace=wrap;html=1;align=center;verticalAlign=middle;spacing=6;fillColor=").Append(NodeColor(node.kind))
                    .Append(";strokeColor=#697785;strokeWidth=1.4;fontColor=#17202b;locked=0;\" vertex=\"1\" connectable=\"1\" parent=\"").Append(Xml(parent)).Append("\"><mxGeometry x=\"")
                    .Append(Number(x)).Append("\" y=\"").Append(Number(y)).Append("\" width=\"").Append(Number(node.w)).Append("\" height=\"").Append(Number(node.h)).Append("\" as=\"geometry\"/></mxCell>");
            }

            foreach (GraphEdge edge in graph.edges)
            {
                RectangleF sourceRect, targetRect;
                if (!TryRect(edge.sourceType, edge.source, nodes, groups, out sourceRect) || !TryRect(edge.targetType, edge.target, nodes, groups, out targetRect)) continue;
                string sourceSide = String.IsNullOrEmpty(edge.sourceSide) ? ConnectionSide(sourceRect, targetRect) : edge.sourceSide;
                string targetSide = String.IsNullOrEmpty(edge.targetSide) ? ConnectionSide(targetRect, sourceRect) : edge.targetSide;
                string lineStyle = edge.lineType == "straight" ? "edgeStyle=none;noEdgeStyle=1;" : edge.lineType == "polyline" || edge.lineType == "auto" ? "edgeStyle=orthogonalEdgeStyle;rounded=0;orthogonalLoop=1;jettySize=auto;" : "edgeStyle=none;curved=1;rounded=1;";
                xml.Append("<mxCell id=\"").Append(Xml(MxId("edge", edge.id))).Append("\" rgEntity=\"edge\" rgId=\"").Append(Xml(edge.id))
                    .Append("\" rgCategory=\"").Append(Xml(edge.category)).Append("\" rgLineType=\"").Append(Xml(edge.lineType))
                    .Append("\" rgSourceSide=\"").Append(Xml(sourceSide)).Append("\" rgTargetSide=\"").Append(Xml(targetSide)).Append("\" value=\"").Append(Xml(edge.label))
                    .Append("\" style=\"").Append(lineStyle).Append("html=1;endArrow=block;endFill=1;strokeWidth=2;strokeColor=").Append(SourceAccentColor(edge, nodes)).Append(';')
                    .Append(DrawioPortStyle("exit", sourceSide)).Append(DrawioPortStyle("entry", targetSide)).Append("locked=0;\" edge=\"1\" parent=\"1\" source=\"")
                    .Append(Xml(MxId(edge.sourceType, edge.source))).Append("\" target=\"").Append(Xml(MxId(edge.targetType, edge.target))).Append("\"><mxGeometry relative=\"1\" as=\"geometry\"/></mxCell>");
            }
            xml.Append("</root></mxGraphModel></diagram></mxfile>");
            return xml.ToString();
        }

        private static string MxId(string type, string id)
        {
            return (type == "group" ? "g_" : type == "edge" ? "e_" : "n_") + (id ?? "item");
        }

        private static string DrawioPortStyle(string prefix, string side)
        {
            float x = .5f, y = .5f;
            if (side == "top") y = 0; else if (side == "bottom") y = 1; else if (side == "left") x = 0; else x = 1;
            return prefix + "X=" + Number(x) + ";" + prefix + "Y=" + Number(y) + ";" + prefix + "Dx=0;" + prefix + "Dy=0;" + prefix + "Perimeter=1;";
        }

        private static string DrawioNodeShapeStyle(string shape)
        {
            switch (GraphSerialization.NormalizeNodeShape(shape))
            {
                case "terminator": return "rounded=1;arcSize=50;";
                case "decision": return "rhombus;";
                case "data": return "shape=parallelogram;perimeter=parallelogramPerimeter;fixedSize=1;";
                case "document": return "shape=document;boundedLbl=1;";
                default: return "rounded=1;arcSize=16;";
            }
        }

        public static string BuildSvg(GraphDocument graph)
        {
            graph = GraphSerialization.Normalize(GraphSerialization.Clone(graph));
            Dictionary<string, GraphNode> nodes = graph.nodes.ToDictionary(delegate(GraphNode item) { return item.id; }, StringComparer.Ordinal);
            Dictionary<string, GraphGroup> groups = graph.groups.ToDictionary(delegate(GraphGroup item) { return item.id; }, StringComparer.Ordinal);
            RectangleF bounds = ExportBounds(graph, nodes, groups);
            GraphLayoutResult routing = GraphLayout.CalculateRoutes(graph, GraphLayoutOptions.ForDocument(graph));
            if (routing != null && routing.ContentBounds.Width > 0 && routing.ContentBounds.Height > 0)
            {
                RectangleF routedBounds = routing.ContentBounds; routedBounds.Inflate(36f, 36f);
                bounds = RectangleF.Union(bounds, routedBounds);
            }
            StringBuilder svg = new StringBuilder(32768);
            svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(Number(bounds.Width)).Append("\" height=\"").Append(Number(bounds.Height))
                .Append("\" viewBox=\"").Append(Number(bounds.X)).Append(' ').Append(Number(bounds.Y)).Append(' ').Append(Number(bounds.Width)).Append(' ').Append(Number(bounds.Height)).Append("\" role=\"img\" aria-label=\"").Append(Xml(graph.meta.title)).Append("\">");
            svg.Append("<rect x=\"").Append(Number(bounds.X)).Append("\" y=\"").Append(Number(bounds.Y)).Append("\" width=\"").Append(Number(bounds.Width)).Append("\" height=\"").Append(Number(bounds.Height)).Append("\" fill=\"#fff\"/>");
            Dictionary<string, string> markerIds = new Dictionary<string, string>(StringComparer.Ordinal);
            svg.Append("<defs>");
            for (int i = 0; i < graph.edges.Count; i++)
            {
                GraphEdge edge = graph.edges[i]; string markerId = "arrow-edge-" + i; markerIds[edge.id] = markerId;
                string color = SourceAccentColor(edge, nodes);
                svg.Append("<marker id=\"").Append(markerId).Append("\" viewBox=\"0 0 10 10\" refX=\"9\" refY=\"5\" markerWidth=\"7\" markerHeight=\"7\" orient=\"auto-start-reverse\"><path d=\"M 0 0 L 10 5 L 0 10 z\" fill=\"").Append(color).Append("\"/></marker>");
            }
            svg.Append("</defs>");

            foreach (GraphGroup group in graph.groups)
            {
                svg.Append("<g data-type=\"group\" data-id=\"").Append(Xml(group.id)).Append("\"><rect x=\"").Append(Number(group.x)).Append("\" y=\"").Append(Number(group.y)).Append("\" width=\"").Append(Number(group.w)).Append("\" height=\"").Append(Number(group.h)).Append("\" rx=\"12\" fill=\"#d7dee633\" stroke=\"#9ba6b2\" stroke-dasharray=\"7 5\"/>");
                svg.Append("<text x=\"").Append(Number(group.x + 12)).Append("\" y=\"").Append(Number(group.y + 24)).Append("\" font-size=\"14\" font-weight=\"600\" fill=\"#2d3946\">").Append(Xml(group.label)).Append("</text></g>");
            }

            foreach (GraphEdge edge in graph.edges)
            {
                RectangleF sourceRect, targetRect;
                if (!TryRect(edge.sourceType, edge.source, nodes, groups, out sourceRect) || !TryRect(edge.targetType, edge.target, nodes, groups, out targetRect)) continue;
                string sourceSide = String.IsNullOrEmpty(edge.sourceSide) ? ConnectionSide(sourceRect, targetRect) : edge.sourceSide;
                string targetSide = String.IsNullOrEmpty(edge.targetSide) ? ConnectionSide(targetRect, sourceRect) : edge.targetSide;
                PointF source = Port(sourceRect, sourceSide), target = Port(targetRect, targetSide);
                string color = SourceAccentColor(edge, nodes), markerId;
                if (!markerIds.TryGetValue(edge.id, out markerId)) markerId = "arrow-edge-0";
                GraphEdgeLayout routedEdge = null;
                bool hasRoute = edge.lineType == "auto" && routing != null && routing.TryGetEdge(edge.id, out routedEdge) && routedEdge != null && routedEdge.Points.Count >= 2;
                string path = hasRoute ? SvgPolylinePath(routedEdge.Points) : EdgePath(edge.lineType, sourceSide, targetSide, source, target);
                svg.Append("<g data-type=\"edge\" data-id=\"").Append(Xml(edge.id)).Append("\"><path d=\"").Append(path).Append("\" fill=\"none\" stroke=\"").Append(color).Append("\" stroke-width=\"2\" stroke-linecap=\"round\" marker-end=\"url(#").Append(markerId).Append(")\"/>");
                if (!String.IsNullOrWhiteSpace(edge.label))
                {
                    PointF labelPoint = hasRoute ? routedEdge.LabelPoint : EdgePathMidpoint(edge.lineType, sourceSide, targetSide, source, target);
                    RectangleF labelBox = hasRoute && !routedEdge.LabelBounds.IsEmpty
                        ? routedEdge.LabelBounds
                        : new RectangleF(labelPoint.X - Math.Max(44, edge.label.Length * 14 + 14) / 2f, labelPoint.Y - 13, Math.Max(44, edge.label.Length * 14 + 14), 26f);
                    svg.Append("<rect x=\"").Append(Number(labelBox.X)).Append("\" y=\"").Append(Number(labelBox.Y)).Append("\" width=\"").Append(Number(labelBox.Width)).Append("\" height=\"").Append(Number(labelBox.Height)).Append("\" rx=\"7\" fill=\"#fff\" stroke=\"#d9e0e7\"/><text x=\"").Append(Number(labelBox.X + labelBox.Width / 2f)).Append("\" y=\"").Append(Number(labelBox.Y + labelBox.Height / 2f + 5)).Append("\" text-anchor=\"middle\" font-size=\"12\" fill=\"#384657\">").Append(Xml(edge.label)).Append("</text>");
                }
                svg.Append("</g>");
            }

            foreach (GraphNode node in graph.nodes)
            {
                string color = NodeColor(node.kind);
                bool flowchart = graph.meta != null && graph.meta.diagramType == "flowchart";
                svg.Append("<g data-type=\"node\" data-id=\"").Append(Xml(node.id)).Append("\" data-kind=\"").Append(Xml(node.kind)).Append("\" data-shape=\"").Append(Xml(node.shape)).Append("\">");
                AppendSvgNodeShape(svg, node, color);
                if (!flowchart) svg.Append("<text x=\"").Append(Number(node.x + 9)).Append("\" y=\"").Append(Number(node.y + 17)).Append("\" font-size=\"10\" fill=\"#425064\">").Append(Xml(node.type)).Append("</text>");
                svg.Append("<text x=\"").Append(Number(node.x + node.w / 2)).Append("\" y=\"").Append(Number(node.y + node.h / 2 + (flowchart ? 5 : 10))).Append("\" text-anchor=\"middle\" font-size=\"14\" font-weight=\"600\" fill=\"#17202b\">").Append(Xml(node.label)).Append("</text></g>");
            }
            svg.Append("</svg>");
            return svg.ToString();
        }

        private static string SvgPolylinePath(IList<PointF> points)
        {
            if (points == null || points.Count < 2) return "";
            StringBuilder path = new StringBuilder();
            path.Append("M ").Append(Number(points[0].X)).Append(' ').Append(Number(points[0].Y));
            for (int index = 1; index < points.Count; index++) path.Append(" L ").Append(Number(points[index].X)).Append(' ').Append(Number(points[index].Y));
            return path.ToString();
        }

        private static void AppendSvgNodeShape(StringBuilder svg, GraphNode node, string color)
        {
            string style = " fill=\"" + color + "\" stroke=\"#697785\" stroke-width=\"1.4\"";
            string shape = GraphSerialization.NormalizeNodeShape(node.shape);
            if (shape == "decision")
            {
                svg.Append("<polygon points=\"").Append(Number(node.x + node.w / 2)).Append(',').Append(Number(node.y)).Append(' ')
                    .Append(Number(node.x + node.w)).Append(',').Append(Number(node.y + node.h / 2)).Append(' ')
                    .Append(Number(node.x + node.w / 2)).Append(',').Append(Number(node.y + node.h)).Append(' ')
                    .Append(Number(node.x)).Append(',').Append(Number(node.y + node.h / 2)).Append("\"").Append(style).Append("/>");
            }
            else if (shape == "data")
            {
                float inset = Math.Min(18f, node.w / 6f);
                svg.Append("<polygon points=\"").Append(Number(node.x + inset)).Append(',').Append(Number(node.y)).Append(' ')
                    .Append(Number(node.x + node.w)).Append(',').Append(Number(node.y)).Append(' ')
                    .Append(Number(node.x + node.w - inset)).Append(',').Append(Number(node.y + node.h)).Append(' ')
                    .Append(Number(node.x)).Append(',').Append(Number(node.y + node.h)).Append("\"").Append(style).Append("/>");
            }
            else if (shape == "document")
            {
                svg.Append("<path d=\"M ").Append(Number(node.x)).Append(' ').Append(Number(node.y)).Append(" L ").Append(Number(node.x + node.w)).Append(' ').Append(Number(node.y))
                    .Append(" L ").Append(Number(node.x + node.w)).Append(' ').Append(Number(node.y + node.h - 8)).Append(" L ").Append(Number(node.x + node.w * .75f)).Append(' ').Append(Number(node.y + node.h))
                    .Append(" L ").Append(Number(node.x + node.w * .25f)).Append(' ').Append(Number(node.y + node.h - 8)).Append(" L ").Append(Number(node.x)).Append(' ').Append(Number(node.y + node.h)).Append(" Z\"").Append(style).Append("/>");
            }
            else
            {
                float radius = shape == "terminator" ? node.h / 2f : 9f;
                svg.Append("<rect x=\"").Append(Number(node.x)).Append("\" y=\"").Append(Number(node.y)).Append("\" width=\"").Append(Number(node.w)).Append("\" height=\"").Append(Number(node.h)).Append("\" rx=\"").Append(Number(radius)).Append("\"").Append(style).Append("/>");
            }
        }

        private static RectangleF ExportBounds(GraphDocument graph, Dictionary<string, GraphNode> nodes, Dictionary<string, GraphGroup> groups)
        {
            bool hasContent = false;
            float minX = 0, minY = 0, maxX = 0, maxY = 0;
            foreach (GraphGroup group in graph.groups) IncludeRect(ref hasContent, ref minX, ref minY, ref maxX, ref maxY, new RectangleF(group.x, group.y, group.w, group.h));
            foreach (GraphNode node in graph.nodes) IncludeRect(ref hasContent, ref minX, ref minY, ref maxX, ref maxY, new RectangleF(node.x, node.y, node.w, node.h));
            foreach (GraphEdge edge in graph.edges)
            {
                RectangleF sourceRect, targetRect;
                if (!TryRect(edge.sourceType, edge.source, nodes, groups, out sourceRect) || !TryRect(edge.targetType, edge.target, nodes, groups, out targetRect)) continue;
                string sourceSide = String.IsNullOrEmpty(edge.sourceSide) ? ConnectionSide(sourceRect, targetRect) : edge.sourceSide;
                string targetSide = String.IsNullOrEmpty(edge.targetSide) ? ConnectionSide(targetRect, sourceRect) : edge.targetSide;
                PointF source = Port(sourceRect, sourceSide), target = Port(targetRect, targetSide);
                IncludePoint(ref hasContent, ref minX, ref minY, ref maxX, ref maxY, source);
                IncludePoint(ref hasContent, ref minX, ref minY, ref maxX, ref maxY, target);
                if (edge.lineType == "polyline" || edge.lineType == "auto")
                {
                    if (sourceSide == "left" || sourceSide == "right")
                    {
                        float middle = (source.X + target.X) / 2f;
                        IncludePoint(ref hasContent, ref minX, ref minY, ref maxX, ref maxY, new PointF(middle, source.Y));
                        IncludePoint(ref hasContent, ref minX, ref minY, ref maxX, ref maxY, new PointF(middle, target.Y));
                    }
                    else
                    {
                        float middle = (source.Y + target.Y) / 2f;
                        IncludePoint(ref hasContent, ref minX, ref minY, ref maxX, ref maxY, new PointF(source.X, middle));
                        IncludePoint(ref hasContent, ref minX, ref minY, ref maxX, ref maxY, new PointF(target.X, middle));
                    }
                }
                else if (edge.lineType != "straight")
                {
                    PointF sourceVector = Vector(sourceSide), targetVector = Vector(targetSide);
                    float distance = Math.Max(45, Math.Min(140, Distance(source, target) * .42f));
                    IncludePoint(ref hasContent, ref minX, ref minY, ref maxX, ref maxY, new PointF(source.X + sourceVector.X * distance, source.Y + sourceVector.Y * distance));
                    IncludePoint(ref hasContent, ref minX, ref minY, ref maxX, ref maxY, new PointF(target.X + targetVector.X * distance, target.Y + targetVector.Y * distance));
                }
                if (!String.IsNullOrWhiteSpace(edge.label))
                {
                    PointF label = EdgePathMidpoint(edge.lineType, sourceSide, targetSide, source, target);
                    float width = Math.Max(44, edge.label.Length * 14 + 14);
                    IncludeRect(ref hasContent, ref minX, ref minY, ref maxX, ref maxY, new RectangleF(label.X - width / 2f, label.Y - 13, width, 25));
                }
            }
            if (!hasContent) return new RectangleF(0, 0, graph.meta.canvasWidth, graph.meta.canvasHeight);
            const float padding = 36f;
            return new RectangleF(minX - padding, minY - padding, Math.Max(1, maxX - minX + padding * 2), Math.Max(1, maxY - minY + padding * 2));
        }

        private static void IncludeRect(ref bool hasContent, ref float minX, ref float minY, ref float maxX, ref float maxY, RectangleF rect)
        {
            IncludePoint(ref hasContent, ref minX, ref minY, ref maxX, ref maxY, new PointF(rect.Left, rect.Top));
            IncludePoint(ref hasContent, ref minX, ref minY, ref maxX, ref maxY, new PointF(rect.Right, rect.Bottom));
        }

        private static void IncludePoint(ref bool hasContent, ref float minX, ref float minY, ref float maxX, ref float maxY, PointF point)
        {
            if (!hasContent) { minX = maxX = point.X; minY = maxY = point.Y; hasContent = true; return; }
            minX = Math.Min(minX, point.X); minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X); maxY = Math.Max(maxY, point.Y);
        }

        private static bool TryRect(string type, string id, Dictionary<string, GraphNode> nodes, Dictionary<string, GraphGroup> groups, out RectangleF rect)
        {
            GraphGroup group; GraphNode node;
            if (type == "group" && groups.TryGetValue(id ?? "", out group)) { rect = new RectangleF(group.x, group.y, group.w, group.h); return true; }
            if (nodes.TryGetValue(id ?? "", out node)) { rect = new RectangleF(node.x, node.y, node.w, node.h); return true; }
            rect = RectangleF.Empty; return false;
        }

        private static string EdgePath(string lineType, string sourceSide, string targetSide, PointF source, PointF target)
        {
            if (lineType == "straight") return "M " + Number(source.X) + " " + Number(source.Y) + " L " + Number(target.X) + " " + Number(target.Y);
            if (lineType == "polyline" || lineType == "auto")
            {
                if (sourceSide == "left" || sourceSide == "right")
                {
                    float middle = (source.X + target.X) / 2f;
                    return "M " + Number(source.X) + " " + Number(source.Y) + " L " + Number(middle) + " " + Number(source.Y) + " L " + Number(middle) + " " + Number(target.Y) + " L " + Number(target.X) + " " + Number(target.Y);
                }
                float y = (source.Y + target.Y) / 2f;
                return "M " + Number(source.X) + " " + Number(source.Y) + " L " + Number(source.X) + " " + Number(y) + " L " + Number(target.X) + " " + Number(y) + " L " + Number(target.X) + " " + Number(target.Y);
            }
            PointF sv = Vector(sourceSide), tv = Vector(targetSide);
            float distance = Math.Max(45, Math.Min(140, Distance(source, target) * .42f));
            return "M " + Number(source.X) + " " + Number(source.Y) + " C " + Number(source.X + sv.X * distance) + " " + Number(source.Y + sv.Y * distance) + " " + Number(target.X + tv.X * distance) + " " + Number(target.Y + tv.Y * distance) + " " + Number(target.X) + " " + Number(target.Y);
        }

        private static PointF EdgePathMidpoint(string lineType, string sourceSide, string targetSide, PointF source, PointF target)
        {
            List<PointF> points = new List<PointF>(); points.Add(source);
            if (lineType == "straight") points.Add(target);
            else if (lineType == "polyline" || lineType == "auto")
            {
                if (sourceSide == "left" || sourceSide == "right")
                {
                    float middle = (source.X + target.X) / 2f; points.Add(new PointF(middle, source.Y)); points.Add(new PointF(middle, target.Y));
                }
                else
                {
                    float middle = (source.Y + target.Y) / 2f; points.Add(new PointF(source.X, middle)); points.Add(new PointF(target.X, middle));
                }
                points.Add(target);
            }
            else
            {
                PointF sv = Vector(sourceSide), tv = Vector(targetSide);
                float distance = Math.Max(45, Math.Min(140, Distance(source, target) * .42f));
                PointF firstControl = new PointF(source.X + sv.X * distance, source.Y + sv.Y * distance);
                PointF secondControl = new PointF(target.X + tv.X * distance, target.Y + tv.Y * distance);
                for (int i = 1; i <= 48; i++) points.Add(CubicBezier(source, firstControl, secondControl, target, i / 48f));
            }
            return PolylinePointAtFraction(points, .5f);
        }

        internal static PointF EdgePathMidpointForTesting(string lineType, string sourceSide, string targetSide, PointF source, PointF target)
        {
            return EdgePathMidpoint(lineType, sourceSide, targetSide, source, target);
        }

        private static PointF CubicBezier(PointF start, PointF firstControl, PointF secondControl, PointF end, float t)
        {
            float inverse = 1f - t, a = inverse * inverse * inverse, b = 3f * inverse * inverse * t, c = 3f * inverse * t * t, d = t * t * t;
            return new PointF(a * start.X + b * firstControl.X + c * secondControl.X + d * end.X, a * start.Y + b * firstControl.Y + c * secondControl.Y + d * end.Y);
        }

        private static PointF PolylinePointAtFraction(List<PointF> points, float fraction)
        {
            if (points == null || points.Count == 0) return PointF.Empty;
            if (points.Count == 1) return points[0];
            float total = 0; for (int i = 1; i < points.Count; i++) total += Distance(points[i - 1], points[i]);
            if (total <= .001f) return points[0];
            float target = total * Math.Max(0, Math.Min(1, fraction)), travelled = 0;
            for (int i = 1; i < points.Count; i++)
            {
                float segment = Distance(points[i - 1], points[i]);
                if (travelled + segment >= target && segment > .001f)
                {
                    float ratio = (target - travelled) / segment;
                    return new PointF(points[i - 1].X + (points[i].X - points[i - 1].X) * ratio, points[i - 1].Y + (points[i].Y - points[i - 1].Y) * ratio);
                }
                travelled += segment;
            }
            return points[points.Count - 1];
        }

        private static string ConnectionSide(RectangleF from, RectangleF to)
        {
            PointF a = Center(from), b = Center(to); float dx = b.X - a.X, dy = b.Y - a.Y;
            if (Math.Abs(dx) >= Math.Abs(dy)) return dx >= 0 ? "right" : "left";
            return dy >= 0 ? "bottom" : "top";
        }

        private static PointF Port(RectangleF rect, string side)
        {
            if (side == "top") return new PointF(rect.X + rect.Width / 2, rect.Top);
            if (side == "bottom") return new PointF(rect.X + rect.Width / 2, rect.Bottom);
            if (side == "left") return new PointF(rect.Left, rect.Y + rect.Height / 2);
            return new PointF(rect.Right, rect.Y + rect.Height / 2);
        }

        private static PointF Vector(string side)
        {
            if (side == "top") return new PointF(0, -1); if (side == "bottom") return new PointF(0, 1);
            if (side == "left") return new PointF(-1, 0); return new PointF(1, 0);
        }

        private static PointF Center(RectangleF rect) { return new PointF(rect.X + rect.Width / 2, rect.Y + rect.Height / 2); }
        private static float Distance(PointF a, PointF b) { float x = a.X - b.X, y = a.Y - b.Y; return (float)Math.Sqrt(x * x + y * y); }
        private static string Number(float value) { return value.ToString("0.###", CultureInfo.InvariantCulture); }
        private static string Xml(string value)
        {
            string escaped = System.Security.SecurityElement.Escape(value ?? "") ?? "";
            return escaped.Replace("\r", "&#13;").Replace("\n", "&#10;").Replace("\t", "&#9;");
        }
        private static string NodeColor(string kind)
        {
            if (kind == "resource") return "#dcecff"; if (kind == "output") return "#dff3e7"; if (kind == "content") return "#f6e2ef";
            if (kind == "staff") return "#eee5fb"; if (kind == "commercial") return "#fff0d9"; return "#e6ebf0";
        }

        private static string SourceAccentColor(GraphEdge edge, Dictionary<string, GraphNode> nodes)
        {
            if (edge != null && edge.sourceType == "group") return "#9ba6b2";
            GraphNode source;
            return edge != null && nodes.TryGetValue(edge.source ?? "", out source) ? NodeAccentColor(source.kind) : "#7189a3";
        }

        private static string NodeAccentColor(string kind)
        {
            if (kind == "resource") return "#4b9eea"; if (kind == "output") return "#54ad72"; if (kind == "content") return "#d765a4";
            if (kind == "staff") return "#8b6fbc"; if (kind == "commercial") return "#e8963e"; return "#7189a3";
        }
    }
}
