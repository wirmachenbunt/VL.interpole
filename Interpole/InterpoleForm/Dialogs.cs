using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Reflection;
using System.Drawing;
using System.Diagnostics;
using System.IO;

namespace Interpole
{
    partial class InterpoleBase
    {
        string lastColorName = "Aqua";
        SaveFileDialog saveFileDialog1 = new SaveFileDialog();
        OpenFileDialog openFileDialog1 = new OpenFileDialog();

        public static DialogResult ClipDialog(ref string input, ref int startFrame, ref int duration, ref int fadeIn, ref int fadeOut, ref string colorName, ref string varInput, string DialogName)
        {
            System.Drawing.Size size = new System.Drawing.Size(250, 248);

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

            System.Windows.Forms.TextBox varTextBox = new System.Windows.Forms.TextBox();
            varTextBox.Size = new System.Drawing.Size(size.Width - 20, 15);
            varTextBox.Location = new System.Drawing.Point(10, 150);
            varTextBox.Font = new Font("Consolas", 11, FontStyle.Regular);
            varTextBox.Text = varInput;
            varTextBox.TabStop = false;
            inputBox.Controls.Add(varTextBox);

            System.Windows.Forms.ComboBox cmbboxClr = new System.Windows.Forms.ComboBox();
            cmbboxClr.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            cmbboxClr.DropDownWidth = size.Width - 10;
            cmbboxClr.ItemHeight = 20;
            cmbboxClr.Location = new System.Drawing.Point(10, 183);
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
            okButton.Location = new System.Drawing.Point(10, 216);
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
            cancelButton.Location = new System.Drawing.Point(size.Width - 130, 216);
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

            inputBox.Scale(1);

            DialogResult result = inputBox.ShowDialog();
            if (textBox.Text.Length > 0) input = textBox.Text;
            else input = "empty";
            startFrame = Convert.ToInt32(timeUpDown.Value);
            duration = Convert.ToInt32(timeUpDownend.Value);
            fadeIn = Convert.ToInt32(fadeInNum.Value);
            fadeOut = Convert.ToInt32(fadeOutNum.Value);
            varInput = (varTextBox.Text == "string variable" || varTextBox.Text.Length == 0) ? "" : varTextBox.Text;

            colorName = cmbboxClr.SelectedItem.ToString();
            return result;
        }

