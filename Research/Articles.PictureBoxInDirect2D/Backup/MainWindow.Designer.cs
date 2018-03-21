namespace Articles.PictureBoxInDirect2D
{
    partial class MainWindow
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainWindow));
            this.direct2DPictureBox1 = new Testing.FrequentDirect2DDRawing.Direct2DPictureBox();
            this.SuspendLayout();
            // 
            // direct2DPictureBox1
            // 
            this.direct2DPictureBox1.BackColor = System.Drawing.Color.White;
            this.direct2DPictureBox1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.direct2DPictureBox1.Image = global::Articles.PictureBoxInDirect2D.Properties.Resources.Logo;
            this.direct2DPictureBox1.Location = new System.Drawing.Point(0, 0);
            this.direct2DPictureBox1.Name = "direct2DPictureBox1";
            this.direct2DPictureBox1.Size = new System.Drawing.Size(433, 280);
            this.direct2DPictureBox1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.CenterImage;
            this.direct2DPictureBox1.TabIndex = 0;
            this.direct2DPictureBox1.Text = "direct2DPictureBox1";
            // 
            // MainWindow
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(433, 280);
            this.Controls.Add(this.direct2DPictureBox1);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "MainWindow";
            this.Text = "www.rkoenig.eu - Direct2DPictureBox";
            this.ResumeLayout(false);

        }

        #endregion

        private Testing.FrequentDirect2DDRawing.Direct2DPictureBox direct2DPictureBox1;
    }
}

