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
using System.ComponentModel.Composition;
using System.Runtime.InteropServices;
using System.Threading;

namespace Interpole
{
    partial class InterpoleForm : Form
    {
        #region variables
        
        // WINDOWS FORMS VARIABLES ///////////////////////////////////////////////////////////////
        Graphics g;

        Color MyBackgroundColor = Color.FromArgb(20, 20, 20);
        SolidBrush OrangeBrush = new SolidBrush(Color.Orange);
        SolidBrush textBrush = new SolidBrush(Color.FromArgb(150, 150, 150));
        SolidBrush grayBrush = new SolidBrush(Color.FromArgb(50, 50, 50));
        SolidBrush darkgrayBrush = new SolidBrush(Color.FromArgb(30, 30, 30));
        SolidBrush brightGrayBrush = new SolidBrush(Color.FromArgb(150, 150, 150));
        SolidBrush whiteBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
        Brush selectbrush = new SolidBrush(Color.FromArgb(150, 255, 255, 255));

        Pen GrayPen = new Pen(Color.FromArgb(70, 70, 70), 1.0f);
        Pen DarkPen = new Pen(Color.FromArgb(50, 50, 50), 1.0f);

        public Point StagePos; //StagePosition
        //public int timeZoom = 1; // Magnifiy Time on Stage, larger means you see more of time
        public float Zoom = 1; 
       
        public int frameRate = 60;

        Font clipFont = new Font("Segoe UI", 8, FontStyle.Regular);
        Font rulerFont = new Font("Segoe UI", 8, FontStyle.Regular);
        Font timeCodefont = new Font("Consolas", 20, FontStyle.Regular);
        Font framesfont = new Font("Consolas", 8, FontStyle.Regular);

        StringFormat format = new StringFormat(StringFormatFlags.LineLimit | StringFormatFlags.NoWrap, 1003);

        Playbar myplaybar = new Playbar();  // playbar
        TimeCodeDisplay mytimecode = new TimeCodeDisplay();
        Rectangle footer = new Rectangle();
        WorkSpace myworkspace = new WorkSpace();
        SizeF stringSize = new SizeF();

        // WINDOWS FORMS VARIABLES ///////////////////////////////////////////////////////////////

        #endregion

        private void InitializeComponent()
        {
            Application.Idle += new EventHandler(Application_Idle);

            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.Opaque, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);

            this.ShowIcon = false;

            this.Name = "Interpole";
            this.Text = "Interpole";
            this.ResumeLayout(false);
            this.PerformLayout();

            this.ControlBox = false;

            this.DoubleBuffered = true;
            g = this.CreateGraphics();

            //bind events
            MouseMove += Form1_MouseMove; //mouse move event
            MouseDoubleClick += Form1_MouseDoubleClick; //mouse click event
            MouseDown += Form1_MouseDown; //mouse down event
            MouseUp += Form1_MouseUp;
            MouseWheel += Form1_MouseWheel;
            KeyDown += Form_Keydown;

            this.Location = ipD.WLocation;
            this.Size = ipD.WSize;
        }

        public void Application_Idle(object sender, EventArgs e)
        {
            NativeMethods.Message message;

            while (!NativeMethods.PeekMessage(out message, IntPtr.Zero, 0, 0, 0))
            {
                Render();               
                Thread.Sleep(5);                
            }
        }

        void Render()
        {
            this.Invalidate();
        }

        static class NativeMethods
        {
            [StructLayout(LayoutKind.Sequential)]
            public struct Message
            {
                public IntPtr hWnd;
                public uint Msg;
                public IntPtr wParam;
                public IntPtr lParam;
                public uint Time;
                public System.Drawing.Point Point;
            }

            [DllImport("User32.dll", CharSet = CharSet.Auto)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool PeekMessage(out Message message, IntPtr hWnd, uint filterMin, uint filterMax, uint flags);
        }

        protected override void OnPaint(PaintEventArgs pe)
        {    
            g = pe.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            g.ScaleTransform(Zoom, 1); //ScaleStage
            g.TranslateTransform(StagePos.X, StagePos.Y); //Move stage

            g.Clear(MyBackgroundColor);

            DrawPlayBar(pe);
            DrawPlayHead(pe);
            DrawFooter(pe);

            //g.ScaleTransform(Zoom, 1); //ScaleStage
            
            DrawMarker(pe);

            if (ipD.ClipList.Count > 0) DrawClips(pe);

            DrawSelectedClips(pe);
            DrawSelectionRect(pe);
            
        }

