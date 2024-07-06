namespace BetterJoy
{
    partial class _3rdPartyControllers
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
            var resources = new System.ComponentModel.ComponentResourceManager(typeof(_3rdPartyControllers));
            list_allControllers = new System.Windows.Forms.ListBox();
            list_customControllers = new System.Windows.Forms.ListBox();
            btn_add = new System.Windows.Forms.Button();
            btn_remove = new System.Windows.Forms.Button();
            group_props = new System.Windows.Forms.GroupBox();
            label2 = new System.Windows.Forms.Label();
            chooseType = new System.Windows.Forms.ComboBox();
            btn_applyAndClose = new System.Windows.Forms.Button();
            btn_apply = new System.Windows.Forms.Button();
            lbl_all = new System.Windows.Forms.Label();
            label1 = new System.Windows.Forms.Label();
            tip_device = new System.Windows.Forms.ToolTip(components);
            btn_refresh = new System.Windows.Forms.Button();
            group_props.SuspendLayout();
            SuspendLayout();
            // 
            // list_allControllers
            // 
            list_allControllers.FormattingEnabled = true;
            list_allControllers.ItemHeight = 15;
            list_allControllers.Location = new System.Drawing.Point(14, 31);
            list_allControllers.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            list_allControllers.Name = "list_allControllers";
            list_allControllers.Size = new System.Drawing.Size(220, 229);
            list_allControllers.TabIndex = 0;
            list_allControllers.SelectedValueChanged += list_allControllers_SelectedValueChanged;
            list_allControllers.MouseDown += list_allControllers_MouseDown;
            // 
            // list_customControllers
            // 
            list_customControllers.FormattingEnabled = true;
            list_customControllers.ItemHeight = 15;
            list_customControllers.Location = new System.Drawing.Point(297, 31);
            list_customControllers.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            list_customControllers.Name = "list_customControllers";
            list_customControllers.Size = new System.Drawing.Size(219, 154);
            list_customControllers.TabIndex = 1;
            list_customControllers.SelectedValueChanged += list_customControllers_SelectedValueChanged;
            list_customControllers.MouseDown += list_customControllers_MouseDown;
            // 
            // btn_add
            // 
            btn_add.Location = new System.Drawing.Point(241, 43);
            btn_add.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            btn_add.Name = "btn_add";
            btn_add.Size = new System.Drawing.Size(49, 27);
            btn_add.TabIndex = 2;
            btn_add.Text = "->";
            btn_add.UseVisualStyleBackColor = true;
            btn_add.Click += btn_add_Click;
            // 
            // btn_remove
            // 
            btn_remove.Location = new System.Drawing.Point(241, 144);
            btn_remove.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            btn_remove.Name = "btn_remove";
            btn_remove.Size = new System.Drawing.Size(49, 27);
            btn_remove.TabIndex = 3;
            btn_remove.Text = "<-";
            btn_remove.UseVisualStyleBackColor = true;
            btn_remove.Click += btn_remove_Click;
            // 
            // group_props
            // 
            group_props.Controls.Add(label2);
            group_props.Controls.Add(chooseType);
            group_props.Location = new System.Drawing.Point(292, 194);
            group_props.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            group_props.Name = "group_props";
            group_props.Padding = new System.Windows.Forms.Padding(4, 3, 4, 3);
            group_props.Size = new System.Drawing.Size(225, 66);
            group_props.TabIndex = 4;
            group_props.TabStop = false;
            group_props.Text = "Settings";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new System.Drawing.Point(8, 28);
            label2.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            label2.Name = "label2";
            label2.Size = new System.Drawing.Size(31, 15);
            label2.TabIndex = 1;
            label2.Text = "Type";
            // 
            // chooseType
            // 
            chooseType.FormattingEnabled = true;
            chooseType.Location = new System.Drawing.Point(55, 25);
            chooseType.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            chooseType.Name = "chooseType";
            chooseType.Size = new System.Drawing.Size(162, 23);
            chooseType.TabIndex = 0;
            chooseType.SelectedValueChanged += chooseType_SelectedValueChanged;
            // 
            // btn_applyAndClose
            // 
            btn_applyAndClose.Location = new System.Drawing.Point(272, 264);
            btn_applyAndClose.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            btn_applyAndClose.Name = "btn_applyAndClose";
            btn_applyAndClose.Size = new System.Drawing.Size(80, 27);
            btn_applyAndClose.TabIndex = 5;
            btn_applyAndClose.Text = "Close";
            btn_applyAndClose.UseVisualStyleBackColor = true;
            btn_applyAndClose.Click += btn_applyAndClose_Click;
            // 
            // btn_apply
            // 
            btn_apply.Location = new System.Drawing.Point(180, 264);
            btn_apply.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            btn_apply.Name = "btn_apply";
            btn_apply.Size = new System.Drawing.Size(80, 27);
            btn_apply.TabIndex = 6;
            btn_apply.Text = "Apply";
            btn_apply.UseVisualStyleBackColor = true;
            btn_apply.Click += btn_apply_Click;
            // 
            // lbl_all
            // 
            lbl_all.AutoSize = true;
            lbl_all.Location = new System.Drawing.Point(14, 10);
            lbl_all.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            lbl_all.Name = "lbl_all";
            lbl_all.Size = new System.Drawing.Size(64, 15);
            lbl_all.TabIndex = 7;
            lbl_all.Text = "All Devices";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new System.Drawing.Point(297, 10);
            label1.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            label1.Name = "label1";
            label1.Size = new System.Drawing.Size(103, 15);
            label1.TabIndex = 8;
            label1.Text = "Switch Controllers";
            // 
            // btn_refresh
            // 
            btn_refresh.Location = new System.Drawing.Point(241, 77);
            btn_refresh.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            btn_refresh.Name = "btn_refresh";
            btn_refresh.Size = new System.Drawing.Size(49, 58);
            btn_refresh.TabIndex = 9;
            btn_refresh.Text = "Re-\r\nfresh";
            btn_refresh.UseVisualStyleBackColor = true;
            btn_refresh.Click += btn_refresh_Click;
            // 
            // _3rdPartyControllers
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            ClientSize = new System.Drawing.Size(531, 301);
            Controls.Add(btn_refresh);
            Controls.Add(label1);
            Controls.Add(lbl_all);
            Controls.Add(btn_apply);
            Controls.Add(btn_applyAndClose);
            Controls.Add(group_props);
            Controls.Add(btn_remove);
            Controls.Add(btn_add);
            Controls.Add(list_customControllers);
            Controls.Add(list_allControllers);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
            Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "_3rdPartyControllers";
            Text = "Add 3rd-Party Controllers";
            FormClosing += _3rdPartyControllers_FormClosing;
            group_props.ResumeLayout(false);
            group_props.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.ListBox list_allControllers;
        private System.Windows.Forms.ListBox list_customControllers;
        private System.Windows.Forms.Button btn_add;
        private System.Windows.Forms.Button btn_remove;
        private System.Windows.Forms.GroupBox group_props;
        private System.Windows.Forms.Button btn_applyAndClose;
        private System.Windows.Forms.Button btn_apply;
        private System.Windows.Forms.Label lbl_all;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.ToolTip tip_device;
        private System.Windows.Forms.Button btn_refresh;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.ComboBox chooseType;
    }
}
