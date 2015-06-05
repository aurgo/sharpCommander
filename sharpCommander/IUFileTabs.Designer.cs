namespace sharpCommander
{
   partial class IUFileTabs
   {
      /// <summary> 
      /// Variable del diseñador requerida.
      /// </summary>
      private System.ComponentModel.IContainer components = null;

      /// <summary> 
      /// Limpiar los recursos que se estén utilizando.
      /// </summary>
      /// <param name="disposing">true si los recursos administrados se deben eliminar; false en caso contrario, false.</param>
      protected override void Dispose(bool disposing)
      {
         if (disposing && (components != null))
         {
            components.Dispose();
         }
         base.Dispose(disposing);
      }

      #region Código generado por el Diseñador de componentes

      /// <summary> 
      /// Método necesario para admitir el Diseñador. No se puede modificar 
      /// el contenido del método con el editor de código.
      /// </summary>
      private void InitializeComponent()
      {
         this.tabControl = new System.Windows.Forms.TabControl();
         this.tabPage1 = new System.Windows.Forms.TabPage();
         this.listView = new System.Windows.Forms.ListView();
         this.statusStrip = new System.Windows.Forms.StatusStrip();
         this.tsStatus = new System.Windows.Forms.ToolStripStatusLabel();
         this.splitContainer1 = new System.Windows.Forms.SplitContainer();
         this.panelDir = new System.Windows.Forms.Panel();
         this.comboDir = new System.Windows.Forms.ComboBox();
         this.panelButtons = new System.Windows.Forms.Panel();
         this.toolStripBtns = new System.Windows.Forms.ToolStrip();
         this.tsBtnAdd = new System.Windows.Forms.ToolStripButton();
         this.tsBtnDel = new System.Windows.Forms.ToolStripButton();
         this.tsBtnDrives = new System.Windows.Forms.ToolStripButton();
         this.tabControl.SuspendLayout();
         this.tabPage1.SuspendLayout();
         this.statusStrip.SuspendLayout();
         this.splitContainer1.Panel1.SuspendLayout();
         this.splitContainer1.Panel2.SuspendLayout();
         this.splitContainer1.SuspendLayout();
         this.panelDir.SuspendLayout();
         this.panelButtons.SuspendLayout();
         this.toolStripBtns.SuspendLayout();
         this.SuspendLayout();
         // 
         // tabControl
         // 
         this.tabControl.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                     | System.Windows.Forms.AnchorStyles.Left)
                     | System.Windows.Forms.AnchorStyles.Right)));
         this.tabControl.Controls.Add(this.tabPage1);
         this.tabControl.Location = new System.Drawing.Point(0, 28);
         this.tabControl.Name = "tabControl";
         this.tabControl.SelectedIndex = 0;
         this.tabControl.Size = new System.Drawing.Size(642, 409);
         this.tabControl.TabIndex = 0;
         this.tabControl.Selecting += new System.Windows.Forms.TabControlCancelEventHandler(this.tabControl_Selecting);
         this.tabControl.SelectedIndexChanged += new System.EventHandler(this.tabControl_SelectedIndexChanged);
         // 
         // tabPage1
         // 
         this.tabPage1.Controls.Add(this.listView);
         this.tabPage1.Location = new System.Drawing.Point(4, 22);
         this.tabPage1.Name = "tabPage1";
         this.tabPage1.Padding = new System.Windows.Forms.Padding(3);
         this.tabPage1.Size = new System.Drawing.Size(634, 383);
         this.tabPage1.TabIndex = 0;
         this.tabPage1.Text = "tabPage";
         this.tabPage1.UseVisualStyleBackColor = true;
         // 
         // listView
         // 
         this.listView.AllowColumnReorder = true;
         this.listView.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                     | System.Windows.Forms.AnchorStyles.Left)
                     | System.Windows.Forms.AnchorStyles.Right)));
         this.listView.FullRowSelect = true;
         this.listView.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.Nonclickable;
         this.listView.LabelEdit = true;
         this.listView.Location = new System.Drawing.Point(3, 3);
         this.listView.Name = "listView";
         this.listView.Size = new System.Drawing.Size(628, 359);
         this.listView.TabIndex = 0;
         this.listView.UseCompatibleStateImageBehavior = false;
         this.listView.View = System.Windows.Forms.View.Details;
         this.listView.VirtualMode = true;
         this.listView.DoubleClick += new System.EventHandler(this.listView_DoubleClick);
         this.listView.KeyDown += new System.Windows.Forms.KeyEventHandler(this.listView_KeyDown);
         // 
         // statusStrip
         // 
         this.statusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsStatus});
         this.statusStrip.Location = new System.Drawing.Point(0, 415);
         this.statusStrip.Name = "statusStrip";
         this.statusStrip.RenderMode = System.Windows.Forms.ToolStripRenderMode.Professional;
         this.statusStrip.Size = new System.Drawing.Size(642, 22);
         this.statusStrip.TabIndex = 3;
         // 
         // tsStatus
         // 
         this.tsStatus.Name = "tsStatus";
         this.tsStatus.Size = new System.Drawing.Size(0, 17);
         // 
         // splitContainer1
         // 
         this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Top;
         this.splitContainer1.Location = new System.Drawing.Point(0, 0);
         this.splitContainer1.Name = "splitContainer1";
         // 
         // splitContainer1.Panel1
         // 
         this.splitContainer1.Panel1.Controls.Add(this.panelDir);
         // 
         // splitContainer1.Panel2
         // 
         this.splitContainer1.Panel2.Controls.Add(this.panelButtons);
         this.splitContainer1.Size = new System.Drawing.Size(642, 26);
         this.splitContainer1.SplitterDistance = 525;
         this.splitContainer1.TabIndex = 4;
         // 
         // panelDir
         // 
         this.panelDir.Controls.Add(this.comboDir);
         this.panelDir.Dock = System.Windows.Forms.DockStyle.Top;
         this.panelDir.Location = new System.Drawing.Point(0, 0);
         this.panelDir.Name = "panelDir";
         this.panelDir.Size = new System.Drawing.Size(525, 26);
         this.panelDir.TabIndex = 2;
         // 
         // comboDir
         // 
         this.comboDir.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                     | System.Windows.Forms.AnchorStyles.Right)));
         this.comboDir.FormattingEnabled = true;
         this.comboDir.Location = new System.Drawing.Point(3, 2);
         this.comboDir.Name = "comboDir";
         this.comboDir.Size = new System.Drawing.Size(520, 21);
         this.comboDir.TabIndex = 0;
         this.comboDir.SelectedIndexChanged += new System.EventHandler(this.comboDir_SelectedValueChanged);
         this.comboDir.TextChanged += new System.EventHandler(this.comboDir_TextChanged);
         // 
         // panelButtons
         // 
         this.panelButtons.Controls.Add(this.toolStripBtns);
         this.panelButtons.Dock = System.Windows.Forms.DockStyle.Top;
         this.panelButtons.Location = new System.Drawing.Point(0, 0);
         this.panelButtons.Name = "panelButtons";
         this.panelButtons.Size = new System.Drawing.Size(113, 26);
         this.panelButtons.TabIndex = 4;
         // 
         // toolStripBtns
         // 
         this.toolStripBtns.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
         this.toolStripBtns.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsBtnAdd,
            this.tsBtnDel,
            this.tsBtnDrives});
         this.toolStripBtns.Location = new System.Drawing.Point(0, 0);
         this.toolStripBtns.Name = "toolStripBtns";
         this.toolStripBtns.Size = new System.Drawing.Size(113, 25);
         this.toolStripBtns.TabIndex = 0;
         this.toolStripBtns.Text = "toolStrip1";
         // 
         // tsBtnAdd
         // 
         this.tsBtnAdd.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
         this.tsBtnAdd.Image = global::sharpCommander.Properties.Resources.Add;
         this.tsBtnAdd.ImageTransparentColor = System.Drawing.Color.Magenta;
         this.tsBtnAdd.Name = "tsBtnAdd";
         this.tsBtnAdd.Size = new System.Drawing.Size(23, 22);
         this.tsBtnAdd.Text = "toolStripButton1";
         // 
         // tsBtnDel
         // 
         this.tsBtnDel.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
         this.tsBtnDel.Image = global::sharpCommander.Properties.Resources.Del;
         this.tsBtnDel.ImageTransparentColor = System.Drawing.Color.Magenta;
         this.tsBtnDel.Name = "tsBtnDel";
         this.tsBtnDel.Size = new System.Drawing.Size(23, 22);
         this.tsBtnDel.Text = "toolStripButton2";
         // 
         // tsBtnDrives
         // 
         this.tsBtnDrives.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
         this.tsBtnDrives.Image = global::sharpCommander.Properties.Resources.Disk;
         this.tsBtnDrives.ImageTransparentColor = System.Drawing.Color.Magenta;
         this.tsBtnDrives.Name = "tsBtnDrives";
         this.tsBtnDrives.Size = new System.Drawing.Size(23, 22);
         this.tsBtnDrives.Text = "toolStripButton1";
         this.tsBtnDrives.Click += new System.EventHandler(this.tsBtnDrives_Click);
         // 
         // IUFileTabs
         // 
         this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
         this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
         this.Controls.Add(this.splitContainer1);
         this.Controls.Add(this.statusStrip);
         this.Controls.Add(this.tabControl);
         this.Name = "IUFileTabs";
         this.Size = new System.Drawing.Size(642, 437);
         this.tabControl.ResumeLayout(false);
         this.tabPage1.ResumeLayout(false);
         this.statusStrip.ResumeLayout(false);
         this.statusStrip.PerformLayout();
         this.splitContainer1.Panel1.ResumeLayout(false);
         this.splitContainer1.Panel2.ResumeLayout(false);
         this.splitContainer1.ResumeLayout(false);
         this.panelDir.ResumeLayout(false);
         this.panelButtons.ResumeLayout(false);
         this.panelButtons.PerformLayout();
         this.toolStripBtns.ResumeLayout(false);
         this.toolStripBtns.PerformLayout();
         this.ResumeLayout(false);
         this.PerformLayout();

      }

      #endregion

      private System.Windows.Forms.TabControl tabControl;
      private System.Windows.Forms.TabPage tabPage1;
      private System.Windows.Forms.ListView listView;
      private System.Windows.Forms.StatusStrip statusStrip;
      private System.Windows.Forms.ToolStripStatusLabel tsStatus;
      private System.Windows.Forms.SplitContainer splitContainer1;
      private System.Windows.Forms.Panel panelDir;
      private System.Windows.Forms.ComboBox comboDir;
      private System.Windows.Forms.Panel panelButtons;
      private System.Windows.Forms.ToolStrip toolStripBtns;
      private System.Windows.Forms.ToolStripButton tsBtnAdd;
      private System.Windows.Forms.ToolStripButton tsBtnDel;
      private System.Windows.Forms.ToolStripButton tsBtnDrives;

   }
}