        public static DialogResult TimecodeDialog(ref string TimeCode, string DialogName, ref int fps, ref bool Loop)
        {
            Size size = new System.Drawing.Size(190, 120);

            Form timecodeWindow = new Form();

            timecodeWindow.StartPosition = FormStartPosition.Manual;
            timecodeWindow.Location = new Point(Cursor.Position.X, Cursor.Position.Y);

            timecodeWindow.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            timecodeWindow.BackColor = Color.FromArgb(50, 50, 50);
            timecodeWindow.ClientSize = size;
            timecodeWindow.Text = DialogName;

            TextBox textBox = new TextBox();
            textBox.Size = new System.Drawing.Size(size.Width - 10, 23);
            textBox.Location = new System.Drawing.Point(5, 5);
            textBox.Font = new Font("Consolas", 20, FontStyle.Regular);
            textBox.TabStop = false;
            textBox.Text = TimeCode;
            timecodeWindow.Controls.Add(textBox);

            Label timeCodeLabel = new System.Windows.Forms.Label();
            timeCodeLabel.Location = new System.Drawing.Point(60, 59);
            timeCodeLabel.Size = new System.Drawing.Size(30, 15);
            timeCodeLabel.Text = "FPS";
            timeCodeLabel.ForeColor = Color.FromArgb(110, 110, 110);
            timecodeWindow.Controls.Add(timeCodeLabel);

            NumericUpDown frameBase = new System.Windows.Forms.NumericUpDown();
            frameBase.Name = "FrameBase";
            frameBase.Location = new System.Drawing.Point(6, 56);
            frameBase.Size = new System.Drawing.Size(50, 20);
            frameBase.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            frameBase.Maximum = Decimal.MaxValue;
            frameBase.Value = fps;
            timecodeWindow.Controls.Add(frameBase);

            // 
            // checkBox1
            //
            CheckBox loopCheck = new System.Windows.Forms.CheckBox();
            loopCheck.Checked = Loop;
            loopCheck.Appearance = System.Windows.Forms.Appearance.Button;
            loopCheck.AutoSize = true;
            //loopCheck.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(224)))), ((int)(((byte)(224)))), ((int)(((byte)(224)))));
            loopCheck.BackColor = Color.FromArgb(100, 100, 100);
            loopCheck.FlatAppearance.CheckedBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(128)))), ((int)(((byte)(0)))));
            loopCheck.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            loopCheck.Location = new System.Drawing.Point(100, 54);
            loopCheck.Name = "checkBox1";
            loopCheck.Size = new System.Drawing.Size(41, 23);
            loopCheck.TabIndex = 1;
            loopCheck.Text = "Loop";
            loopCheck.UseVisualStyleBackColor = true;

            #region buttons
            Button okButton = new Button();
            okButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            okButton.Name = "okButton";
            okButton.Size = new System.Drawing.Size(90, 23);
            okButton.Text = "&OK";
            okButton.Location = new System.Drawing.Point(5, 93);
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
            cancelButton.Location = new System.Drawing.Point(size.Width - 95, 93);
            cancelButton.FlatStyle = FlatStyle.Flat;
            cancelButton.FlatAppearance.BorderSize = 0;
            cancelButton.FlatAppearance.BorderColor = Color.FromArgb(0, 255, 255, 255);
            cancelButton.ForeColor = Color.FromArgb(200, 200, 200);
            #endregion

            timecodeWindow.Controls.Add(loopCheck);
            timecodeWindow.Controls.Add(cancelButton);
            timecodeWindow.AcceptButton = okButton;
            timecodeWindow.CancelButton = cancelButton;



            DialogResult myresult = timecodeWindow.ShowDialog();

            TimeCode = textBox.Text;
            fps = Convert.ToInt32(frameBase.Value);
            Loop = loopCheck.Checked;
            return myresult;
        }

        public static DialogResult MarkerDialog(string DialogName, ref string TimeCode, ref string MarkerName, int XPos)
        {
            Size size = new System.Drawing.Size(190, 120);

            Form markerWindow = new Form();

            markerWindow.StartPosition = FormStartPosition.Manual;

            markerWindow.Location = new Point(XPos, Cursor.Position.Y);

            markerWindow.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            markerWindow.BackColor = Color.FromArgb(50, 50, 50);
            markerWindow.ClientSize = size;
            markerWindow.Text = DialogName;

            TextBox textBox = new TextBox();
            textBox.Size = new System.Drawing.Size(size.Width - 10, 70);
            textBox.Location = new System.Drawing.Point(5, 50);
            textBox.Font = new Font("Consolas", 20, FontStyle.Regular);
            textBox.TabStop = false;
            textBox.Text = TimeCode;
            markerWindow.Controls.Add(textBox);

            System.Windows.Forms.TextBox varTextBox = new System.Windows.Forms.TextBox();
            varTextBox.Size = new System.Drawing.Size(size.Width - 10, 15);
            varTextBox.Location = new System.Drawing.Point(5, 10);
            varTextBox.Font = new Font("Consolas", 11, FontStyle.Regular);
            varTextBox.Text = MarkerName;
            varTextBox.TabStop = false;
            markerWindow.Controls.Add(varTextBox);


            #region buttons
            Button okButton = new Button();
            okButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            okButton.Name = "okButton";
            okButton.Size = new System.Drawing.Size(90, 23);
            okButton.Text = "&OK";
            okButton.Location = new System.Drawing.Point(5, 93);
            okButton.FlatStyle = FlatStyle.Flat;
            okButton.FlatAppearance.BorderSize = 0;
            okButton.ForeColor = Color.FromArgb(200, 200, 200);
            okButton.FlatAppearance.BorderColor = Color.FromArgb(0, 255, 255, 255);
            markerWindow.Controls.Add(okButton);

            Button cancelButton = new Button();
            cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new System.Drawing.Size(90, 23);
            cancelButton.Text = "&Cancel";
            cancelButton.Location = new System.Drawing.Point(size.Width - 95, 93);
            cancelButton.FlatStyle = FlatStyle.Flat;
            cancelButton.FlatAppearance.BorderSize = 0;
            cancelButton.FlatAppearance.BorderColor = Color.FromArgb(0, 255, 255, 255);
            cancelButton.ForeColor = Color.FromArgb(200, 200, 200);
            #endregion


            markerWindow.Controls.Add(cancelButton);
            markerWindow.AcceptButton = okButton;
            markerWindow.CancelButton = cancelButton;



            DialogResult myresult = markerWindow.ShowDialog();

            TimeCode = textBox.Text;
            MarkerName = varTextBox.Text;
            return myresult;
        }

        //export interpole data to xml file
        private void saveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            saveFileDialog1.Filter = "XML|*.xml";

            saveFileDialog1.ShowDialog();


            if (saveFileDialog1.FileName != "")
            {
                Debug.WriteLine(saveFileDialog1.FileName);
                File.WriteAllText(saveFileDialog1.FileName, SerializeInterpoleData(ipD));

            }
        }

        //import interpole data from xml file
        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            openFileDialog1.Filter = "XML|*.xml";
            DialogResult result = openFileDialog1.ShowDialog(); // Show the dialog.
            if (result == DialogResult.OK) // Test result.
            {
                string file = openFileDialog1.FileName;
                try
                {
                    //string text = File.ReadAllText(file);
                    ipD = DeserializeInterpoleData(File.ReadAllText(file));
                }
                catch (IOException)
                {
                }
            }
        }

        //merge interpole data from xml file
        private void mergeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            openFileDialog1.Filter = "XML|*.xml";
            DialogResult result = openFileDialog1.ShowDialog(); // Show the dialog.
            if (result == DialogResult.OK) // Test result.
            {
                PushUndoStep();
                string file = openFileDialog1.FileName;
                try
                {
                    InterpoleData tempIPD = DeserializeInterpoleData(File.ReadAllText(file));
                    foreach(Clip clip in tempIPD.ClipList)
                    {
                        //add clips
                        ipD.ClipList.Add(new Clip()
                        {
                            ID = RNGCharacterMask(), //make sure new clips have a unique id
                            Name = clip.Name,
                            StartFrame = clip.StartFrame,
                            Duration = clip.Duration,
                            FadeIn = clip.FadeIn,
                            FadeOut = clip.FadeOut,
                            Bounds = clip.Bounds,
                            Color = clip.Color,
                            Variable = clip.Variable
                        });
                    }

                    //add Marker
                    foreach(Marker marker in tempIPD.MarkerList)
                    {
                        ipD.MarkerList.Add(new Marker()
                        {
                            FramePosition = marker.FramePosition,
                            Name = marker.Name
                        });
                    }

                    UpdateXML();
                }
                catch (IOException)
                {
                }
            }
        }

        private void AddMarker(object sender, EventArgs e)
        {
            var item = ipD.MarkerList.FirstOrDefault(r => r.FramePosition == ipD.PlayHead);
            if(item == null)
            {
                string pos = Frames2Timecode(ipD.PlayHead, ipD.FrameRate, false);
                string name = "empty";
                int XPos = this.Location.X + (int)((StagePos.x + ipD.PlayHead) * Zoom) + 20;

                if (MarkerDialog("Create Marker", ref pos, ref name, XPos) == DialogResult.OK)
                {
                    ipD.MarkerList.Add(new Marker()
                    {
                        FramePosition = TimeCode2Frames(pos, ipD.FrameRate),
                        Name = name
                    });

                    Changed(this, EventArgs.Empty);
                }
            }  else MessageBox.Show("Oops, there is a marker.");
        }

        private void EditMarker(object sender, EventArgs e)
        {
            var item = ipD.MarkerList.FirstOrDefault(r => r.FramePosition == ipD.PlayHead);

            if(item != null)
            {
                string pos = Frames2Timecode(item.FramePosition, ipD.FrameRate, false);
                string name = item.Name;
                int XPos = this.Location.X + (int)((StagePos.x + ipD.PlayHead) * Zoom) + 20;

                if (MarkerDialog("Edit Marker", ref pos, ref name, XPos) == DialogResult.OK)
                {
                    item.FramePosition = TimeCode2Frames(pos, ipD.FrameRate);
                    item.Name = name;
                    Changed(this, EventArgs.Empty);
                }
            } 
            else MessageBox.Show("No marker found.");
        }

        private void RemoveMarker(object sender, EventArgs e)
        {
            var item = ipD.MarkerList.FirstOrDefault(r => r.FramePosition == ipD.PlayHead);

            if (item != null)
            {
                ipD.MarkerList.Remove(item);
                Changed(this, EventArgs.Empty);
            }
            else MessageBox.Show("No marker found.");
        }

        public void InitMenuToolStrip()
        {

            MenuStrip menuStrip1 = new MenuStrip();
            ToolStripMenuItem menuToolStripMenuItem = new ToolStripMenuItem();
            ToolStripMenuItem openToolStripMenuItem = new ToolStripMenuItem();
            ToolStripMenuItem saveToolStripMenuItem = new ToolStripMenuItem();
            ToolStripMenuItem mergeToolStripMenuItem = new ToolStripMenuItem();

            ToolStripMenuItem markerToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            ToolStripMenuItem addMarkerToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            ToolStripMenuItem editMarkerToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            ToolStripMenuItem deleteMarkerToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();

            // 
            // menuStrip1
            // 
            menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            menuToolStripMenuItem,
            markerToolStripMenuItem});

            menuStrip1.Location = new System.Drawing.Point(0, 0);
            menuStrip1.Name = "menuStrip1";
            menuStrip1.Size = new System.Drawing.Size(284, 24);
            menuStrip1.TabIndex = 0;
            menuStrip1.Text = "menuStrip1";
            menuStrip1.BackColor = Color.FromArgb(20, 20, 20);
            menuStrip1.ForeColor = Color.FromArgb(200, 200, 200);
            // 
            // menuToolStripMenuItem
            // 
            menuToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            openToolStripMenuItem,
            saveToolStripMenuItem,
            mergeToolStripMenuItem});
            menuToolStripMenuItem.Name = "menuToolStripMenuItem";
            menuToolStripMenuItem.Size = new System.Drawing.Size(37, 20);
            menuToolStripMenuItem.Text = "File";
            // 
            // openToolStripMenuItem
            // 
            openToolStripMenuItem.Name = "openToolStripMenuItem";
            openToolStripMenuItem.Size = new System.Drawing.Size(152, 22);
            openToolStripMenuItem.Text = "Import";
            openToolStripMenuItem.ForeColor = Color.FromArgb(200, 200, 200);
            openToolStripMenuItem.Click += new System.EventHandler(this.openToolStripMenuItem_Click);
            // 
            // saveToolStripMenuItem
            // 
            saveToolStripMenuItem.Name = "saveToolStripMenuItem";
            saveToolStripMenuItem.Size = new System.Drawing.Size(152, 22);
            saveToolStripMenuItem.Text = "Export";
            saveToolStripMenuItem.ForeColor = Color.FromArgb(200, 200, 200);
            saveToolStripMenuItem.Click += new System.EventHandler(this.saveToolStripMenuItem_Click);
            // 
            // mergeToolStripMenuItem
            // 
            mergeToolStripMenuItem.Name = "saveToolStripMenuItem";
            mergeToolStripMenuItem.Size = new System.Drawing.Size(152, 22);
            mergeToolStripMenuItem.Text = "Merge";
            mergeToolStripMenuItem.ForeColor = Color.FromArgb(200, 200, 200);
            mergeToolStripMenuItem.Click += new System.EventHandler(mergeToolStripMenuItem_Click);



            #region Marker
            markerToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            addMarkerToolStripMenuItem,
            editMarkerToolStripMenuItem,
            deleteMarkerToolStripMenuItem});
            markerToolStripMenuItem.Name = "markerToolStripMenuItem";
            markerToolStripMenuItem.Size = new System.Drawing.Size(56, 20);
            markerToolStripMenuItem.Text = "Marker";
            // 
            // addMarkerToolStripMenuItem
            // 
            addMarkerToolStripMenuItem.Name = "addMarkerToolStripMenuItem";
            addMarkerToolStripMenuItem.Size = new System.Drawing.Size(147, 22);
            addMarkerToolStripMenuItem.Text = "Add Marker";
            addMarkerToolStripMenuItem.ForeColor = Color.FromArgb(200, 200, 200);
            addMarkerToolStripMenuItem.Click += new System.EventHandler(AddMarker);
            // 
            // editMarkerToolStripMenuItem
            // 
            editMarkerToolStripMenuItem.Name = "editMarkerToolStripMenuItem";
            editMarkerToolStripMenuItem.Size = new System.Drawing.Size(147, 22);
            editMarkerToolStripMenuItem.Text = "Edit Marker";
            editMarkerToolStripMenuItem.ForeColor = Color.FromArgb(200, 200, 200);
            editMarkerToolStripMenuItem.Click += new System.EventHandler(EditMarker);
            // 
            // deleteMarkerToolStripMenuItem
            // 
            deleteMarkerToolStripMenuItem.Name = "deleteMarkerToolStripMenuItem";
            deleteMarkerToolStripMenuItem.Size = new System.Drawing.Size(147, 22);
            deleteMarkerToolStripMenuItem.Text = "Delete Marker";
            deleteMarkerToolStripMenuItem.ForeColor = Color.FromArgb(200, 200, 200);
            deleteMarkerToolStripMenuItem.Click += new System.EventHandler(RemoveMarker);
            #endregion

            // 
            // Form1
            //        

            Controls.Add(menuStrip1);
            MainMenuStrip = menuStrip1;
            menuStrip1.Renderer = new ToolStripProfessionalRenderer(new MyColorTable());
        }

        public class MyColorTable : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground
            {
                get
                {
                    return Color.FromArgb(20, 20, 20);
                }
            }

            public override Color ImageMarginGradientBegin
            {
                get
                {
                    return Color.FromArgb(20, 20, 20);
                }
            }

            public override Color ImageMarginGradientMiddle
            {
                get
                {
                    return Color.FromArgb(20, 20, 20);
                }
            }

            public override Color ImageMarginGradientEnd
            {
                get
                {
                    return Color.FromArgb(20, 20, 20);
                }
            }

            public override Color MenuBorder
            {
                get
                {
                    return Color.FromArgb(90, 90, 90);
                }
            }

            public override Color MenuItemBorder
            {
                get
                {
                    return Color.FromArgb(90, 90, 90);
                }
            }

            public override Color MenuItemSelected
            {
                get
                {
                    return Color.FromArgb(90, 90, 90);
                }
            }

            public override Color MenuStripGradientBegin
            {
                get
                {
                    return Color.FromArgb(20, 20, 20);
                }
            }

            public override Color MenuStripGradientEnd
            {
                get
                {
                    return Color.FromArgb(20, 20, 20);
                }
            }

            public override Color MenuItemSelectedGradientBegin
            {
                get
                {
                    return Color.FromArgb(20, 20, 20);
                }
            }

            public override Color MenuItemSelectedGradientEnd
            {
                get
                {
                    return Color.FromArgb(20, 20, 20);
                }
            }

            public override Color MenuItemPressedGradientBegin
            {
                get
                {
                    return Color.FromArgb(20, 20, 20);
                }
            }



            public override Color MenuItemPressedGradientEnd
            {
                get
                {
                    return Color.FromArgb(20, 20, 20);
                }
            }
        }



    }

}
