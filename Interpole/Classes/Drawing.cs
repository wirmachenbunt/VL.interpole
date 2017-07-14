using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using VVVV.Core.Logging;

namespace VVVV.Nodes
{
    partial class Interpole
    {

        Color MyBackgroundColor = Color.FromArgb(20, 20, 20);
        SolidBrush OrangeBrush = new SolidBrush(Color.Orange);

        public Point StagePos; //StagePosition
        public float dpi = 1; //DPI Value

        public void MainDraw(object sender, PaintEventArgs e)
        {
          try
            {
                e.Graphics.TranslateTransform(StagePos.X, StagePos.Y); //Move stage
                e.Graphics.ScaleTransform(dpi, dpi); //dpi scaling 
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; // shapeman
                e.Graphics.Clear(MyBackgroundColor);

                DrawClips(e);
            }
            catch { FLogger.Log(LogType.Debug, "Something went wrong with drawing !"); }
        }

        public void DrawClips(PaintEventArgs e)
        {
            foreach (Clip clip in clipList)
            {
                
                e.Graphics.FillRectangle(OrangeBrush, clip.Bounds);

            }          
        }



    }
}
