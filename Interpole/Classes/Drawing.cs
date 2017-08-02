using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using VVVV.Core.Logging;
using VVVV.PluginInterfaces.V2;



namespace VVVV.Nodes
{
    partial class Interpole : IPluginEvaluate
    {
        #region variables
        Color MyBackgroundColor = Color.FromArgb(20, 20, 20);
        SolidBrush OrangeBrush = new SolidBrush(Color.Orange);
        SolidBrush textBrush = new SolidBrush(Color.FromArgb(150, 150, 150));
        SolidBrush grayBrush = new SolidBrush(Color.FromArgb(50, 50, 50));
        Brush selectbrush = new SolidBrush(Color.FromArgb(150, 255, 255, 255));

        Pen GrayPen = new Pen(Color.FromArgb(70, 70, 70), 1.0f);
  
        public Point StagePos; //StagePosition
        public int timeZoom = 4; // Magnifiy Time on Stage, larger means you see more of time
        public float dpi = 1; //DPI Value

        Font clipFont = new Font("Serif", 8, FontStyle.Regular);
        Font timeCodefont = new Font("Consolas", 20, FontStyle.Regular);

        StringFormat format = new StringFormat(StringFormatFlags.LineLimit | StringFormatFlags.NoWrap, 1003);

        Playbar myplaybar = new Playbar();  // playbar
        TimeCodeDisplay mytimecode = new TimeCodeDisplay();
        ZoomBar myzoombar = new ZoomBar();
        WorkSpace myworkspace = new WorkSpace();



        #endregion

        public void MainDraw(object sender, PaintEventArgs e)
        {
          try
            {
                if (IsVisible)
                {
                    e.Graphics.TranslateTransform(StagePos.X, StagePos.Y); //Move stage
                    e.Graphics.ScaleTransform(dpi, dpi); //dpi scaling 
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; // shapeman
                    e.Graphics.Clear(MyBackgroundColor);

                    DrawClips(e);
                    DrawPlayBar(e);
                    DrawSelectedClips(e);
                    DrawSelectionRect(e);
                    //FLogger.Log(LogType.Debug, "Busy Drawing");

                }


                

            }
            catch { FLogger.Log(LogType.Debug, "Something went wrong with drawing !"); }
        }

        public void DrawClips(PaintEventArgs e)
        {
            foreach (Clip clip in clipList)
            {
                Color ClipColor = Color.FromName(clip.Color);
                SolidBrush ClipBrush = new SolidBrush(ClipColor);

                e.Graphics.FillRectangle(grayBrush, clip.Bounds);

                int fadeIn = clip.FadeIn/timeZoom;
                int fadeOut = clip.FadeOut/timeZoom;

                Point[] points = {
                    new Point(clip.Bounds.X, clip.Bounds.Y+20),
                    new Point(clip.Bounds.X+fadeIn, clip.Bounds.Y),
                    new Point(clip.Bounds.Width - fadeOut + clip.Bounds.X, clip.Bounds.Y),
                    new Point(clip.Bounds.Width + clip.Bounds.X, clip.Bounds.Y+20) };


            e.Graphics.FillPolygon(ClipBrush, points);


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

            // for workspace hittest
            myworkspace.Bounds = new Rectangle(new Point(0 - StagePos.X, 0), new Size(this.Width, this.Height));

            DrawZoomValue(e);

            DrawPlayHead(e);

            DrawTimeCode(e);


        }

        public void DrawPlayHead(PaintEventArgs e)
        {
            int pos = playPosFrames / timeZoom;

            Point[] points = { new Point(-7 + pos, 20), new Point(7 + pos, 20), new Point(0 + pos, 30) }; //play head triangle
            e.Graphics.FillPolygon(new SolidBrush(Color.Orange), points);
            e.Graphics.DrawLine(GrayPen,new Point(pos,30),new Point(pos,3000));
        }

        public void DrawTimeCode(PaintEventArgs e)
        {
            mytimecode.Bounds = new Rectangle(
                new Point(2 - StagePos.X, 30),
                new Size(200, 30));

            //e.Graphics.DrawString(playPosFrames.ToString("D10"), timeCodefont, textBrush, Bounds, format); //draw frameCount
            
            e.Graphics.DrawString(Frames2Timecode(playPosFrames,Framerate[0]), timeCodefont, textBrush, mytimecode.Bounds, format); //draw frameCount
        }

        public void DrawZoomValue(PaintEventArgs e)
        {
            string Percentage = Convert.ToString(100 / timeZoom) + "%";
            var format = new StringFormat() { Alignment = StringAlignment.Far };

            myzoombar.Bounds = new Rectangle(new Point(this.Width - 50 - StagePos.X, this.Height - 15), new Size(50, 20)); //update playbar bounds
            e.Graphics.DrawString(Percentage, clipFont, textBrush, myzoombar.Bounds, format); //text
        }

        public void DrawSelectionRect(PaintEventArgs e)
        {

            Brush selectbrush = new SolidBrush(Color.FromArgb(50, 255, 255, 255));
            e.Graphics.FillRectangle(selectbrush, selectionRectangle);
               
           
        }

        public void DrawSelectedClips(PaintEventArgs e)
        {

            if (selectionList.Count > 0)
            {
               
                
                foreach (Clip clip in selectionList)
                {
                    e.Graphics.FillRectangle(selectbrush, clip.Bounds);
                }
            }
            
        }

        public void ApplyTimeZoom()
        {
            foreach (Clip myclip in clipList)
            {
                Point mypoint = new Point(myclip.StartFrame / timeZoom, myclip.Bounds.Y);
                Size mysize = new Size(myclip.Duration / timeZoom, myclip.Bounds.Size.Height);

                myclip.Bounds = new Rectangle(mypoint, mysize);
            }
        }

        public void PlaybackMoveStage()
        {
            int pos = playPosFrames / timeZoom;
            if (pos > (this.Width / 2) && IsVisible)
            {

                StagePos.X = this.Width / 2 - (playPosFrames / timeZoom);
            }

        }

        public void SnapToClip()
        {
            //float closest = MyStuff.OrderBy(n => Math.Abs(n - 21)).First(); //find closest value

        }

        public bool IsVisible
        {
            get
            {
                INode node = (INode)this.FHost; //IPluginHost imported from constructor or as property

                if (node.Window != null)
                {
                    return node.Window.IsVisible();
                }
                else
                {
                    return false;
                }
            }
        }

        public void HideWindow()
        {
           
            if(IsVisible)
            {
                var myPath = FPluginHost.GetNodePath(false);
                var selfAsNode = FHdeHost.GetNodeFromPath(myPath);
                FHdeHost.SetComponentMode(selfAsNode, ComponentMode.Hidden);
            }
            

        }

    }
}
