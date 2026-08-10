window.DEFAULT_GRAPH = {
  "version": 1,
  "meta": {
    "title": "测试用图",
    "canvasWidth": 1380,
    "canvasHeight": 760,
    "updatedAt": "2026-08-08T00:00:00.000Z"
  },
  "settings": {
    "theme": "system",
    "colors": {},
    "relationTypes": [
      {
        "id": "core",
        "label": "主要流程",
        "color": "#e8963e"
      },
      {
        "id": "economy",
        "label": "资源支持",
        "color": "#4b9eea"
      },
      {
        "id": "content",
        "label": "目标与记录",
        "color": "#d765a4"
      },
      {
        "id": "growth",
        "label": "反馈改进",
        "color": "#54ad72"
      }
    ],
    "nodeTypes": []
  },
  "groups": [
    {
      "id": "input",
      "label": "1  输入与目标",
      "x": 30,
      "y": 55,
      "w": 280,
      "h": 650
    },
    {
      "id": "process",
      "label": "2  分析与执行",
      "x": 340,
      "y": 55,
      "w": 320,
      "h": 650
    },
    {
      "id": "output",
      "label": "3  检查与交付",
      "x": 690,
      "y": 55,
      "w": 300,
      "h": 650
    },
    {
      "id": "feedback",
      "label": "4  反馈与改进",
      "x": 1020,
      "y": 275,
      "w": 330,
      "h": 430
    }
  ],
  "nodes": [
    {
      "id": "user_need",
      "label": "用户需求",
      "kind": "resource",
      "type": "输入信息",
      "x": 70,
      "y": 135,
      "w": 200,
      "h": 68,
      "note": "说明希望解决的问题和期望结果。",
      "group": "input"
    },
    {
      "id": "clear_goal",
      "label": "明确目标",
      "kind": "content",
      "type": "目标与标准",
      "x": 70,
      "y": 325,
      "w": 200,
      "h": 68,
      "note": "把需求整理为清晰、可验证的目标。",
      "group": "input"
    },
    {
      "id": "available_resource",
      "label": "可用资源",
      "kind": "resource",
      "type": "输入信息",
      "x": 70,
      "y": 515,
      "w": 200,
      "h": 68,
      "note": "当前可投入的人员、时间和工具。",
      "group": "input"
    },
    {
      "id": "analyze",
      "label": "分析需求",
      "kind": "system",
      "type": "工作步骤",
      "x": 400,
      "y": 125,
      "w": 200,
      "h": 68,
      "note": "识别重点、范围和限制条件。",
      "group": "process"
    },
    {
      "id": "make_plan",
      "label": "制定方案",
      "kind": "system",
      "type": "工作步骤",
      "x": 400,
      "y": 315,
      "w": 200,
      "h": 68,
      "note": "确定步骤、负责人和完成标准。",
      "group": "process"
    },
    {
      "id": "execute",
      "label": "执行任务",
      "kind": "system",
      "type": "工作步骤",
      "x": 400,
      "y": 505,
      "w": 200,
      "h": 68,
      "note": "按照方案完成实际工作。",
      "group": "process"
    },
    {
      "id": "quality_check",
      "label": "质量检查",
      "kind": "system",
      "type": "工作步骤",
      "x": 740,
      "y": 145,
      "w": 200,
      "h": 68,
      "note": "对照目标检查完整性和质量。",
      "group": "output"
    },
    {
      "id": "deliver_result",
      "label": "交付结果",
      "kind": "output",
      "type": "结果产出",
      "x": 740,
      "y": 335,
      "w": 200,
      "h": 68,
      "note": "把检查通过的成果交给使用者。",
      "group": "output"
    },
    {
      "id": "process_record",
      "label": "过程记录",
      "kind": "content",
      "type": "过程记录",
      "x": 740,
      "y": 525,
      "w": 200,
      "h": 68,
      "note": "保存关键决策、问题和处理方法。",
      "group": "output"
    },
    {
      "id": "collect_feedback",
      "label": "收集反馈",
      "kind": "resource",
      "type": "反馈信息",
      "x": 1085,
      "y": 345,
      "w": 200,
      "h": 68,
      "note": "记录使用结果和新的需求。",
      "group": "feedback"
    },
    {
      "id": "improve_plan",
      "label": "改进方案",
      "kind": "system",
      "type": "工作步骤",
      "x": 1085,
      "y": 480,
      "w": 200,
      "h": 68,
      "note": "根据问题和反馈确定改进措施。",
      "group": "feedback"
    },
    {
      "id": "next_cycle",
      "label": "下一轮执行",
      "kind": "output",
      "type": "结果产出",
      "x": 1085,
      "y": 615,
      "w": 200,
      "h": 68,
      "note": "把改进措施带入下一轮工作。",
      "group": "feedback"
    }
  ],
  "edges": [
    {
      "id": "e01",
      "source": "user_need",
      "target": "analyze",
      "label": "提供信息",
      "category": "core"
    },
    {
      "id": "e02",
      "source": "clear_goal",
      "target": "analyze",
      "label": "设定方向",
      "category": "content"
    },
    {
      "id": "e03",
      "source": "available_resource",
      "target": "make_plan",
      "label": "提供约束",
      "category": "economy"
    },
    {
      "id": "e04",
      "source": "analyze",
      "target": "make_plan",
      "label": "形成结论",
      "category": "core"
    },
    {
      "id": "e05",
      "source": "make_plan",
      "target": "execute",
      "label": "指导执行",
      "category": "core"
    },
    {
      "id": "e06",
      "source": "available_resource",
      "target": "execute",
      "label": "提供支持",
      "category": "economy"
    },
    {
      "id": "e07",
      "source": "execute",
      "target": "quality_check",
      "label": "提交检查",
      "category": "core"
    },
    {
      "id": "e08",
      "source": "clear_goal",
      "target": "quality_check",
      "label": "验收标准",
      "category": "content"
    },
    {
      "id": "e09",
      "source": "quality_check",
      "target": "deliver_result",
      "label": "检查通过",
      "category": "core"
    },
    {
      "id": "e10",
      "source": "execute",
      "target": "process_record",
      "label": "记录过程",
      "category": "content"
    },
    {
      "id": "e11",
      "source": "deliver_result",
      "target": "collect_feedback",
      "label": "获得反馈",
      "category": "growth"
    },
    {
      "id": "e12",
      "source": "quality_check",
      "target": "improve_plan",
      "label": "发现问题",
      "category": "growth"
    },
    {
      "id": "e13",
      "source": "process_record",
      "target": "improve_plan",
      "label": "提供依据",
      "category": "content"
    },
    {
      "id": "e14",
      "source": "collect_feedback",
      "target": "improve_plan",
      "label": "汇总建议",
      "category": "growth"
    },
    {
      "id": "e15",
      "source": "improve_plan",
      "target": "next_cycle",
      "label": "形成优化项",
      "category": "growth"
    },
    {
      "id": "e16",
      "source": "next_cycle",
      "target": "analyze",
      "label": "进入下一轮",
      "category": "growth"
    }
  ]
};
