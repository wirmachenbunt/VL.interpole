using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Reflection;
using System.Drawing;

namespace VVVV.Nodes
{
    partial class Interpole
    {
        string lastColorName = "Aqua";
         
        public static DialogResult ClipDialog(ref string input, ref int startFrame, ref int duration,ref int fadeIn,ref int fadeOut, ref string colorName, string DialogName, float dpi)
        {
            System.Drawing.Size size = new System.Drawing.Size(250, 215);

            Form inputBox = new Form();

            inputBox.StartPosition = FormStartPosition.Manual;
            inputBox.Location = new Point(Cursor.Position.X, Cursor.Position.Y);

            inputBox.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            inputBox.BackColor = Color.FromArgb(50, 50, 50);
            inputBox.ClientSize = size;
            inputBox.Text = DialogName;

            System.Windows.Forms.TextBox textBox = new TextBox();
            textBox.Size = new System.Drawing.Size(size.Width - 20, 33);
            textBox.Location = new System.Drawing.Point(10, 10);
            textBox.Font = new Font("Consolas", 20, FontStyle.Regular);
            textBox.Text = input;
            textBox.TabStop = false;
            inputBox.Controls.Add(textBox);

            #region numerics_start_end
            System.Windows.Forms.NumericUpDown timeUpDown = new System.Windows.Forms.NumericUpDown();
            timeUpDown.Name = "Time(f)";
            timeUpDown.Location = new System.Drawing.Point(10, 62);
            timeUpDown.Size = new System.Drawing.Size(109, 20);
            timeUpDown.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            timeUpDown.Maximum = Decimal.MaxValue;
            timeUpDown.Value = startFrame;
            timeUpDown.TabIndex = 0;
            inputBox.Controls.Add(timeUpDown);

            System.Windows.Forms.Label framesLabel = new System.Windows.Forms.Label();
            framesLabel.Location = new System.Drawing.Point(10, 83);
            framesLabel.Size = new System.Drawing.Size(100, 15);
            framesLabel.Text = "StartFrame";
            framesLabel.ForeColor = Color.FromArgb(110, 110, 110);
            inputBox.Controls.Add(framesLabel);

            System.Windows.Forms.NumericUpDown timeUpDownend = new System.Windows.Forms.NumericUpDown();
            timeUpDownend.Name = "Time(f)";
            timeUpDownend.Location = new System.Drawing.Point(130, 62);
            timeUpDownend.Size = new System.Drawing.Size(109, 20);
            timeUpDownend.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            timeUpDownend.Maximum = Decimal.MaxValue;
            timeUpDownend.Value = duration;
            //timeUpDownend.BackColor = Color.FromArgb(50, 50, 50);
            timeUpDownend.TabIndex = 0;
            inputBox.Controls.Add(timeUpDownend);

            System.Windows.Forms.Label framesLabelend = new System.Windows.Forms.Label();
            framesLabelend.Location = new System.Drawing.Point(130, 83);
            framesLabelend.Size = new System.Drawing.Size(100, 15);
            framesLabelend.Text = "Duration in frames";
            framesLabelend.ForeColor = Color.FromArgb(110, 110, 110);
            inputBox.Controls.Add(framesLabelend);
            #endregion

            #region numerics_fade
            System.Windows.Forms.NumericUpDown fadeInNum = new System.Windows.Forms.NumericUpDown();
            fadeInNum.Location = new System.Drawing.Point(10, 105);
            fadeInNum.Size = new System.Drawing.Size(109, 20);
            fadeInNum.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            fadeInNum.Maximum = Decimal.MaxValue;
            fadeInNum.Value = fadeIn;
            fadeInNum.TabIndex = 0;
            inputBox.Controls.Add(fadeInNum);

            System.Windows.Forms.Label fadeInLabel = new System.Windows.Forms.Label();
            fadeInLabel.Location = new System.Drawing.Point(10, 127);
            fadeInLabel.Size = new System.Drawing.Size(100, 15);
            fadeInLabel.Text = "FadeIn in frames";
            fadeInLabel.ForeColor = Color.FromArgb(110, 110, 110);
            inputBox.Controls.Add(fadeInLabel);

            System.Windows.Forms.NumericUpDown fadeOutNum = new System.Windows.Forms.NumericUpDown();
            fadeOutNum.Location = new System.Drawing.Point(130, 105);
            fadeOutNum.Size = new System.Drawing.Size(109, 20);
            fadeOutNum.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            fadeOutNum.Maximum = Decimal.MaxValue;
            fadeOutNum.Value = fadeOut;
            //timeUpDownend.BackColor = Color.FromArgb(50, 50, 50);
            fadeOutNum.TabIndex = 0;
            inputBox.Controls.Add(fadeOutNum);

            System.Windows.Forms.Label fadeOutLabel = new System.Windows.Forms.Label();
            fadeOutLabel.Location = new System.Drawing.Point(130, 127);
            fadeOutLabel.Size = new System.Drawing.Size(100, 15);
            fadeOutLabel.Text = "FadeOut in frames";
            fadeOutLabel.ForeColor = Color.FromArgb(110, 110, 110);
            inputBox.Controls.Add(fadeOutLabel); 
            #endregion


            System.Windows.Forms.ComboBox cmbboxClr = new System.Windows.Forms.ComboBox();
            cmbboxClr.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            cmbboxClr.DropDownWidth = size.Width - 10;
            cmbboxClr.ItemHeight = 20;
            cmbboxClr.Location = new System.Drawing.Point(10, 150);
            cmbboxClr.Name = "cmbboxClr";
            cmbboxClr.Size = new System.Drawing.Size(size.Width - 20, 20); 
            Type colorType = typeof(System.Drawing.Color);
            
            PropertyInfo[] propInfoList = colorType.GetProperties(BindingFlags.Static | BindingFlags.DeclaredOnly | BindingFlags.Public);
            foreach (PropertyInfo c in propInfoList)
            {
                cmbboxClr.Items.Add(c.Name);
            } 
            
            cmbboxClr.SelectedItem = colorName;

            #region buttons
            Button okButton = new Button();
            okButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            okButton.Name = "okButton";
            okButton.Size = new System.Drawing.Size(120, 23);
            okButton.Text = "&Ok";
            okButton.Location = new System.Drawing.Point(10, 183);
            okButton.FlatStyle = FlatStyle.Flat;
            okButton.ForeColor = Color.FromArgb(200, 200, 200);
            okButton.FlatAppearance.BorderColor = Color.FromArgb(0, 255, 255, 255);
            okButton.FlatAppearance.BorderSize = 0;
            okButton.TabStop = false;
            inputBox.Controls.Add(okButton);

            Button cancelButton = new Button();
            cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new System.Drawing.Size(120, 23);
            cancelButton.Text = "&Cancel";
            cancelButton.Location = new System.Drawing.Point(size.Width - 130, 183);
            cancelButton.FlatStyle = FlatStyle.Flat;
            cancelButton.FlatAppearance.BorderColor = Color.FromArgb(0, 255, 255, 255);
            cancelButton.TabStop = false;
            cancelButton.FlatAppearance.BorderSize = 0;
            cancelButton.ForeColor = Color.FromArgb(200, 200, 200);


            inputBox.Controls.Add(cancelButton);
            inputBox.Controls.Add(cmbboxClr);
            inputBox.AcceptButton = okButton;
            inputBox.CancelButton = cancelButton; 
            #endregion

            inputBox.Scale(dpi);

            DialogResult result = inputBox.ShowDialog();
            if (textBox.Text.Length > 0) input = textBox.Text;
            else input = "empty";
            startFrame = Convert.ToInt32(timeUpDown.Value);
            duration = Convert.ToInt32(timeUpDownend.Value);
            fadeIn = Convert.ToInt32(fadeInNum.Value);
            fadeOut = Convert.ToInt32(fadeOutNum.Value);

            colorName = cmbboxClr.SelectedItem.ToString();
            return result;
        }

