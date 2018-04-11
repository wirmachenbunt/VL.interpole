namespace customWinformsControl
{
    partial class Form1
    {
        /// <summary>
        /// Erforderliche Designervariable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Verwendete Ressourcen bereinigen.
        /// </summary>
        /// <param name="disposing">True, wenn verwaltete Ressourcen gelöscht werden sollen; andernfalls False.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Vom Windows Form-Designer generierter Code

        /// <summary>
        /// Erforderliche Methode für die Designerunterstützung.
        /// Der Inhalt der Methode darf nicht mit dem Code-Editor geändert werden.
        /// </summary>
        private void InitializeComponent()
        {
            this.textBox1 = new System.Windows.Forms.TextBox();
            this.customLabel1 = new customWinformsControl.CustomLabel();
            this.customTextbox1 = new customWinformsControl.CustomTextbox();
            this.SuspendLayout();
            // 
            // textBox1
            // 
            this.textBox1.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.textBox1.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.textBox1.Location = new System.Drawing.Point(391, 203);
            this.textBox1.Name = "textBox1";
            this.textBox1.Size = new System.Drawing.Size(100, 15);
            this.textBox1.TabIndex = 5;
            // 
            // customLabel1
            // 
            this.customLabel1.BackColor = System.Drawing.SystemColors.ControlDarkDark;
            this.customLabel1.ForeColor = System.Drawing.SystemColors.ButtonFace;
            this.customLabel1.Location = new System.Drawing.Point(241, 247);
            this.customLabel1.Name = "customLabel1";
            this.customLabel1.Size = new System.Drawing.Size(138, 22);
            this.customLabel1.TabIndex = 3;
            this.customLabel1.Text = "0";
            this.customLabel1.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // customTextbox1
            // 
            this.customTextbox1.BackColor = System.Drawing.SystemColors.ControlDarkDark;
            this.customTextbox1.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.customTextbox1.Cursor = System.Windows.Forms.Cursors.Arrow;
            this.customTextbox1.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.customTextbox1.Location = new System.Drawing.Point(231, 135);
            this.customTextbox1.Name = "customTextbox1";
            this.customTextbox1.Size = new System.Drawing.Size(100, 13);
            this.customTextbox1.TabIndex = 6;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(632, 492);
            this.Controls.Add(this.customTextbox1);
            this.Controls.Add(this.textBox1);
            this.Controls.Add(this.customLabel1);
            this.Name = "Form1";
            this.Text = "Form1";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private CustomLabel customLabel1;
        private System.Windows.Forms.TextBox textBox1;
        private CustomTextbox customTextbox1;
    }
}

