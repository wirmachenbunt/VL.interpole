namespace WinFormApp
{
  partial class Form1
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
      if (disposing && (components != null))
      {
        components.Dispose();
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
      this.components = new System.ComponentModel.Container();
      GraphicsControl.CanvasGDIplus canvasGDIplus1 = new GraphicsControl.CanvasGDIplus();
      this.groupBox1 = new System.Windows.Forms.GroupBox();
      this.rbDirect2D = new System.Windows.Forms.RadioButton();
      this.rbGDI = new System.Windows.Forms.RadioButton();
      this.timer1 = new System.Windows.Forms.Timer(this.components);
      this.groupBox2 = new System.Windows.Forms.GroupBox();
      this.cbPolygon = new System.Windows.Forms.CheckBox();
      this.cbRectangle = new System.Windows.Forms.CheckBox();
      this.cbText = new System.Windows.Forms.CheckBox();
      this.cbEllipse = new System.Windows.Forms.CheckBox();
      this.cbLine = new System.Windows.Forms.CheckBox();
      this.cbAntiAlias = new System.Windows.Forms.CheckBox();
      this.tbDensity = new System.Windows.Forms.TrackBar();
      this.label1 = new System.Windows.Forms.Label();
      this.winFormControl1 = new GraphicsControl.WinFormControl();
      this.groupBox1.SuspendLayout();
      this.groupBox2.SuspendLayout();
      ((System.ComponentModel.ISupportInitialize)(this.tbDensity)).BeginInit();
      this.SuspendLayout();
      // 
      // groupBox1
      // 
      this.groupBox1.Controls.Add(this.rbDirect2D);
      this.groupBox1.Controls.Add(this.rbGDI);
      this.groupBox1.Location = new System.Drawing.Point(12, 12);
      this.groupBox1.Name = "groupBox1";
      this.groupBox1.Size = new System.Drawing.Size(150, 39);
      this.groupBox1.TabIndex = 1;
      this.groupBox1.TabStop = false;
      this.groupBox1.Text = "Canvas Type";
      // 
      // rbDirect2D
      // 
      this.rbDirect2D.AutoSize = true;
      this.rbDirect2D.Location = new System.Drawing.Point(73, 16);
      this.rbDirect2D.Name = "rbDirect2D";
      this.rbDirect2D.Size = new System.Drawing.Size(67, 17);
      this.rbDirect2D.TabIndex = 1;
      this.rbDirect2D.Text = "Direct2D";
      this.rbDirect2D.UseVisualStyleBackColor = true;
      this.rbDirect2D.CheckedChanged += new System.EventHandler(this.rbCanvas_CheckedChanged);
      // 
      // rbGDI
      // 
      this.rbGDI.AutoSize = true;
      this.rbGDI.Checked = true;
      this.rbGDI.Location = new System.Drawing.Point(17, 16);
      this.rbGDI.Name = "rbGDI";
      this.rbGDI.Size = new System.Drawing.Size(50, 17);
      this.rbGDI.TabIndex = 0;
      this.rbGDI.TabStop = true;
      this.rbGDI.Text = "GDI+";
      this.rbGDI.UseVisualStyleBackColor = true;
      // 
      // timer1
      // 
      this.timer1.Interval = 10;
      this.timer1.Tick += new System.EventHandler(this.timer1_Tick);
      // 
      // groupBox2
      // 
      this.groupBox2.Controls.Add(this.cbPolygon);
      this.groupBox2.Controls.Add(this.cbRectangle);
      this.groupBox2.Controls.Add(this.cbText);
      this.groupBox2.Controls.Add(this.cbEllipse);
      this.groupBox2.Controls.Add(this.cbLine);
      this.groupBox2.Location = new System.Drawing.Point(168, 12);
      this.groupBox2.Name = "groupBox2";
      this.groupBox2.Size = new System.Drawing.Size(329, 39);
      this.groupBox2.TabIndex = 2;
      this.groupBox2.TabStop = false;
      this.groupBox2.Text = "Paint Elements";
      // 
      // cbPolygon
      // 
      this.cbPolygon.AutoSize = true;
      this.cbPolygon.Checked = true;
      this.cbPolygon.CheckState = System.Windows.Forms.CheckState.Checked;
      this.cbPolygon.Location = new System.Drawing.Point(254, 16);
      this.cbPolygon.Name = "cbPolygon";
      this.cbPolygon.Size = new System.Drawing.Size(64, 17);
      this.cbPolygon.TabIndex = 7;
      this.cbPolygon.Text = "Polygon";
      this.cbPolygon.UseVisualStyleBackColor = true;
      this.cbPolygon.CheckedChanged += new System.EventHandler(this.cbElements_CheckedChanged);
      // 
      // cbRectangle
      // 
      this.cbRectangle.AutoSize = true;
      this.cbRectangle.Checked = true;
      this.cbRectangle.CheckState = System.Windows.Forms.CheckState.Checked;
      this.cbRectangle.Location = new System.Drawing.Point(173, 16);
      this.cbRectangle.Name = "cbRectangle";
      this.cbRectangle.Size = new System.Drawing.Size(75, 17);
      this.cbRectangle.TabIndex = 6;
      this.cbRectangle.Text = "Rectangle";
      this.cbRectangle.UseVisualStyleBackColor = true;
      this.cbRectangle.CheckedChanged += new System.EventHandler(this.cbElements_CheckedChanged);
      // 
      // cbText
      // 
      this.cbText.AutoSize = true;
      this.cbText.Checked = true;
      this.cbText.CheckState = System.Windows.Forms.CheckState.Checked;
      this.cbText.Location = new System.Drawing.Point(120, 16);
      this.cbText.Name = "cbText";
      this.cbText.Size = new System.Drawing.Size(47, 17);
      this.cbText.TabIndex = 5;
      this.cbText.Text = "Text";
      this.cbText.UseVisualStyleBackColor = true;
      this.cbText.CheckedChanged += new System.EventHandler(this.cbElements_CheckedChanged);
      // 
      // cbEllipse
      // 
      this.cbEllipse.AutoSize = true;
      this.cbEllipse.Checked = true;
      this.cbEllipse.CheckState = System.Windows.Forms.CheckState.Checked;
      this.cbEllipse.Location = new System.Drawing.Point(58, 16);
      this.cbEllipse.Name = "cbEllipse";
      this.cbEllipse.Size = new System.Drawing.Size(56, 17);
      this.cbEllipse.TabIndex = 4;
      this.cbEllipse.Text = "Ellipse";
      this.cbEllipse.UseVisualStyleBackColor = true;
      this.cbEllipse.CheckedChanged += new System.EventHandler(this.cbElements_CheckedChanged);
      // 
      // cbLine
      // 
      this.cbLine.AutoSize = true;
      this.cbLine.Checked = true;
      this.cbLine.CheckState = System.Windows.Forms.CheckState.Checked;
      this.cbLine.Location = new System.Drawing.Point(6, 16);
      this.cbLine.Name = "cbLine";
      this.cbLine.Size = new System.Drawing.Size(46, 17);
      this.cbLine.TabIndex = 3;
      this.cbLine.Text = "Line";
      this.cbLine.UseVisualStyleBackColor = true;
      this.cbLine.CheckedChanged += new System.EventHandler(this.cbElements_CheckedChanged);
      // 
      // cbAntiAlias
      // 
      this.cbAntiAlias.AutoSize = true;
      this.cbAntiAlias.Location = new System.Drawing.Point(543, 29);
      this.cbAntiAlias.Name = "cbAntiAlias";
      this.cbAntiAlias.Size = new System.Drawing.Size(66, 17);
      this.cbAntiAlias.TabIndex = 4;
      this.cbAntiAlias.Text = "AntiAlias";
      this.cbAntiAlias.UseVisualStyleBackColor = true;
      this.cbAntiAlias.CheckedChanged += new System.EventHandler(this.cbAntiAlias_CheckedChanged);
      // 
      // tbDensity
      // 
      this.tbDensity.LargeChange = 10;
      this.tbDensity.Location = new System.Drawing.Point(615, 23);
      this.tbDensity.Maximum = 50;
      this.tbDensity.Name = "tbDensity";
      this.tbDensity.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
      this.tbDensity.Size = new System.Drawing.Size(162, 42);
      this.tbDensity.SmallChange = 5;
      this.tbDensity.TabIndex = 5;
      this.tbDensity.Scroll += new System.EventHandler(this.tbDensity_Scroll);
      // 
      // label1
      // 
      this.label1.AutoSize = true;
      this.label1.Location = new System.Drawing.Point(642, 7);
      this.label1.Name = "label1";
      this.label1.Size = new System.Drawing.Size(86, 13);
      this.label1.TabIndex = 6;
      this.label1.Text = "Element Density:";
      // 
      // winFormControl1
      // 
      this.winFormControl1.BackColor = System.Drawing.Color.White;
      canvasGDIplus1.AntiAlias = false;
      this.winFormControl1.Canvas = canvasGDIplus1;
      this.winFormControl1.Dock = System.Windows.Forms.DockStyle.Bottom;
      this.winFormControl1.ElementCount = 5240;
      this.winFormControl1.ElementDensity = 0;
      this.winFormControl1.Location = new System.Drawing.Point(0, 71);
      this.winFormControl1.Name = "winFormControl1";
      this.winFormControl1.Size = new System.Drawing.Size(789, 476);
      this.winFormControl1.TabIndex = 3;
      this.winFormControl1.UseBuffer = true;
      // 
      // Form1
      // 
      this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
      this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
      this.ClientSize = new System.Drawing.Size(789, 547);
      this.Controls.Add(this.label1);
      this.Controls.Add(this.tbDensity);
      this.Controls.Add(this.cbAntiAlias);
      this.Controls.Add(this.winFormControl1);
      this.Controls.Add(this.groupBox2);
      this.Controls.Add(this.groupBox1);
      this.Name = "Form1";
      this.Text = "Form1";
      this.Load += new System.EventHandler(this.Form1_Load);
      this.groupBox1.ResumeLayout(false);
      this.groupBox1.PerformLayout();
      this.groupBox2.ResumeLayout(false);
      this.groupBox2.PerformLayout();
      ((System.ComponentModel.ISupportInitialize)(this.tbDensity)).EndInit();
      this.ResumeLayout(false);
      this.PerformLayout();

    }

    #endregion

    private System.Windows.Forms.GroupBox groupBox1;
    private System.Windows.Forms.RadioButton rbDirect2D;
    private System.Windows.Forms.RadioButton rbGDI;
    private System.Windows.Forms.Timer timer1;
    private System.Windows.Forms.GroupBox groupBox2;
    private System.Windows.Forms.CheckBox cbRectangle;
    private System.Windows.Forms.CheckBox cbText;
    private System.Windows.Forms.CheckBox cbEllipse;
    private System.Windows.Forms.CheckBox cbLine;
    private GraphicsControl.WinFormControl winFormControl1;
    private System.Windows.Forms.CheckBox cbAntiAlias;
    private System.Windows.Forms.CheckBox cbPolygon;
    private System.Windows.Forms.TrackBar tbDensity;
    private System.Windows.Forms.Label label1;
  }
}