        public static DialogResult TimecodeDialog(ref string TimeCode, string DialogName)
        {
            System.Drawing.Size size = new System.Drawing.Size(190, 90);

            Form timecodeWindow = new Form();

            timecodeWindow.StartPosition = FormStartPosition.Manual;
            timecodeWindow.Location = new Point(Cursor.Position.X, Cursor.Position.Y);

            timecodeWindow.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            timecodeWindow.BackColor = Color.FromArgb(50, 50, 50);
            timecodeWindow.ClientSize = size;
            timecodeWindow.Text = DialogName;

            System.Windows.Forms.TextBox textBox = new TextBox();
            textBox.Size = new System.Drawing.Size(size.Width - 10, 23);
            textBox.Location = new System.Drawing.Point(5, 5);
            textBox.Font = new Font("Consolas", 20, FontStyle.Regular);
            textBox.TabStop = false;

            textBox.Text = TimeCode;
            
            timecodeWindow.Controls.Add(textBox);

            System.Windows.Forms.Label timeCodeLabel = new System.Windows.Forms.Label();
            timeCodeLabel.Location = new System.Drawing.Point(5, 45);
            timeCodeLabel.Size = new System.Drawing.Size(200, 15);
            timeCodeLabel.Text = "hh:mm:ss:ff";
            timeCodeLabel.ForeColor = Color.FromArgb(110, 110, 110);
            timecodeWindow.Controls.Add(timeCodeLabel);

            #region buttons
            Button okButton = new Button();
            okButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            okButton.Name = "okButton";
            okButton.Size = new System.Drawing.Size(90, 23);
            okButton.Text = "&OK";
            okButton.Location = new System.Drawing.Point(5, 63);
            okButton.FlatStyle = FlatStyle.Flat;
            okButton.FlatAppearance.BorderSize = 0;
            okButton.ForeColor = Color.FromArgb(200, 200, 200);
            okButton.FlatAppearance.BorderColor = Color.FromArgb(0, 255, 255, 255);
            timecodeWindow.Controls.Add(okButton);

            Button cancelButton = new Button();
            cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new System.Drawing.Size(90, 23);
            cancelButton.Text = "&Cancel";
            cancelButton.Location = new System.Drawing.Point(size.Width - 95, 63);
            cancelButton.FlatStyle = FlatStyle.Flat;
            cancelButton.FlatAppearance.BorderSize = 0;
            cancelButton.FlatAppearance.BorderColor = Color.FromArgb(0, 255, 255, 255);
            cancelButton.ForeColor = Color.FromArgb(200, 200, 200);


            timecodeWindow.Controls.Add(cancelButton);
            timecodeWindow.AcceptButton = okButton;
            timecodeWindow.CancelButton = cancelButton;
            #endregion

            DialogResult myresult = timecodeWindow.ShowDialog();

            TimeCode = textBox.Text;
            return myresult;
        }

        
    }

}
