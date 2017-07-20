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
        #region variables
        Color MyBackgroundColor = Color.FromArgb(20, 20, 20);
        SolidBrush OrangeBrush = new SolidBrush(Color.Orange);
        SolidBrush textBrush = new SolidBrush(Color.FromArgb(150, 150, 150));
        SolidBrush grayBrush = new SolidBrush(Color.FromArgb(50, 50, 50));

        Pen GrayPen = new Pen(Color.FromArgb(70, 70, 70), 1.0f);

        public Point StagePos; //StagePosition
        public int timeZoom = 1; // Magnifiy Time on Stage, larger means you see more of time
        public float dpi = 1; //DPI Value

        Font clipFont = new Font("Serif", 8, FontStyle.Regular);
        Font timeCodefont = new Font("Courier New", 12, FontStyle.Regular);

        Rectangle selectionRectangle = new Rectangle(); //multiselection rectangle

        StringFormat format = new StringFormat(StringFormatFlags.LineLimit | StringFormatFlags.NoWrap, 1003);

        Playbar myplaybar = new Playbar();  // playbar
        ZoomBar myzoombar = new ZoomBar();

        #endregion

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
                DrawSelectionRect(e);

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
            myplaybar.Bounds = new Rectangle(new Point(0 - StagePos.X, 0), new Size(this.Width, 30)); //update playbar bounds
            e.Graphics.FillRectangle(grayBrush, myplaybar.Bounds); // background

            DrawZoomValue(e);

            DrawPlayHead(e);

            DrawTimeCode(e);


        }

        public void DrawPlayHead(PaintEventArgs e)
        {
            Point[] points = { new Point(-7 + playPosFrames, 20), new Point(7 + playPosFrames, 20), new Point(0 + playPosFrames, 30) }; //play head triangle
            e.Graphics.FillPolygon(new SolidBrush(Color.Orange), points);
            e.Graphics.DrawLine(GrayPen,new Point(playPosFrames,30),new Point(playPosFrames,3000));
        }

        public void DrawTimeCode(PaintEventArgs e)
        {
            Rectangle Bounds = new Rectangle(
                new Point(2 - StagePos.X, 2),
                new Size(200, 100));

            e.Graphics.DrawString(playPosFrames.ToString("D10"), timeCodefont, textBrush, Bounds, format); //text
        }

        public void DrawZoomValue(PaintEventArgs e)
        {
            myzoombar.Bounds = new Rectangle(new Point(this.Width - 50 - StagePos.X, this.Height - 20), new Size(50, 20)); //update playbar bounds
            e.Graphics.FillRectangle(grayBrush, myzoombar.Bounds); // background

            e.Graphics.DrawString(Convert.ToString(timeZoom) + "x", timeCodefont, textBrush, myzoombar.Bounds, format); //text
        }

        public void DrawSelectionRect(PaintEventArgs e)
        {
            if(selectionRectangle != Rectangle.Empty)
            {
                e.Graphics.DrawRectangle(GrayPen, selectionRectangle);
            }
        }

    }
}
