using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using System.Windows.Forms;
using System.Drawing.Drawing2D;

namespace VVVV.Nodes
{
    partial class Clips
    {
        Color MyBackgroundColor = Color.FromArgb(20, 20, 20);
        SolidBrush OrangeBrush = new SolidBrush(Color.Orange);

        public Point StagePos; //StagePosition
        public float dpi = 1; //DPI Value



        public void PaintAutomata(object sender, PaintEventArgs e)
        {


            try
            {


                e.Graphics.TranslateTransform(StagePos.X, StagePos.Y); //Move stage
                //e.Graphics.ScaleTransform(dpi, dpi); //dpi scaling 
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; // shapeman
                e.Graphics.Clear(MyBackgroundColor);

                Rectangle Bounds = new Rectangle(new Point(100, 100), new Size(100, 20));

                e.Graphics.FillRectangle(OrangeBrush, Bounds);


            }
            catch { }
        }
    }
}