        public void DrawClips(PaintEventArgs e)
        {

            foreach (Clip clip in ipD.ClipList)
            {
                Color ClipColor = Color.FromName(clip.Color);
                SolidBrush ClipBrush = new SolidBrush(ClipColor);

                e.Graphics.FillRectangle(grayBrush, clip.Bounds);

                int fadeIn = clip.FadeIn;
                int fadeOut = clip.FadeOut;

                Point[] points = {
                    new Point(clip.Bounds.X, clip.Bounds.Y+20),
                    new Point(clip.Bounds.X+fadeIn, clip.Bounds.Y),
                    new Point(clip.Bounds.Width - fadeOut + clip.Bounds.X, clip.Bounds.Y),
                    new Point(clip.Bounds.Width + clip.Bounds.X, clip.Bounds.Y+20) };
           
            e.Graphics.FillPolygon(ClipBrush, points);


                string text = clip.Name; // create text
                SizeF stringSize = new SizeF();
                stringSize = e.Graphics.MeasureString(text, clipFont, 400);

                Point center = new Point(Convert.ToInt32( clip.Bounds.Location.X * Zoom),clip.Bounds.Y - Convert.ToInt16(stringSize.Height));

                Rectangle Bounds = new Rectangle(
                center,
                new Size(Convert.ToInt32(stringSize.Width + 1),
                Convert.ToInt32(stringSize.Height + 1)));

                e.Graphics.ScaleTransform(1 / Zoom, 1); //unscale stage to display proper text
                e.Graphics.DrawString(text, clipFont, textBrush, Bounds, format); //text
                e.Graphics.ScaleTransform(Zoom, 1); //rescale stage
            }          
        }

        public void DrawPlayBar(PaintEventArgs e)
        {
            myplaybar.Bounds = new Rectangle(new Point(0 - StagePos.X, 0 - StagePos.Y), new Size(this.Width, 30)); //update playbar bounds

            // show pre-roll
            e.Graphics.FillRectangle(grayBrush, myplaybar.Bounds); // background

            // for workspace hittest
            myworkspace.Bounds = new Rectangle(new Point(0 - StagePos.X, 0 - StagePos.Y), new Size(this.Width, this.Height));

            DrawLoopRegion(e);

            DrawRuler(e);

            DrawTimeCode(e);
        }

        public void DrawRuler(PaintEventArgs e)
        {
            int rulerSteps = 1; //in seconds
            //if (timeZoom > 1) rulerSteps = 5;
            //if (timeZoom > 4) rulerSteps = 10;
            //if (timeZoom > 10) rulerSteps = 20;

            double startRulerPos = Math.Abs(StagePos.X);
            startRulerPos = (startRulerPos / frameRate) / rulerSteps;
            startRulerPos = Math.Truncate(startRulerPos) * rulerSteps; // hier ist die stelle für die sekunden

            double endRulerPos = ((this.Width + Math.Abs(StagePos.X))) / frameRate;

            for (int i = Convert.ToInt32(startRulerPos); i < Convert.ToInt32(endRulerPos); i += rulerSteps)
            {
                int pos = (i * frameRate);
                int size = 30;
                int height = 5;
                int y = 25;
                int thickness = 1;
                string timecode = Frames2Timecode(i* frameRate, frameRate, true);
                string time = (i%60).ToString() + "s";
                if (time == "0s")
                {
                    time = timecode + "s";
                    size = 200;
                    height = 12;
                    thickness = 3;
                    y = 17;
                }
                stringSize = e.Graphics.MeasureString(time, rulerFont, 1000);


                e.Graphics.DrawString(time, rulerFont, whiteBrush, new Rectangle(new Point(pos - Convert.ToInt32(stringSize.Width/2) +1, 5 - StagePos.Y), new Size(size, 50)), format); //draw seconds
                e.Graphics.FillRectangle(brightGrayBrush, new Rectangle(new Point(pos- (thickness/2),y - StagePos.Y), new Size(thickness, height)));
            }
            
            //FLogger.Log(LogType.Debug, countBars.ToString());

        }

        public void DrawMarker(PaintEventArgs e)
        {
            /*

            for (int i = 0; i < FMarker.SliceCount;i++)
            {
               if(FMarker[i] != 0)
                {
                    DarkPen.DashStyle = DashStyle.Dash;
                    Int32 pos = FMarker[i] / timeZoom;
                    Point[] mypoints = { new Point(-3 + pos, 30 - StagePos.Y), new Point(3 + pos, 30 - StagePos.Y), new Point(0 + pos, 34 - StagePos.Y) }; //play head triangle
                    e.Graphics.FillPolygon(new SolidBrush(Color.Aqua), mypoints);
                    e.Graphics.DrawString((i+1).ToString(), rulerFont, whiteBrush, new Rectangle(new Point(pos, 50 - StagePos.Y), new Size(20, 50)), format); //draw seconds
                    e.Graphics.DrawLine(DarkPen, new Point(pos, 34 - StagePos.Y), new Point(pos, 3000 - StagePos.Y));
                }                         
            }
            */
        }

