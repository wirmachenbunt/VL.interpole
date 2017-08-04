using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
using VVVV.Core.Logging;

namespace VVVV.Nodes
{
    partial class Interpole
    {
        public Point mousePos; //current Mouse Pos
        public Point prevMousePos; //previous Mouse Pos
        public Point holdMousePos; // hold mouse for selection rectangle
        public Point deltaMousePos; // better delta variable, get rid of prevMouse
        

        private void Form1_MouseDown(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            
            prevMousePos = MousePosition; // for delta calculation below
            holdMousePos = mousePos; //keep last Mouseposition for rectangle selection

            hittest(); //global hittest for everything

            UpdateSnapList(); // update snap positions

            SetPlayPosition();// set playhead

            if (e.Button == MouseButtons.Middle && hitObject is Clip) DeleteClip(hitObject);
            if (e.Button == MouseButtons.Left && hitObject is WorkSpace || hitObject is Playbar) selectionList.Clear(); // clear selection obviously

            this.Invalidate(); //redraw
        }

        private void Form1_MouseUp(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (hitObject is Clip && rememberLastFunction == "dragclip")
            {
                UpdateClipXML();
            }

            hitObject = null; //clear hitObject in case there is no button pressed
            selectionRectangle.Size = new Size(0, 0);
            this.Invalidate(); //redraw
            //UpdateClipXML();
        }

        private void Form1_MouseDoubleClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (hitObject is WorkSpace && e.Button == MouseButtons.Left) AddClip(); //ADD CLIP
            if (hitObject is Clip && e.Button == MouseButtons.Left) EditClip(hitObject); // EDIT CLIP
            if (hitObject is TimeCodeDisplay && e.Button == MouseButtons.Left) EditTimeCode(); // EDIT TIMECODE
            Invalidate(); //redraw
        }

        private void Form1_MouseWheel(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Delta > 0 && timeZoom < 20)
            {
                timeZoom += 1;
                ApplyTimeZoom();
            }
            if (e.Delta < 0 && timeZoom > 1)
            {
                timeZoom -= 1;
                ApplyTimeZoom();
            }
            this.Invalidate(); //redraw
        }

        private void Form1_MouseMove(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            SetGlobalMousePos(e); // get Mouse Position and calibrate with stagepos and dpi

            CalcStagePosition(MousePosition, e.Button == MouseButtons.Right); // calculate delta on rightclick

            SetPlayPosition(); // set playhead

            SetSelectionRectangle(e); // selection rectangle
            

            //dragging single clip
            if (hitObject is Clip) DragClip(hitObject,e);

            //dragging multiselection
            if (selectionList.Count > 0 && e.Button == MouseButtons.Left && selectionList.Any(cus => cus == hitObject)) DragSelection();

            deltaMousePos = mousePos;

        }

        private void Form_Keydown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                play = !play; // invert bool play
            }

            if (e.KeyCode == Keys.R)
            {
                StagePos.X = 0;
                Invalidate();
            }
        }

        public void CalcStagePosition(Point mousePos, bool click)
        {
            if (click && !(hitObject is Clip))
            {
                int deltaX = mousePos.X - prevMousePos.X;
                prevMousePos = mousePos;
                StagePos.X += deltaX;
                if (StagePos.X > 0) StagePos.X = 0;
                Invalidate();        
            }
        }

        public void SetGlobalMousePos(MouseEventArgs e)
        {
            mousePos.X = Convert.ToInt32((e.X - StagePos.X) / dpi);
            mousePos.Y = Convert.ToInt32((e.Y - StagePos.Y) / dpi);
        }

        public void SetPlayPosition()
        {
           

            if (hitObject is Playbar)
            {
                playPosFrames = mousePos.X*timeZoom;
                if (mousePos.X < 0) playPosFrames = 0;
                play = false;

                // snap playhead to clip in out
                if(Form.ModifierKeys == Keys.Shift)
                {
                    int closest = snapPosList.OrderBy(n => Math.Abs(n - (playPosFrames/timeZoom))).First();

                    int distance = Math.Abs(playPosFrames / timeZoom - closest);

                    if (distance < 12) playPosFrames = closest * timeZoom;

                    //FLogger.Log(LogType.Debug, distance.ToString());
                }
                Invalidate(); //redraw
            }
        }

        public void UpdateSnapList()
        {
            snapPosList.Clear();
            List<Clip> snapClipList = clipList.Where(x => x.Bounds.IntersectsWith(myworkspace.Bounds)).ToList();       

            foreach (Clip myclip in snapClipList)
            {
                snapPosList.Add(myclip.Bounds.X);
                snapPosList.Add(myclip.Bounds.X + myclip.Bounds.Width);
                if(myclip.FadeIn > 0) snapPosList.Add(myclip.Bounds.X + (myclip.FadeIn/timeZoom));
                if (myclip.FadeOut > 0) snapPosList.Add(myclip.Bounds.X + myclip.Bounds.Width - (myclip.FadeOut / timeZoom));
            }
            //FLogger.Log(LogType.Debug, snapPosList.Count.ToString());
        }

        public void SetSelectionRectangle(System.Windows.Forms.MouseEventArgs e)
        {
            if(e.Button == MouseButtons.Left && hitObject is WorkSpace)
            {
                Size targetSize = new Size(mousePos.X - holdMousePos.X, mousePos.Y - holdMousePos.Y);
                selectionRectangle = new Rectangle(holdMousePos, targetSize);
                MultiSelectionClips();
                Invalidate();
            }
        }

    }
}
