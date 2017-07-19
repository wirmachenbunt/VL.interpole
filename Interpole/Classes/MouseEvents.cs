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

            SetPlayPosition();// set playhead

            if (e.Button == MouseButtons.Middle && hitObject is Clip) DeleteClip(hitObject);

            this.Invalidate(); //redraw
        }

        private void Form1_MouseUp(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            hitObject = null; //clear hitObject in case there is no button pressed
            selectionRectangle = Rectangle.Empty;
            this.Invalidate(); //redraw
        }

        private void Form1_MouseDoubleClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (hitObject == null && e.Button == MouseButtons.Left) AddClip(); //ADD CLIP
            if (hitObject is Clip && e.Button == MouseButtons.Left) EditClip(hitObject); // EDIT CLIP
            this.Invalidate(); //redraw
        }

        private void Form1_MouseMove(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            SetGlobalMousePos(e); // get Mouse Position and calibrate with stagepos and dpi

            CalcStagePosition(MousePosition, e.Button == MouseButtons.Right); // calculate delta on rightclick

            SetPlayPosition(); // set playhead

            SetSelectionRectangle(e); // selection rectangle

            if (hitObject is Clip) DragClip(hitObject,e);

            deltaMousePos = mousePos;

            this.Invalidate(); //redraw
        }

        public void CalcStagePosition(Point mousePos, bool click)
        {
            if (click && !(hitObject is Clip))
            {
                int deltaX = mousePos.X - prevMousePos.X;
                prevMousePos = mousePos;
                StagePos.X += deltaX;
                if (StagePos.X > 0) StagePos.X = 0;         
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
                playPosFrames = mousePos.X;
                if (mousePos.X < 0) playPosFrames = 0;
            }
        }

        public void SetSelectionRectangle(System.Windows.Forms.MouseEventArgs e)
        {
            if(hitObject == null && e.Button == MouseButtons.Left)
            {
                Size targetSize = new Size(mousePos.X - holdMousePos.X, mousePos.Y - holdMousePos.Y);
                selectionRectangle = new Rectangle(holdMousePos, targetSize);
            }
        }
 
    }
}
