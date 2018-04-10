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
            this.customLabel1 = new customWinformsControl.CustomLabel();
            this.SuspendLayout();
            // 
            // customLabel1
            // 
            this.customLabel1.BackColor = System.Drawing.SystemColors.ControlDarkDark;
            this.customLabel1.ForeColor = System.Drawing.SystemColors.ButtonFace;
            this.customLabel1.Location = new System.Drawing.Point(365, 289);
            this.customLabel1.Name = "customLabel1";
            this.customLabel1.Size = new System.Drawing.Size(138, 22);
            this.customLabel1.TabIndex = 3;
            this.customLabel1.Text = "0";
            this.customLabel1.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(632, 492);
            this.Controls.Add(this.customLabel1);
            this.Name = "Form1";
            this.Text = "Form1";
            this.ResumeLayout(false);

        }

        #endregion
        private CustomLabel customLabel1;
    }
}