        public void DrawPlayHead(PaintEventArgs e)
        {
            int pos = Convert.ToInt32( playPosFrames*Zoom);
            

            Point[] points = { new Point(-7 + pos, 20 - StagePos.Y), new Point(7 + pos, 20 - StagePos.Y), new Point(0 + pos, 30 - StagePos.Y) }; //play head triangle
            e.Graphics.FillPolygon(new SolidBrush(Color.Orange), points);
            e.Graphics.DrawLine(GrayPen,new Point(pos,30 - StagePos.Y),new Point(pos,this.Height - StagePos.Y));
        }

        public void DrawLoopRegion(PaintEventArgs e)
        {
            /*
            if (FIsLoop[0])
            {
                e.Graphics.FillRectangle(OrangeBrush, new Rectangle(new Point(FLoopRegion[0] / timeZoom, 28 - StagePos.Y), new Size((FLoopRegion[1] - FLoopRegion[0]) / timeZoom, 2)));
                e.Graphics.FillRectangle(darkgrayBrush, new Rectangle(new Point(FLoopRegion[0] / timeZoom, 30 - StagePos.Y), new Size((FLoopRegion[1] - FLoopRegion[0]) / timeZoom, 2000)));
            }
            
            */
        }

        public void DrawTimeCode(PaintEventArgs e)
        {
            mytimecode.Bounds = new Rectangle(
                new Point(Convert.ToInt32(5 - StagePos.X ), 60 - StagePos.Y),
                new Size(200, 30));   

            e.Graphics.DrawString(playPosFrames.ToString("D10"), framesfont, textBrush, mytimecode.Bounds, format); //draw frameCount


            mytimecode.Bounds = new Rectangle(
                new Point(Convert.ToInt32(2 - StagePos.X ), 30 - StagePos.Y),
                new Size(200, 60));

            if(ipD.Loop) e.Graphics.DrawString(Frames2Timecode(playPosFrames, frameRate, false), timeCodefont, OrangeBrush, mytimecode.Bounds, format); //draw frameCount
            else e.Graphics.DrawString(Frames2Timecode(playPosFrames,frameRate,false), timeCodefont, textBrush, mytimecode.Bounds, format); //draw frameCount
            
        }

        public void DrawFooter(PaintEventArgs e)
        {
            string Percentage = Convert.ToString(100) + "%";
            var format = new StringFormat() { Alignment = StringAlignment.Far };

            //zoom display rectangle
            
            footer = new Rectangle(new Point(this.Width - 70 - StagePos.X, this.Height - 55 - StagePos.Y), new Size(50, 20)); //update playbar bounds
            e.Graphics.DrawString(Percentage, clipFont, textBrush, footer, format); //text

            /*
            //UI Lock display
            if(UI_Lock)
            {
                format = new StringFormat() { Alignment = StringAlignment.Near };
                footer = new Rectangle(new Point(0 - StagePos.X, this.Height - 15 - StagePos.Y), new Size(50, 20)); //update playbar bounds
                e.Graphics.DrawString("Locked", clipFont, OrangeBrush, footer, format); //text
            } 
            */
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
        
        //debug draw for snap positions
        public void DrawSnapPositions(PaintEventArgs e)
        {
            if(snapPosList.Count > 0)
            {
                Pen DebugPen = new Pen(Color.BlueViolet);
                foreach (int pos in snapPosList)
                {
                    e.Graphics.DrawLine(DebugPen, new Point(pos, 30), new Point(pos, 3000));
                }
            } 
        }


        public void PlaybackMoveStage()
        {
            int pos = playPosFrames;
            
            if (pos > this.Width  - StagePos.X)
            {
                StagePos.X = (StagePos.X) - this.Width / 2;
                Invalidate();
            }

            if(pos < Math.Abs(StagePos.X))
            {
                StagePos.X = 0 - pos;
                Invalidate();
            }

        }


        /*

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

        public void ShowWindow()
        {
           
            if(!IsVisible)
            {
                var myPath = FPluginHost.GetNodePath(false);
                var selfAsNode = FHdeHost.GetNodeFromPath(myPath);
                FHdeHost.SetComponentMode(selfAsNode, ComponentMode.InAWindow);
            }
            

        }

    */

    }
}
