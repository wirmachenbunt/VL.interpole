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
         
        public static DialogResult ClipDialog(ref string input, ref int startFrame, ref int duration, ref string colorName, string DialogName, float dpi)
        {
            System.Drawing.Size size = new System.Drawing.Size(200, 165);

            Form inputBox = new Form();

            inputBox.StartPosition = FormStartPosition.CenterScreen;

            inputBox.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            inputBox.ClientSize = size;
            inputBox.Text = DialogName;

            System.Windows.Forms.TextBox textBox = new TextBox();
            textBox.Size = new System.Drawing.Size(size.Width - 10, 23);
            textBox.Location = new System.Drawing.Point(5, 5);
            textBox.Text = input;
            inputBox.Controls.Add(textBox);

            #region numerics
            System.Windows.Forms.NumericUpDown timeUpDown = new System.Windows.Forms.NumericUpDown();
            timeUpDown.Name = "Time(f)";
            timeUpDown.Location = new System.Drawing.Point(5, 39);
            timeUpDown.Size = new System.Drawing.Size(80, 20);
            timeUpDown.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            timeUpDown.Maximum = Decimal.MaxValue;
            timeUpDown.Value = startFrame;
            timeUpDown.TabIndex = 0;
            inputBox.Controls.Add(timeUpDown);

            System.Windows.Forms.Label framesLabel = new System.Windows.Forms.Label();
            framesLabel.Location = new System.Drawing.Point(88, 42);
            framesLabel.Size = new System.Drawing.Size(100, 23);
            framesLabel.Text = "StartFrame";
            inputBox.Controls.Add(framesLabel);

            System.Windows.Forms.NumericUpDown timeUpDownend = new System.Windows.Forms.NumericUpDown();
            timeUpDownend.Name = "Time(f)";
            timeUpDownend.Location = new System.Drawing.Point(5, 69);
            timeUpDownend.Size = new System.Drawing.Size(80, 20);
            timeUpDownend.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            timeUpDownend.Maximum = Decimal.MaxValue;
            timeUpDownend.Value = duration;
            timeUpDownend.TabIndex = 0;
            inputBox.Controls.Add(timeUpDownend);

            System.Windows.Forms.Label framesLabelend = new System.Windows.Forms.Label();
            framesLabelend.Location = new System.Drawing.Point(88, 72);
            framesLabelend.Size = new System.Drawing.Size(100, 23);
            framesLabelend.Text = "Duration (f)";
            inputBox.Controls.Add(framesLabelend);
            #endregion

            System.Windows.Forms.ComboBox cmbboxClr = new System.Windows.Forms.ComboBox();
            cmbboxClr.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            cmbboxClr.DropDownWidth = size.Width - 10;
            cmbboxClr.ItemHeight = 20;
            cmbboxClr.Location = new System.Drawing.Point(5, 100);
            cmbboxClr.Name = "cmbboxClr";
            cmbboxClr.Size = new System.Drawing.Size(size.Width - 10, 20); 
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
            okButton.Size = new System.Drawing.Size(90, 23);
            okButton.Text = "&OK";
            okButton.Location = new System.Drawing.Point(5, 133);
            inputBox.Controls.Add(okButton);

            Button cancelButton = new Button();
            cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new System.Drawing.Size(90, 23);
            cancelButton.Text = "&Cancel";
            cancelButton.Location = new System.Drawing.Point(size.Width - 95, 133);


            inputBox.Controls.Add(cancelButton);
            inputBox.Controls.Add(cmbboxClr);
            inputBox.AcceptButton = okButton;
            inputBox.CancelButton = cancelButton; 
            #endregion

            inputBox.Scale(dpi);

            DialogResult result = inputBox.ShowDialog();
            if (textBox.Text.Length > 0) input = textBox.Text;
            else input = "empty";
            startFrame = Convert.ToInt16(timeUpDown.Value);
            duration = Convert.ToInt16(timeUpDownend.Value);

            colorName = cmbboxClr.SelectedItem.ToString();
            return result;
        }

    }

}
