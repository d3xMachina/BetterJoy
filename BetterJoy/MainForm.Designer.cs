namespace BetterJoy
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            var resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            console = new System.Windows.Forms.TextBox();
            notifyIcon = new System.Windows.Forms.NotifyIcon(components);
            contextMenu = new System.Windows.Forms.ContextMenuStrip(components);
            showToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            exitToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            version_lbl = new System.Windows.Forms.Label();
            lb_github = new System.Windows.Forms.LinkLabel();
            conCntrls = new System.Windows.Forms.GroupBox();
            con8 = new System.Windows.Forms.Button();
            con7 = new System.Windows.Forms.Button();
            con6 = new System.Windows.Forms.Button();
            con5 = new System.Windows.Forms.Button();
            con4 = new System.Windows.Forms.Button();
            con3 = new System.Windows.Forms.Button();
            con2 = new System.Windows.Forms.Button();
            con1 = new System.Windows.Forms.Button();
            btnTip = new System.Windows.Forms.ToolTip(components);
            foldLbl = new System.Windows.Forms.Label();
            startInTrayBox = new System.Windows.Forms.CheckBox();
            btn_open3rdP = new System.Windows.Forms.Button();
            groupBox1 = new System.Windows.Forms.GroupBox();
            settingsTable = new System.Windows.Forms.TableLayoutPanel();
            rightPanel = new System.Windows.Forms.Panel();
            settingsApply = new System.Windows.Forms.Button();
            btn_calibrate = new System.Windows.Forms.Button();
            btn_reassign_open = new System.Windows.Forms.Button();
            btn_locate = new System.Windows.Forms.Button();
            startOnBoot = new System.Windows.Forms.CheckBox();
            contextMenu.SuspendLayout();
            conCntrls.SuspendLayout();
            groupBox1.SuspendLayout();
            rightPanel.SuspendLayout();
            SuspendLayout();
            // 
            // console
            // 
            console.Location = new System.Drawing.Point(13, 220);
            console.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            console.Multiline = true;
            console.Name = "console";
            console.ReadOnly = true;
            console.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            console.Size = new System.Drawing.Size(306, 149);
            console.TabIndex = 2;
            // 
            // notifyIcon
            // 
            notifyIcon.BalloonTipText = "Click the tray icon to maximise!";
            notifyIcon.BalloonTipTitle = "BetterJoy";
            notifyIcon.ContextMenuStrip = contextMenu;
            notifyIcon.Icon = (System.Drawing.Icon)resources.GetObject("notifyIcon.Icon");
            notifyIcon.Text = "BetterJoy";
            notifyIcon.Visible = true;
            notifyIcon.MouseClick += notifyIcon_MouseClick;
            // 
            // contextMenu
            // 
            contextMenu.ImageScalingSize = new System.Drawing.Size(24, 24);
            contextMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { showToolStripMenuItem, exitToolStripMenuItem });
            contextMenu.Name = "contextMenu";
            contextMenu.Size = new System.Drawing.Size(104, 48);
            // 
            // showToolStripMenuItem
            // 
            showToolStripMenuItem.Name = "showToolStripMenuItem";
            showToolStripMenuItem.Size = new System.Drawing.Size(103, 22);
            showToolStripMenuItem.Text = "Show";
            showToolStripMenuItem.Click += showToolStripMenuItem_Click;
            // 
            // exitToolStripMenuItem
            // 
            exitToolStripMenuItem.Name = "exitToolStripMenuItem";
            exitToolStripMenuItem.Size = new System.Drawing.Size(103, 22);
            exitToolStripMenuItem.Text = "Exit";
            exitToolStripMenuItem.Click += exitToolStripMenuItem_Click;
            // 
            // version_lbl
            // 
            version_lbl.AutoSize = true;
            version_lbl.Location = new System.Drawing.Point(284, 375);
            version_lbl.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            version_lbl.Name = "version_lbl";
            version_lbl.Size = new System.Drawing.Size(37, 15);
            version_lbl.TabIndex = 2;
            version_lbl.Text = "v1.0.0";
            // 
            // lb_github
            // 
            lb_github.AutoSize = true;
            lb_github.Location = new System.Drawing.Point(237, 375);
            lb_github.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            lb_github.Name = "lb_github";
            lb_github.Size = new System.Drawing.Size(43, 15);
            lb_github.TabIndex = 5;
            lb_github.TabStop = true;
            lb_github.Text = "Github";
            lb_github.LinkClicked += linkLabel1_LinkClicked;
            // 
            // conCntrls
            // 
            conCntrls.Controls.Add(con8);
            conCntrls.Controls.Add(con7);
            conCntrls.Controls.Add(con6);
            conCntrls.Controls.Add(con5);
            conCntrls.Controls.Add(con4);
            conCntrls.Controls.Add(con3);
            conCntrls.Controls.Add(con2);
            conCntrls.Controls.Add(con1);
            conCntrls.Location = new System.Drawing.Point(14, 14);
            conCntrls.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            conCntrls.Name = "conCntrls";
            conCntrls.Padding = new System.Windows.Forms.Padding(4, 3, 4, 3);
            conCntrls.Size = new System.Drawing.Size(304, 173);
            conCntrls.TabIndex = 0;
            conCntrls.TabStop = false;
            conCntrls.Text = "Connected Controllers";
            // 
            // con8
            // 
            con8.BackColor = System.Drawing.SystemColors.Control;
            con8.BackgroundImage = Properties.Resources.cross;
            con8.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            con8.Enabled = false;
            con8.Location = new System.Drawing.Point(230, 98);
            con8.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            con8.Name = "con8";
            con8.Size = new System.Drawing.Size(68, 68);
            con8.TabIndex = 11;
            con8.TabStop = false;
            con8.UseVisualStyleBackColor = true;
            // 
            // con7
            // 
            con7.BackColor = System.Drawing.SystemColors.Control;
            con7.BackgroundImage = Properties.Resources.cross;
            con7.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            con7.Enabled = false;
            con7.Location = new System.Drawing.Point(155, 98);
            con7.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            con7.Name = "con7";
            con7.Size = new System.Drawing.Size(68, 68);
            con7.TabIndex = 10;
            con7.TabStop = false;
            con7.UseVisualStyleBackColor = true;
            // 
            // con6
            // 
            con6.BackColor = System.Drawing.SystemColors.Control;
            con6.BackgroundImage = Properties.Resources.cross;
            con6.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            con6.Enabled = false;
            con6.Location = new System.Drawing.Point(81, 98);
            con6.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            con6.Name = "con6";
            con6.Size = new System.Drawing.Size(68, 68);
            con6.TabIndex = 9;
            con6.TabStop = false;
            con6.UseVisualStyleBackColor = true;
            // 
            // con5
            // 
            con5.BackColor = System.Drawing.SystemColors.Control;
            con5.BackgroundImage = Properties.Resources.cross;
            con5.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            con5.Enabled = false;
            con5.Location = new System.Drawing.Point(6, 98);
            con5.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            con5.Name = "con5";
            con5.Size = new System.Drawing.Size(68, 68);
            con5.TabIndex = 8;
            con5.TabStop = false;
            btnTip.SetToolTip(con5, "Click on Joycons to join/split them");
            con5.UseVisualStyleBackColor = true;
            // 
            // con4
            // 
            con4.BackColor = System.Drawing.SystemColors.Control;
            con4.BackgroundImage = Properties.Resources.cross;
            con4.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            con4.Enabled = false;
            con4.Location = new System.Drawing.Point(231, 23);
            con4.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            con4.Name = "con4";
            con4.Size = new System.Drawing.Size(68, 68);
            con4.TabIndex = 3;
            con4.TabStop = false;
            con4.UseVisualStyleBackColor = true;
            // 
            // con3
            // 
            con3.BackColor = System.Drawing.SystemColors.Control;
            con3.BackgroundImage = Properties.Resources.cross;
            con3.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            con3.Enabled = false;
            con3.Location = new System.Drawing.Point(156, 23);
            con3.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            con3.Name = "con3";
            con3.Size = new System.Drawing.Size(68, 68);
            con3.TabIndex = 2;
            con3.TabStop = false;
            con3.UseVisualStyleBackColor = true;
            // 
            // con2
            // 
            con2.BackColor = System.Drawing.SystemColors.Control;
            con2.BackgroundImage = Properties.Resources.cross;
            con2.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            con2.Enabled = false;
            con2.Location = new System.Drawing.Point(82, 23);
            con2.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            con2.Name = "con2";
            con2.Size = new System.Drawing.Size(68, 68);
            con2.TabIndex = 1;
            con2.TabStop = false;
            con2.UseVisualStyleBackColor = true;
            // 
            // con1
            // 
            con1.BackColor = System.Drawing.SystemColors.Control;
            con1.BackgroundImage = Properties.Resources.cross;
            con1.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            con1.Enabled = false;
            con1.Location = new System.Drawing.Point(7, 23);
            con1.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            con1.Name = "con1";
            con1.Size = new System.Drawing.Size(68, 68);
            con1.TabIndex = 0;
            con1.TabStop = false;
            btnTip.SetToolTip(con1, "Click on Joycons to join/split them");
            con1.UseVisualStyleBackColor = true;
            // 
            // foldLbl
            // 
            foldLbl.Location = new System.Drawing.Point(320, 70);
            foldLbl.Margin = new System.Windows.Forms.Padding(4, 0, 0, 0);
            foldLbl.Name = "foldLbl";
            foldLbl.Size = new System.Drawing.Size(15, 267);
            foldLbl.TabIndex = 12;
            foldLbl.Text = ">";
            foldLbl.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            btnTip.SetToolTip(foldLbl, "Config");
            foldLbl.Click += foldLbl_Click;
            // 
            // startInTrayBox
            // 
            startInTrayBox.AutoSize = true;
            startInTrayBox.Location = new System.Drawing.Point(13, 375);
            startInTrayBox.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            startInTrayBox.Name = "startInTrayBox";
            startInTrayBox.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            startInTrayBox.Size = new System.Drawing.Size(86, 19);
            startInTrayBox.TabIndex = 6;
            startInTrayBox.Text = "Start in tray";
            startInTrayBox.UseVisualStyleBackColor = true;
            startInTrayBox.Click += startInTrayBox_Click;
            // 
            // btn_open3rdP
            // 
            btn_open3rdP.Location = new System.Drawing.Point(77, 191);
            btn_open3rdP.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            btn_open3rdP.Name = "btn_open3rdP";
            btn_open3rdP.Size = new System.Drawing.Size(100, 25);
            btn_open3rdP.TabIndex = 7;
            btn_open3rdP.Text = "Add Controllers";
            btn_open3rdP.UseVisualStyleBackColor = true;
            btn_open3rdP.Click += btn_open3rdP_Click;
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(settingsTable);
            groupBox1.Location = new System.Drawing.Point(4, 13);
            groupBox1.Margin = new System.Windows.Forms.Padding(2);
            groupBox1.Name = "groupBox1";
            groupBox1.Padding = new System.Windows.Forms.Padding(2);
            groupBox1.Size = new System.Drawing.Size(352, 352);
            groupBox1.TabIndex = 9;
            groupBox1.TabStop = false;
            groupBox1.Text = "Configuration";
            // 
            // settingsTable
            // 
            settingsTable.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            settingsTable.AutoScroll = true;
            settingsTable.ColumnCount = 2;
            settingsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 60F));
            settingsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 40F));
            settingsTable.Location = new System.Drawing.Point(5, 20);
            settingsTable.Margin = new System.Windows.Forms.Padding(2);
            settingsTable.MinimumSize = new System.Drawing.Size(345, 0);
            settingsTable.Name = "settingsTable";
            settingsTable.RowCount = 1;
            settingsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            settingsTable.Size = new System.Drawing.Size(345, 328);
            settingsTable.TabIndex = 1;
            // 
            // rightPanel
            // 
            rightPanel.Controls.Add(settingsApply);
            rightPanel.Controls.Add(groupBox1);
            rightPanel.Location = new System.Drawing.Point(337, 1);
            rightPanel.Margin = new System.Windows.Forms.Padding(2, 2, 6, 2);
            rightPanel.Name = "rightPanel";
            rightPanel.Size = new System.Drawing.Size(364, 394);
            rightPanel.TabIndex = 11;
            rightPanel.Visible = false;
            // 
            // settingsApply
            // 
            settingsApply.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            settingsApply.Font = new System.Drawing.Font("Microsoft Sans Serif", 8F);
            settingsApply.Location = new System.Drawing.Point(286, 367);
            settingsApply.Margin = new System.Windows.Forms.Padding(2);
            settingsApply.Name = "settingsApply";
            settingsApply.Size = new System.Drawing.Size(71, 24);
            settingsApply.TabIndex = 10;
            settingsApply.Text = "Apply";
            settingsApply.UseVisualStyleBackColor = true;
            settingsApply.Click += settingsApply_Click;
            // 
            // btn_calibrate
            // 
            btn_calibrate.Enabled = false;
            btn_calibrate.Location = new System.Drawing.Point(249, 191);
            btn_calibrate.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            btn_calibrate.Name = "btn_calibrate";
            btn_calibrate.Size = new System.Drawing.Size(70, 25);
            btn_calibrate.TabIndex = 8;
            btn_calibrate.Text = "Calibrate";
            btn_calibrate.UseVisualStyleBackColor = true;
            btn_calibrate.Click += StartCalibrate;
            // 
            // btn_reassign_open
            // 
            btn_reassign_open.Location = new System.Drawing.Point(13, 191);
            btn_reassign_open.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            btn_reassign_open.Name = "btn_reassign_open";
            btn_reassign_open.Size = new System.Drawing.Size(60, 25);
            btn_reassign_open.TabIndex = 13;
            btn_reassign_open.Text = "Remap";
            btn_reassign_open.UseVisualStyleBackColor = true;
            btn_reassign_open.Click += btn_reassign_open_Click;
            // 
            // btn_locate
            // 
            btn_locate.Enabled = false;
            btn_locate.Location = new System.Drawing.Point(181, 191);
            btn_locate.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            btn_locate.Name = "btn_locate";
            btn_locate.Size = new System.Drawing.Size(64, 25);
            btn_locate.TabIndex = 14;
            btn_locate.Text = "Locate";
            btn_locate.UseVisualStyleBackColor = true;
            btn_locate.Click += StartLocate;
            // 
            // startOnBoot
            // 
            startOnBoot.AutoSize = true;
            startOnBoot.Location = new System.Drawing.Point(107, 375);
            startOnBoot.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            startOnBoot.Name = "startOnBoot";
            startOnBoot.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            startOnBoot.Size = new System.Drawing.Size(95, 19);
            startOnBoot.TabIndex = 15;
            startOnBoot.Text = "Start on boot";
            startOnBoot.UseVisualStyleBackColor = true;
            startOnBoot.Click += startOnBoot_Click;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            AutoSize = true;
            AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            ClientSize = new System.Drawing.Size(712, 401);
            Controls.Add(startOnBoot);
            Controls.Add(btn_locate);
            Controls.Add(btn_reassign_open);
            Controls.Add(foldLbl);
            Controls.Add(rightPanel);
            Controls.Add(btn_calibrate);
            Controls.Add(btn_open3rdP);
            Controls.Add(startInTrayBox);
            Controls.Add(conCntrls);
            Controls.Add(lb_github);
            Controls.Add(version_lbl);
            Controls.Add(console);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
            Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            MaximizeBox = false;
            MinimumSize = new System.Drawing.Size(0, 430);
            Name = "MainForm";
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            Text = "BetterJoy LTS";
            FormClosing += MainForm_FormClosing;
            Load += MainForm_Load;
            Resize += MainForm_Resize;
            contextMenu.ResumeLayout(false);
            conCntrls.ResumeLayout(false);
            groupBox1.ResumeLayout(false);
            rightPanel.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        public System.Windows.Forms.TextBox console;
        public System.Windows.Forms.NotifyIcon notifyIcon;
        private System.Windows.Forms.Label version_lbl;
        private System.Windows.Forms.ContextMenuStrip contextMenu;
        private System.Windows.Forms.ToolStripMenuItem exitToolStripMenuItem;
        private System.Windows.Forms.LinkLabel lb_github;
        private System.Windows.Forms.GroupBox conCntrls;
        private System.Windows.Forms.Button con1;
        private System.Windows.Forms.Button con4;
        private System.Windows.Forms.Button con3;
        private System.Windows.Forms.Button con2;
        private System.Windows.Forms.ToolTip btnTip;
        private System.Windows.Forms.CheckBox startInTrayBox;
        private System.Windows.Forms.Button btn_open3rdP;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.TableLayoutPanel settingsTable;
        private System.Windows.Forms.Panel rightPanel;
        private System.Windows.Forms.Button settingsApply;
        private System.Windows.Forms.Label foldLbl;
        private System.Windows.Forms.Button btn_calibrate;
        private System.Windows.Forms.Button btn_reassign_open;
        private System.Windows.Forms.Button con8;
        private System.Windows.Forms.Button con7;
        private System.Windows.Forms.Button con6;
        private System.Windows.Forms.Button con5;
        private System.Windows.Forms.Button btn_locate;
        private System.Windows.Forms.CheckBox startOnBoot;
        private System.Windows.Forms.ToolStripMenuItem showToolStripMenuItem;
    }
}
