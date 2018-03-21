using System;
using System.Drawing;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using VVVV.Utils.VMath;
using System.Diagnostics;


namespace Interpole
{
    partial class InterpoleBase : Form
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
        HatchBrush myHatchBrush = new HatchBrush(HatchStyle.BackwardDiagonal, Color.FromArgb(90, 90, 90), Color.Black);
        Brush selectbrush = new SolidBrush(Color.FromArgb(150, 255, 255, 255));

        Pen GrayPen = new Pen(Color.FromArgb(70, 70, 70), 1.0f);
        Pen DarkPen = new Pen(Color.FromArgb(50, 50, 50), 1.0f);

        public Vector2D StagePos; //StagePosition

        public float Zoom = 0.25f; // less means you see a greater range of time

        //public int ipD.FrameRate = 50;

        Font clipFont = new Font("Segoe UI", 8, FontStyle.Regular);
        Font rulerFont = new Font("Segoe UI", 8, FontStyle.Regular);
        Font timeCodefont = new Font("Consolas", 20, FontStyle.Regular);
        Font framesfont = new Font("Consolas", 8, FontStyle.Regular);

        StringFormat format = new StringFormat(StringFormatFlags.LineLimit | StringFormatFlags.NoWrap, 1003);

        Playbar myplaybar = new Playbar();  // playbar
        TimeCodeDisplay mytimecode = new TimeCodeDisplay();
        Rectangle footer = new Rectangle();
        Rectangle clippingRegion = new Rectangle();
        WorkSpace myworkspace = new WorkSpace();
        SizeF stringSize = new SizeF();

        bool Animation = false;

        // WINDOWS FORMS VARIABLES ///////////////////////////////////////////////////////////////

        #endregion

        protected override void OnPaint(PaintEventArgs pe)
        {
            PlaybackMoveStage();

            //pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            pe.Graphics.Clear(MyBackgroundColor);

            //
            //UnTransformed Stuff
            //

            if (ipMode == InterpoleMode.KeyframeEditor) DrawKFUntransformed(pe);
            DrawPlayBar(pe, 20);
            DrawPlayHead(pe);
            DrawFooter(pe);
            DrawMarker(pe);
            
         
            //
            //Transformed Stuff
            //
            pe.Graphics.ScaleTransform(Zoom, 1); //ScaleStage
            pe.Graphics.TranslateTransform((float)StagePos.x, (float)StagePos.y); //Move stage

            if (ipMode == InterpoleMode.ClipEditor)
            {
                DrawClips(pe);
                DrawSelectedClips(pe);                       
            }

            if(ipMode == InterpoleMode.KeyframeEditor) DrawKFTransformed(pe);

            DrawSelectionRect(pe);
        }

