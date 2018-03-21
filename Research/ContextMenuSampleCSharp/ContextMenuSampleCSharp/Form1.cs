using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ContextMenuSampleCSharp
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();     
        }

        private void closeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void foregroundToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ColorDialog colorDlg = new ColorDialog();
            if (colorDlg.ShowDialog() == DialogResult.OK)
            {
                CreateButton.ForeColor = colorDlg.Color;
            }
        }

        private void backgroundToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ColorDialog colorDlg = new ColorDialog();
            if (colorDlg.ShowDialog() == DialogResult.OK)
            {
                CreateButton.BackColor = colorDlg.Color;
            }
        }

        private void CreateButton_Click(object sender, EventArgs e)
        {
            // Create a Menu Item
            ToolStripMenuItem FileMenu = new ToolStripMenuItem("File");
            FileMenu.BackColor = Color.OrangeRed;
            FileMenu.ForeColor = Color.Black;
            FileMenu.Text = "File Menu";
            FileMenu.Font = new Font("Georgia", 16);
            FileMenu.TextAlign = ContentAlignment.BottomRight;
            FileMenu.ToolTipText = "Click Me";

            FileMenu.Image = Image.FromFile("C:\\Images\\Samples\\Garden.jpg");
            FileMenu.BackgroundImage = Image.FromFile("C:\\Images\\Samples\\Dock.jpg");
           // FileMenu.TextDirection = ToolStripTextDirection.Vertical90;
            FileMenu.ToolTipText = "Click Me";
            PopupMenu.GripStyle = ToolStripGripStyle.Visible;

            PopupMenu.Items.Add(FileMenu);


        }

        private void Form1_Load(object sender, EventArgs e)
        {
            PopupMenu.Name = "PopupMenu";
            PopupMenu.BackColor = Color.OrangeRed;
            PopupMenu.ForeColor = Color.Black;
            PopupMenu.Text = "File Menu";
            PopupMenu.Font = new Font("Georgia", 16);
           
        }

        private void buttonToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

    }
}
