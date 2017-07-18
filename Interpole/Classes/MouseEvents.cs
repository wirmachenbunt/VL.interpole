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

        private void Form1_MouseDown(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            prevMousePos = MousePosition; // for delta calculation below
            hittest(); //global hittest for everything

        }

        private void Form1_MouseUp(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            hitObject = null; //clear hitObject in case there is no button pressed

        }

        private void Form1_MouseDoubleClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {

            if(hitObject == null) AddClip(); //ADD CLIP
           
        }

        private void Form1_MouseMove(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            SetGlobalMousePos(e); // get Mouse Position and calibrate with stagepos and dpi

            CalcStagePosition(MousePosition, e.Button == MouseButtons.Right); // calculate delta on rightclick 

            

            this.Invalidate(); //redraw
        }

        public void CalcStagePosition(Point mousePos, bool click)
        {
            if (click)
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

 
    }
}
