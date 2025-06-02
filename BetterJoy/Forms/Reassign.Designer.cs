using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BetterJoy.Forms
{
	// from https://stackoverflow.com/a/27173509
	public class SplitButton : Button {
		[DefaultValue(null), Browsable(true),
		DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
		public ContextMenuStrip Menu { get; set; }

		[DefaultValue(20), Browsable(true),
		DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
		public int SplitWidth { get; set; }

		public SplitButton() {
			SplitWidth = 20;
		}

		protected override void OnMouseDown(MouseEventArgs mevent) {
			var splitRect = new Rectangle(this.Width - this.SplitWidth, 0, this.SplitWidth, this.Height);

			// Figure out if the button click was on the button itself or the menu split
			if (Menu != null &&
				((mevent.Button == MouseButtons.Left &&
				splitRect.Contains(mevent.Location)) || mevent.Button == MouseButtons.Right)) {
				Menu.Tag = this;
				Menu.Show(this, 0, this.Height);    // Shows menu under button
			} else {
				base.OnMouseDown(mevent);
			}
		}

		protected override void OnPaint(PaintEventArgs pevent) {
			base.OnPaint(pevent);

			if (this.Menu != null && this.SplitWidth > 0) {
				// Draw the arrow glyph on the right side of the button
				int arrowX = ClientRectangle.Width - 14;
				int arrowY = ClientRectangle.Height / 2 - 1;

				var arrowBrush = Enabled ? SystemBrushes.ControlText : SystemBrushes.ButtonShadow;
				var arrows = new[] { new Point(arrowX, arrowY), new Point(arrowX + 7, arrowY), new Point(arrowX + 3, arrowY + 4) };
				pevent.Graphics.FillPolygon(arrowBrush, arrows);

				// Draw a dashed separator on the left of the arrow
				int lineX = ClientRectangle.Width - this.SplitWidth;
				int lineYFrom = arrowY - 4;
				int lineYTo = arrowY + 8;
				using (var separatorPen = new Pen(Brushes.DarkGray) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot }) {
					pevent.Graphics.DrawLine(separatorPen, lineX, lineYFrom, lineX, lineYTo);
				}
			}
		}
	}

	partial class Reassign {
		/// <summary>
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing) {
			if (disposing) {
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
            components = new Container();
            var resources = new ComponentResourceManager(typeof(Reassign));
            btn_capture = new SplitButton();
            lbl_capture = new Label();
            lbl_home = new Label();
            btn_home = new SplitButton();
            lbl_sl_l = new Label();
            btn_sl_l = new SplitButton();
            lbl_sr_l = new Label();
            btn_sr_l = new SplitButton();
            lbl_sl_r = new Label();
            btn_sl_r = new SplitButton();
            lbl_sr_r = new Label();
            btn_sr_r = new SplitButton();
            btn_ok = new Button();
            btn_apply = new Button();
            tip_reassign = new ToolTip(components);
            lbl_shake = new Label();
            btn_shake = new SplitButton();
            gb_inputs = new GroupBox();
            gb_actions = new GroupBox();
            lbl_reset_mouse = new Label();
            btn_reset_mouse = new SplitButton();
            btn_active_gyro = new SplitButton();
            lbl_activate_gyro = new Label();
            btn_swap_ab = new SplitButton();
            lbl_swap_ab = new Label();
            btn_swap_xy = new SplitButton();
            lbl_swap_xy = new Label();
            gb_inputs.SuspendLayout();
            gb_actions.SuspendLayout();
            SuspendLayout();
            // 
            // btn_capture
            // 
            btn_capture.Location = new Point(111, 22);
            btn_capture.Margin = new Padding(4, 3, 4, 3);
            btn_capture.Name = "btn_capture";
            btn_capture.Size = new Size(88, 27);
            btn_capture.TabIndex = 0;
            btn_capture.UseVisualStyleBackColor = true;
            // 
            // lbl_capture
            // 
            lbl_capture.AutoSize = true;
            lbl_capture.Location = new Point(7, 28);
            lbl_capture.Margin = new Padding(4, 0, 4, 0);
            lbl_capture.Name = "lbl_capture";
            lbl_capture.Size = new Size(49, 15);
            lbl_capture.TabIndex = 2;
            lbl_capture.Text = "Capture";
            lbl_capture.TextAlign = ContentAlignment.TopCenter;
            // 
            // lbl_home
            // 
            lbl_home.AutoSize = true;
            lbl_home.Location = new Point(7, 61);
            lbl_home.Margin = new Padding(4, 0, 4, 0);
            lbl_home.Name = "lbl_home";
            lbl_home.Size = new Size(40, 15);
            lbl_home.TabIndex = 4;
            lbl_home.Text = "Home";
            lbl_home.TextAlign = ContentAlignment.TopCenter;
            // 
            // btn_home
            // 
            btn_home.Location = new Point(111, 55);
            btn_home.Margin = new Padding(4, 3, 4, 3);
            btn_home.Name = "btn_home";
            btn_home.Size = new Size(88, 27);
            btn_home.TabIndex = 3;
            btn_home.UseVisualStyleBackColor = true;
            // 
            // lbl_sl_l
            // 
            lbl_sl_l.AutoSize = true;
            lbl_sl_l.Location = new Point(7, 94);
            lbl_sl_l.Margin = new Padding(4, 0, 4, 0);
            lbl_sl_l.Name = "lbl_sl_l";
            lbl_sl_l.Size = new Size(82, 15);
            lbl_sl_l.TabIndex = 6;
            lbl_sl_l.Text = "SL Left Joycon";
            lbl_sl_l.TextAlign = ContentAlignment.TopCenter;
            // 
            // btn_sl_l
            // 
            btn_sl_l.Location = new Point(111, 88);
            btn_sl_l.Margin = new Padding(4, 3, 4, 3);
            btn_sl_l.Name = "btn_sl_l";
            btn_sl_l.Size = new Size(88, 27);
            btn_sl_l.TabIndex = 5;
            btn_sl_l.UseVisualStyleBackColor = true;
            // 
            // lbl_sr_l
            // 
            lbl_sr_l.AutoSize = true;
            lbl_sr_l.Location = new Point(7, 127);
            lbl_sr_l.Margin = new Padding(4, 0, 4, 0);
            lbl_sr_l.Name = "lbl_sr_l";
            lbl_sr_l.Size = new Size(83, 15);
            lbl_sr_l.TabIndex = 8;
            lbl_sr_l.Text = "SR Left Joycon";
            lbl_sr_l.TextAlign = ContentAlignment.TopCenter;
            // 
            // btn_sr_l
            // 
            btn_sr_l.Location = new Point(111, 121);
            btn_sr_l.Margin = new Padding(4, 3, 4, 3);
            btn_sr_l.Name = "btn_sr_l";
            btn_sr_l.Size = new Size(88, 27);
            btn_sr_l.TabIndex = 7;
            btn_sr_l.UseVisualStyleBackColor = true;
            // 
            // lbl_sl_r
            // 
            lbl_sl_r.AutoSize = true;
            lbl_sl_r.Location = new Point(7, 160);
            lbl_sl_r.Margin = new Padding(4, 0, 4, 0);
            lbl_sl_r.Name = "lbl_sl_r";
            lbl_sl_r.Size = new Size(90, 15);
            lbl_sl_r.TabIndex = 10;
            lbl_sl_r.Text = "SL Right Joycon";
            lbl_sl_r.TextAlign = ContentAlignment.TopCenter;
            // 
            // btn_sl_r
            // 
            btn_sl_r.Location = new Point(111, 154);
            btn_sl_r.Margin = new Padding(4, 3, 4, 3);
            btn_sl_r.Name = "btn_sl_r";
            btn_sl_r.Size = new Size(88, 27);
            btn_sl_r.TabIndex = 9;
            btn_sl_r.UseVisualStyleBackColor = true;
            // 
            // lbl_sr_r
            // 
            lbl_sr_r.AutoSize = true;
            lbl_sr_r.Location = new Point(7, 193);
            lbl_sr_r.Margin = new Padding(4, 0, 4, 0);
            lbl_sr_r.Name = "lbl_sr_r";
            lbl_sr_r.Size = new Size(91, 15);
            lbl_sr_r.TabIndex = 12;
            lbl_sr_r.Text = "SR Right Joycon";
            lbl_sr_r.TextAlign = ContentAlignment.TopCenter;
            // 
            // btn_sr_r
            // 
            btn_sr_r.Location = new Point(111, 187);
            btn_sr_r.Margin = new Padding(4, 3, 4, 3);
            btn_sr_r.Name = "btn_sr_r";
            btn_sr_r.Size = new Size(88, 27);
            btn_sr_r.TabIndex = 11;
            btn_sr_r.UseVisualStyleBackColor = true;
            // 
            // btn_ok
            // 
            btn_ok.Location = new Point(125, 288);
            btn_ok.Margin = new Padding(4, 3, 4, 3);
            btn_ok.Name = "btn_ok";
            btn_ok.Size = new Size(88, 27);
            btn_ok.TabIndex = 13;
            btn_ok.Text = "OK";
            btn_ok.UseVisualStyleBackColor = true;
            btn_ok.Click += btn_ok_Click;
            // 
            // btn_apply
            // 
            btn_apply.Location = new Point(229, 288);
            btn_apply.Margin = new Padding(4, 3, 4, 3);
            btn_apply.Name = "btn_apply";
            btn_apply.Size = new Size(88, 27);
            btn_apply.TabIndex = 14;
            btn_apply.Text = "Apply";
            btn_apply.UseVisualStyleBackColor = true;
            btn_apply.Click += btn_apply_Click;
            // 
            // lbl_shake
            // 
            lbl_shake.AutoSize = true;
            lbl_shake.Location = new Point(7, 226);
            lbl_shake.Margin = new Padding(4, 0, 4, 0);
            lbl_shake.Name = "lbl_shake";
            lbl_shake.Size = new Size(38, 15);
            lbl_shake.TabIndex = 20;
            lbl_shake.Text = "Shake";
            lbl_shake.TextAlign = ContentAlignment.TopCenter;
            // 
            // btn_shake
            // 
            btn_shake.Location = new Point(111, 220);
            btn_shake.Margin = new Padding(4, 3, 4, 3);
            btn_shake.Name = "btn_shake";
            btn_shake.Size = new Size(88, 27);
            btn_shake.TabIndex = 19;
            btn_shake.UseVisualStyleBackColor = true;
            // 
            // gb_inputs
            // 
            gb_inputs.Controls.Add(lbl_capture);
            gb_inputs.Controls.Add(btn_capture);
            gb_inputs.Controls.Add(lbl_shake);
            gb_inputs.Controls.Add(btn_home);
            gb_inputs.Controls.Add(btn_shake);
            gb_inputs.Controls.Add(lbl_home);
            gb_inputs.Controls.Add(btn_sl_l);
            gb_inputs.Controls.Add(lbl_sl_l);
            gb_inputs.Controls.Add(btn_sr_l);
            gb_inputs.Controls.Add(lbl_sr_l);
            gb_inputs.Controls.Add(btn_sl_r);
            gb_inputs.Controls.Add(lbl_sl_r);
            gb_inputs.Controls.Add(lbl_sr_r);
            gb_inputs.Controls.Add(btn_sr_r);
            gb_inputs.Location = new Point(12, 12);
            gb_inputs.Name = "gb_inputs";
            gb_inputs.Size = new Size(206, 264);
            gb_inputs.TabIndex = 21;
            gb_inputs.TabStop = false;
            gb_inputs.Text = "Inputs";
            // 
            // gb_actions
            // 
            gb_actions.Controls.Add(lbl_reset_mouse);
            gb_actions.Controls.Add(btn_reset_mouse);
            gb_actions.Controls.Add(btn_active_gyro);
            gb_actions.Controls.Add(lbl_activate_gyro);
            gb_actions.Controls.Add(btn_swap_ab);
            gb_actions.Controls.Add(lbl_swap_ab);
            gb_actions.Controls.Add(btn_swap_xy);
            gb_actions.Controls.Add(lbl_swap_xy);
            gb_actions.Location = new Point(224, 12);
            gb_actions.Name = "gb_actions";
            gb_actions.Size = new Size(206, 264);
            gb_actions.TabIndex = 23;
            gb_actions.TabStop = false;
            gb_actions.Text = "Actions";
            // 
            // lbl_reset_mouse
            // 
            lbl_reset_mouse.AutoSize = true;
            lbl_reset_mouse.Location = new Point(7, 28);
            lbl_reset_mouse.Margin = new Padding(4, 0, 4, 0);
            lbl_reset_mouse.Name = "lbl_reset_mouse";
            lbl_reset_mouse.Size = new Size(88, 15);
            lbl_reset_mouse.TabIndex = 2;
            lbl_reset_mouse.Text = "Re-Centre Gyro";
            lbl_reset_mouse.TextAlign = ContentAlignment.TopCenter;
            // 
            // btn_reset_mouse
            // 
            btn_reset_mouse.Location = new Point(111, 22);
            btn_reset_mouse.Margin = new Padding(4, 3, 4, 3);
            btn_reset_mouse.Name = "btn_reset_mouse";
            btn_reset_mouse.Size = new Size(88, 27);
            btn_reset_mouse.TabIndex = 0;
            btn_reset_mouse.UseVisualStyleBackColor = true;
            // 
            // btn_active_gyro
            // 
            btn_active_gyro.Location = new Point(111, 55);
            btn_active_gyro.Margin = new Padding(4, 3, 4, 3);
            btn_active_gyro.Name = "btn_active_gyro";
            btn_active_gyro.Size = new Size(88, 27);
            btn_active_gyro.TabIndex = 3;
            btn_active_gyro.UseVisualStyleBackColor = true;
            // 
            // lbl_activate_gyro
            // 
            lbl_activate_gyro.AutoSize = true;
            lbl_activate_gyro.Location = new Point(7, 61);
            lbl_activate_gyro.Margin = new Padding(4, 0, 4, 0);
            lbl_activate_gyro.Name = "lbl_activate_gyro";
            lbl_activate_gyro.Size = new Size(78, 15);
            lbl_activate_gyro.TabIndex = 4;
            lbl_activate_gyro.Text = "Activate Gyro";
            lbl_activate_gyro.TextAlign = ContentAlignment.TopCenter;
            // 
            // btn_swap_ab
            // 
            btn_swap_ab.Location = new Point(111, 88);
            btn_swap_ab.Margin = new Padding(4, 3, 4, 3);
            btn_swap_ab.Name = "btn_swap_ab";
            btn_swap_ab.Size = new Size(88, 27);
            btn_swap_ab.TabIndex = 5;
            btn_swap_ab.UseVisualStyleBackColor = true;
            // 
            // lbl_swap_ab
            // 
            lbl_swap_ab.AutoSize = true;
            lbl_swap_ab.Location = new Point(7, 94);
            lbl_swap_ab.Margin = new Padding(4, 0, 4, 0);
            lbl_swap_ab.Name = "lbl_swap_ab";
            lbl_swap_ab.Size = new Size(53, 15);
            lbl_swap_ab.TabIndex = 6;
            lbl_swap_ab.Text = "Swap AB";
            lbl_swap_ab.TextAlign = ContentAlignment.TopCenter;
            // 
            // btn_swap_xy
            // 
            btn_swap_xy.Location = new Point(111, 121);
            btn_swap_xy.Margin = new Padding(4, 3, 4, 3);
            btn_swap_xy.Name = "btn_swap_xy";
            btn_swap_xy.Size = new Size(88, 27);
            btn_swap_xy.TabIndex = 7;
            btn_swap_xy.UseVisualStyleBackColor = true;
            // 
            // lbl_swap_xy
            // 
            lbl_swap_xy.AutoSize = true;
            lbl_swap_xy.Location = new Point(7, 127);
            lbl_swap_xy.Margin = new Padding(4, 0, 4, 0);
            lbl_swap_xy.Name = "lbl_swap_xy";
            lbl_swap_xy.Size = new Size(52, 15);
            lbl_swap_xy.TabIndex = 8;
            lbl_swap_xy.Text = "Swap XY";
            lbl_swap_xy.TextAlign = ContentAlignment.TopCenter;
            // 
            // Reassign
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(442, 324);
            Controls.Add(gb_actions);
            Controls.Add(gb_inputs);
            Controls.Add(btn_apply);
            Controls.Add(btn_ok);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            Icon = (Icon)resources.GetObject("$this.Icon");
            Margin = new Padding(4, 3, 4, 3);
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "Reassign";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Map Special Buttons";
            FormClosing += Reassign_FormClosing;
            Load += Reassign_Load;
            gb_inputs.ResumeLayout(false);
            gb_inputs.PerformLayout();
            gb_actions.ResumeLayout(false);
            gb_actions.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private SplitButton btn_capture;
		private System.Windows.Forms.Label lbl_capture;
		private System.Windows.Forms.Label lbl_home;
		private SplitButton btn_home;
		private System.Windows.Forms.Label lbl_sl_l;
		private SplitButton btn_sl_l;
		private System.Windows.Forms.Label lbl_sr_l;
		private SplitButton btn_sr_l;
		private System.Windows.Forms.Label lbl_sl_r;
		private SplitButton btn_sl_r;
		private System.Windows.Forms.Label lbl_sr_r;
		private SplitButton btn_sr_r;
		private Button btn_ok;
		private Button btn_apply;
		private System.Windows.Forms.ToolTip tip_reassign;
        private Label lbl_shake;
        private SplitButton btn_shake;
        private GroupBox gb_inputs;
        private GroupBox gb_actions;
        private Label lbl_reset_mouse;
        private SplitButton btn_reset_mouse;
        private SplitButton btn_active_gyro;
        private Label lbl_activate_gyro;
        private SplitButton btn_swap_ab;
        private Label lbl_swap_ab;
        private SplitButton btn_swap_xy;
        private Label lbl_swap_xy;
    }
}
