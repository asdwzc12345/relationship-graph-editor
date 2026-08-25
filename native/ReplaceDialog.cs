using System;
using System.Drawing;
using System.Windows.Forms;

namespace RelationshipGraphNative
{
    internal sealed class ReplaceDialog : Form
    {
        private readonly TextBox _findBox = new TextBox();
        private readonly TextBox _replaceBox = new TextBox();
        private readonly Label _status = new Label();
        private readonly float _uiScale;

        public event Action<string> FindNextRequested;
        public event Action<string, string> ReplaceSelectionRequested;
        public event Action<string, string> ReplaceAllRequested;

        public ReplaceDialog(string initialQuery)
        {
            Text = "查找和替换";
            ShowIcon = false; MinimizeBox = false; MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
            _uiScale = CurrentDpiScale();
            ClientSize = new Size(S(570), S(278));

            Label findLabel = new Label { Text = "查找内容(&N)", AutoSize = true, Location = new Point(S(20), S(23)) };
            Label replaceLabel = new Label { Text = "替换为(&P)", AutoSize = true, Location = new Point(S(20), S(68)) };
            _findBox.SetBounds(S(120), S(18), S(426), S(30)); _findBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _replaceBox.SetBounds(S(120), S(63), S(426), S(30)); _replaceBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _findBox.MaxLength = 300; _replaceBox.MaxLength = 300; _findBox.Text = initialQuery ?? "";

            Label hint = new Label
            {
                Text = "范围包括节点名称、节点类型、备注、分组名称和关系名称。替换所选只影响画布中当前选中的对象。",
                AutoSize = false,
                Location = new Point(S(20), S(108)),
                Size = new Size(S(526), S(42))
            };
            _status.AutoSize = false; _status.Location = new Point(S(20), S(151)); _status.Size = new Size(S(526), S(28)); _status.Text = "可先在画布中单选、框选或多选对象。";

            Button findNext = MakeButton("查找下一个(&F)", 20, 201, 120);
            Button replaceSelection = MakeButton("替换当前所选(&R)", 148, 201, 142);
            Button replaceAll = MakeButton("全部替换(&A)", 298, 201, 116);
            Button close = MakeButton("关闭", 426, 201, 120);
            findNext.Click += delegate { RaiseFindNext(); };
            replaceSelection.Click += delegate { if (ReplaceSelectionRequested != null) ReplaceSelectionRequested(_findBox.Text, _replaceBox.Text); };
            replaceAll.Click += delegate { if (ReplaceAllRequested != null) ReplaceAllRequested(_findBox.Text, _replaceBox.Text); };
            close.Click += delegate { Close(); };

            Controls.Add(findLabel); Controls.Add(replaceLabel); Controls.Add(_findBox); Controls.Add(_replaceBox);
            Controls.Add(hint); Controls.Add(_status); Controls.Add(findNext); Controls.Add(replaceSelection); Controls.Add(replaceAll); Controls.Add(close);
            AcceptButton = findNext; CancelButton = close;
            Shown += delegate { _findBox.Focus(); _findBox.SelectAll(); };
        }

        private int S(int value) { return (int)Math.Round(value * _uiScale); }

        private float CurrentDpiScale()
        {
            using (Graphics graphics = CreateGraphics()) return Math.Max(1f, graphics.DpiX / 96f);
        }

        private Button MakeButton(string text, int x, int y, int width)
        {
            Button button = new Button { Text = text, UseVisualStyleBackColor = true };
            button.SetBounds(S(x), S(y), S(width), S(36)); return button;
        }

        private void RaiseFindNext()
        {
            if (FindNextRequested != null) FindNextRequested(_findBox.Text);
        }

        public void SetStatus(string message, bool error)
        {
            _status.Text = message ?? "";
            _status.ForeColor = error ? Color.FromArgb(196, 48, 43) : SystemColors.ControlText;
        }

        public void Prepare(string query)
        {
            if (!String.IsNullOrEmpty(query)) _findBox.Text = query;
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Show(); Activate(); _findBox.Focus(); _findBox.SelectAll();
        }
    }
}
