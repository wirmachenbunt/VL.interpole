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
        SolidBrush textBrush = new SolidBrush(Color.FromArgb(150, 150, 150));
        SolidBrush grayBrush = new SolidBrush(Color.FromArgb(50, 50, 50));

        public Point StagePos; //StagePosition
        public int stageZoom = 1; // Magnifiy Time on Stage, larger means you see more of time
        public float dpi = 1; //DPI Value

        Font clipFont = new Font("Serif", 8, FontStyle.Regular);
        Font timeCodefont = new Font("Courier New",10, FontStyle.Regular);

        StringFormat format = new StringFormat(StringFormatFlags.LineLimit | StringFormatFlags.NoWrap, 1003);


        public void MainDraw(object sender, PaintEventArgs e)
        {
          try
            {
                e.Graphics.TranslateTransform(StagePos.X, StagePos.Y); //Move stage
                e.Graphics.ScaleTransform(dpi, dpi); //dpi scaling 
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; // shapeman
                e.Graphics.Clear(MyBackgroundColor);

                DrawClips(e);
                DrawPlayBar(e);
            }
            catch { FLogger.Log(LogType.Debug, "Something went wrong with drawing !"); }
        }

        public void DrawClips(PaintEventArgs e)
        {
            foreach (Clip clip in clipList)
            {
                Color ClipColor = Color.FromName(clip.Color);
                SolidBrush ClipBrush = new SolidBrush(ClipColor);

                e.Graphics.FillRectangle(ClipBrush, clip.Bounds);

                string text = clip.Name; // create text
                SizeF stringSize = new SizeF();
                stringSize = e.Graphics.MeasureString(text, clipFont, 400);

                Point center = new Point(clip.Bounds.Location.X,clip.Bounds.Y - Convert.ToInt16(stringSize.Height));

                Rectangle Bounds = new Rectangle(
                center,
                new Size(Convert.ToInt32(stringSize.Width + 1),
                Convert.ToInt32(stringSize.Height + 1)));       

                e.Graphics.DrawString(text, clipFont, textBrush, Bounds, format); //text
            }          
        }

        public void DrawPlayBar(PaintEventArgs e)
        {
            e.Graphics.FillRectangle(grayBrush, new Rectangle(new Point(0-StagePos.X,0),new Size(this.Width,30))); // background

            Point[] points = { new Point(-7 + playPosFrames, 20), new Point(7 + playPosFrames, 20), new Point(0 + playPosFrames, 30) }; //play head triangle
            e.Graphics.FillPolygon(new SolidBrush(Color.Orange), points);

            Rectangle Bounds = new Rectangle(
                new Point(0 - StagePos.X, 0),
                new Size(200,100));

            e.Graphics.DrawString(playPosFrames.ToString("D10"), timeCodefont, textBrush, Bounds, format); //text

        }

    }
}