        public void DrawClips(PaintEventArgs e)
        {     
            //update region to render just clips in view   
            clippingRegion = new Rectangle(-(int)(StagePos.x), (int)StagePos.y, (int)(this.Width / Zoom), this.Height);
      
            if (ipD.ClipList.Count > 0)
            {
                foreach (Clip clip in ipD.ClipList)
                {
                    if (clip.Bounds.IntersectsWith(clippingRegion))
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

                        stringSize = e.Graphics.MeasureString(text, clipFont, 400);

                        Point center = new Point(Convert.ToInt32(clip.Bounds.Location.X * Zoom), clip.Bounds.Y - Convert.ToInt16(stringSize.Height));

                        Rectangle Bounds = new Rectangle(
                        center,
                        new Size(Convert.ToInt32(stringSize.Width + 1),
                        Convert.ToInt32(stringSize.Height + 1)));

                        e.Graphics.ScaleTransform(1 / Zoom, 1); //unscale stage to display proper text
                        e.Graphics.DrawString(text, clipFont, textBrush, Bounds, format); //text
                        e.Graphics.ScaleTransform(Zoom, 1); //rescale stage
                    }
                }
            }


        }

        public void DrawPlayBar(PaintEventArgs e, int VerticalPosition)
        {

            // playbar background
            myplaybar.Bounds = new Rectangle(new Point(0, VerticalPosition), new Size(this.Width, 30)); //update playbar bounds          
            e.Graphics.FillRectangle(grayBrush, myplaybar.Bounds); // background

            //move playbar bounds for hittest
            myplaybar.Bounds = new Rectangle(new Point((int)-StagePos.x,(int)(VerticalPosition -StagePos.y)), new Size((int)(this.Width / Zoom), 30));

            // for workspace hittest
            myworkspace.Bounds = new Rectangle(0, 30 + VerticalPosition, (int)(((this.Width / Zoom) - StagePos.x)), this.Height);

            // draw negativ workspace
            if(ipMode == InterpoleMode.ClipEditor)  e.Graphics.FillRectangle(myHatchBrush, new Rectangle(0, 30 + VerticalPosition, (int)(StagePos.x * Zoom), this.Height)); // background

            DrawRuler(e, VerticalPosition);

            DrawTimeCode(e, VerticalPosition);

            DrawLoopRegion(e);
        }

        public void DrawRuler(PaintEventArgs e, int VerticalPosition)
        {
            int steps = 5; //seconds
            if (Zoom >= 1) steps = 1;
            //if (Zoom < 0.75) steps = 2;
            if (Zoom < 0.5) steps = 5;
            if (Zoom < 0.25) steps = 10;
            if (Zoom < 0.1) steps = 30;
            if (Zoom < 0.034) steps = 120;



            double startTime = -StagePos.x / ipD.FrameRate / steps; //canvas starting at point in time in seconds

            double roundedTime = Math.Ceiling(startTime); //round seconds

            double gap = steps * Math.Abs((startTime - roundedTime) * (ipD.FrameRate * Zoom));

            string time = Convert.ToString(startTime);
            stringSize = e.Graphics.MeasureString(time, rulerFont, 1000);


            int frames = 0;

            while (gap < this.Width)
            {
                frames = Convert.ToInt32(((gap / Zoom) - StagePos.x));

                if (frames >= 0)
                {
                    e.Graphics.FillRectangle(brightGrayBrush, new Rectangle(new Point((int)gap - 1, 25 + VerticalPosition), new Size(1, 5)));
                    e.Graphics.DrawString(Frames2Timecode(frames, ipD.FrameRate, true) + "s", rulerFont, whiteBrush, new Rectangle((int)gap - 15, 7 + VerticalPosition, 50, 50), format);
                }

                gap += ipD.FrameRate * Zoom * steps;

            }

        }

        public void DrawMarker(PaintEventArgs e)
        {
            DarkPen.DashStyle = DashStyle.Dash;

            foreach (Marker m in ipD.MarkerList)
            {
                int pos = Convert.ToInt32((m.FramePosition + StagePos.x) * Zoom);

                if (m.FramePosition == ipD.PlayHead)
                {
                    Point[] mypoints = { new Point(-7 + pos, 40), new Point(7 + pos, 40), new Point(0 + pos, 50) }; //play head triangle
                    e.Graphics.FillPolygon(new SolidBrush(Color.Aqua), mypoints);
                    stringSize = e.Graphics.MeasureString(m.Name, rulerFont, 400);
                    e.Graphics.DrawString(m.Name, rulerFont, OrangeBrush, new Rectangle(pos, 50, (int)stringSize.Width + 10, (int)stringSize.Height), format); //draw seconds
                }
                else
                {
                    Point[] mypoints = { new Point(-3 + pos, 45), new Point(3 + pos, 45), new Point(0 + pos, 49) }; //play head triangle
                    e.Graphics.FillPolygon(new SolidBrush(Color.Aqua), mypoints);
                    stringSize = e.Graphics.MeasureString(m.Name, rulerFont, 400);
                    e.Graphics.DrawString(m.Name, rulerFont, whiteBrush, new Rectangle(pos, 50, (int)stringSize.Width + 10, (int)stringSize.Height), format); //draw seconds
                }
         
                e.Graphics.DrawLine(DarkPen, new Point(pos, 50), new Point(pos, this.Height));
            }
        }

        public void DrawPlayHead(PaintEventArgs e)
        {
            int pos = Convert.ToInt32((ipD.PlayHead + StagePos.x) * Zoom);
            int VOffset = 20; ;

            Point[] points = { new Point(-7 + pos, 20 + VOffset), new Point(7 + pos, VOffset + 20), new Point(0 + pos, VOffset + 30) }; //play head triangle
            e.Graphics.FillPolygon(new SolidBrush(Color.Orange), points);
            e.Graphics.DrawLine(GrayPen, new Point(pos, VOffset + 30 ), new Point(pos, VOffset + this.Height));
        }

        public void DrawLoopRegion(PaintEventArgs e)
        {
            if (ipD.Loop)
            {
                e.Graphics.FillRectangle(OrangeBrush, new Rectangle((int)((ipD.LoopStart+StagePos.x)*Zoom), 48, (int)((ipD.LoopEnd - ipD.LoopStart)*Zoom), 2));
            }
        }

        public void DrawTimeCode(PaintEventArgs e, int VerticalPosition)
        {
            mytimecode.Bounds = new Rectangle(
                new Point(5, 60 + VerticalPosition),
                new Size(200, 30));

            // background rectangle
            //e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(20, 20, 20)), new Rectangle(0,50,180,50));

            e.Graphics.DrawString(ipD.PlayHead.ToString("D10"), framesfont, textBrush, mytimecode.Bounds, format); //draw frameCount

            mytimecode.Bounds = new Rectangle(
                new Point(2, 30 + VerticalPosition),
                new Size(200, 60));

            if (ipD.Loop) e.Graphics.DrawString(Frames2Timecode(ipD.PlayHead, ipD.FrameRate, false), timeCodefont, OrangeBrush, mytimecode.Bounds, format); //draw frameCount
            else e.Graphics.DrawString(Frames2Timecode(ipD.PlayHead, ipD.FrameRate, false), timeCodefont, textBrush, mytimecode.Bounds, format); //draw frameCount

            //timecode bounds for hittest
            mytimecode.Bounds = new Rectangle((int)-StagePos.x,(int)(30 + VerticalPosition -StagePos.y), (int)(200 / Zoom), 60);
        }

        public void DrawFooter(PaintEventArgs e)
        {
            string Percentage = Convert.ToString(Convert.ToInt32((100 / Zoom))) + "%";
            var format = new StringFormat() { Alignment = StringAlignment.Far };

            //zoom display rectangle

            footer = new Rectangle(new Point(this.Width - 70, this.Height - 55), new Size(50, 20)); //update playbar bounds
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
            if (snapPosList.Count > 0)
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
            if (play && (ipD.PlayHead + StagePos.x) * Zoom > this.Width / 2 || play && (ipD.PlayHead + StagePos.x) * Zoom < 0)
            {
                StagePos.x = 0 - ipD.PlayHead + (this.Width / 2 / Zoom);
            }

        }

        public void Application_Idle(object sender, EventArgs e)
        {
            NativeMethods.Message message;

            while (!NativeMethods.PeekMessage(out message, IntPtr.Zero, 0, 0, 0))
            {
                Render();
                
                if(this.Focused) Thread.Sleep(5);
                else Thread.Sleep(25);
                
            }
        }

        void Render()
        {
            RenderAnimations();
            //this.Invalidate();
        }

        void RenderAnimations()
        {
            if (ipMode == InterpoleMode.KeyframeEditor && StagePos.y >= 0 - KeyframeEditorClip.Bounds.Y + 80)
            {
                StagePos.y -= (StagePos.y + KeyframeEditorClip.Bounds.Y) / 10;
                Animation = true;
            }
            else Animation = false;

            if (ipMode == InterpoleMode.ClipEditor && StagePos.y < 0)
            {
                StagePos.y -= StagePos.y / 5;
                Animation = true;
            }
            else Animation = false;
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
